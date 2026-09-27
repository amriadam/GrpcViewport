import { Vector3 } from "@babylonjs/core";
import type { ArcRotateCamera, Scene } from "@babylonjs/core";
import type { ViewerCommand } from "../gen/grpcviewport/v1/viewer_link_pb";
import { GeometryBuilder, type GeometryEntry } from "./geometry-builder";

export class SceneController {
  private readonly scene: Scene;
  private readonly camera: ArcRotateCamera;
  private readonly builder: GeometryBuilder;
  private readonly entries = new Map<bigint, GeometryEntry>();
  private readonly onFit?: (center: Vector3, radius: number) => void;
  private fitPending = false;

  constructor(scene: Scene, camera: ArcRotateCamera, onFit?: (center: Vector3, radius: number) => void) {
    this.scene = scene;
    this.camera = camera;
    this.builder = new GeometryBuilder(scene);
    this.onFit = onFit;

  }

  get count(): number {
    return this.entries.size;
  }

  apply(cmd: ViewerCommand): void {
    const c = cmd.command;
    switch (c.case) {
      case "clear":
        for (const e of this.entries.values()) this.builder.dispose(e);
        this.entries.clear();
        break;

      case "add": {
        const id = c.value.handle!.id;
        const wasEmpty = this.entries.size === 0;
        const old = this.entries.get(id);
        if (old) this.builder.dispose(old);
        try {
          this.entries.set(id, this.builder.build(id, c.value.request!));
        } catch (e) {
          console.error(`failed to build geometry #${id}`, e);
        }
        if (wasEmpty) this.requestFit(); // first geometry: frame it
        break;
      }

      case "remove": {
        const id = c.value.handle!.id;
        const e = this.entries.get(id);
        if (e) {
          this.builder.dispose(e);
          this.entries.delete(id);
        }
        break;
      }
    }
  }

  /** Frames everything on the next frame (a replay of many batches = one fit). */
  requestFit(): void {
    if (this.fitPending) return;
    this.fitPending = true;
    this.scene.onBeforeRenderObservable.addOnce(() => {
      this.fitPending = false;
      this.fitAll();
    });
  }

  /** Points the camera at the bounding box of all geometry. */
  fitAll(): void {
    if (this.entries.size === 0) return;

    const min = new Vector3(Infinity, Infinity, Infinity);
    const max = new Vector3(-Infinity, -Infinity, -Infinity);
    for (const e of this.entries.values()) {
      const b = e.root.getHierarchyBoundingVectors(true);
      min.minimizeInPlace(b.min);
      max.maximizeInPlace(b.max);
    }
    if (!Number.isFinite(min.x)) return;

    const center = min.add(max).scale(0.5);
    const radius = Math.max(max.subtract(min).length() * 0.5, 1e-3); // bounding sphere

    const cam = this.camera;
    cam.setTarget(center);
    cam.radius = (radius / Math.sin(cam.fov / 2)) * 1.05; // sphere fits the vertical FOV
    cam.minZ = cam.radius * 0.001;                        // near/far scaled to the scene
    cam.maxZ = cam.radius * 100;
    this.onFit?.(center, radius);
  }
}