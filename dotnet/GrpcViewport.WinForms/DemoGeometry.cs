using GrpcViewport.V1;
using System;

namespace GrpcViewport.WinForms
{
    internal static class DemoGeometry
    {
        /// <summary>
        /// 20 closed rings around the origin, ONE PolylineBatch.
        /// </summary>
        public static AddGeometryRequest Rings()
        {
            var batch = new PolylineBatch();
            for (int k = 0; k < 20_000; k++)
            {
                var ring = new Polyline { Closed = true, Color = new Color { R = 0.3f, G = 0.8f, B = 1f } };
                float radius = 1f + k * 0.1f;
                for (int i = 0; i < 64; i++)
                {
                    double angle = i / 64.0 * 2 * Math.PI;
                    ring.Positions.Add((float)(Math.Cos(angle) * radius));
                    ring.Positions.Add(k * 0.05f);
                    ring.Positions.Add((float)(Math.Sin(angle) * radius));
                }
                batch.Polylines.Add(ring);
            }
            return new AddGeometryRequest { Name = "rings", Polylines = batch };
        }

        /// <summary>
        /// 100,000 colored points (a wavy surface) left of the rings, ONE PointCloudBatch.
        /// </summary>
        public static AddGeometryRequest SurfaceScan(int count)
        {
            var rng = new Random(1);
            var cloud = new PointCloud();
            for (int i = 0; i < count; i++)
            {
                float x = (float)rng.NextDouble() * 4 - 2;
                float z = (float)rng.NextDouble() * 4 - 2;
                float y = 1f + 0.3f * (float)(Math.Sin(3 * x) * Math.Cos(3 * z));

                cloud.Positions.Add(x - 6f); // shifted left, next to the rings
                cloud.Positions.Add(y);
                cloud.Positions.Add(z);
                cloud.Colors.Add(Rgba((x + 2) / 4, 0.6f, (z + 2) / 4)); // one color per point
            }
            return new AddGeometryRequest
            {
                Name = "surface-scan",
                PointClouds = new PointCloudBatch { Clouds = { cloud }, PointSize = 2 },
            };
        }

        /// <summary>
        /// One cube drawn 2,500 times with GPU instancing, right of the rings.</summary>
        public static AddGeometryRequest InstancedBoxes()
        {
            Mesh cube = Cube();
            cube.Color = new Color { R = 0.95f, G = 0.75f, B = 0.2f };

            for (int x = 0; x < 50; x++)
            {
                for (int z = 0; z < 50; z++)
                {
                    float h = 0.1f + 0.2f * (1f + (float)(Math.Sin(x * 0.3) * Math.Cos(z * 0.3)));
                    cube.Instances.Add(ScaleTranslation(0.08f, 4f + x * 0.1f, h, -2.5f + z * 0.1f));
                }
            }
            return new AddGeometryRequest { Name = "instanced-boxes", Meshes = new MeshBatch { Meshes = { cube } } };
        }

