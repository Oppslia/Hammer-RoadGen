using System.Globalization;
using RoadGen.Core;
using RoadGen.Tests;

// A dependency-free test runner. Each test is a local function that throws on
// failure. Run with: dotnet run --project RoadGen.Tests
// Exit code is non-zero when any test fails.

int passed = 0;
int failed = 0;

Run("Welding two roads end-to-start makes one continuous road", TestEndToStartJoin);
Run("Welding two road starts keeps both halves attached (no dropped shared point)", TestStartToStartReverses);
Run("Welding two road ends keeps both halves attached", TestEndToEndReverses);
Run("A road with joining disabled stays a separate road", TestEnableJoiningSeparates);
Run("Dragging a welded point moves the point joined to it too", TestMovePointWelded);
Run("Joining a third track keeps the middle track's first point rendered", TestThreeTrackJoinKeepsMiddleSpanCoverage);
Run("A welded loop closes into one continuous circular road", TestClosedLoopFlowsContinuously);
Run("CRITICAL: A closed loop's seam cross-section returns (no twist) for any track count", TestClosedLoopSeamReturnsForAnyTrackCount);
Run("CRITICAL: A closed loop's VMF seam cross-section returns (no twist)", TestClosedLoopExportSeamReturns);
Run("CRITICAL: Sidewalk stays glued to the road edge on a closed loop", TestClosedLoopSidewalkStaysOnEdge);
Run("Sidewalk stays on its own side when two road starts are joined", TestSideAwareSplit);
Run("Sidewalk width blends across a weld and reaches the last control point", TestWidthConcatenation);
Run("Sidewalk stops where a road has no sidewalk (doesn't run off the rails)", TestStopsAtFeaturelessTrack);
Run("Sidewalk width/height/bank ramp smoothly between control points", TestPointAtInterpolates);
Run("Left and right sidewalks export as valid (not inside-out) solids", TestWindingConsistent);
Run("Optimization preview matches the exported brush count", TestSegmentCountMatchesExport);
Run("Joined sidewalk optimization matches each track's own segment length", TestJoinedSidewalkUsesPerTrackOptimization);
Run("Saving and reopening keeps sidewalk width/height/bank and coverage", TestTrackFileRoundTrip);
Run("Old files with one sidewalk size upgrade to per-point values", TestTrackFileMigrationV4);
Run("Merging two welded roads keeps the sidewalk continuous to the very end", TestMergeKeepsSidewalkContinuous);
Run("Merging a start-to-start join keeps each sidewalk on its own side", TestMergeKeepsEachSidewalkOnItsOwnSide);
Run("Undo removes a just-added sidewalk; redo brings it back", TestUndoRestoresSnapshot);
Run("Editing one field is a single undo step", TestUndoBatchCoalesces);

// Test areas each expose their own cases; VMT parser regressions live in VmtParserTests.cs.
foreach ((string name, Action body) in VmtParserTests.Cases())
{
    Run(name, body);
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------------------
// Test runner + assertions
// ---------------------------------------------------------------------------

void Run(string name, Action body)
{
    try
    {
        body();
        Console.WriteLine($"PASS  {name}");
        passed++;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"FAIL  {name}: {exception.Message}");
        failed++;
    }
}

void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"{message} (expected '{expected}', got '{actual}')");
    }
}

// ---------------------------------------------------------------------------
// Test data helpers
// ---------------------------------------------------------------------------

EdgeFeature MakeSidewalk(int pointCount, bool leftSide, double baseWidth)
{
    EdgeFeature feature = new EdgeFeature { Kind = EdgeFeatureKind.Sidewalk, LeftSide = leftSide };
    for (int index = 0; index < pointCount; index++)
    {
        feature.Points.Add(new EdgeFeaturePoint
        {
            Width = baseWidth + index * 32,
            TopOffset = 64,
            BottomOffset = 0,
            BankDegrees = 0
        });
    }

    return feature;
}

// ---------------------------------------------------------------------------
// Chain joining
// ---------------------------------------------------------------------------

void TestEndToStartJoin()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0)); // shares first's last point
    second.Points.Add(new RoadPoint(new Vec3(1536, 0, 0), 256, 0));
    document.Tracks.Add(second);

    List<RoadChain> chains = document.BuildChains();
    AssertEqual(1, chains.Count, "one chain expected");
    AssertEqual(4, chains[0].Points.Count, "3 + 2 - 1 shared point");
    AssertEqual(false, chains[0].Spans[1].Reversed, "end-to-start appends forward");
}

void TestStartToStartReverses()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0)); // shares first's start
    second.Points.Add(new RoadPoint(new Vec3(-512, 0, 0), 256, 0));
    document.Tracks.Add(second);

    List<RoadChain> chains = document.BuildChains();
    AssertEqual(1, chains.Count, "one chain expected");
    AssertEqual(3, chains[0].Points.Count, "2 + 2 - 1 shared point");

    // Chain order is reverse(B) then A: B.1 -> B.0 -> A.1.
    AssertTrue(chains[0].Points[0].Position.X < 0, "first chain point is B's far end");
    AssertEqual(0.0, chains[0].Points[1].Position.X, "junction point in the middle");
    AssertTrue(chains[0].Points[2].Position.X > 0, "last chain point is A's second point");

    ChainSpan secondSpan = chains[0].Spans.First(span => ReferenceEquals(span.Track, second));
    AssertTrue(secondSpan.Reversed, "second track's span is reversed");
}

