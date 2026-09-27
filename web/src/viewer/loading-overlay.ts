import type { AddGeometryRequest } from "../gen/grpcviewport/v1/scene_pb";

export class LoadingOverlay {
  private readonly el = document.getElementById("loading")!;
  private readonly text = document.getElementById("loading-text")!;

  constructor() 
  {
    if (!this.el || !this.text) 
    {
      console.warn("LoadingOverlay: #loading / #loading-text not found in index.html");
    }
  }
  show(message: string): void {
    this.text.textContent = message;
    this.el.hidden = false;
  }

  hide(): void {
    this.el.hidden = true;
  }
}

/** Resolves after the browser has painted the next frame. */
export function nextPaint(): Promise<void> {
  return new Promise((resolve) => requestAnimationFrame(() => setTimeout(resolve, 0)));
}

/** Rough size of a batch (instanced meshes count every instance). */
export function countVertices(req: AddGeometryRequest): number {
  const g = req.geometry;
  let n = 0;
  if (g.case === "polylines") {
    for (const p of g.value.polylines) n += p.positions.length / 3;
  } else if (g.case === "pointClouds") {
    for (const c of g.value.clouds) n += c.positions.length / 3;
  } else if (g.case === "meshes") {
    for (const m of g.value.meshes) n += (m.positions.length / 3) * Math.max(1, m.instances.length);
  }
  return n;
}