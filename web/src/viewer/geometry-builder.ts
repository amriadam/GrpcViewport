import {
  Color3,
  LinesMesh,
  Material,
  Matrix,
  Mesh,
  StandardMaterial,
  TransformNode,
  Vector3,
  VertexData,
} from "@babylonjs/core";
import type { Scene } from "@babylonjs/core";
import type {
  AddGeometryRequest,
  Color,
  Mesh as ProtoMesh, // renamed: Babylon also has a "Mesh"
  MeshBatch,
  PointCloudBatch,
  PolylineBatch,
} from "../gen/grpcviewport/v1/scene_pb";

/** Everything the page created for one GeometryHandle. */
export interface GeometryEntry {
  root: TransformNode;
  ownedMaterials: Material[];
}

export class GeometryBuilder {
  private readonly scene: Scene;
  private readonly meshMaterial: StandardMaterial; // shared by all triangle meshes

  constructor(scene: Scene)
  {
    this.scene = scene;

    const m = new StandardMaterial("mesh-material", scene);
    m.specularColor = new Color3(0.15, 0.15, 0.15);
    // The proto says: counter-clockwise = front face (OpenGL convention).
    // Babylon's default in a right-handed scene is the opposite, so set it explicitly.
    m.sideOrientation = Material.CounterClockWiseSideOrientation;
    // CAD data is often open or thin: draw both sides and light the back sides too.
    m.backFaceCulling = false;
    m.twoSidedLighting = true;
    this.meshMaterial = m;
  }

  build(id: bigint, req: AddGeometryRequest): GeometryEntry 
  {
    const root = new TransformNode(`geometry#${id} ${req.name}`, this.scene);
    const entry: GeometryEntry = { root, ownedMaterials: [] };

    const g = req.geometry;
    if (g.case === "polylines") 
        this.buildPolylines(g.value, root);
    else 
        if (g.case === "pointClouds") 
            this.buildPointClouds(g.value, root, entry);
    else if (g.case === "meshes") this.buildMeshes(g.value, root);

    root.freezeWorldMatrix(req.transform ? Matrix.FromArray(req.transform.m) : Matrix.Identity());
    for (const child of root.getChildMeshes()) child.freezeWorldMatrix();
    return entry;
  }

  dispose(entry: GeometryEntry): void {
    entry.root.dispose(false, false);
    for (const m of entry.ownedMaterials) m.dispose();
  }

  // ------------------------------------------------------------ polylines --

  /** All polylines of the batch -> ONE LinesMesh = one draw call. */
  private buildPolylines(batch: PolylineBatch, root: TransformNode): void {
    let vertexCount = 0;
    let segmentCount = 0;
    for (const p of batch.polylines) {
      const n = p.positions.length / 3;
      vertexCount += n;
      segmentCount += n - 1 + (p.closed && n > 2 ? 1 : 0);
    }

    const positions = new Float32Array(vertexCount * 3);
    const colors = new Float32Array(vertexCount * 4);
    const indices = new Uint32Array(segmentCount * 2);

    let v = 0;
    let s = 0;
    let hasAlpha = false;
    for (const p of batch.polylines) {
      const n = p.positions.length / 3;
      positions.set(p.positions, v * 3);
      hasAlpha = fillColors(colors, v, n, p.colors, p.color) || hasAlpha;
      for (let i = 0; i < n - 1; i++) {
        indices[s++] = v + i;
        indices[s++] = v + i + 1;
      }
      if (p.closed && n > 2) {
        indices[s++] = v + n - 1;
        indices[s++] = v;
      }
      v += n;
    }

    const lines = new LinesMesh("polylines", this.scene, null, null, undefined, true, hasAlpha);
    applyVertexData(lines, positions, indices, colors);
    lines.parent = root;
  }

  // --------------------------------------------------------- point clouds --

  /** All clouds of the batch -> ONE points mesh = one draw call. */
  private buildPointClouds(batch: PointCloudBatch, root: TransformNode, entry: GeometryEntry): void {
    let vertexCount = 0;
    for (const c of batch.clouds) vertexCount += c.positions.length / 3;

    const positions = new Float32Array(vertexCount * 3);
    const colors = new Float32Array(vertexCount * 4);
    const indices = new Uint32Array(vertexCount);
    for (let i = 0; i < vertexCount; i++) indices[i] = i;

    let v = 0;
    let hasAlpha = false;
    for (const c of batch.clouds) {
      const n = c.positions.length / 3;
      positions.set(c.positions, v * 3);
      hasAlpha = fillColors(colors, v, n, c.colors, c.color) || hasAlpha;
      v += n;
    }

    const mesh = new Mesh("point-clouds", this.scene);
    applyVertexData(mesh, positions, indices, colors);
    mesh.hasVertexAlpha = hasAlpha;

    // Own material per batch, because point_size is per batch.
    const mat = new StandardMaterial("points", this.scene);
    mat.pointsCloud = true;             // draw vertices as points
    mat.pointSize = batch.pointSize > 0 ? batch.pointSize : 2;
    mat.disableLighting = true;         // points have no normals
    mat.emissiveColor = Color3.White(); // final color = vertex color
    mesh.material = mat;
    mesh.parent = root;
    entry.ownedMaterials.push(mat);     // disposed together with the handle
  }

  // --------------------------------------------------------------- meshes --