void TestEndToEndReverses()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    second.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0)); // second.end == first.end
    document.Tracks.Add(second);

    List<RoadChain> chains = document.BuildChains();
    AssertEqual(1, chains.Count, "one chain expected");
    AssertEqual(3, chains[0].Points.Count, "2 + 2 - 1 shared point");

    ChainSpan secondSpan = chains[0].Spans.First(span => ReferenceEquals(span.Track, second));
    AssertTrue(secondSpan.Reversed, "second track's span is reversed");
}

void TestEnableJoiningSeparates()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));

    Track second = new Track("B") { EnableJoining = false };
    second.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    second.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    document.Tracks.Add(second);

    List<RoadChain> chains = document.BuildChains();
    AssertEqual(2, chains.Count, "joining disabled keeps tracks separate");
}

void TestMovePointWelded()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    second.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    document.Tracks.Add(second);

    document.MovePointWelded(first, 1, new Vec3(600, 0, 0), new Vec3(512, 0, 0));

    AssertEqual(600.0, second.Points[0].Position.X, "welded point moved with the delta");
}

void TestThreeTrackJoinKeepsMiddleSpanCoverage()
{
    // Two tracks join (A end -> B start), then a third (C) joins B's end.
    // Re-merging the A+B chain into A+B+C used to drop the B span's colouring
    // extension, so the segment right after the A/B junction (the middle track's
    // first point) was not drawn.
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));

    Track middle = new Track("Middle");
    middle.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));   // joins A end
    middle.Points.Add(new RoadPoint(new Vec3(1536, 256, 0), 256, 0));
    middle.Points.Add(new RoadPoint(new Vec3(2048, 0, 0), 256, 0));
    middle.Points.Add(new RoadPoint(new Vec3(2560, -256, 0), 256, 0));
    middle.Points.Add(new RoadPoint(new Vec3(3072, 0, 0), 256, 0));
    middle.Points.Add(new RoadPoint(new Vec3(3584, 256, 0), 256, 0)); // joins C start
    document.Tracks.Add(middle);

    Track third = new Track("Third");
    third.Points.Add(new RoadPoint(new Vec3(3584, 256, 0), 256, 0));  // joins middle end
    third.Points.Add(new RoadPoint(new Vec3(4096, 256, 0), 256, 0));
    third.Points.Add(new RoadPoint(new Vec3(4608, 256, 0), 256, 0));
    document.Tracks.Add(third);

    List<RoadChain> chains = document.BuildChains();
    AssertEqual(1, chains.Count, "all three tracks join into one chain");

    RoadChain chain = chains[0];
    ChainSpan middleSpan = chain.Spans.First(span => ReferenceEquals(span.Track, middle));

    // The middle span must colour the shared junction segment leading out of its
    // own first point. When the colouring extension survives the re-merge,
    // StartPoint is one behind TrueStart; when it is dropped they are equal and
    // the segment between the A/B junction and the middle's second point is left
    // undrawn (so the middle track's first point appears unrendered).
    AssertEqual(middleSpan.TrueStart - 1, middleSpan.StartPoint,
        "middle track's span colours the junction segment out of its first point");

    // Every span after the first should carry the extension, so the whole road is
    // drawn continuously across each junction.
    foreach (ChainSpan span in chain.Spans.Skip(1))
    {
        AssertEqual(span.TrueStart - 1, span.StartPoint, "every span colours the junction leading into it");
    }
}

