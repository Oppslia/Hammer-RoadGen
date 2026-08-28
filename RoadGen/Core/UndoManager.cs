using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>Snapshot-based undo/redo for a RoadDocument. Every snapshot captures
/// the full point list plus the road settings, so undo covers both geometry and
/// settings changes.</summary>
public sealed class UndoManager
{
    private sealed class Snapshot
    {
        public readonly List<RoadPoint> Points = new List<RoadPoint>();
        public int Power;
        public string Material = string.Empty;
        public double Thickness;
        public bool SolidLeft;
        public bool SolidRight;
        public bool SolidBottom;
        public double SegmentLength;
        public double TextureScale;
        public double Snap;
        public int LightmapScale;
        public bool IncUseGridX;
        public bool IncUseGridY;
        public bool IncUseGridZ;
        public bool IncUseGridWidth;
        public bool IncUseGridBank;
        public double IncCustomX;
        public double IncCustomY;
        public double IncCustomZ;
        public double IncCustomWidth;
        public double IncCustomBank;

        public static Snapshot Capture(RoadDocument doc)
        {
            var s = new Snapshot
            {
                Power = doc.Settings.Power,
                Material = doc.Settings.Material,
                Thickness = doc.Settings.Thickness,
                SolidLeft = doc.Settings.SolidLeft,
                SolidRight = doc.Settings.SolidRight,
                SolidBottom = doc.Settings.SolidBottom,
                SegmentLength = doc.Settings.SegmentLength,
                TextureScale = doc.Settings.TextureScale,
                Snap = doc.Settings.Snap,
                LightmapScale = doc.Settings.LightmapScale,
                IncUseGridX = doc.Settings.IncUseGridX,
                IncUseGridY = doc.Settings.IncUseGridY,
                IncUseGridZ = doc.Settings.IncUseGridZ,
                IncUseGridWidth = doc.Settings.IncUseGridWidth,
                IncUseGridBank = doc.Settings.IncUseGridBank,
                IncCustomX = doc.Settings.IncCustomX,
                IncCustomY = doc.Settings.IncCustomY,
                IncCustomZ = doc.Settings.IncCustomZ,
                IncCustomWidth = doc.Settings.IncCustomWidth,
                IncCustomBank = doc.Settings.IncCustomBank
            };

            foreach (RoadPoint p in doc.Points)
            {
                s.Points.Add(p.Clone());
            }

            return s;
        }

        public void Restore(RoadDocument doc)
        {
            doc.Points.Clear();
            foreach (RoadPoint p in Points)
            {
                doc.Points.Add(p.Clone());
            }

            doc.Settings.Power = Power;
            doc.Settings.Material = Material;
            doc.Settings.Thickness = Thickness;
            doc.Settings.SolidLeft = SolidLeft;
            doc.Settings.SolidRight = SolidRight;
            doc.Settings.SolidBottom = SolidBottom;
            doc.Settings.SegmentLength = SegmentLength;
            doc.Settings.TextureScale = TextureScale;
            doc.Settings.Snap = Snap;
            doc.Settings.LightmapScale = LightmapScale;
            doc.Settings.IncUseGridX = IncUseGridX;
            doc.Settings.IncUseGridY = IncUseGridY;
            doc.Settings.IncUseGridZ = IncUseGridZ;
            doc.Settings.IncUseGridWidth = IncUseGridWidth;
            doc.Settings.IncUseGridBank = IncUseGridBank;
            doc.Settings.IncCustomX = IncCustomX;
            doc.Settings.IncCustomY = IncCustomY;
            doc.Settings.IncCustomZ = IncCustomZ;
            doc.Settings.IncCustomWidth = IncCustomWidth;
            doc.Settings.IncCustomBank = IncCustomBank;
        }
    }

    private readonly RoadDocument _doc;
    private readonly Stack<Snapshot> _undo = new Stack<Snapshot>();
    private readonly Stack<Snapshot> _redo = new Stack<Snapshot>();
    private Snapshot _batch;

    public UndoManager(RoadDocument doc)
    {
        _doc = doc;
    }

    /// <summary>Start a coalesced edit. The state is captured once; EndBatch commits
    /// it as a single undo step even if many changes happen in between.</summary>
    public void BeginBatch()
    {
        _batch ??= Snapshot.Capture(_doc);
    }

    public void EndBatch()
    {
        if (_batch == null)
        {
            return;
        }

        _undo.Push(_batch);
        _redo.Clear();
        _batch = null;
    }

    /// <summary>Record a single discrete undo step (before mutating).</summary>
    public void RecordSingle()
    {
        _undo.Push(Snapshot.Capture(_doc));
        _redo.Clear();
        _batch = null;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        // Commit any in-progress edit first (e.g. a still-focused checkbox), so the
        // most recent change is what actually gets undone.
        EndBatch();

        if (!CanUndo)
        {
            return;
        }

        _redo.Push(Snapshot.Capture(_doc));
        _undo.Pop().Restore(_doc);
    }

    public void Redo()
    {
        EndBatch();

        if (!CanRedo)
        {
            return;
        }

        _undo.Push(Snapshot.Capture(_doc));
        _redo.Pop().Restore(_doc);
    }
}
