using System.IO;
using System.Windows;
using GrpcViewport.Server;
using GrpcViewport.V1;
using GrpcViewport.WinForms; // namespace of the linked DemoGeometry.cs
using Microsoft.Web.WebView2.Core;

namespace GrpcViewport.Wpf;

public partial class MainWindow : Window
{
    private ViewerServer? _server;
    private readonly Stack<GeometryHandle> _handles = new();
    private readonly CancellationTokenSource _cameraCts = new();
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 1. Start Kestrel INSIDE this process (what the Host exe does for WinForms).
        try
        {
            _server = await ViewerServer.StartAsync(new ViewerServerOptions
            {
                StaticFilesRoot = Path.Combine(AppContext.BaseDirectory, "viewer"),
            });
        }
        catch (IOException ex)
        {
            // Typically: the WinForms app / Host is still running on 8080 or 50051.
            MessageBox.Show(ex.ToString(), "GrpcViewport – server failed to start");
            Close();
            return;
        }

        // 2. Show the page.
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GrpcViewport.Wpf", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await WebView.EnsureCoreWebView2Async(env);

            // Development: set VIEWER_DEV_URL=http://localhost:5173 to load the page from Vite.
            string? dev = Environment.GetEnvironmentVariable("VIEWER_DEV_URL");
            WebView.Source = new Uri(string.IsNullOrWhiteSpace(dev) ? "http://127.0.0.1:8080/" : dev);
        }
        catch (Exception ex)
        {
            MessageBox.Show("WebView2 failed: " + ex.Message, "GrpcViewport");
        }

        Tools.IsEnabled = true;
        StatusText.Text = "server running on 127.0.0.1:8080 (in-process)";

        _ = WatchCameraAsync(_server.Hub, _cameraCts.Token);
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _cameraCts.Cancel();

        if (_server is not null)
        {
            await _server.DisposeAsync(); // stops Kestrel, closes the page's stream
        }
    }


    /// <summary>Reads the camera stream until the token is cancelled.</summary>
    private async Task WatchCameraAsync(SceneHub hub, CancellationToken ct)
    {
        try
        {
            // Resumes on the UI thread, so UI elements can be set directly.
            await foreach (var c in hub.SubscribeCamera(30, ct))
            {
                Title = $"GrpcViewport – camera #{c.Sequence}: " +
                        $"pos ({c.Position.X:F2}, {c.Position.Y:F2}, {c.Position.Z:F2})  " +
                        $"target ({c.Target.X:F2}, {c.Target.Y:F2}, {c.Target.Z:F2})";
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled in OnClosed: normal end
        }
    }

    // ------------------------------------------------------------ buttons --
    // Use the same DemoGeometry methods / counts as in your Form1.

    private async void AddLines_Click(object sender, RoutedEventArgs e) =>
        await AddAsync("rings", DemoGeometry.Rings);

    private async void AddPoints_Click(object sender, RoutedEventArgs e) =>
        await AddAsync("scan", () => DemoGeometry.SurfaceScan(50_000));

    private async void AddBigCloud_Click(object sender, RoutedEventArgs e) =>
        await AddAsync("big cloud", () => DemoGeometry.SurfaceScan(2_000_000));

    private async void AddBoxes_Click(object sender, RoutedEventArgs e) =>
        await AddAsync("boxes", DemoGeometry.InstancedBoxes);

    private async void AddSegment_Click(object sender, RoutedEventArgs e) =>
        await AddGroupAsync("segment", DemoGeometry.SceneSegment);

    private void RemoveLast_Click(object sender, RoutedEventArgs e)
    {
        if (_server is null || _handles.Count == 0) return;

        GeometryHandle handle = _handles.Pop();
        StatusText.Text = _server.Hub.Remove(handle.Id)
            ? $"removed #{handle.Id}"
            : $"#{handle.Id} was already gone";
    }

    // ------------------------------------------------------------ helpers --

    /// <summary>
    /// Builds the request and hands it to the hub on a worker thread,
    /// so a 2-million-point cloud doesn't freeze the window.
    /// </summary>
    private async Task AddAsync(string label, Func<AddGeometryRequest> build)
    {
        if (_server is null) return;

        SceneHub hub = _server.Hub;
        Tools.IsEnabled = false;
        StatusText.Text = $"adding {label}…";
        try
        {
            GeometryHandle handle = await Task.Run(() => hub.Add(build()));
            _handles.Push(handle);
            StatusText.Text = $"added #{handle.Id} {label} · pages attached: {hub.ViewerCount}";
        }
        catch (GeometryValidationException ex)
        {
            // In-process there's no RpcException: the hub throws this directly.
            StatusText.Text = "invalid geometry: " + ex.Message;
        }
        finally
        {
            Tools.IsEnabled = true;
        }
    }

    private async Task AddGroupAsync(string label, Func<AddGeometryRequest[]> build)
    {
        
        if (_server is null)
        {
            return;
        }

        SceneHub hub = _server.Hub;
        Tools.IsEnabled = false;
        StatusText.Text = $"adding {label}…";
        try
        {
            List<GeometryHandle> handles = [];
            
            await Task.Run(() =>
            {
                var requests = build();

                foreach (var request in requests)
                {
                    handles.Add(hub.Add(request));
                }
            });

            foreach (var handle in handles)
            {
                _handles.Push(handle);
                StatusText.Text = $"added #{handle.Id} {label} · pages attached: {hub.ViewerCount}";

            }
        }
        catch (GeometryValidationException ex)
        {
            // In-process there's no RpcException: the hub throws this directly.
            StatusText.Text = "invalid geometry: " + ex.Message;
        }
        finally
        {
            Tools.IsEnabled = true;
        }
    }
}