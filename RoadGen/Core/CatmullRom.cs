using System;

namespace RoadGen.Core;

/// <summary>Uniform Catmull-Rom spline evaluation. The curve passes exactly through
/// every control point, which makes it natural for connecting road pieces end-to-end.</summary>
public static class CatmullRom
{
    /// <summary>Point on the segment from p1 to p2 at local parameter u in [0,1].</summary>
    public static Vec3 Position(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double u)
    {
        double u2 = u * u;
        double u3 = u2 * u;
        return 0.5 * (
            2.0 * p1
            + (-p0 + p2) * u
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * u2
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * u3);
    }

    /// <summary>Derivative with respect to the local parameter (not normalized).</summary>
    public static Vec3 Tangent(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double u)
    {
        double u2 = u * u;
        return 0.5 * (
            (-p0 + p2)
            + 2.0 * (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * u
            + 3.0 * (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * u2);
    }
}
