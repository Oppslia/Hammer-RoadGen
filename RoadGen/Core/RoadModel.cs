using System;
using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>A single control point along the road. Position is in Hammer units.</summary>
public sealed class RoadPoint
{
    public Vec3 Position;
    public double Width;
    public double BankDegrees;

    /// <summary>Brush depth below this control point, in units. Interpolated
    /// between points so tracks with different thickness join smoothly.</summary>
    public double Thickness = 64;

    public RoadPoint(Vec3 position, double width, double bankDegrees, double thickness = 64)
    {
        Position = position;
        Width = width;
        BankDegrees = bankDegrees;
        Thickness = thickness;
    }

    public RoadPoint Clone() => new RoadPoint(Position, Width, BankDegrees, Thickness);
}

/// <summary>Road-wide generation settings.</summary>
public sealed class RoadSettings
{
    /// <summary>Displacement power (2, 3 or 4).</summary>
    public int Power = 3;

    /// <summary>Material applied to every generated face.</summary>
    public string Material = "CONCRETE/CONCRETEFLOOR005A";

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

    /// <summary>Deep copy of these settings (every field is a value type or string).</summary>
    public RoadSettings Clone()
    {
        RoadSettings copy = new RoadSettings
        {
            Power = Power,
            Material = Material,
            SolidLeft = SolidLeft,
            SolidRight = SolidRight,
            SolidBottom = SolidBottom,
            SegmentLength = SegmentLength,
            TextureScale = TextureScale,
            LightmapScale = LightmapScale,
            Snap = Snap,
            IncUseGridX = IncUseGridX,
            IncUseGridY = IncUseGridY,
            IncUseGridZ = IncUseGridZ,
            IncUseGridWidth = IncUseGridWidth,
            IncUseGridBank = IncUseGridBank,
            IncCustomX = IncCustomX,
            IncCustomY = IncCustomY,
            IncCustomZ = IncCustomZ,
            IncCustomWidth = IncCustomWidth,
            IncCustomBank = IncCustomBank
        };

        return copy;
    }
}

/// <summary>One named road (a "layer" in the UI): its control points plus its own
/// road settings.</summary>
public sealed class Track
{
    public string Name = "Track";
    public readonly List<RoadPoint> Points = new List<RoadPoint>();
    public RoadSettings Settings = new RoadSettings();

    /// <summary>When true, this track's endpoints weld/join with other tracks that
    /// share a point. Duplicated tracks default this to false so a copy starts
    /// separate instead of auto-merging with its source.</summary>
    public bool EnableJoining = true;

    public Track() { }

    public Track(string name)
    {
        Name = name;
    }

    public Track Clone()
    {
        Track copy = new Track(Name)
        {
            EnableJoining = EnableJoining
        };
        copy.Settings = Settings.Clone();
        foreach (RoadPoint point in Points)
        {
            copy.Points.Add(point.Clone());
        }

        return copy;
    }
}

/// <summary>The editable document: an ordered list of tracks (roads) plus the
/// index of the track currently being edited.</summary>
public sealed class RoadDocument
{
    public readonly List<Track> Tracks = new List<Track>();
    public int ActiveTrackIndex = 0;

    public Track ActiveTrack => Tracks.Count > 0 ? Tracks[ActiveTrackIndex] : null;

    // Convenience accessors pointing at the active track. They keep the bulk of
    // the existing single-track code working unchanged during the migration.
    public List<RoadPoint> Points => ActiveTrack?.Points;
    public RoadSettings Settings => ActiveTrack?.Settings;

    public RoadDocument()
    {
        Tracks.Add(new Track("Track 1"));
    }

    public event EventHandler Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Points closer than this (in units) count as the same shared point.</summary>
    public const double WeldTolerance = 0.001;

    /// <summary>True when two positions are close enough to be the same shared
    /// point (within WeldTolerance).</summary>
    public static bool PositionsMatch(Vec3 first, Vec3 second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        return (dx * dx + dy * dy + dz * dz) <= (WeldTolerance * WeldTolerance);
    }

    /// <summary>Move a point and, when another track has a point at the same old
    /// position, move that welded point by the same delta. Tracks stay separate;
    /// this only keeps shared junctions connected while editing.</summary>
    public void MovePointWelded(Track track, int pointIndex, Vec3 newPosition, Vec3 oldPosition)
    {
        if (track == null || pointIndex < 0 || pointIndex >= track.Points.Count)
        {
            return;
        }

        track.Points[pointIndex].Position = newPosition;

        Vec3 delta = newPosition - oldPosition;
        if (delta.X == 0 && delta.Y == 0 && delta.Z == 0)
        {
            return;
        }

        if (!track.EnableJoining)
        {
            return;
        }

        foreach (Track otherTrack in Tracks)
        {
            if (ReferenceEquals(otherTrack, track) || !otherTrack.EnableJoining)
            {
                continue;
            }

            foreach (RoadPoint otherPoint in otherTrack.Points)
            {
                if (PositionsMatch(otherPoint.Position, oldPosition))
                {
                    otherPoint.Position = otherPoint.Position + delta;
                }
            }
        }
    }

