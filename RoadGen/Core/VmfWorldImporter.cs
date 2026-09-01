using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RoadGen.Core;

/// <summary>Imports an entire VMF's world geometry — convex brushes and displacement
/// surfaces — as a reference layout for the viewports. Deliberately separate from
/// <see cref="VmfImporter"/> (which is specialised for importing roads).</summary>
public static class VmfWorldImporter
{
    private sealed class SidePlane
    {
        public Vec3 Normal;
        public double D;
        public string Material = "";
    }

    private const double Epsilon = 0.5;

    /// <summary>Parses the VMF text and extracts every solid in the "world" block as
    /// either a convex brush or a displacement surface.</summary>
    public static VmfWorld ImportWorld(string vmfText)
    {
        VmfBlock root = VmfParser.Parse(vmfText);
        VmfBlock world = root.Children.FirstOrDefault(b => b.Name.Equals("world", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No 'world' block found in the VMF.");

        var result = new VmfWorld();

        foreach (VmfBlock solid in world.Children)
        {
            if (!solid.Name.Equals("solid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            List<VmfBlock> sides = solid.Children
                .Where(b => b.Name.Equals("side", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sides.Count == 0)
            {
                continue;
            }

            // A solid with one or more dispinfo sides is displacement geometry;
            // its remaining sides are hidden (NODRAW). Otherwise it is a plain
            // convex brush.
            bool hasDisplacement = false;
            foreach (VmfBlock side in sides)
            {
                if (!side.Children.Any(b => b.Name.Equals("dispinfo", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                hasDisplacement = true;
                VmfDisplacement displacement = ReconstructDisplacement(side);
                if (displacement != null)
                {
                    result.Displacements.Add(displacement);
                }
            }

            if (hasDisplacement)
            {
                continue;
            }

            VmfBrush brush = ReconstructBrush(sides);
            if (brush != null)
            {
                result.Brushes.Add(brush);
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- brushes

    /// <summary>Reconstructs a convex brush by clipping the intersection of its side
    /// half-spaces: enumerate all 3-plane intersection points, keep those inside every
    /// half-space, then gather and angularly sort each face's vertices.</summary>
    private static VmfBrush ReconstructBrush(List<VmfBlock> sides)
    {
        var planes = new List<SidePlane>();
        Vec3 centroid = Vec3.Zero;
        int pointCount = 0;

        foreach (VmfBlock side in sides)
        {
            if (!side.Properties.TryGetValue("plane", out string planeStr))
            {
                continue;
            }

            Vec3[] corners = ParsePlane(planeStr);
            Vec3 a = corners[0], b = corners[1], c = corners[2];

            Vec3 normal = Vec3.Cross(b - a, c - a);
            if (normal.LengthSq < 1e-12)
            {
                continue; // degenerate plane
            }

            normal = normal.Normalized();
            planes.Add(new SidePlane
            {
                Normal = normal,
                D = Vec3.Dot(normal, a),
                Material = side.Properties.TryGetValue("material", out string mat) ? mat : ""
            });

            centroid += a + b + c;
            pointCount += 3;
        }

        if (planes.Count < 4)
        {
            return null; // cannot form a closed solid
        }

        centroid = centroid / pointCount;

        // Orient every plane outward (away from the brush centroid) so the import
        // doesn't depend on Hammer's winding convention. Interior = dot(N, p) <= D.
        foreach (SidePlane plane in planes)
        {
            if (Vec3.Dot(plane.Normal, centroid) - plane.D > 0)
            {
                plane.Normal = -plane.Normal;
                plane.D = -plane.D;
            }
        }

        var vertices = new List<Vec3>();
        for (int i = 0; i < planes.Count; i++)
        {
            for (int j = i + 1; j < planes.Count; j++)
            {
                for (int k = j + 1; k < planes.Count; k++)
                {
                    if (TryPlaneIntersection(planes[i], planes[j], planes[k], out Vec3 p)
                        && IsInside(p, planes))
                    {
                        AddDistinct(vertices, p);
                    }
                }
            }
        }

        if (vertices.Count < 4)
        {
            return null;
        }

        var brush = new VmfBrush();
        foreach (SidePlane plane in planes)
        {
            var faceVertices = new List<Vec3>();
            foreach (Vec3 v in vertices)
            {
                if (Math.Abs(Vec3.Dot(plane.Normal, v) - plane.D) < Epsilon)
                {
                    faceVertices.Add(v);
                }
            }

            if (faceVertices.Count < 3)
            {
                continue;
            }

            SortPolygon(faceVertices, plane.Normal);
            VmfFace face = new VmfFace
            {
                Material = plane.Material,
                Normal = plane.Normal
            };
            face.Vertices.AddRange(faceVertices);
            brush.Faces.Add(face);
        }

        return brush.Faces.Count >= 4 ? brush : null;
    }

    private static bool TryPlaneIntersection(SidePlane p1, SidePlane p2, SidePlane p3, out Vec3 point)
    {
        Vec3 n1 = p1.Normal, n2 = p2.Normal, n3 = p3.Normal;

        double det = Vec3.Dot(n1, Vec3.Cross(n2, n3));
        if (Math.Abs(det) < 1e-9)
        {
            point = Vec3.Zero;
            return false;
        }

        // Solve n_i . x = d_i for the intersection of the three planes.
        point = (p1.D * Vec3.Cross(n2, n3)
               + p2.D * Vec3.Cross(n3, n1)
               + p3.D * Vec3.Cross(n1, n2)) / det;
        return true;
    }

    private static bool IsInside(Vec3 p, List<SidePlane> planes)
    {
        foreach (SidePlane plane in planes)
        {
            if (Vec3.Dot(plane.Normal, p) - plane.D > Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddDistinct(List<Vec3> list, Vec3 p)
    {
        double e2 = Epsilon * Epsilon;
        foreach (Vec3 v in list)
        {
            if ((v - p).LengthSq < e2)
            {
                return;
            }
        }

        list.Add(p);
    }

    /// <summary>Orders a set of coplanar points into a simple convex polygon by angle
    /// around the polygon's centre, using an arbitrary in-plane reference axis.</summary>
    private static void SortPolygon(List<Vec3> vertices, Vec3 normal)
    {
        Vec3 center = Vec3.Zero;
        foreach (Vec3 v in vertices)
        {
            center += v;
        }

        center = center / vertices.Count;

        Vec3 reference = Math.Abs(normal.Z) < 0.9
            ? Vec3.Cross(normal, Vec3.UnitZ).Normalized()
            : Vec3.Cross(normal, Vec3.UnitX).Normalized();
        Vec3 up = Vec3.Cross(normal, reference).Normalized();

        vertices.Sort((x, y) =>
        {
            double ax = Vec3.Dot(x - center, reference);
            double ay = Vec3.Dot(x - center, up);
            double bx = Vec3.Dot(y - center, reference);
            double by = Vec3.Dot(y - center, up);
            return Math.Atan2(ay, ax).CompareTo(Math.Atan2(by, bx));
        });
    }

    // ---------------------------------------------------------------- displacements

    private static VmfDisplacement ReconstructDisplacement(VmfBlock side)
    {
        VmfBlock disp = side.Children.FirstOrDefault(b =>
            b.Name.Equals("dispinfo", StringComparison.OrdinalIgnoreCase));

        if (disp == null
            || !side.Properties.TryGetValue("plane", out string planeStr)
            || !disp.Properties.TryGetValue("power", out string powerStr))
        {
            return null;
        }

        Vec3[] corners = ParsePlane(planeStr);
        Vec3 p0 = corners[0], p1 = corners[1], p2 = corners[2];

        // The side's three plane points are the first three vertices of the face
        // winding [p0, p1, p2, p3]. dispinfo's startposition is the displacement's
        // grid origin (vertex [0,0]); it may be ANY of the four corners — it is not
        // necessarily one of the three stored here.
        Vec3 start;
        if (disp.Properties.TryGetValue("startposition", out string startStr))
        {
            List<double> startNums = ParseNumbers(startStr);
            if (startNums.Count < 3)
            {
                return null;
            }

            start = new Vec3(startNums[0], startNums[1], startNums[2]);
        }
        else
        {
            start = p0; // fall back to the first plane point when absent
        }

        // Complete the quad: p1 and p3 are opposite corners (parallelogram rule).
        Vec3 p3 = p0 + p2 - p1;

        // Locate start in the winding [p0, p1, p2, p3] and derive the displacement
        // axes: the u axis (rows) runs to the next corner after start, the v axis
        // (columns) to the previous corner.
        Vec3[] winding = { p0, p1, p2, p3 };
        int startIndex = -1;
        for (int i = 0; i < 4; i++)
        {
            if ((winding[i] - start).LengthSq < 1e-6)
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
        {
            start = p0;
            startIndex = 0; // startposition didn't match a corner: fall back to p0
        }

        Vec3 u = winding[(startIndex + 1) % 4] - start; // row direction
        Vec3 v = winding[(startIndex + 3) % 4] - start; // column direction

        int power = int.Parse(powerStr, CultureInfo.InvariantCulture);
        int res = 1 << power;
        int n = res + 1;

        Vec3[,] normals = ParseVectorGrid(disp, "normals", n);
        double[,] distances = ParseScalarGrid(disp, "distances", n);

        // Subdivided (subdiv >= 1) displacements store their shape in "offsets":
        // world-space displacement vectors applied on top of the base surface.
        // (For those, normals/distances are typically all zero.)
        Vec3[,] offsets = ParseVectorGrid(disp, "offsets", n);

        // Rebuild each surface vertex: base + normal * distance + offset. Rows run
        // along u, columns along v, so adjacent patches share identical edge
        // vertices and stay seamed together.
        Vec3[,] grid = new Vec3[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int col = 0; col < n; col++)
            {
                Vec3 basePoint = start + u * (r / (double)res) + v * (col / (double)res);
                grid[r, col] = basePoint + normals[r, col] * distances[r, col] + offsets[r, col];
            }
        }

        return new VmfDisplacement
        {
            Grid = grid,
            Power = power,
            Material = side.Properties.TryGetValue("material", out string mat) ? mat : ""
        };
    }

    // ---------------------------------------------------------------- parsing

    private static Vec3[] ParsePlane(string s)
    {
        List<double> nums = ParseNumbers(s);
        if (nums.Count < 9)
        {
            throw new InvalidOperationException("Invalid plane string: " + s);
        }

        return new[]
        {
            new Vec3(nums[0], nums[1], nums[2]),
            new Vec3(nums[3], nums[4], nums[5]),
            new Vec3(nums[6], nums[7], nums[8])
        };
    }

    private static Vec3[,] ParseVectorGrid(VmfBlock disp, string blockName, int n)
    {
        var result = new Vec3[n, n];
        VmfBlock block = disp.Children.FirstOrDefault(b =>
            b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
        if (block == null)
        {
            return result;
        }

        for (int r = 0; r < n; r++)
        {
            if (block.Properties.TryGetValue("row" + r, out string line))
            {
                List<double> nums = ParseNumbers(line);
                for (int c = 0; c < n && c * 3 + 2 < nums.Count; c++)
                {
                    result[r, c] = new Vec3(nums[c * 3], nums[c * 3 + 1], nums[c * 3 + 2]);
                }
            }
        }

        return result;
    }

    private static double[,] ParseScalarGrid(VmfBlock disp, string blockName, int n)
    {
        var result = new double[n, n];
        VmfBlock block = disp.Children.FirstOrDefault(b =>
            b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
        if (block == null)
        {
            return result;
        }

        for (int r = 0; r < n; r++)
        {
            if (block.Properties.TryGetValue("row" + r, out string line))
            {
                List<double> nums = ParseNumbers(line);
                for (int c = 0; c < n && c < nums.Count; c++)
                {
                    result[r, c] = nums[c];
                }
            }
        }

        return result;
    }

    private static List<double> ParseNumbers(string s)
    {
        var result = new List<double>();
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s)
        {
            if (char.IsWhiteSpace(ch) || ch == '(' || ch == ')' || ch == '[' || ch == ']' || ch == ',')
            {
                if (sb.Length > 0 && double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    result.Add(v);
                }

                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }

        if (sb.Length > 0 && double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double last))
        {
            result.Add(last);
        }

        return result;
    }
}