void TestClosedLoopFlowsContinuously()
{
    // Three tracks welded into a ring: A end -> B start, B end -> C start,
    // C end -> A start (so the loop closes). BuildChains must detect the seam and
    // mark the chain closed, and the spline must be continuous across it.
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 300, 10));    // loop seam (defines the junction values)
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 512, 0), 256, 0));

    Track middle = new Track("Middle");
    middle.Points.Add(new RoadPoint(new Vec3(512, 512, 0), 256, 0)); // joins A end
    middle.Points.Add(new RoadPoint(new Vec3(0, 512, 0), 256, 0));
    middle.Points.Add(new RoadPoint(new Vec3(-512, 512, 0), 256, 0));
    document.Tracks.Add(middle);

    Track third = new Track("Third");
    third.Points.Add(new RoadPoint(new Vec3(-512, 512, 0), 256, 0)); // joins middle end
    third.Points.Add(new RoadPoint(new Vec3(-512, 0, 0), 256, 0));
    third.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 200, 30, 96)); // back to A start (loop seam, different values)
    document.Tracks.Add(third);

    List<RoadChain> chains = document.BuildChains();
    AssertEqual(1, chains.Count, "the three tracks join into one chain");

    RoadChain chain = chains[0];
    AssertTrue(chain.Closed, "chain is detected as a closed loop");

    // The seam is the duplicate point, so the first and last chain points coincide.
    AssertTrue(RoadDocument.PositionsMatch(chain.Points[0].Position, chain.Points[chain.Points.Count - 1].Position),
        "seam points coincide");

    // The closed spline treats the seam as a single interior point (like a normal
    // end-to-end join), so it flows through the seam: position AND tangent are
    // continuous there.
    double tMax = chain.Points.Count - 1;
    Vec3 pos0 = RoadCurve.Position(chain.Points, 0, closed: true);
    Vec3 posEnd = RoadCurve.Position(chain.Points, tMax, closed: true);
    AssertTrue((pos0 - posEnd).Length < 0.001, "curve position is continuous at the seam");

    Vec3 tan0 = RoadCurve.Tangent(chain.Points, 0, closed: true).Normalized();
    Vec3 tanEnd = RoadCurve.Tangent(chain.Points, tMax, closed: true).Normalized();
    AssertTrue((tan0 - tanEnd).Length < 0.001, "curve tangent is continuous at the seam (flows as one road)");

    // The junction's scalar values are inherited from the loop's first (seam)
    // point, so bank/width/thickness do not jump to the other track's endpoint.
    AssertTrue(Math.Abs(RoadCurve.Bank(chain.Points, 0, closed: true) - RoadCurve.Bank(chain.Points, tMax, closed: true)) < 0.001,
        "bank is continuous at the seam");
    AssertTrue(Math.Abs(RoadCurve.Bank(chain.Points, 0, closed: true) - first.Points[0].BankDegrees) < 0.001,
        "seam bank inherits the loop's first point");
    AssertTrue(Math.Abs(RoadCurve.Width(chain.Points, tMax, closed: true) - first.Points[0].Width) < 0.001,
        "seam width inherits the loop's first point");
    AssertTrue(Math.Abs(RoadCurve.Thickness(chain.Points, tMax, closed: true) - first.Points[0].Thickness) < 0.001,
        "seam thickness inherits the loop's first point");

    // The whole road cross-section must line up at the seam: the first and last
    // preview samples coincide across every edge, so the two road arms don't
    // stick out at the join.
    RoadPreviewMesh pm = RoadPreviewMesh.Build(chain.Points, 24, chain.Closed);
    int last = pm.Center.Count - 1;
    AssertTrue((pm.Center[0] - pm.Center[last]).Length < 0.001, "preview centerline closes at the seam");
    AssertTrue((pm.Left[0] - pm.Left[last]).Length < 0.001, "preview left edge lines up at the seam");
    AssertTrue((pm.Right[0] - pm.Right[last]).Length < 0.001, "preview right edge lines up at the seam");
    AssertTrue((pm.BottomLeft[0] - pm.BottomLeft[last]).Length < 0.001, "preview bottom-left edge lines up at the seam");
    AssertTrue((pm.BottomRight[0] - pm.BottomRight[last]).Length < 0.001, "preview bottom-right edge lines up at the seam");
}

// ---------------------------------------------------------------------------
// CRITICAL: closed-loop seam twist (frame holonomy)
//
// A closed loop is a chain whose first and last control points coincide (the
// "seam"). This test guards against the two ways a closed loop can break:
//
// 1. FLOW – the seam is two coincident chain points (first + last) that never
//    get deduplicated, because closing a loop means merging a chain with ITSELF,
//    which does not fall under any of the four `MergeChains` join cases. Normal
//    joins deduplicate the shared point into ONE interior spline point (so the
//    road flows through it, C1-continuous); the closed seam never gets that, so
//    it was two clamped endpoints -> a broken/loose loop. Fix: `RoadCurve`
//    evaluates the spline cyclically (`closed`), treating the seam as a single
//    interior point so the centerline flows through it like a normal join.
//
// 2. TWIST – even with the centerline flowing, the road cross-section is carried
//    by a parallel-transported frame (`FrameWalker`). Around a NON-PLANAR loop
//    (any loop that changes Z), that frame does not return to its starting
//    orientation at the seam: it accumulates a residual rotation (holonomy). So
//    the opening cross-section and the closing cross-section are rotated relative
//    to one another -> the road looks "twisted wrong" at the seam.
//
//    Why 2 tracks works but 3+ fails: a simple 2-track loop happens to come back
//    with ~zero twist, while each extra track adds more elevation/turning, i.e.
//    more holonomy, so the twist grows with track count (a 3-track loop measured
//    a 4.73-unit mismatch, and it only gets larger with more tracks).
//
//    Fix: `RoadSurface.ClosedLoopTwist` measures the residual rotation between the
//    loop's first and last frames, and `RoadSurface.TwistCorrected` distributes it
//    EVENLY across every sample (rotate the cross-section around its tangent by
//    `-twist * t/maxT`). This smoothly returns the cross-section to its start at
//    the seam. It is deliberately NOT "force the last sample to equal the first",
//    which snapped the whole cross-section and caused a sharp turn at the end.
//
//    The correction is applied in `RoadPreviewMesh.Build` (preview) and in
//    `RoadSurface.SampleGrid` via `RoadGenerator.ClosedLoopTwistOf` (export).
//
// This test builds N-track "roller-coaster" loops (a circle with Z always rising
// and falling, so they are never planar) and asserts the seam cross-section
// returns for 3, 4, 5 and 6 tracks. Before the twist fix this failed with a
// growing mismatch; with the fix it returns cleanly for every track count.
// ---------------------------------------------------------------------------
void TestClosedLoopSeamReturnsForAnyTrackCount()
{
    for (int trackCount = 3; trackCount <= 6; trackCount++)
    {
        RoadDocument doc = BuildRollerCoasterLoop(trackCount);
        RoadChain chain = doc.BuildChains()[0];

        // The seam must be detected so the road is treated as one continuous loop.
        AssertTrue(chain.Closed, $"{trackCount}-track loop is detected as closed");

        // The seam cross-section must return: the last sample's left/right edge
        // must coincide with the first sample's, otherwise the road is twisted.
        RoadPreviewMesh pm = RoadPreviewMesh.Build(chain.Points, 24, chain.Closed);
        int last = pm.Center.Count - 1;
        double edgeMismatch = Math.Max((pm.Left[0] - pm.Left[last]).Length, (pm.Right[0] - pm.Right[last]).Length);
        AssertTrue(edgeMismatch < 0.01, $"{trackCount}-track loop cross-section returns at the seam (mismatch {edgeMismatch:0.###})");
    }
}

