using System;
using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>A single cordon volume, mirroring Valve's classic Hammer cordon (one editable
/// box; the 2013 "multiple cordons" list is a later extension RoadGen does not need).
/// While <see cref="Enabled"/> is set (and a real box exists), anything whose bounds do NOT
/// intersect <see cref="Mins"/>..<see cref="Maxs"/> is hidden in the views and excluded from
/// the VMF export — exactly Hammer's "only objects that intersect the cordon are rendered /
/// saved" rule. Toggling <see cref="Enabled"/> off disables all culling.</summary>
public sealed class Cordon
{
    public Cordon()
    {
        // A box always exists so there is something to grab and move from the start: a
        // 5,000 x 5,000 x 5,000 unit volume centred on the origin. It is repositioned by
        // dragging inside it in the 2D views (or corner handles to resize); "Fit to map"
        // re-seeds it around the imported layout.
        Mins = new Vec3(-2500, -2500, -2500);
        Maxs = new Vec3(2500, 2500, 2500);
    }

    /// <summary>Whether cordoning (culling) is active. When false nothing is culled and the
    /// box is only drawn while the cordon tool is being edited.</summary>
    public bool Enabled;

    public Vec3 Mins;
    public Vec3 Maxs;

    /// <summary>True once the box has any extent (mins != maxs on at least one axis). A zero
    /// box is treated as "not defined yet", so toggling on never hides the whole world before
    /// the user has drawn a box.</summary>
    public bool HasBounds => Mins.X != Maxs.X || Mins.Y != Maxs.Y || Mins.Z != Maxs.Z;

    /// <summary>True when cordoning is active AND a real box is defined (the only state in
    /// which geometry is actually culled).</summary>
    public bool Active => Enabled && HasBounds;

    /// <summary>Raised whenever <see cref="Enabled"/> or the box changes. The viewports and
    /// the cordon read-out subscribe to this so they repaint and re-label immediately.</summary>
    public event Action Changed;

    /// <summary>Updates the enabled flag and/or the box, normalizing mins <= maxs, and raises
    /// <see cref="Changed"/> only when something actually changed.</summary>
    public void Set(bool enabled, Vec3 mins, Vec3 maxs)
    {
        Vec3 nMin = new Vec3(
            Math.Min(mins.X, maxs.X),
            Math.Min(mins.Y, maxs.Y),
            Math.Min(mins.Z, maxs.Z));
        Vec3 nMax = new Vec3(
            Math.Max(mins.X, maxs.X),
            Math.Max(mins.Y, maxs.Y),
            Math.Max(mins.Z, maxs.Z));

        bool changed = enabled != Enabled
            || nMin.X != Mins.X || nMin.Y != Mins.Y || nMin.Z != Mins.Z
            || nMax.X != Maxs.X || nMax.Y != Maxs.Y || nMax.Z != Maxs.Z;

        Enabled = enabled;
        Mins = nMin;
        Maxs = nMax;

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>True when an object whose bounds are <paramref name="objMin"/>..
    /// <paramref name="objMax"/> must be hidden because it is outside the active cordon.
    /// Matches Hammer: an object is kept if it INTERSECTS the cordon (touching counts).</summary>
    public bool Culls(Vec3 objMin, Vec3 objMax)
    {
        return Active && !Intersects(Mins, Maxs, objMin, objMax);
    }

    /// <summary>Standard inclusive AABB overlap test.</summary>
    public static bool Intersects(Vec3 aMin, Vec3 aMax, Vec3 bMin, Vec3 bMax)
    {
        return aMin.X <= bMax.X && aMax.X >= bMin.X
            && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y
            && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;
    }

    /// <summary>Union AABB over a set of points. When <paramref name="points"/> is empty the
    /// returned box is a zero box at the origin.</summary>
    public static void ComputeBounds(IEnumerable<Vec3> points, out Vec3 mins, out Vec3 maxs)
    {
        bool any = false;
        mins = Vec3.Zero;
        maxs = Vec3.Zero;
        foreach (Vec3 p in points)
        {
            if (!any)
            {
                mins = p;
                maxs = p;
                any = true;
            }
            else
            {
                mins = Vec3.Min(mins, p);
                maxs = Vec3.Max(maxs, p);
            }
        }
    }

    /// <summary>Union AABB of a world (brushes + displacements). Returns a zero box for an
    /// empty world.</summary>
    public static void ComputeWorldBounds(VmfWorld world, out Vec3 mins, out Vec3 maxs)
    {
        bool any = false;
        Vec3 mn = Vec3.Zero;
        Vec3 mx = Vec3.Zero;

        void Include(Vec3 p)
        {
            if (!any)
            {
                mn = p;
                mx = p;
                any = true;
            }
            else
            {
                mn = Vec3.Min(mn, p);
                mx = Vec3.Max(mx, p);
            }
        }

        if (world != null)
        {
            foreach (VmfBrush brush in world.Brushes)
            {
                foreach (VmfFace face in brush.Faces)
                {
                    foreach (Vec3 v in face.Vertices)
                    {
                        Include(v);
                    }
                }
            }

            foreach (VmfDisplacement displacement in world.Displacements)
            {
                int n = displacement.Grid.GetLength(0);
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        Include(displacement.Grid[r, c]);
                    }
                }
            }
        }

        mins = mn;
        maxs = mx;
    }
}
