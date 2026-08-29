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