// Build a closed loop of `trackCount` tracks arranged around a circle, each track
// a 3-point arc (start / mid / end) so joins are smooth even for 2 tracks, with Z
// rising/falling along the loop so it is never planar (which is what triggers the
// frame twist).
RoadDocument BuildRollerCoasterLoop(int trackCount)
{
    RoadDocument doc = new RoadDocument();
    for (int t = 0; t < trackCount; t++)
    {
        Track track = t == 0 ? doc.Tracks[0] : new Track("T" + t);
        if (t > 0)
        {
            doc.Tracks.Add(track);
        }

        double a0 = 2.0 * Math.PI * t / trackCount;
        double a1 = 2.0 * Math.PI * (t + 1) / trackCount;
        Vec3 p0 = new Vec3(Math.Cos(a0) * 512, Math.Sin(a0) * 512, 128 * Math.Sin(a0 * 2));
        Vec3 p1 = new Vec3(Math.Cos((a0 + a1) / 2) * 512, Math.Sin((a0 + a1) / 2) * 512, 128 * Math.Sin((a0 + a1)));
        Vec3 p2 = new Vec3(Math.Cos(a1) * 512, Math.Sin(a1) * 512, 128 * Math.Sin(a1 * 2));
        track.Points.Add(new RoadPoint(p0, 256, 0));
        track.Points.Add(new RoadPoint(p1, 256, 0));
        track.Points.Add(new RoadPoint(p2, 256, 0));
    }

    return doc;
}

// ---------------------------------------------------------------------------
// CRITICAL: a closed loop's VMF seam cross-section returns (no twist in export)
//
// The preview fix measured the twist with its own build walker, but the EXPORT
// (RoadGenerator.ClosedLoopTwistOf) measured it with a SEPARATE coarse walker that
// steps one sample per control point. Because FrameWalker parallel transport
// depends on the chords between samples, the coarse walker measures a different
// holonomy than the fine walker that builds the exported geometry — so the exported
// VMF still appears twisted at the seam even after the preview is fixed. This test
// exports the closed loop and checks that the FIRST displacement segment's
// cross-section coincides with the LAST one's (the two sides of the seam).
// ---------------------------------------------------------------------------

void TestClosedLoopExportSeamReturns()
{
    RoadDocument doc = BuildRollerCoasterLoop(4);
    string vmf = RoadGenerator.GenerateVmf(doc);
    var planes = ExtractTopFacePlanes(vmf);
    AssertTrue(planes.Count >= 2, "loop exports at least two road solids");

    // First solid: A = grid[0,0] = the actual left-edge surface point @ t=0.
    // Last solid:  B = grid[res,0] = the actual left-edge surface point at the seam
    // (t = count-1). These are both REAL surface corners stored as plane anchors.
    // The right edge can't be read this way because only the parallelogram corner
    // A + C - B is stored for the non-anchor corner, which a curved/banked segment
    // displaces away from — so we validate the left edge, which (because the loop
    // closes its centerline and wraps width back to the first point) is sufficient:
    // matching left edges implies the seam frame is un-twisted, so the right edge
    // must match too.
    Vec3 leftAt0 = planes[0].A;
    Vec3 leftAtSeam = planes[planes.Count - 1].B;

    double leftMismatch = (leftAtSeam - leftAt0).Length;
    AssertTrue(leftMismatch < 0.01, $"exported road left edge returns at the seam (mismatch {leftMismatch:0.###})");
}

List<(Vec3 A, Vec3 B, Vec3 C)> ExtractTopFacePlanes(string vmf)
{
    List<(Vec3, Vec3, Vec3)> planes = new List<(Vec3, Vec3, Vec3)>();
    int position = 0;
    while ((position = vmf.IndexOf("\tsolid\r\n", position, StringComparison.Ordinal)) >= 0)
    {
        int sideStart = vmf.IndexOf("\t\tside\r\n", position, StringComparison.Ordinal);
        int planeStart = vmf.IndexOf("\"plane\" \"", sideStart, StringComparison.Ordinal) + "\"plane\" \"".Length;
        int planeEnd = vmf.IndexOf('"', planeStart);
        string planeText = vmf.Substring(planeStart, planeEnd - planeStart).Replace("(", "").Replace(")", "");
        string[] parts = planeText.Split(' ');
        Vec3 a = new Vec3(
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture));
        Vec3 b = new Vec3(
            double.Parse(parts[3], CultureInfo.InvariantCulture),
            double.Parse(parts[4], CultureInfo.InvariantCulture),
            double.Parse(parts[5], CultureInfo.InvariantCulture));
        Vec3 c = new Vec3(
            double.Parse(parts[6], CultureInfo.InvariantCulture),
            double.Parse(parts[7], CultureInfo.InvariantCulture),
            double.Parse(parts[8], CultureInfo.InvariantCulture));
        planes.Add((a, b, c));
        position += 1;
    }

    return planes;
}