        /// <summary>
        /// A "scene segment": wire box + very transparent faces + local XYZ axes inside,
        /// slightly tilted (not parallel to the main planes).
        /// Returns TWO requests (lines, faces) that share the same transform.
        /// </summary>
        public static AddGeometryRequest[] SceneSegment()
        {
            // Box size 4 x 2 x 3, centred on the segment's local origin
            const float hx = 2f, hy = 1f, hz = 1.5f;

            // Pose: yaw 25° (about Y), pitch 10° (about X), roll -8° (about Z), moved to (3, 1.5, -2)
            Matrix4 pose = Pose(25, 10, -8, 3f, 1.5f, -2f);

            // ---------------------------------------------------------- lines --
            var lines = new PolylineBatch();
            var edge = new Color { R = 0.85f, G = 0.9f, B = 1f }; // A = 0 -> opaque

            // bottom and top rectangle (closed), then the 4 vertical edges
            lines.Polylines.Add(Line(edge, true, -hx, -hy, -hz, hx, -hy, -hz, hx, -hy, hz, -hx, -hy, hz));
            lines.Polylines.Add(Line(edge, true, -hx, hy, -hz, hx, hy, -hz, hx, hy, hz, -hx, hy, hz));
            lines.Polylines.Add(Line(edge, false, -hx, -hy, -hz, -hx, hy, -hz));
            lines.Polylines.Add(Line(edge, false, hx, -hy, -hz, hx, hy, -hz));
            lines.Polylines.Add(Line(edge, false, hx, -hy, hz, hx, hy, hz));
            lines.Polylines.Add(Line(edge, false, -hx, -hy, hz, -hx, hy, hz));

            // local coordinate system at the centre: shaft + arrow head per axis
            float len = 0.8f * Math.Min(hx, Math.Min(hy, hz)); // fits inside the box
            float head = 0.2f * len;
            float w = 0.5f * head;

            var red = new Color { R = 1f, G = 0.2f, B = 0.32f };
            var green = new Color { R = 0.55f, G = 0.86f, B = 0f };
            var blue = new Color { R = 0.16f, G = 0.56f, B = 1f };

            lines.Polylines.Add(Line(red, false, 0, 0, 0, len, 0, 0));
            lines.Polylines.Add(Line(red, false, len - head, w, 0, len, 0, 0, len - head, -w, 0));

            lines.Polylines.Add(Line(green, false, 0, 0, 0, 0, len, 0));
            lines.Polylines.Add(Line(green, false, w, len - head, 0, 0, len, 0, -w, len - head, 0));

            lines.Polylines.Add(Line(blue, false, 0, 0, 0, 0, 0, len));
            lines.Polylines.Add(Line(blue, false, w, 0, len - head, 0, 0, len, -w, 0, len - head));

            // ---------------------------------------------------------- faces --
            // 4 own vertices per face -> flat normals; corners wound CCW seen from outside.
            var box = new Mesh { Color = new Color { R = 0.45f, G = 0.75f, B = 1f, A = 0.07f } };
            Quad(box, hx, hy, hz, 1, -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1);          // +X
            Quad(box, hx, hy, hz, -1, -1, -1, -1, -1, 1, -1, 1, 1, -1, 1, -1);      // -X
            Quad(box, hx, hy, hz, -1, 1, -1, -1, 1, 1, 1, 1, 1, 1, 1, -1);          // +Y
            Quad(box, hx, hy, hz, -1, -1, -1, 1, -1, -1, 1, -1, 1, -1, -1, 1);      // -Y
            Quad(box, hx, hy, hz, -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1);          // +Z
            Quad(box, hx, hy, hz, -1, -1, -1, -1, 1, -1, 1, 1, -1, 1, -1, -1);      // -Z

            var faces = new MeshBatch();
            faces.Meshes.Add(box);

            return
            [
                new AddGeometryRequest { Name = "segment lines", Transform = pose, Polylines = lines },
                new AddGeometryRequest { Name = "segment faces", Transform = pose.Clone(), Meshes = faces },
            ];
        }

        /// <summary>A polyline from flat xyz values.</summary>
        private static Polyline Line(Color color, bool closed, params float[] xyz)
        {
            var p = new Polyline { Color = color, Closed = closed };
            p.Positions.Add(xyz);
            return p;
        }

        /// <summary>
        /// Adds one box face as 2 triangles. Corners are given as signs (-1/+1)
        /// and scaled by the half sizes; they must be counter-clockwise seen from outside.
        /// </summary>
        private static void Quad(Mesh mesh, float hx, float hy, float hz, params int[] signs)
        {
            uint start = (uint)(mesh.Positions.Count / 3);
            for (int i = 0; i < 12; i += 3)
            {
                mesh.Positions.Add(signs[i] * hx);
                mesh.Positions.Add(signs[i + 1] * hy);
                mesh.Positions.Add(signs[i + 2] * hz);
            }
            mesh.Indices.Add([start, start + 1, start + 2, start, start + 2, start + 3]);
        }

