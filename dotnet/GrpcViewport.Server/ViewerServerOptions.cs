using Microsoft.Extensions.Logging;

namespace GrpcViewport.Server;

public sealed class ViewerServerOptions
{
    /// <summary>Plain gRPC over HTTP/2 (for .NET 8+ clients).</summary>
    public int GrpcPort { get; init; } = 50051;

    /// <summary>HTTP/1.1: gRPC-Web (page + your .NET 4.8 app) and the page's files.</summary>
    public int WebPort { get; init; } = 8080;

    /// <summary>Folder with the built page (Vite dist). Null or missing = serve no files.</summary>
    public string? StaticFilesRoot { get; init; }

    /// <summary>Origins allowed to call cross-origin (the Vite dev server).</summary>
    public string[] CorsOrigins { get; init; } = ["http://localhost:5173", "http://127.0.0.1:5173"];

    public int MaxMessageBytes { get; init; } = 256 * 1024 * 1024;

    public Action<ILoggingBuilder>? ConfigureLogging { get; init; }
}
