using Google.Protobuf.Collections;

namespace GrpcViewport.Server;

/// <summary>
/// Returns an error message with the exact field path, or null if the request is valid.
/// </summary>
public static class GeometryValidator
{
    public static string? Validate(AddGeometryRequest req)
    {
        if (req.Transform is not null 
         && CheckMatrix(req.Transform, "transform") is { } te)
        {
            return te;
        }

        switch (req.GeometryCase)
        {
            case AddGeometryRequest.GeometryOneofCase.Polylines:
                if (req.Polylines.Polylines.Count == 0)
                {
                    return "polylines: batch is empty";
                }

                for (int i = 0; i < req.Polylines.Polylines.Count; i++)
                {
                    var p = req.Polylines.Polylines[i];
                    var path = $"polylines[{i}]";
                    if ((CheckPositions(p.Positions, path, 2) ?? CheckColors(p.Colors, p.Positions.Count / 3, path)) is { } e)
                    {
                        return e;
                    }
                }

                return null;

            case AddGeometryRequest.GeometryOneofCase.PointClouds:
                if (req.PointClouds.Clouds.Count == 0)
                {
                    return "point_clouds: batch is empty";
                }

                if (!float.IsFinite(req.PointClouds.PointSize) || req.PointClouds.PointSize < 0)
                {
                    return "point_clouds.point_size must be a finite value >= 0";
                }

                for (int i = 0; i < req.PointClouds.Clouds.Count; i++)
                {
                    var c = req.PointClouds.Clouds[i];
                    var path = $"clouds[{i}]";
                    if ((CheckPositions(c.Positions, path, 1) ?? CheckColors(c.Colors, c.Positions.Count / 3, path)) is { } e)
                    {
                        return e;
                    }
                }

                return null;

            case AddGeometryRequest.GeometryOneofCase.Meshes:
                if (req.Meshes.Meshes.Count == 0)
                {
                    return "meshes: batch is empty";
                }

                for (int i = 0; i < req.Meshes.Meshes.Count; i++)
                {
                    if (CheckMesh(req.Meshes.Meshes[i], $"meshes[{i}]") is { } e) return e;
                }
                return null;

            default:
                return "geometry is required (polylines, point_clouds or meshes)";
        }
    }

    private static string? CheckPositions(RepeatedField<float> positions, string path, int minVertices)
    {
        if (positions.Count % 3 != 0)
        {
            return $"{path}.positions length {positions.Count} is not a multiple of 3";
        }

        if (positions.Count / 3 < minVertices)
        {
            return $"{path} needs at least {minVertices} vertices";
        }

        for (int i = 0; i < positions.Count; i++)
        {
            if (!float.IsFinite(positions[i]))
            {
                return $"{path}.positions[{i}] is not finite";
            }
        }
        return null;
    }

    private static string? CheckColors(RepeatedField<uint> colors, int vertexCount, string path)
    {
        return colors.Count != 0 && colors.Count != vertexCount
             ? $"{path}.colors has {colors.Count} entries, expected 0 or {vertexCount} (one per vertex)"
             : null;
    }

    private static string? CheckMatrix(Matrix4 m, string path)
    {
        if (m.M.Count != 16)
        {
            return $"{path} must have 16 floats, got {m.M.Count}";
        }

        foreach (var v in m.M)
        {
            if (!float.IsFinite(v))
            {
                return $"{path} contains non-finite values";
            }
        }
        return null;
    }

    private static string? CheckMesh(Mesh m, string path)
    {
        if (CheckPositions(m.Positions, path, 3) is { } pe)
        {
            return pe;
        }

        var vertexCount = m.Positions.Count / 3;
        if (m.Indices.Count == 0)
        {
            return $"{path}.indices is empty";
        }

        if (m.Indices.Count % 3 != 0)
        {
            return $"{path}.indices length {m.Indices.Count} is not a multiple of 3";
        }

        for (int i = 0; i < m.Indices.Count; i++)
        {
            if (m.Indices[i] >= vertexCount)
            {
                return $"{path}.indices[{i}] = {m.Indices[i]} is out of range (vertex count {vertexCount})";
            }
        }
        if (m.Normals.Count != 0 && m.Normals.Count != m.Positions.Count)
        {
            return $"{path}.normals has {m.Normals.Count} floats, expected 0 or {m.Positions.Count}";
        }

        if (CheckColors(m.Colors, vertexCount, path) is { } ce)
        {
            return ce;
        }

        if (m.Transform is not null && CheckMatrix(m.Transform, $"{path}.transform") is { } te)
        {
            return te;
        }

        for (int i = 0; i < m.Instances.Count; i++)
        {
            if (CheckMatrix(m.Instances[i], $"{path}.instances[{i}]") is { } ie) return ie;
        }
        return null;
    }
}