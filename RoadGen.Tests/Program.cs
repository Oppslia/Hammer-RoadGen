using System.Globalization;
using RoadGen.Core;

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
Run("Sidewalk stays on its own side when two road starts are joined", TestSideAwareSplit);
Run("Sidewalk width blends across a weld and reaches the last control point", TestWidthConcatenation);
Run("Sidewalk stops where a road has no sidewalk (doesn't run off the rails)", TestStopsAtFeaturelessTrack);
Run("Sidewalk width/height/bank ramp smoothly between control points", TestPointAtInterpolates);
Run("Left and right sidewalks export as valid (not inside-out) solids", TestWindingConsistent);
Run("Optimization preview matches the exported brush count", TestSegmentCountMatchesExport);
Run("Saving and reopening keeps sidewalk width/height/bank and coverage", TestTrackFileRoundTrip);
Run("Old files with one sidewalk size upgrade to per-point values", TestTrackFileMigrationV4);
Run("Merging two welded roads keeps the sidewalk continuous to the very end", TestMergeKeepsSidewalkContinuous);
Run("Merging a start-to-start join keeps each sidewalk on its own side", TestMergeKeepsEachSidewalkOnItsOwnSide);
Run("Undo removes a just-added sidewalk; redo brings it back", TestUndoRestoresSnapshot);
Run("Editing one field is a single undo step", TestUndoBatchCoalesces);

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
