using System;
using System.Globalization;

namespace RoadGen.Core;

/// <summary>VMF text helpers: file header/footer and invariant number formatting.</summary>
public static class Vmf
{
    public const string Header =
        "versioninfo\r\n" +
        "{\r\n" +
        "\t\"editorversion\" \"400\"\r\n" +
        "\t\"editorbuild\" \"3576\"\r\n" +
        "\t\"mapversion\" \"1\"\r\n" +
        "\t\"formatversion\" \"100\"\r\n" +
        "\t\"prefab\" \"0\"\r\n" +
        "}\r\n" +
        "visgroups\r\n" +
        "{\r\n" +
        "}\r\n" +
        "viewsettings\r\n" +
        "{\r\n" +
        "\t\"bSnapToGrid\" \"1\"\r\n" +
        "\t\"bShowGrid\" \"1\"\r\n" +
        "\t\"bShowLogicalGrid\" \"0\"\r\n" +
        "\t\"nGridSpacing\" \"64\"\r\n" +
        "\t\"bShow3DGrid\" \"0\"\r\n" +
        "}\r\n" +
        "world\r\n" +
        "{\r\n" +
        "\t\"id\" \"1\"\r\n" +
        "\t\"mapversion\" \"1\"\r\n" +
        "\t\"classname\" \"worldspawn\"\r\n" +
        "\t\"skyname\" \"sky_day01_01\"\r\n" +
        "\t\"maxpropscreenwidth\" \"-1\"\r\n" +
        "\t\"detailvbsp\" \"detail.vbsp\"\r\n" +
        "\t\"detailmaterial\" \"detail/detailsprites\"\r\n";

    public const string Footer =
        "}\r\n" +
        "cameras\r\n" +
        "{\r\n" +
        "\t\"activecamera\" \"-1\"\r\n" +
        "}\r\n" +
        "cordon\r\n" +
        "{\r\n" +
        "\t\"mins\" \"(-1024 -1024 -1024)\"\r\n" +
        "\t\"maxs\" \"(1024 1024 1024)\"\r\n" +
        "\t\"active\" \"0\"\r\n" +
        "}\r\n";

    /// <summary>Format a number with invariant culture (never a decimal comma).</summary>
    public static string Num(double value, int maxDecimals = 6)
    {
        double r = Math.Round(value, maxDecimals, MidpointRounding.AwayFromZero);
        if (r == 0.0)
        {
            r = 0.0; // normalize negative zero
        }

        return r.ToString("0.#################", CultureInfo.InvariantCulture);
    }

    public static string Vec(Vec3 v, int maxDecimals = 6) =>
        $"{Num(v.X, maxDecimals)} {Num(v.Y, maxDecimals)} {Num(v.Z, maxDecimals)}";

    public static string Point(Vec3 v, int maxDecimals = 6) => $"({Vec(v, maxDecimals)})";
}
