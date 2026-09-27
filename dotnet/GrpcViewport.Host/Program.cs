using System.Diagnostics;
using GrpcViewport.Server;
using Microsoft.Extensions.Logging;

// ------------------------------------------------------------------ arguments --
int? parentPid = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--parent-pid" && int.TryParse(args[i + 1], out var pid))
    {
        parentPid = pid;
    }
}

bool startedByApp = parentPid is not null;

// --------------------------------------------------------------------- start --
ViewerServer server;
try
{
    server = await ViewerServer.StartAsync(new ViewerServerOptions
    {
        StaticFilesRoot = Path.Combine(AppContext.BaseDirectory, "viewer"),

        // Started by the app: stdout is reserved for the READY handshake, so no logging there.
        // Started by hand: keep the ASP.NET log on the console for debugging.
        ConfigureLogging = logging =>
        {
            if (startedByApp)
            {
                logging.ClearProviders();
            }
        },
    });
}
catch (IOException ex)
{
    // Port 50051 or 8080 busy. stderr + exit code 1 tell the parent app what happened.
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// --------------------------------------------------------------------- run --
await using (server)
{
    // The one line the WinForms app waits for.
    Console.WriteLine("READY");

    if (parentPid is int parent)
    {
        // Live exactly as long as the WinForms app: also ends if it crashes.
        try
        {
            await Process.GetProcessById(parent).WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // The parent was already gone before we started waiting.
        }
    }
    else
    {
        Console.WriteLine("Serving on http://127.0.0.1:8080 (page + gRPC-Web) and 127.0.0.1:50051 (gRPC).");
        Console.WriteLine("Press Ctrl+C to stop.");

        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;      // don't kill the process abruptly...
            stop.TrySetResult();  // ...leave the using block so the server stops cleanly
        };
        await stop.Task;
    }
}   // <- DisposeAsync: stops Kestrel, ends all open streams

return 0;