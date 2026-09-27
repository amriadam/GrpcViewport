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

        /// <summary>Unit cube: 24 vertices (4 per face, flat shading), counter-clockwise triangles.</summary>
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