// ---------------------------------------------------------------------------
// CRITICAL: sidewalk stays glued to the road edge on a closed loop
//
// The road surface is twist-corrected on a closed loop, so the sidewalk frame must
// receive the SAME correction or it decouples from the road edge. This test builds a
// closed loop with a constant-width left sidewalk on every track (they merge into one
// strip around the loop) and asserts the strip's inner/outer edges return to their
// start at the seam, just like the road does. It guards the `EdgePreviewMesh.Build`
// and `RoadGenerator.SampleEdgeGrid` paths.
// ---------------------------------------------------------------------------
void TestClosedLoopSidewalkStaysOnEdge()
{
    RoadDocument doc = BuildRollerCoasterLoop(4);
    foreach (Track track in doc.Tracks)
    {
        EdgeFeature sw = new EdgeFeature { Kind = EdgeFeatureKind.Sidewalk, LeftSide = true };
        for (int i = 0; i < track.Points.Count; i++)
        {
            sw.Points.Add(new EdgeFeaturePoint { Width = 128, TopOffset = 64, BottomOffset = 0, BankDegrees = 0 });
        }

        track.EdgeFeatures.Add(sw);
    }

    RoadChain chain = doc.BuildChains()[0];
    AssertTrue(chain.Closed, "loop is detected as closed");

    List<ChainFeature> features = chain.CollectFeatures();
    ChainFeature strip = features.FirstOrDefault(f => f.Feature.Kind == EdgeFeatureKind.Sidewalk && f.Feature.LeftSide);
    AssertTrue(strip != null, "a left sidewalk strip exists around the loop");

    EdgePreviewMesh em = EdgePreviewMesh.Build(chain.Points, 24, strip, chain.Closed);
    AssertTrue(em.InnerTop.Count >= 2, "sidewalk strip is sampled");
    int last = em.InnerTop.Count - 1;
    double innerMismatch = (em.InnerTop[0] - em.InnerTop[last]).Length;
    double outerMismatch = (em.OuterTop[0] - em.OuterTop[last]).Length;
    AssertTrue(innerMismatch < 0.01, $"sidewalk inner edge returns at the seam (mismatch {innerMismatch:0.###})");
    AssertTrue(outerMismatch < 0.01, $"sidewalk outer edge returns at the seam (mismatch {outerMismatch:0.###})");
}

// ---------------------------------------------------------------------------
// Edge feature chain resolution
// ---------------------------------------------------------------------------

void TestSideAwareSplit()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 128));

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    second.Points.Add(new RoadPoint(new Vec3(-512, 0, 0), 256, 0));
    second.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 256));
    document.Tracks.Add(second);

    RoadChain chain = document.BuildChains()[0];
    List<ChainFeature> features = chain.CollectFeatures();

    // A start-to-start join reverses one span, so one left sidewalk becomes a
    // chain-right strip and the two strips stay on their own physical sides.
    AssertEqual(2, features.Count, "start-to-start join splits the sidewalk");
    AssertTrue(features.Any(feature => feature.Feature.LeftSide), "one chain-left strip");
    AssertTrue(features.Any(feature => !feature.Feature.LeftSide), "one chain-right strip");
}

void TestWidthConcatenation()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 100)); // widths 100, 132

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    second.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    second.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 200)); // widths 200, 232
    document.Tracks.Add(second);

    RoadChain chain = document.BuildChains()[0];
    List<ChainFeature> features = chain.CollectFeatures();

    AssertEqual(1, features.Count, "same side + kind merges into one strip");
    ChainFeature strip = features[0];

    // The strip must span the FULL joined road — starting at the first control
    // point and reaching the last one — not stopping one control point short of
    // the weld.
    AssertEqual(0, strip.StartPoint, "sidewalk starts at the first control point");
    AssertEqual(chain.Points.Count, strip.EndPoint, "sidewalk reaches the last control point (not cut off one early)");
    AssertEqual(chain.Points.Count, strip.Points.Count, "one width per control point");

    AssertEqual(100.0, strip.Points[0].Width, "first width");
    AssertEqual(132.0, strip.Points[1].Width, "junction width from first track");
    AssertEqual(232.0, strip.Points[2].Width, "second track's last width");
}

void TestStopsAtFeaturelessTrack()
{
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 128));

    Track middle = new Track("B"); // no sidewalk
    middle.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    middle.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    document.Tracks.Add(middle);

    Track last = new Track("C");
    last.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    last.Points.Add(new RoadPoint(new Vec3(1536, 0, 0), 256, 0));
    last.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 200));
    document.Tracks.Add(last);

    RoadChain chain = document.BuildChains()[0];
    List<ChainFeature> features = chain.CollectFeatures();

    AssertEqual(2, features.Count, "sidewalk stops at the featureless track and restarts after it");
}

