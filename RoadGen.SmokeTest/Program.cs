using RoadGen.Core;

var doc = new RoadDocument();
doc.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
doc.Points.Add(new RoadPoint(new Vec3(512, 256, 32), 256, 12));
doc.Points.Add(new RoadPoint(new Vec3(1024, 0, 96), 256, -12));
doc.Points.Add(new RoadPoint(new Vec3(1536, 256, 160), 256, 14));
doc.Points.Add(new RoadPoint(new Vec3(2048, 0, 224), 256, -14));
doc.Points.Add(new RoadPoint(new Vec3(2560, 256, 288), 256, 0));

doc.Settings.Power = 2; // keep the file small for inspection
doc.Settings.SegmentLength = 256;

string vmf = RoadGenerator.GenerateVmf(doc);

string outPath = Path.Combine(AppContext.BaseDirectory, "sample_road.vmf");
File.WriteAllText(outPath, vmf);

int solids = CountOccurrences(vmf, "\tsolid\r\n");
int dispInfo = CountOccurrences(vmf, "dispinfo\r\n");
Console.WriteLine($"Wrote {outPath}");
Console.WriteLine($"Solids: {solids}, Dispinfo blocks: {dispInfo}, bytes: {vmf.Length}");

// Verify that adjacent displacement segments share identical boundary vertices
// (this is what makes the road sew together with no cracks).
var walker = new FrameWalker();
int res = 1 << doc.Settings.Power;
var segA = RoadSurface.SampleGrid(doc.Points, 0.0, 0.5, res, walker);
var segB = RoadSurface.SampleGrid(doc.Points, 0.5, 1.0, res, walker);
double maxDiff = 0;
for (int col = 0; col <= res; col++)
{
    double d = (segA[res, col] - segB[0, col]).Length;
    maxDiff = Math.Max(maxDiff, d);
}

Console.WriteLine($"Seam max vertex delta: {maxDiff:E}");

// VMF import round-trip: reconstruct control points from the generated VMF.
var imported = VmfImporter.ImportRoad(vmf);
Console.WriteLine($"VMF import: {imported.Count} control points reconstructed");
Console.WriteLine($"  first imported: {imported[0].Position} width={imported[0].Width:0.##}");

// Native .trk round-trip.
string trkPath = Path.Combine(AppContext.BaseDirectory, "sample_road.trk");
TrackFile.Save(doc, trkPath);
RoadDocument reloaded = TrackFile.Load(trkPath).Document;
Console.WriteLine($"Track round-trip: {reloaded.Points.Count} points, power={reloaded.Settings.Power}, material={reloaded.Settings.Material}");

Console.WriteLine("First 600 chars:");
Console.WriteLine(vmf.Substring(0, Math.Min(600, vmf.Length)));

static int CountOccurrences(string haystack, string needle)
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