  /**
   * Meshes WITHOUT instances -> merged into ONE mesh (transforms baked in).
   * Each mesh WITH instances -> one GPU-instanced mesh.
   */
  private buildMeshes(batch: MeshBatch, root: TransformNode): void {
    const merged = batch.meshes.filter((m) => m.instances.length === 0);
    const instanced = batch.meshes.filter((m) => m.instances.length > 0);

    if (merged.length > 0) {
      let vertexCount = 0;
      let indexCount = 0;
      for (const m of merged) {
        vertexCount += m.positions.length / 3;
        indexCount += m.indices.length;
      }

      const positions = new Float32Array(vertexCount * 3);
      const normals = new Float32Array(vertexCount * 3);
      const colors = new Float32Array(vertexCount * 4);
      const indices = new Uint32Array(indexCount);

      let v = 0;
      let k = 0;
      let hasAlpha = false;
      for (const m of merged) {
        const n = m.positions.length / 3;
        writeTransformed(m, positions, normals, v);
        hasAlpha = fillColors(colors, v, n, m.colors, m.color) || hasAlpha;
        for (const i of m.indices) indices[k++] = i + v; // shift into the merged buffer
        v += n;
      }

      const mesh = new Mesh("meshes-merged", this.scene);
      applyVertexData(mesh, positions, indices, colors, normals);
      mesh.hasVertexAlpha = hasAlpha;
      mesh.material = this.meshMaterial;
      mesh.parent = root;
    }

    for (const m of instanced) {
      const n = m.positions.length / 3;
      const positions = new Float32Array(n * 3);
      const normals = new Float32Array(n * 3);
      const colors = new Float32Array(n * 4);
      writeTransformed(m, positions, normals, 0);
      const hasAlpha = fillColors(colors, 0, n, m.colors, m.color);

      const mesh = new Mesh("mesh-instanced", this.scene);
      applyVertexData(mesh, positions, Uint32Array.from(m.indices), colors, normals);
      mesh.hasVertexAlpha = hasAlpha;
      mesh.material = this.meshMaterial;

      // One 4x4 matrix (16 floats) per instance, same memory layout as the proto.
      const matrices = new Float32Array(m.instances.length * 16);
      m.instances.forEach((inst, j) => matrices.set(inst.m, j * 16));
      mesh.thinInstanceSetBuffer("matrix", matrices, 16, true); // all instances in one draw call
      mesh.thinInstanceRefreshBoundingInfo(false);
      mesh.parent = root;
    }
  }
}

// ------------------------------------------------------------------ helpers --

export function applyVertexData(
  mesh: Mesh,
  positions: Float32Array,
  indices: Uint32Array,
  colors: Float32Array,
  normals?: Float32Array,
): void {
  const vd = new VertexData();
  vd.positions = positions;
  vd.indices = indices;
  vd.colors = colors;
  if (normals) vd.normals = normals;
  vd.applyToMesh(mesh, false);
}

/** RGBA floats from packed 0xRRGGBBAA, or the uniform color. Returns true if translucent. */
export function fillColors(
  out: Float32Array,
  start: number,
  count: number,
  packed: number[],
  uniform: Color | undefined,
): boolean {
  let translucent = false;
  for (let i = 0; i < count; i++) {
    const o = (start + i) * 4;
    if (packed.length === count) {
      const c = packed[i] >>> 0;
      out[o] = ((c >>> 24) & 0xff) / 255;
      out[o + 1] = ((c >>> 16) & 0xff) / 255;
      out[o + 2] = ((c >>> 8) & 0xff) / 255;
      out[o + 3] = (c & 0xff) / 255;
    } else {
      out[o] = uniform?.r ?? 1;
      out[o + 1] = uniform?.g ?? 1;
      out[o + 2] = uniform?.b ?? 1;
      out[o + 3] = uniform && uniform.a > 0 ? uniform.a : 1;
    }
    if (out[o + 3] < 1) translucent = true;
  }
  return translucent;
}

/**
 * Copies a mesh's vertices into the output buffers at `offset`, baking in its transform.
 * Computes normals if the mesh has none. Normals use the inverse-transpose matrix,
 * so they stay correct under non-uniform scaling.
 */
function writeTransformed(m: ProtoMesh, outPos: Float32Array, outNrm: Float32Array, offset: number): void {
  let normals: ArrayLike<number> = m.normals;
  if (m.normals.length !== m.positions.length) {
    const computed = new Float32Array(m.positions.length);
    VertexData.ComputeNormals(m.positions, m.indices, computed, { useRightHandedSystem: true }); // CCW = front
    normals = computed;
  }

  const t = m.transform ? Matrix.FromArray(m.transform.m) : Matrix.Identity();
  const nt = t.clone().invert().transpose();
  const p = new Vector3();
  const q = new Vector3();

  for (let i = 0; i < m.positions.length / 3; i++) {
    const o = i * 3;
    const d = (offset + i) * 3;
    Vector3.TransformCoordinatesFromFloatsToRef(m.positions[o], m.positions[o + 1], m.positions[o + 2], t, p);
    Vector3.TransformNormalFromFloatsToRef(normals[o], normals[o + 1], normals[o + 2], nt, q);
    q.normalize();
    outPos[d] = p.x;
    outPos[d + 1] = p.y;
    outPos[d + 2] = p.z;
    outNrm[d] = q.x;
    outNrm[d + 1] = q.y;
    outNrm[d + 2] = q.z;
  }
}