    /// <summary>Assemble the document's tracks into chains: tracks whose endpoints
    /// share a position are joined into one continuous road. Each chain uses the
    /// settings of its first (topmost) track.</summary>
    public List<RoadChain> BuildChains()
    {
        List<RoadChain> chains = new List<RoadChain>();

        foreach (Track track in Tracks)
        {
            if (track.Points.Count == 0)
            {
                continue;
            }

            RoadChain chain = new RoadChain { Settings = track.Settings, Joinable = track.EnableJoining };
            chain.Points.AddRange(track.Points);
            chain.Spans.Add(new ChainSpan { Track = track, StartPoint = 0, EndPoint = track.Points.Count });
            chains.Add(chain);
        }

        bool anyMerged = true;
        while (anyMerged)
        {
            anyMerged = false;
            for (int leftIndex = 0; leftIndex < chains.Count && !anyMerged; leftIndex++)
            {
                for (int rightIndex = leftIndex + 1; rightIndex < chains.Count; rightIndex++)
                {
                    RoadChain merged = MergeChains(chains[leftIndex], chains[rightIndex]);
                    if (merged != null)
                    {
                        chains[leftIndex] = merged;
                        chains.RemoveAt(rightIndex);
                        anyMerged = true;
                        break;
                    }
                }
            }
        }

        return chains;
    }

    private static RoadChain MergeChains(RoadChain first, RoadChain second)
    {
        if (!first.Joinable || !second.Joinable)
        {
            return null;
        }

        if (first.Points.Count == 0 || second.Points.Count == 0)
        {
            return null;
        }

        Vec3 firstStart = first.Points[0].Position;
        Vec3 firstEnd = first.Points[first.Points.Count - 1].Position;
        Vec3 secondStart = second.Points[0].Position;
        Vec3 secondEnd = second.Points[second.Points.Count - 1].Position;

        RoadChain merged = new RoadChain { Settings = first.Settings };

        if (PositionsMatch(firstEnd, secondStart))
        {
            AppendChainRange(merged, first, 0, first.Points.Count, forward: true);
            AppendChainRange(merged, second, 1, second.Points.Count, forward: true);
            return merged;
        }

        if (PositionsMatch(firstEnd, secondEnd))
        {
            AppendChainRange(merged, first, 0, first.Points.Count, forward: true);
            AppendChainRange(merged, second, 0, second.Points.Count - 1, forward: false);
            return merged;
        }

        if (PositionsMatch(firstStart, secondEnd))
        {
            AppendChainRange(merged, second, 0, second.Points.Count, forward: true);
            AppendChainRange(merged, first, 1, first.Points.Count, forward: true);
            return merged;
        }

        if (PositionsMatch(firstStart, secondStart))
        {
            AppendChainRange(merged, second, 0, second.Points.Count, forward: false);
            AppendChainRange(merged, first, 1, first.Points.Count, forward: true);
            return merged;
        }

        return null;
    }

    private static void AppendChainRange(RoadChain chain, RoadChain source, int startIndex, int endIndex, bool forward)
    {
        if (startIndex >= endIndex)
        {
            return;
        }

        int chainStart = chain.Points.Count;

        if (forward)
        {
            for (int index = startIndex; index < endIndex; index++)
            {
                chain.Points.Add(source.Points[index]);
            }
        }
        else
        {
            for (int index = endIndex - 1; index >= startIndex; index--)
            {
                chain.Points.Add(source.Points[index]);
            }
        }

        foreach (ChainSpan span in source.Spans)
        {
            int overlapStart = Math.Max(span.StartPoint, startIndex);
            int overlapEnd = Math.Min(span.EndPoint, endIndex);
            if (overlapStart >= overlapEnd)
            {
                continue;
            }

            int newStart;
            int newEnd;
            if (forward)
            {
                newStart = chainStart + (overlapStart - startIndex);
                newEnd = chainStart + (overlapEnd - startIndex);
            }
            else
            {
                newStart = chainStart + (endIndex - overlapEnd);
                newEnd = chainStart + (endIndex - overlapStart);
            }

            chain.Spans.Add(new ChainSpan { Track = span.Track, StartPoint = newStart, EndPoint = newEnd });
        }

        // If this append skipped the source's shared boundary point (startIndex > 0
        // skips the leading point, endIndex < count skips the trailing point), the
        // segment from that shared point into this range belongs to this range's
        // track. Extend the first appended span back to include the shared point
        // (which already sits at chainStart - 1) so the boundary segment is colored
        // with the correct track instead of staying muted.
        bool skippedSharedPoint = startIndex > 0 || endIndex < source.Points.Count;
        if (skippedSharedPoint && chainStart > 0)
        {
            foreach (ChainSpan span in chain.Spans)
            {
                if (span.StartPoint == chainStart)
                {
                    span.StartPoint = chainStart - 1;
                    break;
                }
            }
        }
    }
}

/// <summary>A sequence of control points assembled from one or more tracks whose
/// endpoints share the same position (welded). Chains are what actually get drawn
/// and exported, so joined tracks flow as one continuous road.</summary>
public sealed class RoadChain
{
    public readonly List<RoadPoint> Points = new List<RoadPoint>();
    public readonly List<ChainSpan> Spans = new List<ChainSpan>();
    public RoadSettings Settings;

    /// <summary>False when the chain contains a track with joining disabled, which
    /// keeps it from being merged into another road.</summary>
    public bool Joinable = true;
}

/// <summary>The range of a chain's control points that came from one track. Used
/// to color the active track's portion separately from the muted tracks it is
/// welded to.</summary>
public sealed class ChainSpan
{
    public Track Track;
    public int StartPoint;
    public int EndPoint;
}