void TestPointAtInterpolates()
{
    ChainFeature strip = new ChainFeature { StartPoint = 0, EndPoint = 3 };
    strip.Points.Add(new EdgeFeaturePoint { Width = 100, TopOffset = 50, BottomOffset = 0, BankDegrees = 0 });
    strip.Points.Add(new EdgeFeaturePoint { Width = 200, TopOffset = 60, BottomOffset = 10, BankDegrees = 4 });
    strip.Points.Add(new EdgeFeaturePoint { Width = 300, TopOffset = 70, BottomOffset = 20, BankDegrees = 8 });

    EdgeFeaturePoint midpoint = strip.PointAt(0.5);
    AssertEqual(150.0, midpoint.Width, "width midpoint");
    AssertEqual(55.0, midpoint.TopOffset, "top offset midpoint");
    AssertEqual(5.0, midpoint.BottomOffset, "bottom offset midpoint");
    AssertEqual(2.0, midpoint.BankDegrees, "bank midpoint");
}

// ---------------------------------------------------------------------------
// VMF export
// ---------------------------------------------------------------------------

void TestWindingConsistent()
{
    RoadDocument document = new RoadDocument();
    document.Settings.Power = 2;
    document.Settings.SegmentLength = 512;

    Track track = document.Tracks[0];
    track.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    track.Points.Add(new RoadPoint(new Vec3(512, 256, 0), 256, 0));
    track.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    track.EdgeFeatures.Add(MakeSidewalk(3, leftSide: true, baseWidth: 128));
    track.EdgeFeatures.Add(MakeSidewalk(3, leftSide: false, baseWidth: 128));

    string vmf = RoadGenerator.GenerateVmf(document);
    List<double> normalZs = ExtractTopFaceNormalZs(vmf);

    AssertTrue(normalZs.Count >= 6, "road plus two sidewalk strips should produce solids");

    // Every top face must wind the same way. A left-side strip used to wind
    // opposite to the road, which made Hammer reject it as an invalid solid.
    double firstSign = Math.Sign(normalZs[0]);
    foreach (double normalZ in normalZs)
    {
        AssertTrue(Math.Sign(normalZ) == firstSign, $"top-face winding flipped (normalZ = {normalZ})");
    }
}

void TestSegmentCountMatchesExport()
{
    RoadDocument document = new RoadDocument();
    document.Settings.Power = 2;
    document.Settings.SegmentLength = 300;

    Track track = document.Tracks[0];
    track.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    track.Points.Add(new RoadPoint(new Vec3(512, 256, 0), 256, 0));
    track.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));

    int expected = SegmentLayout.CountSegments(track.Points, document.Settings.SegmentLength);
    string vmf = RoadGenerator.GenerateVmf(document);
    int solids = CountOccurrences(vmf, "\tsolid\r\n");

    AssertEqual(expected, solids, "exported solids match SegmentLayout.CountSegments");
}

// ---------------------------------------------------------------------------
// CRITICAL: a joined chain's sidewalk displacement count uses each track's own
// optimization (segment length), not the chain's single (first) settings.
//
// The road body already subdivides per-segment via SettingsForSegment, but the
// edge features used chain.Settings for the whole chain. So two tracks with
// different segment lengths, welded together, would use the first track's value
// for the second track's span — changing the sidewalk brush count. This test
// exports the same two tracks both joined and separate and asserts they produce
// the same number of solids (the geometry is identical, just deduplicated at the
// weld), which only holds when each span honors its own track's segment length.
// ---------------------------------------------------------------------------

void TestJoinedSidewalkUsesPerTrackOptimization()
{
    int joined = ExportSolidsFor(segmentLengthA: 300, segmentLengthB: 500, join: true);
    int separate = ExportSolidsFor(segmentLengthA: 300, segmentLengthB: 500, join: false);
    AssertEqual(separate, joined, "joined sidewalk/road brush count matches per-track optimization");
}

int ExportSolidsFor(double segmentLengthA, double segmentLengthB, bool join)
{
    RoadDocument document = new RoadDocument();
    Track a = document.Tracks[0];
    a.Settings.Power = 2;
    a.Settings.SegmentLength = segmentLengthA;
    a.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    a.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    a.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 128));

    Track b = new Track("B");
    b.Settings.Power = 2;
    b.Settings.SegmentLength = segmentLengthB;
    b.EnableJoining = join;
    b.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0)); // shares a's last point
    b.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    b.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 128));
    document.Tracks.Add(b);

    string vmf = RoadGenerator.GenerateVmf(document);
    return CountOccurrences(vmf, "\tsolid\r\n");
}

