using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrpcViewport.Server;

/// <summary>
/// Internal service, called only by the Babylon page.
/// </summary>
public sealed class ViewerLinkImpl(SceneHub hub, IHostApplicationLifetime lifetime, ILogger<ViewerLinkImpl> log)
    : ViewerLink.ViewerLinkBase
{
    public override async Task Attach(AttachRequest request, IServerStreamWriter<ViewerCommand> responseStream, ServerCallContext context)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);
        log.LogInformation("Page {ViewerId} attached", request.ViewerId);
        try
        {
            await foreach (var cmd in hub.AttachViewer(cts.Token).ConfigureAwait(false))
                await responseStream.WriteAsync(cmd).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            log.LogInformation("Page {ViewerId} detached", request.ViewerId);
        }
    }

    public override Task<ReportCameraResponse> ReportCamera(CameraState request, ServerCallContext context)
    {
        hub.PublishCamera(request);
        return Task.FromResult(new ReportCameraResponse());
    }
}