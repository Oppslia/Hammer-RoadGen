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

    /// <summary>Whether point placement snaps to the grid. Kept separate from
    /// <see cref="Snap"/> so the grid interval is preserved when snapping is off.</summary>
    public bool SnapEnabled = true;

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
    public bool IncUseGridThickness = true;
    public double IncCustomThickness = 64;

    // Edge-feature editor increment/decrement interval settings, mirroring the road
    // point editor's Inc* fields above. Each feature value row (Offset, Width,
    // Bottom Z, Top Z, Bank) has its own "Grid" toggle and custom interval.
    public bool FeatureIncUseGridOffset = true;
    public bool FeatureIncUseGridWidth = true;
    public bool FeatureIncUseGridBottomZ = true;
    public bool FeatureIncUseGridTopZ = true;
    public bool FeatureIncUseGridBank = false;
    public double FeatureIncCustomOffset = 64;
    public double FeatureIncCustomWidth = 64;
    public double FeatureIncCustomBottomZ = 64;
    public double FeatureIncCustomTopZ = 64;
    public double FeatureIncCustomBank = 4;

    /// <summary>Snap a value to the configured grid.</summary>
    public double Snapped(double value)
    {
        if (!SnapEnabled || Snap <= 0)
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
            SnapEnabled = SnapEnabled,
            IncUseGridX = IncUseGridX,
            IncUseGridY = IncUseGridY,
            IncUseGridZ = IncUseGridZ,
            IncUseGridWidth = IncUseGridWidth,
            IncUseGridBank = IncUseGridBank,
            IncCustomX = IncCustomX,
            IncCustomY = IncCustomY,
            IncCustomZ = IncCustomZ,
            IncCustomWidth = IncCustomWidth,
            IncCustomBank = IncCustomBank,
            IncUseGridThickness = IncUseGridThickness,
            IncCustomThickness = IncCustomThickness,
            FeatureIncUseGridOffset = FeatureIncUseGridOffset,
            FeatureIncUseGridWidth = FeatureIncUseGridWidth,
            FeatureIncUseGridBottomZ = FeatureIncUseGridBottomZ,
            FeatureIncUseGridTopZ = FeatureIncUseGridTopZ,
            FeatureIncUseGridBank = FeatureIncUseGridBank,
            FeatureIncCustomOffset = FeatureIncCustomOffset,
            FeatureIncCustomWidth = FeatureIncCustomWidth,
            FeatureIncCustomBottomZ = FeatureIncCustomBottomZ,
            FeatureIncCustomTopZ = FeatureIncCustomTopZ,
            FeatureIncCustomBank = FeatureIncCustomBank
        };

        return copy;
    }
}

/// <summary>The kind of geometry an edge feature generates along a road edge.</summary>
public enum EdgeFeatureKind
{
    Sidewalk,
    Guardrail
}

/// <summary>Per-control-point parameters for an edge feature, so its width,
/// thickness and banking vary along the feature just like a road's.</summary>
public sealed class EdgeFeaturePoint
{
    public double Width = 128;
    public double BottomOffset = 0;
    public double TopOffset = 64;
    public double BankDegrees = 0;

    public EdgeFeaturePoint Clone() => new EdgeFeaturePoint
    {
        Width = Width,
        BottomOffset = BottomOffset,
        TopOffset = TopOffset,
        BankDegrees = BankDegrees
    };
}

/// <summary>Extra geometry that rides along one outside edge of a track: a flat
/// raised strip (sidewalk) or a thin barrier (guardrail). All four faces are
/// optional, but at least one must stay enabled.</summary>
public sealed class EdgeFeature
{
    public EdgeFeatureKind Kind = EdgeFeatureKind.Sidewalk;
    public bool LeftSide = true;          // false = right side

    /// <summary>Gap from the road edge, in units.</summary>
    public double Offset = 0;

    public bool SolidBottom = true;
    public bool SolidInner = true;
    public bool SolidOuter = true;

    public string Material = "CONCRETE/CONCRETEFLOOR005A";

    /// <summary>One entry per road control point (width, bottom/top Z, bank). Keep
    /// in sync with the owning track's point count.</summary>
    public readonly List<EdgeFeaturePoint> Points = new List<EdgeFeaturePoint>();

    /// <summary>Optional per-point coverage mask, parallel to Points. Empty means
    /// every point is covered. A merged track uses this to keep a sidewalk only
    /// along part of the road (e.g. after a start-to-start join).</summary>
    public readonly List<bool> Enabled = new List<bool>();

