using System;

namespace RoadGen.Core;

/// <summary>A 3-dimensional vector using double precision for internal math.</summary>
public readonly struct Vec3 : IEquatable<Vec3>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vec3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Vec3 Zero = new Vec3(0, 0, 0);
    public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
    public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
    public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public double LengthSq => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSq);

    public Vec3 Normalized()
    {
        double len = Length;
        if (len < 1e-12)
        {
            return UnitZ;
        }

        return this / len;
    }

    public Vec3 Rounded(int decimals) => new Vec3(
        Math.Round(X, decimals, MidpointRounding.AwayFromZero),
        Math.Round(Y, decimals, MidpointRounding.AwayFromZero),
        Math.Round(Z, decimals, MidpointRounding.AwayFromZero));

    public static Vec3 Min(Vec3 a, Vec3 b) => new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Vec3 Max(Vec3 a, Vec3 b) => new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override string ToString() => $"({X}, {Y}, {Z})";
}
