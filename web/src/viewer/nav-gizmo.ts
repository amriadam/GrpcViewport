import { Vector3 } from "@babylonjs/core";
import type { ArcRotateCamera, Nullable, Observer, Scene } from "@babylonjs/core";

interface GizmoAxis {
  dir: Vector3;      // world direction; clicking it puts the camera on this side
  label: string;
  color: string;
  positive: boolean;
}

// Negative axes first: when depths are equal, positive ones are drawn on top.
const AXES: GizmoAxis[] = [
  { dir: new Vector3(-1, 0, 0), label: "-X", color: "#ff3352", positive: false },
  { dir: new Vector3(0, -1, 0), label: "-Y", color: "#8bdc00", positive: false },
  { dir: new Vector3(0, 0, -1), label: "-Z", color: "#2890ff", positive: false },
  { dir: new Vector3(1, 0, 0), label: "X", color: "#ff3352", positive: true },
  { dir: new Vector3(0, 1, 0), label: "Y", color: "#8bdc00", positive: true },
  { dir: new Vector3(0, 0, 1), label: "Z", color: "#2890ff", positive: true },
];

interface PlacedAxis {
  axis: GizmoAxis;
  x: number;     // position on the gizmo canvas (CSS px)
  y: number;
  depth: number; // larger = closer to the viewer
}

const SIZE = 110;          // gizmo size in CSS px (must match .nav-gizmo in style.css)
const BALL = 11;           // ball radius
const DRAG_THRESHOLD = 3;  // px: below = click, above = orbit
const ANIM_MS = 250;

/** Blender-style view gizmo drawn on a small 2D canvas in the corner. */
export class NavGizmo {
  private readonly scene: Scene;
  private readonly camera: ArcRotateCamera;
  private readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;

  private placed: PlacedAxis[] = [];
  private mouse: { x: number; y: number } | null = null;
  private drag: { startX: number; startY: number; lastX: number; lastY: number; moved: boolean } | null = null;
  private tween: Nullable<Observer<Scene>> = null;

  constructor(scene: Scene, camera: ArcRotateCamera) {
    this.scene = scene;
    this.camera = camera;

    this.canvas = document.createElement("canvas");
    this.canvas.className = "nav-gizmo";
    document.body.appendChild(this.canvas);
    this.ctx = this.canvas.getContext("2d")!;

    this.resize();
    window.addEventListener("resize", () => this.resize());

    this.canvas.addEventListener("pointerdown", (e) => this.onPointerDown(e));
    this.canvas.addEventListener("pointermove", (e) => this.onPointerMove(e));
    this.canvas.addEventListener("pointerup", (e) => this.onPointerUp(e));
    this.canvas.addEventListener("pointerleave", () => {
      if (!this.drag) this.mouse = null;
    });

    // Redraw after every 3D frame, so it always matches the camera
    scene.onAfterRenderObservable.add(() => this.draw());
  }

  // ------------------------------------------------------------ public API --

  /** Animates the camera so it looks from `dir` towards the target (e.g. +X = "Right"). */
  viewFrom(dir: Vector3): void {
    const cam = this.camera;
    const d = dir.normalizeToNew();

    // ArcRotateCamera: position = target + r * (cos a * sin b, cos b, sin a * sin b)
    const lower = cam.lowerBetaLimit ?? 0.01;
    const upper = cam.upperBetaLimit ?? Math.PI - 0.01;
    const beta = Math.min(Math.max(Math.acos(Math.min(Math.max(d.y, -1), 1)), lower), upper);
    // Straight top/bottom: alpha is undefined; PI/2 gives X to the right, like the front view
    const alpha = Math.abs(d.y) > 0.999 ? Math.PI / 2 : Math.atan2(d.z, d.x);

    this.animateTo(alpha, beta);
  }

  /** Look from the opposite side (Blender: numpad 9). */
  viewOpposite(): void {
    this.viewFrom(this.currentViewDir().negate());
  }

  // --------------------------------------------------------------- drawing --