    /// <summary>True when a track point is part of this feature (or when no mask
    /// is set, in which case every point is covered).</summary>
    public bool IsPointEnabled(int trackIndex)
    {
        if (Enabled.Count == 0)
        {
            return true;
        }

        return trackIndex >= 0 && trackIndex < Enabled.Count && Enabled[trackIndex];
    }

    public double WidthAt(double t) => SamplePoint(t).Width;

    public double BottomOffsetAt(double t) => SamplePoint(t).BottomOffset;

    public double TopOffsetAt(double t) => SamplePoint(t).TopOffset;

    public double BankAt(double t) => SamplePoint(t).BankDegrees;

    private EdgeFeaturePoint SamplePoint(double t)
    {
        int n = Points.Count;
        if (n == 0)
        {
            return new EdgeFeaturePoint();
        }

        if (n == 1 || t <= 0)
        {
            return Points[0];
        }

        double maxT = n - 1;
        if (t >= maxT)
        {
            return Points[n - 1];
        }

        int i = (int)Math.Floor(t);
        if (i > n - 2)
        {
            i = n - 2;
        }

        double u = t - i;
        EdgeFeaturePoint a = Points[i];
        EdgeFeaturePoint b = Points[i + 1];

        return new EdgeFeaturePoint
        {
            Width = a.Width + (b.Width - a.Width) * u,
            BottomOffset = a.BottomOffset + (b.BottomOffset - a.BottomOffset) * u,
            TopOffset = a.TopOffset + (b.TopOffset - a.TopOffset) * u,
            BankDegrees = a.BankDegrees + (b.BankDegrees - a.BankDegrees) * u
        };
    }

