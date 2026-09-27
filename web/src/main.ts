import "./style.css";
import { ArcRotateCamera, Color4, Engine, HemisphericLight, Scene, Vector3 } from "@babylonjs/core";
import { createBridgeClients, runAttachLoop } from "./viewer/bridge-link";
import { SceneController } from "./viewer/scene-controller";
import type { ViewerCommand } from "./gen/grpcviewport/v1/viewer_link_pb";
import { LoadingOverlay, countVertices, nextPaint } from "./viewer/loading-overlay";
import { createWatchedFetch } from "./viewer/frame-watcher";
import { SceneHelpers } from "./viewer/scene-helpers";
import { NavGizmo } from "./viewer/nav-gizmo";
import { ViewToolbar } from "./viewer/view-toolbar";
// ------------------------------------------------------------------ scene --
const canvas = document.getElementById("viewport") as HTMLCanvasElement;
const engine = new Engine(canvas, true);
const scene  = new Scene(engine);
scene.useRightHandedSystem = true;
scene.clearColor = Color4.FromHexString("#14171cff");

const camera = new ArcRotateCamera(
  "camera", 
  -Math.PI / 3, 
  Math.PI / 3, 12, 
  Vector3.Zero(), 
  scene);

camera.attachControl(canvas, true);
camera.wheelDeltaPercentage = 0.01;
new HemisphericLight("sky", new Vector3(0, 1, 0), scene);

engine.runRenderLoop(() => scene.render());
window.addEventListener("resize", () => engine.resize());

// ------------------------------------------------------------- the link --
// Served by the host (http://127.0.0.1:8080): talk to exactly that server.
// Served by Vite (http://localhost:5173, development): talk to the host on 8080.
const BRIDGE_URL = location.port === "5173" ? "http://127.0.0.1:8080" : location.origin;

const helpers = new SceneHelpers(scene);
const controller = new SceneController(scene, camera, (center, radius) => helpers.fitTo(center, radius));
const gizmo = new NavGizmo(scene, camera);
const toolbar = new ViewToolbar(helpers);

const overlay = new LoadingOverlay();

const BIG_FRAME_BYTES = 1_000_000; // messages above ~1 MB show "Receiving … %"
const BIG_BATCH = 50_000;          

const frameIsBig: boolean[] = [];

const watchedFetch = createWatchedFetch({
  onFrameStart(totalBytes) 
  {
    const big = totalBytes >= BIG_FRAME_BYTES;
    frameIsBig.push(big);
    try {
      if (big) overlay.show(`Receiving geometry… 0 %`);
    } catch (e) {
      console.error(e);
    }
  },
  onFrameProgress(received, total) {
    try {
      if (total >= BIG_FRAME_BYTES) {
        overlay.show(`Receiving geometry… ${Math.floor((received * 100) / total)} %`);
      }
    } catch (e) {
      console.error(e);
    }
  },
});

const clients = createBridgeClients(BRIDGE_URL, watchedFetch);
const statusEl = document.getElementById("status")!;

async function handleCommand(cmd: ViewerCommand): Promise<void> {
  const wasBigFrame = frameIsBig.shift() ?? false;
  const c = cmd.command;

  try {
    const bigBuild = c.case === "add" && countVertices(c.value.request!) >= BIG_BATCH;

    if (!wasBigFrame && !bigBuild) {
      controller.apply(cmd); // small: no overlay
      return;
    }

    const name = c.case === "add" ? c.value.request!.name || "geometry" : "scene";
    overlay.show(`Building ${name}…`);
    await nextPaint();
    controller.apply(cmd);
  } catch (e) {
    // One bad command must not disconnect the page.
    console.error("Failed to apply command", c.case, e);
  } finally {
    if (!frameIsBig.includes(true)) overlay.hide();
  }
}

void runAttachLoop(clients, handleCommand, (status, detail) => {
  statusEl.textContent =
    status === "connected"
      ? `Connected · ${BRIDGE_URL}`
      : status === "connecting"
        ? `Connecting to ${BRIDGE_URL}…`
        : `Offline · ${detail ?? ""}`;
  if (status === "disconnected") {
    frameIsBig.length = 0; // a new stream starts from scratch
    overlay.hide();
  }
});

window.addEventListener("keydown", (e) => {
  const s = e.ctrlKey ? -1 : 1; // Ctrl = opposite side, like Blender
  switch (e.code) {
    case "KeyF":
    case "Home":
      controller.fitAll();
      break;
    case "Numpad1":
    case "Digit1":
      gizmo.viewFrom(new Vector3(0, 0, s)); // front / back
      break;
    case "Numpad3":
    case "Digit3":
      gizmo.viewFrom(new Vector3(s, 0, 0)); // right / left
      break;
    case "Numpad7":
    case "Digit7":
      gizmo.viewFrom(new Vector3(0, s, 0)); // top / bottom
      break;
    case "Numpad9":
    case "Digit9":
      gizmo.viewOpposite();
      break;
    case "KeyG":
      toolbar.toggleGrid();
      break;
    case "KeyA":
      toolbar.toggleAxes();
      break;
    default:
      return;
  }
  e.preventDefault();
});
// Handy in DevTools (F12): scene, camera, controller
Object.assign(window, { scene, camera, controller, helpers, gizmo, toolbar });