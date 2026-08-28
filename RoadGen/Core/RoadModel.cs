using System;
using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>A single control point along the road. Position is in Hammer units.</summary>
public sealed class RoadPoint
{
    public Vec3 Position;
    public double Width;
    public double BankDegrees;

    public RoadPoint(Vec3 position, double width, double bankDegrees)
    {
        Position = position;
        Width = width;
        BankDegrees = bankDegrees;
    }

    public RoadPoint Clone() => new RoadPoint(Position, Width, BankDegrees);
}

/// <summary>Road-wide generation settings.</summary>
public sealed class RoadSettings
{
    /// <summary>Displacement power (2, 3 or 4).</summary>
    public int Power = 3;

    /// <summary>Material applied to every generated face.</summary>
    public string Material = "CONCRETE/CONCRETEFLOOR005A";

    /// <summary>Brush depth below the road surface, in units.</summary>
    public double Thickness = 64;

    /// <summary>Draw/export the left side wall.</summary>
    public bool SolidLeft = true;

    /// <summary>Draw/export the right side wall.</summary>
    public bool SolidRight = true;

    /// <summary>Draw/export the bottom face.</summary>
    public bool SolidBottom = false;

    /// <summary>Target length of each generated displacement segment. Smaller = smoother,
    /// larger = fewer displacements.</summary>
    public double SegmentLength = 256;

    /// <summary>Texture scale written to the displacement face.</summary>
    public double TextureScale = 0.25;

    /// <summary>Lightmap scale for every face.</summary>
    public int LightmapScale = 16;

    /// <summary>Editor grid snap in units. 0 disables snapping.</summary>
    public double Snap = 64;

    // Per-editor increment/decrement interval settings (editor UI only, not part
    // of the exported VMF). UseGrid = "Grid" box checked (increment follows the
    // grid snap); otherwise the custom value below is used.
    public bool IncUseGridX = true;
    public bool IncUseGridY = true;
    public bool IncUseGridZ = true;
    public bool IncUseGridWidth = true;
    public bool IncUseGridBank = false;
    public double IncCustomX = 64;
    public double IncCustomY = 64;
    public double IncCustomZ = 64;
    public double IncCustomWidth = 64;
    public double IncCustomBank = 4;

    /// <summary>Snap a value to the configured grid.</summary>
    public double Snapped(double value)
    {
        if (Snap <= 0)
        {
            return value;
        }

        return Math.Round(value / Snap, MidpointRounding.AwayFromZero) * Snap;
    }
}

/// <summary>The editable document: a list of control points plus settings.</summary>
public sealed class RoadDocument
{
    public readonly List<RoadPoint> Points = new List<RoadPoint>();
    public readonly RoadSettings Settings = new RoadSettings();

    public event EventHandler Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