    public EdgeFeature Clone()
    {
        EdgeFeature copy = new EdgeFeature
        {
            Kind = Kind,
            LeftSide = LeftSide,
            Offset = Offset,
            SolidBottom = SolidBottom,
            SolidInner = SolidInner,
            SolidOuter = SolidOuter,
            Material = Material
        };

        foreach (EdgeFeaturePoint point in Points)
        {
            copy.Points.Add(point.Clone());
        }

        foreach (bool enabled in Enabled)
        {
            copy.Enabled.Add(enabled);
        }

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
    public readonly List<EdgeFeature> EdgeFeatures = new List<EdgeFeature>();

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

        foreach (EdgeFeature feature in EdgeFeatures)
        {
            copy.EdgeFeatures.Add(feature.Clone());
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

            RoadChain chain = new RoadChain
            {
                Settings = track.Settings,
                Joinable = track.EnableJoining,
                Closed = track.Points.Count >= 3 && PositionsMatch(track.Points[0].Position, track.Points[track.Points.Count - 1].Position)
            };
            chain.Points.AddRange(track.Points);
            chain.Spans.Add(new ChainSpan
            {
                Track = track,
                StartPoint = 0,
                EndPoint = track.Points.Count,
                TrueStart = 0,
                Reversed = false,
                SourceStart = 0,
                SourceEnd = track.Points.Count - 1
            });
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

        // A merged chain can become a closed loop when its two end tracks were
        // welded together (e.g. a third track joined back to the first). Mark every
        // such chain so the preview and export treat it as a continuous ring.
        foreach (RoadChain chain in chains)
        {
            if (!chain.Closed && chain.Points.Count >= 3 && PositionsMatch(chain.Points[0].Position, chain.Points[chain.Points.Count - 1].Position))
            {
                chain.Closed = true;
            }
        }

        // A chain can be built by merging an already-merged chain into a longer one
        // (e.g. when a third track joins an A+B pair). Re-appending a chain computes
        // each span's range from TrueStart and drops the per-span colouring extension
        // (StartPoint = TrueStart - 1) that makes a span draw the shared junction
        // segment leading into it. Re-derive it for every span after the first; a
        // two-track join already extends StartPoint once, but a subsequent re-merge
        // would otherwise leave the segment right after a junction undrawn, so the
        // track's first point appears unrendered.
        foreach (RoadChain chain in chains)
        {
            for (int spanIndex = 1; spanIndex < chain.Spans.Count; spanIndex++)
            {
                ChainSpan span = chain.Spans[spanIndex];
                if (span.TrueStart > 0)
                {
                    span.StartPoint = span.TrueStart - 1;
                }
            }
        }

        return chains;
    }

    /// <summary>Merge every track in a chain into one track (the editor's "Merge"
    /// button). The chain's point sequence is already deduplicated at junctions.
    /// Edge features are rebuilt from the chain so each sidewalk keeps its physical
    /// side, widths interpolate across junctions, and a strip that only spans part
    /// of the chain (a start-to-start or end-to-end join) is stored with a per-point
    /// coverage mask so it stops at the right place.</summary>
    public Track MergeChain(RoadChain chain, string name, RoadSettings settings, bool enableJoining)
    {
        Track mergedTrack = new Track(name)
        {
            EnableJoining = enableJoining
        };
        mergedTrack.Settings = settings.Clone();

        foreach (RoadPoint point in chain.Points)
        {
            mergedTrack.Points.Add(point.Clone());
        }

        foreach (ChainFeature chainFeature in chain.CollectFeatures())
        {
            EdgeFeature mergedFeature = chainFeature.Feature.Clone();
            mergedFeature.Points.Clear();
            mergedFeature.Enabled.Clear();

            int chainPointCount = chain.Points.Count;
            bool partialCoverage = chainFeature.StartPoint > 0 || chainFeature.EndPoint < chainPointCount;
            EdgeFeaturePoint fallback = chainFeature.Points.Count > 0 ? chainFeature.Points[0] : new EdgeFeaturePoint();

            for (int chainIndex = 0; chainIndex < chainPointCount; chainIndex++)
            {
                bool covered = chainIndex >= chainFeature.StartPoint && chainIndex < chainFeature.EndPoint;
                EdgeFeaturePoint source = covered
                    ? chainFeature.Points[chainIndex - chainFeature.StartPoint]
                    : fallback;

                mergedFeature.Points.Add(source.Clone());
                if (partialCoverage)
                {
                    mergedFeature.Enabled.Add(covered);
                }
            }

            mergedTrack.EdgeFeatures.Add(mergedFeature);
        }

        return mergedTrack;
    }

    private static RoadChain MergeChains(RoadChain first, RoadChain second)
    {
        if (!first.Joinable || !second.Joinable || first.Closed || second.Closed)
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
            merged.Closed = IsChainClosed(merged);
            return merged;
        }

        if (PositionsMatch(firstEnd, secondEnd))
        {
            AppendChainRange(merged, first, 0, first.Points.Count, forward: true);
            AppendChainRange(merged, second, 0, second.Points.Count - 1, forward: false);
            merged.Closed = IsChainClosed(merged);
            return merged;
        }

        if (PositionsMatch(firstStart, secondEnd))
        {
            AppendChainRange(merged, second, 0, second.Points.Count, forward: true);
            AppendChainRange(merged, first, 1, first.Points.Count, forward: true);
            merged.Closed = IsChainClosed(merged);
            return merged;
        }

        if (PositionsMatch(firstStart, secondStart))
        {
            AppendChainRange(merged, second, 0, second.Points.Count, forward: false);
            AppendChainRange(merged, first, 1, first.Points.Count, forward: true);
            merged.Closed = IsChainClosed(merged);
            return merged;
        }

        return null;
    }