List<double> ExtractTopFaceNormalZs(string vmf)
{
    List<double> normalZs = new List<double>();
    int position = 0;
    while ((position = vmf.IndexOf("\tsolid\r\n", position, StringComparison.Ordinal)) >= 0)
    {
        int sideStart = vmf.IndexOf("\t\tside\r\n", position, StringComparison.Ordinal);
        int planeStart = vmf.IndexOf("\"plane\" \"", sideStart, StringComparison.Ordinal) + "\"plane\" \"".Length;
        int planeEnd = vmf.IndexOf('"', planeStart);

        // The plane is written as three "(x y z)" points, so strip the parens
        // before splitting into numbers.
        string planeText = vmf.Substring(planeStart, planeEnd - planeStart).Replace("(", "").Replace(")", "");
        string[] parts = planeText.Split(' ');
        double ax = double.Parse(parts[0], CultureInfo.InvariantCulture);
        double ay = double.Parse(parts[1], CultureInfo.InvariantCulture);
        double az = double.Parse(parts[2], CultureInfo.InvariantCulture);
        double bx = double.Parse(parts[3], CultureInfo.InvariantCulture);
        double by = double.Parse(parts[4], CultureInfo.InvariantCulture);
        double bz = double.Parse(parts[5], CultureInfo.InvariantCulture);
        double cx = double.Parse(parts[6], CultureInfo.InvariantCulture);
        double cy = double.Parse(parts[7], CultureInfo.InvariantCulture);
        double cz = double.Parse(parts[8], CultureInfo.InvariantCulture);

        // Face normal = (B - A) x (C - A); only the Z component matters here.
        double normalZ = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        normalZs.Add(normalZ);

        position += 1;
    }

    return normalZs;
}

int CountOccurrences(string haystack, string needle)
{
    int count = 0;
    int index = 0;
    while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += needle.Length;
    }

    return count;
}

// ---------------------------------------------------------------------------
// Track file round-trip + migration
// ---------------------------------------------------------------------------

void TestTrackFileRoundTrip()
{
    RoadDocument document = new RoadDocument();
    Track track = document.Tracks[0];
    track.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0, 64));
    track.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 300, 8, 80));

    EdgeFeature feature = new EdgeFeature { Kind = EdgeFeatureKind.Sidewalk, LeftSide = true };
    feature.Points.Add(new EdgeFeaturePoint { Width = 100, TopOffset = 64, BottomOffset = 0, BankDegrees = 1 });
    feature.Points.Add(new EdgeFeaturePoint { Width = 150, TopOffset = 80, BottomOffset = 8, BankDegrees = 3 });
    feature.Enabled.Add(true);
    feature.Enabled.Add(false);
    track.EdgeFeatures.Add(feature);

    string path = Path.Combine(Path.GetTempPath(), "roadgen_roundtrip_" + Guid.NewGuid().ToString("N") + ".trk");
    try
    {
        TrackFile.Save(document, path);
        RoadDocument loaded = TrackFile.Load(path).Document;

        AssertEqual(1, loaded.Tracks.Count, "track count");
        AssertEqual(2, loaded.Tracks[0].Points.Count, "point count");
        AssertEqual(300.0, loaded.Tracks[0].Points[1].Width, "road width");

        AssertEqual(1, loaded.Tracks[0].EdgeFeatures.Count, "feature count");
        EdgeFeature loadedFeature = loaded.Tracks[0].EdgeFeatures[0];
        AssertEqual(2, loadedFeature.Points.Count, "feature point count");
        AssertEqual(150.0, loadedFeature.Points[1].Width, "feature width");
        AssertEqual(3.0, loadedFeature.Points[1].BankDegrees, "feature bank");
        AssertEqual(2, loadedFeature.Enabled.Count, "coverage mask count");
        AssertEqual(false, loadedFeature.Enabled[1], "coverage mask value");
    }
    finally
    {
        File.Delete(path);
    }
}

void TestTrackFileMigrationV4()
{
    string v4Json = """
{
  "Version": 4,
  "Tracks": [
    {
      "Name": "T",
      "Points": [
        { "X": 0, "Y": 0, "Z": 0, "Width": 256, "Bank": 0 },
        { "X": 512, "Y": 0, "Z": 0, "Width": 256, "Bank": 0 },
        { "X": 1024, "Y": 0, "Z": 0, "Width": 256, "Bank": 0 }
      ],
      "EdgeFeatures": [
        { "Kind": "Sidewalk", "LeftSide": true, "Offset": 0, "Width": 200, "BottomOffset": 8, "TopOffset": 72, "SolidBottom": true, "SolidInner": true, "SolidOuter": true, "Material": "CONCRETE/CONCRETEFLOOR005A" }
      ]
    }
  ]
}
""";

    string path = Path.Combine(Path.GetTempPath(), "roadgen_migration_" + Guid.NewGuid().ToString("N") + ".trk");
    try
    {
        File.WriteAllText(path, v4Json);
        RoadDocument loaded = TrackFile.Load(path).Document;

        AssertEqual(1, loaded.Tracks.Count, "track count");
        AssertEqual(1, loaded.Tracks[0].EdgeFeatures.Count, "feature count");

        EdgeFeature feature = loaded.Tracks[0].EdgeFeatures[0];
        AssertEqual(3, feature.Points.Count, "v4 scalars expand to one point per road point");
        foreach (EdgeFeaturePoint point in feature.Points)
        {
            AssertEqual(200.0, point.Width, "expanded width");
            AssertEqual(8.0, point.BottomOffset, "expanded bottom offset");
            AssertEqual(72.0, point.TopOffset, "expanded top offset");
            AssertEqual(0.0, point.BankDegrees, "bank defaults to zero");
        }
    }
    finally
    {
        File.Delete(path);
    }
}