  private resize(): void {
    const dpr = window.devicePixelRatio || 1;
    this.canvas.width = Math.round(SIZE * dpr);  // sharp on high-DPI screens
    this.canvas.height = Math.round(SIZE * dpr);
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0); // draw in CSS pixels
  }

  private draw(): void {
    const ctx = this.ctx;
    const c = SIZE / 2;
    const len = c - BALL - 2;
    const view = this.camera.getViewMatrix();
    const rightHanded = this.scene.useRightHandedSystem;

    // World axis -> camera space. x = right, y = up; towards the viewer is +z (RH) or -z (LH).
    this.placed = AXES.map((axis) => {
      const v = Vector3.TransformNormal(axis.dir, view);
      return { axis, x: c + v.x * len, y: c - v.y * len, depth: rightHanded ? v.z : -v.z };
    }).sort((a, b) => a.depth - b.depth); // far ones first, near ones painted over them

    const hovered = this.mouse ? this.hitTest(this.mouse.x, this.mouse.y) : null;

    ctx.clearRect(0, 0, SIZE, SIZE);

    // Round background while the mouse is over the gizmo (like Blender)
    if (this.mouse || this.drag) {
      ctx.fillStyle = "rgba(255, 255, 255, 0.08)";
      ctx.beginPath();
      ctx.arc(c, c, c, 0, Math.PI * 2);
      ctx.fill();
    }

    ctx.font = "bold 11px system-ui, sans-serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";

    for (const p of this.placed) {
      const { axis, x, y } = p;
      const isHovered = hovered === axis;

      // Stick from the centre to the positive balls
      if (axis.positive) {
        ctx.strokeStyle = axis.color;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(c, c);
        ctx.lineTo(x, y);
        ctx.stroke();
      }

      // Ball: positive = solid, negative = faint with a coloured ring
      ctx.beginPath();
      ctx.arc(x, y, axis.positive ? BALL : BALL * 0.85, 0, Math.PI * 2);
      ctx.globalAlpha = axis.positive ? 1 : 0.35;
      ctx.fillStyle = axis.color;
      ctx.fill();
      ctx.globalAlpha = 1;
      if (!axis.positive) {
        ctx.strokeStyle = axis.color;
        ctx.lineWidth = 1.5;
        ctx.stroke();
      }
      if (isHovered) {
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      // Labels: always on positive balls, on negative ones only while hovered
      if (axis.positive || isHovered) {
        ctx.fillStyle = axis.positive ? "#101217" : "#ffffff";
        ctx.fillText(axis.label, x, y + 0.5);
      }
    }

    this.canvas.style.cursor = this.drag?.moved ? "grabbing" : hovered ? "pointer" : "grab";
  }

  /** Nearest ball under the point, or null. */
  private hitTest(x: number, y: number): GizmoAxis | null {
    for (let i = this.placed.length - 1; i >= 0; i--) { // front to back
      const p = this.placed[i];
      if ((p.x - x) ** 2 + (p.y - y) ** 2 <= (BALL + 2) ** 2) return p.axis;
    }
    return null;
  }

  // ---------------------------------------------------------------- input --

  private onPointerDown(e: PointerEvent): void {
    this.canvas.setPointerCapture(e.pointerId); // keep receiving moves outside the gizmo
    this.stopTween();
    this.drag = { startX: e.clientX, startY: e.clientY, lastX: e.clientX, lastY: e.clientY, moved: false };
  }

  private onPointerMove(e: PointerEvent): void {
    this.mouse = { x: e.offsetX, y: e.offsetY };
    const d = this.drag;
    if (!d) return;

    if (!d.moved && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) > DRAG_THRESHOLD) {
      d.moved = true;
    }
    if (d.moved) {
      // Same formula as Babylon's own mouse orbit, so it turns the same way as the viewport
      const cam = this.camera;
      cam.inertialAlphaOffset -= (e.clientX - d.lastX) / cam.angularSensibilityX;
      cam.inertialBetaOffset -= (e.clientY - d.lastY) / cam.angularSensibilityY;
    }
    d.lastX = e.clientX;
    d.lastY = e.clientY;
  }

  private onPointerUp(e: PointerEvent): void {
    this.canvas.releasePointerCapture(e.pointerId);
    const d = this.drag;
    this.drag = null;
    if (!d || d.moved) return;

    // A click on a ball: look from that side. Already looking from there? Flip (like Blender).
    const axis = this.hitTest(e.offsetX, e.offsetY);
    if (!axis) return;
    const alreadyThere = Vector3.Dot(this.currentViewDir(), axis.dir) > 0.999;
    this.viewFrom(alreadyThere ? axis.dir.negate() : axis.dir);
  }

  // -------------------------------------------------------------- animation --

  private currentViewDir(): Vector3 {
    return this.camera.position.subtract(this.camera.target).normalize();
  }

  private animateTo(alpha: number, beta: number): void {
    const cam = this.camera;
    this.stopTween();
    cam.inertialAlphaOffset = 0; // stop any running orbit inertia
    cam.inertialBetaOffset = 0;

    // Shortest way round for alpha (e.g. 350° -> 10° goes +20°, not -340°)
    const TAU = Math.PI * 2;
    let da = (alpha - cam.alpha) % TAU;
    if (da > Math.PI) da -= TAU;
    if (da < -Math.PI) da += TAU;

    const a0 = cam.alpha;
    const b0 = cam.beta;
    const start = performance.now();

    this.tween = this.scene.onBeforeRenderObservable.add(() => {
      const t = Math.min((performance.now() - start) / ANIM_MS, 1);
      const k = t * t * (3 - 2 * t); // smoothstep easing
      cam.alpha = a0 + da * k;
      cam.beta = b0 + (beta - b0) * k;
      if (t >= 1) this.stopTween();
    });
  }

  private stopTween(): void {
    if (this.tween) {
      this.scene.onBeforeRenderObservable.remove(this.tween);
      this.tween = null;
    }
  }
}