    /// <summary>True when a chain's first and last control points coincide, forming a
    /// closed loop. The last point is a duplicate of the first, so the curve wraps.</summary>
    private static bool IsChainClosed(RoadChain chain)
    {
        return chain.Points.Count >= 3 && PositionsMatch(chain.Points[0].Position, chain.Points[chain.Points.Count - 1].Position);
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
            int overlapStart = Math.Max(span.TrueStart, startIndex);
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

            int firstSourcePoint = forward ? overlapStart : overlapEnd - 1;
            int lastSourcePoint = forward ? overlapEnd - 1 : overlapStart;

            chain.Spans.Add(new ChainSpan
            {
                Track = span.Track,
                StartPoint = newStart,
                EndPoint = newEnd,
                TrueStart = newStart,
                Reversed = span.Reversed != !forward,
                SourceStart = TrackPointIndexAt(source, span, firstSourcePoint),
                SourceEnd = TrackPointIndexAt(source, span, lastSourcePoint)
            });
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

    /// <summary>Map a source chain point index to the track point index it came
    /// from, following the span's traversal direction.</summary>
    private static int TrackPointIndexAt(RoadChain source, ChainSpan span, int sourceChainPoint)
    {
        if (span.Reversed)
        {
            return span.SourceStart - (sourceChainPoint - span.TrueStart);
        }

        return span.SourceStart + (sourceChainPoint - span.TrueStart);
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

    /// <summary>True when the chain's first and last control points coincide, so the
    /// road is a closed loop and its spline wraps continuously across the seam.</summary>
    public bool Closed;

    public bool ContainsTrack(Track track)
    {
        foreach (ChainSpan span in Spans)
        {
            if (ReferenceEquals(span.Track, track))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The edge features of every track in this chain, resolved to the
    /// chain: per-point values are aligned to the chain's point order and the side
    /// is flipped for spans that were reversed (start-to-start / end-to-end joins),
    /// so a sidewalk stays on the same physical side of its track. A strip only
    /// exists on tracks that actually have it — it stops at a track with no
    /// matching feature.</summary>
    public List<ChainFeature> CollectFeatures()
    {
        List<ChainFeature> features = new List<ChainFeature>();

        // Strips still "open" at the end of the previous span, keyed by kind so a
        // following span with the same (side, kind) can merge into them and
        // interpolate smoothly across the junction.
        Dictionary<EdgeFeatureKind, List<ChainFeature>> activeByKind = new Dictionary<EdgeFeatureKind, List<ChainFeature>>();

        foreach (ChainSpan span in Spans)
        {
            // StartPoint is the coloring range, which may extend one point back to
            // include the shared junction point. Features use the same range so the
            // junction segment is covered by the track that owns it.
            int spanStart = span.StartPoint;
            int spanEnd = span.EndPoint;
            if (spanEnd - spanStart < 1)
            {
                continue;
            }

            if (span.Track.EdgeFeatures.Count == 0)
            {
                // A track with no edge features breaks the strip: sidewalks do not
                // extend onto it. Close the open strips so a later track's sidewalk
                // starts fresh instead of continuing across this span.
                activeByKind = new Dictionary<EdgeFeatureKind, List<ChainFeature>>();
                continue;
            }

            Dictionary<EdgeFeatureKind, List<ChainFeature>> newActive = new Dictionary<EdgeFeatureKind, List<ChainFeature>>();

            foreach (EdgeFeature trackFeature in span.Track.EdgeFeatures)
            {
                bool effectiveLeft = trackFeature.LeftSide != span.Reversed;
                List<EdgeFeaturePoint> spanPoints = BuildSpanFeaturePoints(span, trackFeature, out List<bool> spanEnabled);
                if (spanPoints.Count == 0)
                {
                    continue;
                }

                // Split the span into contiguous enabled runs. A merged track may
                // keep a sidewalk only along part of itself, and those runs become
                // separate chain features so the strip stops where it's disabled.
                int runStart = 0;
                while (runStart < spanPoints.Count)
                {
                    if (!spanEnabled[runStart])
                    {
                        runStart++;
                        continue;
                    }

                    int runEnd = runStart;
                    while (runEnd < spanPoints.Count && spanEnabled[runEnd])
                    {
                        runEnd++;
                    }

                    int runChainStart = spanStart + runStart;
                    int runChainEnd = spanStart + runEnd;
                    List<EdgeFeaturePoint> runPoints = spanPoints.GetRange(runStart, runEnd - runStart);

                    // Merge contiguous runs of the same (side, kind) into one feature
                    // so width/thickness/bank interpolate smoothly across the junction,
                    // just like a road's own control points. A run that starts on the
                    // shared junction point overlaps the previous feature by one point.
                    ChainFeature mergeTarget = null;
                    if (activeByKind.TryGetValue(trackFeature.Kind, out List<ChainFeature> activeList))
                    {
                        foreach (ChainFeature existing in activeList)
                        {
                            if (existing.Feature.LeftSide != effectiveLeft)
                            {
                                continue;
                            }

                            if (existing.EndPoint == runChainStart || existing.EndPoint == runChainStart + 1)
                            {
                                mergeTarget = existing;
                                break;
                            }
                        }
                    }

                    if (mergeTarget != null)
                    {
                        int skipFirst = mergeTarget.EndPoint == runChainStart + 1 ? 1 : 0;
                        mergeTarget.EndPoint = runChainEnd;
                        for (int index = skipFirst; index < runPoints.Count; index++)
                        {
                            mergeTarget.Points.Add(runPoints[index]);
                        }
                    }
                    else
                    {
                        EdgeFeature template = trackFeature.Clone();
                        template.LeftSide = effectiveLeft;
                        template.Points.Clear();
                        template.Enabled.Clear();
                        mergeTarget = new ChainFeature
                        {
                            Feature = template,
                            StartPoint = runChainStart,
                            EndPoint = runChainEnd,
                            Points = runPoints
                        };
                        features.Add(mergeTarget);
                    }

                    if (!newActive.TryGetValue(trackFeature.Kind, out List<ChainFeature> kindList))
                    {
                        kindList = new List<ChainFeature>();
                        newActive[trackFeature.Kind] = kindList;
                    }

                    kindList.Add(mergeTarget);
                    runStart = runEnd;
                }
            }

            activeByKind = newActive;
        }

        return features;
    }

    private static List<EdgeFeaturePoint> BuildSpanFeaturePoints(ChainSpan span, EdgeFeature feature, out List<bool> enabled)
    {
        int count = span.EndPoint - span.StartPoint;
        List<EdgeFeaturePoint> points = new List<EdgeFeaturePoint>(count);
        enabled = new List<bool>(count);

        for (int offset = 0; offset < count; offset++)
        {
            int chainIndex = span.StartPoint + offset;
            int trackIndex = TrackIndexForChainOffset(span, chainIndex);
            points.Add(FeaturePointAt(feature, trackIndex).Clone());
            enabled.Add(feature.IsPointEnabled(trackIndex));
        }

        return points;
    }

    /// <summary>Map a chain point index to the track point index it came from. The
    /// one point before a span's true start is the shared junction point that the
    /// coloring range borrowed from the track's skipped endpoint.</summary>
    private static int TrackIndexForChainOffset(ChainSpan span, int chainIndex)
    {
        int offsetFromTrueStart = chainIndex - span.TrueStart;
        if (offsetFromTrueStart < 0)
        {
            return span.Reversed ? span.SourceStart + 1 : span.SourceStart - 1;
        }

        return span.Reversed ? span.SourceStart - offsetFromTrueStart : span.SourceStart + offsetFromTrueStart;
    }

    private static EdgeFeaturePoint FeaturePointAt(EdgeFeature feature, int trackIndex)
    {
        if (feature.Points.Count == 0)
        {
            return new EdgeFeaturePoint();
        }

        int clamped = Math.Clamp(trackIndex, 0, feature.Points.Count - 1);
        return feature.Points[clamped];
    }
}

/// <summary>The range of a chain's control points that came from one track. Used
/// to color the active track's portion separately from the muted tracks it is
/// welded to.</summary>
public sealed class ChainSpan
{
    public Track Track;

    /// <summary>Chain point range used for rendering/coloring. The start may be
    /// extended back one point so the boundary segment shares the appended track's
    /// color.</summary>
    public int StartPoint;
    public int EndPoint;

    /// <summary>Chain index where this span's own points actually begin (before any
    /// coloring extension). Feature point mapping uses this range.</summary>
    public int TrueStart;

    /// <summary>True when this span's track points were reversed while assembling
    /// the chain (start-to-start or end-to-end joins).</summary>
    public bool Reversed;

    /// <summary>Track point index of the span's first chain point (TrueStart).</summary>
    public int SourceStart;

    /// <summary>Track point index of the span's last chain point (EndPoint - 1).</summary>
    public int SourceEnd;
}

/// <summary>An edge feature resolved to a chain: the template feature (side already
/// flipped for reversed spans) plus per-point values aligned to the chain point
/// range [StartPoint, EndPoint).</summary>
public sealed class ChainFeature
{
    public EdgeFeature Feature;
    public int StartPoint;
    public int EndPoint;
    public List<EdgeFeaturePoint> Points = new List<EdgeFeaturePoint>();

    /// <summary>Linearly interpolated feature values at a chain parameter t
    /// (t runs over the whole chain; [StartPoint, EndPoint) is this feature's
    /// coverage). Matches the road's own width/bank/thickness interpolation.</summary>
    public EdgeFeaturePoint PointAt(double t)
    {
        double localT = t - StartPoint;
        int n = Points.Count;
        if (n == 0)
        {
            return new EdgeFeaturePoint();
        }

        if (n == 1 || localT <= 0)
        {
            return Points[0];
        }

        if (localT >= n - 1)
        {
            return Points[n - 1];
        }

        int index = (int)Math.Floor(localT);
        if (index > n - 2)
        {
            index = n - 2;
        }

        double u = localT - index;
        EdgeFeaturePoint first = Points[index];
        EdgeFeaturePoint second = Points[index + 1];

        return new EdgeFeaturePoint
        {
            Width = first.Width + (second.Width - first.Width) * u,
            BottomOffset = first.BottomOffset + (second.BottomOffset - first.BottomOffset) * u,
            TopOffset = first.TopOffset + (second.TopOffset - first.TopOffset) * u,
            BankDegrees = first.BankDegrees + (second.BankDegrees - first.BankDegrees) * u
        };
    }
}