// ---------------------------------------------------------------------------
// The Merge button (RoadDocument.MergeChain)
// ---------------------------------------------------------------------------

void TestMergeKeepsSidewalkContinuous()
{
    // Two roads welded end-to-start, each with a left sidewalk of a different
    // width. After the user clicks Merge, the sidewalk must be ONE strip covering
    // the whole merged road — reaching the very last control point — with widths
    // blending across the old weld instead of snapping to one road's value.
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 100)); // widths 100, 132

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    second.Points.Add(new RoadPoint(new Vec3(1024, 0, 0), 256, 0));
    second.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 200)); // widths 200, 232
    document.Tracks.Add(second);

    RoadChain chain = document.BuildChains()[0];
    Track merged = document.MergeChain(chain, "Merged", first.Settings, first.EnableJoining);

    AssertEqual(3, merged.Points.Count, "merged road keeps every control point");
    AssertEqual(1, merged.EdgeFeatures.Count, "one continuous sidewalk");

    EdgeFeature sidewalk = merged.EdgeFeatures[0];
    AssertEqual(3, sidewalk.Points.Count, "sidewalk reaches the last control point (not cut off one early)");
    AssertEqual(0, sidewalk.Enabled.Count, "full coverage, so no per-point mask");
    AssertEqual(100.0, sidewalk.Points[0].Width, "first width");
    AssertEqual(132.0, sidewalk.Points[1].Width, "width at the old weld");
    AssertEqual(232.0, sidewalk.Points[2].Width, "last width");
}

void TestMergeKeepsEachSidewalkOnItsOwnSide()
{
    // Two roads share a start point and each has a left sidewalk. Clicking Merge
    // must keep each sidewalk on its ORIGINAL physical side — one strip on the
    // left for one road, one on the right for the other — instead of flipping a
    // sidewalk to the wrong side. The per-point coverage mask records where each
    // strip actually exists.
    RoadDocument document = new RoadDocument();
    Track first = document.Tracks[0];
    first.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    first.Points.Add(new RoadPoint(new Vec3(512, 0, 0), 256, 0));
    first.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 128));

    Track second = new Track("B");
    second.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    second.Points.Add(new RoadPoint(new Vec3(-512, 0, 0), 256, 0));
    second.EdgeFeatures.Add(MakeSidewalk(2, leftSide: true, baseWidth: 256));
    document.Tracks.Add(second);

    RoadChain chain = document.BuildChains()[0];
    Track merged = document.MergeChain(chain, "Merged", first.Settings, first.EnableJoining);

    AssertEqual(3, merged.Points.Count, "merged road keeps every control point");
    AssertEqual(2, merged.EdgeFeatures.Count, "two strips, one per physical side");

    EdgeFeature left = merged.EdgeFeatures.First(feature => feature.LeftSide);
    EdgeFeature right = merged.EdgeFeatures.First(feature => !feature.LeftSide);

    AssertEqual(3, left.Points.Count, "left strip has one value per control point");
    AssertEqual(3, right.Points.Count, "right strip has one value per control point");
    AssertEqual(3, left.Enabled.Count, "left strip stores its coverage mask");
    AssertEqual(3, right.Enabled.Count, "right strip stores its coverage mask");

    // The left strip covers the second half of the merged road (first's portion);
    // the right strip covers the first half (second's portion).
    AssertEqual(false, left.Enabled[0], "left strip is absent before its road");
    AssertEqual(true, left.Enabled[1], "left strip starts at the shared point");
    AssertEqual(true, left.Enabled[2], "left strip runs to the end");
    AssertEqual(true, right.Enabled[0], "right strip runs from the start");
    AssertEqual(true, right.Enabled[1], "right strip reaches the shared point");
    AssertEqual(false, right.Enabled[2], "right strip stops at the shared point");
}

// ---------------------------------------------------------------------------
// Undo
// ---------------------------------------------------------------------------

void TestUndoRestoresSnapshot()
{
    RoadDocument document = new RoadDocument();
    UndoManager undo = new UndoManager(document);

    undo.RecordSingle();
    document.Tracks[0].EdgeFeatures.Add(MakeSidewalk(1, leftSide: true, baseWidth: 128));
    AssertEqual(1, document.Tracks[0].EdgeFeatures.Count, "feature added");

    undo.Undo();
    AssertEqual(0, document.Tracks[0].EdgeFeatures.Count, "undo removes the feature");

    undo.Redo();
    AssertEqual(1, document.Tracks[0].EdgeFeatures.Count, "redo restores the feature");
}

void TestUndoBatchCoalesces()
{
    RoadDocument document = new RoadDocument();
    document.Tracks[0].Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
    UndoManager undo = new UndoManager(document);

    undo.BeginBatch();
    document.Tracks[0].Points[0].Width = 512;
    document.Tracks[0].Points[0].BankDegrees = 45;
    undo.EndBatch();

    AssertTrue(undo.CanUndo, "a batch produces one undo step");
    undo.Undo();

    AssertEqual(256.0, document.Tracks[0].Points[0].Width, "width restored");
    AssertEqual(0.0, document.Tracks[0].Points[0].BankDegrees, "bank restored");
    AssertTrue(!undo.CanUndo, "the whole batch undid in one step");
}

