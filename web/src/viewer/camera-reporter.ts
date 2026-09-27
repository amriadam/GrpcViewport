import type { ArcRotateCamera, Engine } from "@babylonjs/core";
import { create } from "@bufbuild/protobuf";
import { CameraStateSchema, type CameraState } from "../gen/grpcviewport/v1/scene_pb";

/** Snapshot of the Babylon camera in the proto's conventions. */
export function captureCamera(camera: ArcRotateCamera, engine: Engine, sequence: bigint): CameraState {
  const p = camera.globalPosition;
  const t = camera.target;
  const u = camera.upVector;
  return create(CameraStateSchema, {
    position: { x: p.x, y: p.y, z: p.z },
    target: { x: t.x, y: t.y, z: t.z },
    up: { x: u.x, y: u.y, z: u.z },
    fovY: camera.fov,
    near: camera.minZ,
    far: camera.maxZ,
    // Babylon's matrix memory layout == column-major as in scene.proto
    view: { m: Array.from(camera.getViewMatrix().asArray()) },
    projection: { m: Array.from(camera.getProjectionMatrix().asArray()) },
    viewportWidth: engine.getRenderWidth(),
    viewportHeight: engine.getRenderHeight(),
    sequence,
    timestampMs: BigInt(Date.now()), // Date.now() is UTC epoch ms
  });
}

/**
 * Checks once per frame whether the camera changed and sends it,
 * with at most ONE call in flight (latest value wins).
 * Standing still = no traffic at all.
 */
export class CameraReporter {
  private readonly camera: ArcRotateCamera;
  private readonly engine: Engine;
  private readonly send: (state: CameraState) => Promise<unknown>;

  private sequence = 0n;
  private lastSignature = "";
  private dirty = false;
  private inFlight = false;

  constructor(camera: ArcRotateCamera, engine: Engine, send: (state: CameraState) => Promise<unknown>) {
    this.camera = camera;
    this.engine = engine;
    this.send = send;
    camera.getScene().onAfterRenderObservable.add(() => this.check());
  }

  /** Report again on the next frame even if nothing moved (e.g. after reconnecting). */
  invalidate(): void {
    this.lastSignature = "";
  }

  private check(): void {
    const sig =
      this.camera.getViewMatrix().asArray().join(",") +
      "|" + this.camera.getProjectionMatrix().asArray().join(",") +
      "|" + this.engine.getRenderWidth() + "x" + this.engine.getRenderHeight();

    if (sig !== this.lastSignature) {
      this.lastSignature = sig;
      this.dirty = true;
    }
    void this.flush();
  }

  private async flush(): Promise<void> {
    if (!this.dirty || this.inFlight) return;
    this.dirty = false;
    this.inFlight = true;
    try {
      await this.send(captureCamera(this.camera, this.engine, ++this.sequence));
    } catch {
      // Server unreachable: retry with the newest state a second later.
      this.dirty = true;
      await new Promise((r) => setTimeout(r, 1000));
    } finally {
      this.inFlight = false;
    }
  }
}