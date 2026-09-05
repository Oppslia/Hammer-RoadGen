using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>Snapshot-based undo/redo for a RoadDocument. Every snapshot captures
/// the full point list plus the road settings, so undo covers both geometry and
/// settings changes.</summary>
public sealed class UndoManager
{
    private sealed class Snapshot
    {
        public readonly List<Track> Tracks = new List<Track>();
        public int ActiveTrackIndex;

        // Cordon box extents, captured only when the manager was constructed with a cordon.
        // Only the box geometry is undoable (Fit-to-map / box drags); the Active culling
        // toggle is deliberately NOT part of undo.
        public Vec3 CordonMins;
        public Vec3 CordonMaxs;

        public static Snapshot Capture(RoadDocument document)
        {
            Snapshot snapshot = new Snapshot
            {
                ActiveTrackIndex = document.ActiveTrackIndex
            };

            foreach (Track track in document.Tracks)
            {
                snapshot.Tracks.Add(track.Clone());
            }

            return snapshot;
        }

        public void Restore(RoadDocument document)
        {
            document.Tracks.Clear();
            foreach (Track track in Tracks)
            {
                document.Tracks.Add(track.Clone());
            }

            document.ActiveTrackIndex = ActiveTrackIndex;
        }

        /// <summary>Applies the captured cordon state back to <paramref name="cordon"/> (no-op
        /// when the manager has no cordon or the state already matches — Cordon.Set only
        /// raises Changed on a real change).</summary>
        public void RestoreCordon(Cordon cordon)
        {
            if (cordon == null)
            {
                return;
            }

            // Restores only the box extents; the Active toggle is left untouched (not
            // undoable). Cordon.Set only raises Changed on a real change.
            if (!cordon.Mins.Equals(CordonMins) || !cordon.Maxs.Equals(CordonMaxs))
            {
                cordon.Set(cordon.Enabled, CordonMins, CordonMaxs);
            }
        }

