using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrpcViewport.Server;

/// <summary>Public API, called by your WinForms app.</summary>
public sealed class SceneServiceImpl(
    SceneHub                  hub,
    IHostApplicationLifetime  lifetime,
    ILogger<SceneServiceImpl> log)
    : SceneService.SceneServiceBase
{
    public override Task<GeometryHandle> AddGeometry(
        AddGeometryRequest request, 
        ServerCallContext  context)
    {
        try
        {
            var handle = hub.Add(request);
            log.LogInformation("+ #{Id} {Name} ({Kind}), pages attached: {Pages}",
                handle.Id, request.Name, request.GeometryCase, hub.ViewerCount);
            return Task.FromResult(handle);
        }
        catch (GeometryValidationException e)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message));
        }
    }

    public override Task<RemoveGeometryResponse> RemoveGeometry(
        RemoveGeometryRequest request, 
        ServerCallContext     context)
    {
        if (request.Handle is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "handle is required"));
        }

        if (!hub.Remove(request.Handle.Id))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"no geometry with handle {request.Handle.Id}"));
        }

        log.LogInformation("- #{Id}", request.Handle.Id);
        return Task.FromResult(new RemoveGeometryResponse());
    }

    public override async Task SubscribeCamera(
        SubscribeCameraRequest           request, 
        IServerStreamWriter<CameraState> responseStream, 
        ServerCallContext                context)
    {
        // End when the client disconnects OR the host is stopping.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);
        try
        {
            await foreach (var state in hub.SubscribeCamera(request.MaxRateHz, cts.Token).ConfigureAwait(false))
                await responseStream.WriteAsync(state).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // normal end
        }
    }
}
