import { Color3, Color4, MeshBuilder, Vector3 } from "@babylonjs/core";
import type { LinesMesh, Mesh, Scene } from "@babylonjs/core";
import { GridMaterial } from "@babylonjs/materials";

// Blender's axis colours
const X_COLOR = Color4.FromHexString("#ff3352ff");
const Y_COLOR = Color4.FromHexString("#8bdc00ff");
const Z_COLOR = Color4.FromHexString("#2890ffff");

/** Floor grid (XZ plane, Y up) + axis lines through the world origin. */
export class SceneHelpers {
  private readonly ground: Mesh;
  private readonly material: GridMaterial;
  private readonly axes: LinesMesh;

  constructor(scene: Scene) {
    // --- grid -------------------------------------------------------------
    const mat = new GridMaterial("grid-material", scene);
    mat.mainColor = new Color3(0.08, 0.09, 0.11); // floor tint (almost invisible)
    mat.lineColor = new Color3(0.42, 0.45, 0.5);
    mat.majorUnitFrequency = 10;   // every 10th line is a major line
    mat.minorUnitVisibility = 0.35;
    mat.opacity = 0.99;            // < 1 = transparent floor, only the lines stay visible
    mat.backFaceCulling = false;   // visible from below as well
    mat.zOffset = 1;               // push the grid back so the axis lines on it win
    this.material = mat;

    // 2 x 2 plane (-1..1), scaled to the right size in fitTo()
    this.ground = MeshBuilder.CreateGround("helper-grid", { width: 2, height: 2 }, scene);
    this.ground.material = mat;
    this.ground.isPickable = false;

    // --- axis lines (unit length, scaled in fitTo()) ------------------------
    this.axes = MeshBuilder.CreateLineSystem(
      "helper-axes",
      {
        lines: [
          [new Vector3(-1, 0, 0), new Vector3(1, 0, 0)], // X on the floor
          [new Vector3(0, 0, -1), new Vector3(0, 0, 1)], // Z on the floor
          [new Vector3(0, 0, 0), new Vector3(0, 1, 0)],  // Y up from the origin
        ],
        colors: [
          [X_COLOR, X_COLOR],
          [Z_COLOR, Z_COLOR],
          [Y_COLOR, Y_COLOR],
        ],
      },
      scene,
    );
    this.axes.isPickable = false;

    this.fitTo(Vector3.Zero(), 5); // sensible size before any geometry arrives
  }

  /** Adapts cell size and extent to the scene: called after every fitAll. */
  fitTo(center: Vector3, radius: number): void {
    // Cell size = power of 10 that suits the model (radius 5 -> 1, radius 50 -> 10, radius 0.5 -> 0.1)
    const unit = Math.pow(10, Math.floor(Math.log10(Math.max(radius, 1e-6) / 2)));
    const half = unit * 200; // grid extends 200 cells in each direction

    // Snap the grid centre to a major line so lines stay on round world coordinates
    const step = unit * 10;
    this.ground.position.set(Math.round(center.x / step) * step, 0, Math.round(center.z / step) * step);
    this.ground.scaling.set(half, 1, half);

    // The grid shader works in the mesh's LOCAL coordinates (-1..1),
    // so the cell size must be given in local units too.
    this.material.gridRatio = unit / half;

    const reach = half + Math.max(Math.abs(center.x), Math.abs(center.z));
    this.axes.scaling.set(reach, unit * 10, reach); // Y line = one major cell high
  }

  get gridVisible(): boolean 
  {
    return this.ground.isEnabled();
  }
  set gridVisible(value: boolean) 
  {
    this.ground.setEnabled(value);
  }

  get axesVisible(): boolean 
  {
    return this.axes.isEnabled();
  }
  
  set axesVisible(value: boolean) 
  {
    this.axes.setEnabled(value);
  }
}