using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GrpcViewport.Server;

public sealed class ViewerServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public SceneHub Hub { get; }

    private ViewerServer(WebApplication app, SceneHub hub)
    {
        _app = app;
        Hub = hub;
    }

    /// <exception cref="IOException">A port is already in use.</exception>
    public static async Task<ViewerServer> StartAsync(ViewerServerOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ViewerServerOptions();
        var hub = new SceneHub();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = [],
        });

        builder.WebHost.ConfigureKestrel(k =>
        {
            // Exactly 127.0.0.1 (not ListenLocalhost): a busy port must fail loudly.
            k.Listen(IPAddress.Loopback, options.GrpcPort, l => l.Protocols = HttpProtocols.Http2);
            k.Listen(IPAddress.Loopback, options.WebPort, l => l.Protocols = HttpProtocols.Http1);
            k.Limits.MaxRequestBodySize = null;
            k.Limits.Http2.InitialConnectionWindowSize = 32 * 1024 * 1024;
            k.Limits.Http2.InitialStreamWindowSize = 16 * 1024 * 1024;
        });

        // Open streams must not delay shutdown.
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));
        builder.Services.AddSingleton(hub);
        builder.Services.AddGrpc(o =>
        {
            o.MaxReceiveMessageSize = options.MaxMessageBytes;
            o.MaxSendMessageSize = options.MaxMessageBytes;
        });
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(options.CorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));
        options.ConfigureLogging?.Invoke(builder.Logging);

        var app = builder.Build();

        if (options.StaticFilesRoot is { } root && Directory.Exists(root))
        {
            var files = new PhysicalFileProvider(Path.GetFullPath(root));
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        }

        app.UseRouting();
        app.UseCors();
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
        app.MapGrpcService<SceneServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<ViewerLinkImpl>().EnableGrpcWeb();

        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw new IOException(
                $"Port {options.GrpcPort} or {options.WebPort} on 127.0.0.1 is already in use.", ex);
        }

        return new ViewerServer(app, hub);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
