using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace GrpcViewport.Server;

/// <summary>
/// Single source of truth: allocates handles, stores live geometry (for page reloads),
/// pushes commands to every attached page, and fans camera updates out to subscribers.
/// Thread-safe.
/// </summary>
public sealed class SceneHub
{
    private readonly object                                      _sceneGate = new();
    private readonly SortedDictionary<ulong, AddGeometryRequest> _geometry = [];
    private readonly HashSet<Channel<ViewerCommand>>             _viewers  = [];

    private readonly object                        _cameraGate = new();
    private          CameraState?                  _camera;
    private readonly HashSet<Channel<CameraState>> _cameraSubscribers = [];

    private ulong _nextId = 1;

    public int GeometryCount
    {
        get
        {
            lock (_sceneGate)
            {
                return _geometry.Count;
            }
        }
    }

    public int ViewerCount 
    { 
        get 
        { 
            lock (_sceneGate)
            {
                return _viewers.Count;
            }
        } 
    }

    /// <exception cref="GeometryValidationException">
    /// The request is malformed.
    /// </exception>
    public GeometryHandle Add(AddGeometryRequest request)
    {
        if (GeometryValidator.Validate(request) is { } error)
        {
            throw new GeometryValidationException(error);
        }

        lock (_sceneGate)
        {
            var id = _nextId++;
            _geometry.Add(id, request);

            var command = new ViewerCommand
            {
                Add = new AddGeometryCommand
                {
                    Handle = new GeometryHandle
                    {
                        Id = id
                    },
                    Request = request
                },
            };

            Broadcast(command);

            return new GeometryHandle 
            { 
                Id = id 
            };
        }
    }

    /// <returns>
    /// false if the handle is unknown.
    /// </returns>
    public bool Remove(ulong id)
    {
        lock (_sceneGate)
        {
            if (!_geometry.Remove(id))
            {
                return false;
            }

            var command = new ViewerCommand
            {
                Remove = new RemoveGeometryCommand
                {
                    Handle = new GeometryHandle
                    {
                        Id = id
                    }
                }
            };

            Broadcast(command);

            return true;
        }
    }

        /// <summary>
    /// Command stream for one page: Clear, then a replay of everything stored,
    /// then every change, until <paramref name="ct"/> is cancelled.
    /// </summary>
    public async IAsyncEnumerable<ViewerCommand> AttachViewer([EnumeratorCancellation] CancellationToken ct)
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = true
        };

        var channel = Channel.CreateUnbounded<ViewerCommand>(options);

        lock (_sceneGate)
        {
            // Replay + registration under the same lock Add/Remove use:
            // no command can be lost or delivered twice.

            var clearCommand = new ViewerCommand
            {
                Clear = new ClearCommand()
            };

            channel.Writer.TryWrite(clearCommand);

            foreach (var (id, request) in _geometry)
            {
                // One new command per stored batch: built INSIDE the loop,
                // because id and request only exist here.
                var addCommand = new ViewerCommand
                {
                    Add = new AddGeometryCommand
                    {
                        Handle = new GeometryHandle
                        {
                            Id = id
                        },
                        Request = request
                    },
                };

                channel.Writer.TryWrite(addCommand);
            }

            _viewers.Add(channel);
        }

        try
        {
            await foreach (var cmd in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return cmd;
            }
        }
        finally
        {
            // Page disconnected or app stopping: stop sending to it.
            lock (_sceneGate)
            {
                _viewers.Remove(channel);
            }
        }
    }

    // Caller must hold _sceneGate.
    private void Broadcast(ViewerCommand cmd)
    {
        foreach (var viewer in _viewers)
        {
            viewer.Writer.TryWrite(cmd);
        }
    }

    public void PublishCamera(CameraState state)
    {
        lock (_cameraGate)
        {
            _camera = state;

            foreach (var s in _cameraSubscribers)
            {
                s.Writer.TryWrite(state);
            }
        }
    }

    /// <summary>
    /// Latest-value camera stream: the current state right away, then each change,
    /// never faster than <paramref name="maxRateHz"/> (0 = unthrottled).
    /// </summary>
    public async IAsyncEnumerable<CameraState> SubscribeCamera(
        float                                      maxRateHz, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Capacity 1 + DropOldest = "keep only the newest": a slow client never builds a backlog.
        var channel = Channel.CreateBounded<CameraState>(
            new BoundedChannelOptions(1)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });

        lock (_cameraGate)
        {
            if (_camera is not null)
            {
                channel.Writer.TryWrite(_camera);
            }

            _cameraSubscribers.Add(channel);
        }

        long minIntervalMs = maxRateHz > 0 
                           ? (long)Math.Ceiling(1000.0 / maxRateHz) 
                           : 0;

        long lastSent = 0;
        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                var wait = lastSent + minIntervalMs - Environment.TickCount64;
                if (wait > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(wait), ct).ConfigureAwait(false);
                }

                if (!channel.Reader.TryRead(out var state))
                {
                    continue;
                }

                lastSent = Environment.TickCount64;
                
                yield return state;
            }
        }
        finally
        {
            lock (_cameraGate) _cameraSubscribers.Remove(channel);
        }
    }
}