        /// <summary>
        /// Rotation (yaw about Y, then pitch about X, then roll about Z; in degrees)
        /// plus translation, as a column-major Matrix4 (translation in m[12..14]).
        /// </summary>
        public static Matrix4 Pose(double yawDeg, double pitchDeg, double rollDeg, float x, float y, float z)
        {
            double a = yawDeg * Math.PI / 180, b = pitchDeg * Math.PI / 180, c = rollDeg * Math.PI / 180;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            double cb = Math.Cos(b), sb = Math.Sin(b);
            double cc = Math.Cos(c), sc = Math.Sin(c);

            // Rotates one vector: roll (Z), then pitch (X), then yaw (Y)  =>  R = Ry * Rx * Rz
            double[] Rotate(double vx, double vy, double vz)
            {
                double x1 = vx * cc - vy * sc, y1 = vx * sc + vy * cc, z1 = vz;  // about Z
                double y2 = y1 * cb - z1 * sb, z2 = y1 * sb + z1 * cb;           // about X
                double x3 = x1 * ca + z2 * sa, z3 = -x1 * sa + z2 * ca;          // about Y
                return new[] { x3, y2, z3 };
            }

            var m = new Matrix4();
            // The columns of R are the rotated unit axes
            foreach (var col in new[] { Rotate(1, 0, 0), Rotate(0, 1, 0), Rotate(0, 0, 1) })
            {
                m.M.Add((float)col[0]);
                m.M.Add((float)col[1]);
                m.M.Add((float)col[2]);
                m.M.Add(0f);
            }
            m.M.Add(x); m.M.Add(y); m.M.Add(z); m.M.Add(1f); // translation column
            return m;
        }
        
        /// <summary>
        /// Unit cube: 24 vertices (4 per face, flat shading), counter-clockwise triangles.</summary>
        private static Mesh Cube()
        {
            // Per face: normal (3), u axis (3), v axis (3), with u x v == normal
            float[][] faces =
            [
                [1, 0, 0,    0, 1, 0,   0, 0, 1],
                [-1, 0, 0,   0, 0, 1,   0, 1, 0],
                [ 0, 1, 0,   0, 0, 1,   1, 0, 0],
                [ 0,-1, 0,   1, 0, 0,   0, 0, 1],
                [ 0, 0, 1,   1, 0, 0,   0, 1, 0],
                [ 0, 0,-1,   0, 1, 0,   1, 0, 0],
            ];
            float[] su = { -1, 1, 1, -1 };
            float[] sv = { -1, -1, 1, 1 };

            var mesh = new Mesh();
            foreach (float[] f in faces)
            {
                uint b = (uint)(mesh.Positions.Count / 3);
                for (int k = 0; k < 4; k++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        mesh.Positions.Add(0.5f * (f[c] + su[k] * f[3 + c] + sv[k] * f[6 + c]));
                    }
                }
                mesh.Indices.AddRange(new uint[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            return mesh;
        }

        /// <summary>Packs 0..1 RGBA into 0xRRGGBBAA (one uint per vertex).</summary>
        public static uint Rgba(float r, float g, float b, float a = 1f)
        {
            return (ToByte(r) << 24) | (ToByte(g) << 16) | (ToByte(b) << 8) | ToByte(a);
        }

        private static uint ToByte(float v)
        {
            int i = (int)Math.Round(v * 255);
            return (uint)(i < 0 ? 0 : (i > 255 ? 255 : i));
        }

        /// <summary>Uniform scale + translation, OpenGL memory order (translation in m[12..14]).</summary>
        public static Matrix4 ScaleTranslation(float s, float x, float y, float z)
        {
            return new Matrix4 { M = { s, 0, 0, 0, 0, s, 0, 0, 0, 0, s, 0, x, y, z, 1 } };
        }
    }
}