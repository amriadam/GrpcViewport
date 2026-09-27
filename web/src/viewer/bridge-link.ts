import { createClient, ConnectError, type Client } from "@connectrpc/connect";
import { createGrpcWebTransport } from "@connectrpc/connect-web";
import { SceneService } from "../gen/grpcviewport/v1/scene_pb";
import { ViewerLink, type ViewerCommand } from "../gen/grpcviewport/v1/viewer_link_pb";

export type LinkStatus = "connecting" | "connected" | "disconnected";

export interface BridgeClients {
  link : Client<typeof ViewerLink>;    // internal: Attach + ReportCamera
  scene: Client<typeof SceneService>; // public API (not used by the page yet)
}

export function createBridgeClients(baseUrl: string, fetchImpl?: typeof fetch): BridgeClients {
  const transport = createGrpcWebTransport({
    baseUrl,
    useBinaryFormat: true,
    fetch: fetchImpl ?? globalThis.fetch,
  });
  return {
    link: createClient(ViewerLink, transport),
    scene: createClient(SceneService, transport),
  };
}

/**
 * Keeps the Attach stream open forever and reconnects after errors.
 * Every (re)connect starts with Clear + a full replay from the host,
 * so the page never has to work out what it missed.
 */
export async function runAttachLoop(
  clients: BridgeClients,
  onCommand: (cmd: ViewerCommand) => void | Promise<void>,
  onStatus: (status: LinkStatus, detail?: string) => void,
): Promise<never> 
{
  const viewerId = crypto.randomUUID().slice(0, 8);
  let backoff = 500;

  for (;;) 
  {
    onStatus("connecting");
    try 
    {
      let first = true;
      for await (const cmd of clients.link.attach({ viewerId })) 
      {
        if (first)
        {
          onStatus("connected");
          backoff = 500;
          first = false;
        }
        await onCommand(cmd);
      }
      onStatus("disconnected", "stream ended");
    } 
    catch (e) 
    {
      onStatus("disconnected", ConnectError.from(e).message);
    }

    await new Promise((r) => setTimeout(r, backoff));
    backoff = Math.min(backoff * 2, 5000); // 0.5 s, 1 s, 2 s, 4 s, 5 s, 5 s, …
  }
}