        /// <summary>True when two snapshots describe the same document state. Used
        /// to discard no-op undo batches (focus without editing, or toggling a value
        /// back to its original).</summary>
        public static bool Same(Snapshot first, Snapshot second)
        {
            if (first.ActiveTrackIndex != second.ActiveTrackIndex)
            {
                return false;
            }

            if (!first.CordonMins.Equals(second.CordonMins)
                || !first.CordonMaxs.Equals(second.CordonMaxs))
            {
                return false;
            }

            if (first.Tracks.Count != second.Tracks.Count)
            {
                return false;
            }

            for (int i = 0; i < first.Tracks.Count; i++)
            {
                if (!SameTrack(first.Tracks[i], second.Tracks[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameTrack(Track first, Track second)
        {
            if (first.Name != second.Name || first.EnableJoining != second.EnableJoining)
            {
                return false;
            }

            if (!SameSettings(first.Settings, second.Settings))
            {
                return false;
            }

            if (first.Points.Count != second.Points.Count)
            {
                return false;
            }

            for (int i = 0; i < first.Points.Count; i++)
            {
                RoadPoint a = first.Points[i];
                RoadPoint b = second.Points[i];
                if (!a.Position.Equals(b.Position)
                    || a.Width != b.Width
                    || a.BankDegrees != b.BankDegrees
                    || a.Thickness != b.Thickness)
                {
                    return false;
                }
            }

            if (first.EdgeFeatures.Count != second.EdgeFeatures.Count)
            {
                return false;
            }

            for (int i = 0; i < first.EdgeFeatures.Count; i++)
            {
                EdgeFeature a = first.EdgeFeatures[i];
                EdgeFeature b = second.EdgeFeatures[i];
                if (a.Kind != b.Kind || a.LeftSide != b.LeftSide || a.Offset != b.Offset
                    || a.SolidBottom != b.SolidBottom || a.SolidInner != b.SolidInner
                    || a.SolidOuter != b.SolidOuter || a.Material != b.Material)
                {
                    return false;
                }

                if (a.Points.Count != b.Points.Count)
                {
                    return false;
                }

                for (int p = 0; p < a.Points.Count; p++)
                {
                    EdgeFeaturePoint pa = a.Points[p];
                    EdgeFeaturePoint pb = b.Points[p];
                    if (pa.Width != pb.Width || pa.BottomOffset != pb.BottomOffset
                        || pa.TopOffset != pb.TopOffset || pa.BankDegrees != pb.BankDegrees)
                    {
                        return false;
                    }
                }

                if (a.Enabled.Count != b.Enabled.Count)
                {
                    return false;
                }

                for (int e = 0; e < a.Enabled.Count; e++)
                {
                    if (a.Enabled[e] != b.Enabled[e])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SameSettings(RoadSettings first, RoadSettings second)
        {
            return first.Power == second.Power
                && first.Material == second.Material
                && first.SolidLeft == second.SolidLeft
                && first.SolidRight == second.SolidRight
                && first.SolidBottom == second.SolidBottom
                && first.SegmentLength == second.SegmentLength
                && first.TextureScale == second.TextureScale
                && first.FitTextures == second.FitTextures
                && first.LightmapScale == second.LightmapScale
                && first.Snap == second.Snap
                && first.SnapEnabled == second.SnapEnabled
                && first.IncUseGridX == second.IncUseGridX
                && first.IncUseGridY == second.IncUseGridY
                && first.IncUseGridZ == second.IncUseGridZ
                && first.IncUseGridWidth == second.IncUseGridWidth
                && first.IncUseGridBank == second.IncUseGridBank
                && first.IncCustomX == second.IncCustomX
                && first.IncCustomY == second.IncCustomY
                && first.IncCustomZ == second.IncCustomZ
                && first.IncCustomWidth == second.IncCustomWidth
                && first.IncCustomBank == second.IncCustomBank
                && first.IncUseGridThickness == second.IncUseGridThickness
                && first.IncCustomThickness == second.IncCustomThickness
                && first.EdgeIncUseGridOffset == second.EdgeIncUseGridOffset
                && first.EdgeIncUseGridWidth == second.EdgeIncUseGridWidth
                && first.EdgeIncUseGridBottomZ == second.EdgeIncUseGridBottomZ
                && first.EdgeIncUseGridTopZ == second.EdgeIncUseGridTopZ
                && first.EdgeIncUseGridBank == second.EdgeIncUseGridBank
                && first.EdgeIncCustomOffset == second.EdgeIncCustomOffset
                && first.EdgeIncCustomWidth == second.EdgeIncCustomWidth
                && first.EdgeIncCustomBottomZ == second.EdgeIncCustomBottomZ
                && first.EdgeIncCustomTopZ == second.EdgeIncCustomTopZ
                && first.EdgeIncCustomBank == second.EdgeIncCustomBank;
        }
    }

    private readonly RoadDocument _doc;
    private readonly Cordon _cordon;
    private readonly Stack<Snapshot> _undo = new Stack<Snapshot>();
    private readonly Stack<Snapshot> _redo = new Stack<Snapshot>();
    private Snapshot _batch;
    private bool _batchDirty;

    /// <summary>True when the open batch is a coalescing session (from controls that
    /// may not hold focus, such as the increment "Grid" checkboxes or the control
    /// point number boxes). The session must be closed as soon as the user engages
    /// any other control so it doesn't swallow unrelated edits.</summary>
    private bool _sessionBatch;

    public UndoManager(RoadDocument doc, Cordon cordon = null)
    {
        _doc = doc;
        _cordon = cordon;

        // Track whether the document actually changes while a batch is open, so an
        // empty batch (e.g. merely focusing a control) isn't committed as a phantom
        // undo step.
        doc.Changed += (s, e) =>
        {
            if (_batch != null)
            {
                _batchDirty = true;
            }
        };
    }

    /// <summary>Captures the document plus (when present) the cordon state, so cordon edits
    /// participate in the same undo/redo history as road edits.</summary>
    private Snapshot SnapshotNow()
    {
        Snapshot snapshot = Snapshot.Capture(_doc);
        if (_cordon != null)
        {
            snapshot.CordonMins = _cordon.Mins;
            snapshot.CordonMaxs = _cordon.Maxs;
        }

        return snapshot;
    }

    /// <summary>Start a coalesced edit. The state is captured once; EndBatch commits
    /// it as a single undo step even if many changes happen in between.</summary>
    public void BeginBatch()
    {
        // A control is being focused/engaged: first close any lingering coalescing
        // session so the next edit becomes its own undo step instead of joining it.
        CloseSession();

        if (_batch == null)
        {
            _batch = SnapshotNow();
            _batchDirty = false;
        }
    }

    public void EndBatch()
    {
        if (_batch == null)
        {
            return;
        }

        // Commit the batch as a single undo step only when the document actually
        // differs from when the batch began. Merely focusing a control (no edits),
        // or toggling a value back to its original (e.g. a checkbox clicked twice),
        // must not create a phantom undo step.
        if (!Snapshot.Same(_batch, SnapshotNow()))
        {
            _undo.Push(_batch);
            _redo.Clear();
        }

        _batch = null;
        _batchDirty = false;
        _sessionBatch = false;
    }

    /// <summary>Call just before an editor control applies a change to the document.
    /// When a batch is already open (the control has focus) the change joins that
    /// batch, so the whole focus session undoes in one step. When no batch is open
    /// (e.g. an undo/redo closed it while focus stayed on the control) the change is
    /// recorded as its own undo step so it is still undoable.</summary>
    public void BeginChange()
    {
        CloseSession();

        if (_batch != null)
        {
            _batchDirty = true;
            return;
        }

        _undo.Push(SnapshotNow());
        _redo.Clear();
    }

    /// <summary>Call before a control applies a change when the control may not hold
    /// focus long enough for Enter/Leave to open a batch (e.g. the increment section
    /// "Grid" checkboxes or the control point number boxes). The first change opens a
    /// batch that spans every consecutive change from those controls, so stepping a
    /// value up several times (or flipping a toggle on then off) nets out and
    /// produces a single undo step. The session is closed (and committed if it
    /// changed) as soon as the user engages any other control.</summary>
    public void BeginSession()
    {
        if (_batch != null)
        {
            // Join the existing batch (a focused control's session, or a previous
            // coalescing session) so consecutive changes coalesce.
            _batchDirty = true;
            return;
        }

        _batch = SnapshotNow();
        _sessionBatch = true;
        _batchDirty = false;
    }

    /// <summary>Commit (or discard, if unchanged) a lingering coalescing session so
    /// the next edit from a different control starts its own undo step.</summary>
    private void CloseSession()
    {
        if (_batch != null && _sessionBatch)
        {
            EndBatch();
        }
    }

    /// <summary>Record a single discrete undo step (before mutating).</summary>
    public void RecordSingle()
    {
        CloseSession();
        _undo.Push(SnapshotNow());
        _redo.Clear();
        _batch = null;
        _batchDirty = false;
    }

    /// <summary>True when an undo is available, including an in-progress edit batch
    /// that has actually changed the document (so the Undo button lights up as soon
    /// as you start editing a field).</summary>
    public bool CanUndo => _undo.Count > 0 || (_batch != null && _batchDirty);
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Undo the most recent step. Returns true when a step was actually
    /// undone; false when there was nothing to undo (including an open batch whose
    /// net change is zero, e.g. a checkbox toggled twice).</summary>
    public bool Undo()
    {
        // Commit any in-progress edit first (e.g. a still-focused checkbox), so the
        // most recent change is what actually gets undone.
        EndBatch();

        if (!CanUndo)
        {
            return false;
        }

        _redo.Push(SnapshotNow());
        Snapshot snapshot = _undo.Pop();
        snapshot.Restore(_doc);
        snapshot.RestoreCordon(_cordon);
        return true;
    }

    public bool Redo()
    {
        EndBatch();

        if (!CanRedo)
        {
            return false;
        }

        _undo.Push(SnapshotNow());
        Snapshot snapshot = _redo.Pop();
        snapshot.Restore(_doc);
        snapshot.RestoreCordon(_cordon);
        return true;
    }
}
