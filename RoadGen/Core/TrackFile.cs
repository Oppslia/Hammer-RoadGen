using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoadGen.Core;

/// <summary>Reads and writes the native RoadGen track format (.trk).
///
/// VERSIONING
/// ----------
/// Every file carries a "Version". <see cref="CurrentVersion"/> is what this
/// build writes. On load, <see cref="Migrations"/> runs every step from the
/// file's version up to CurrentVersion, in order, so old files are upgraded
/// automatically and the caller can offer to re-save them.
///
/// TO ADD A NEW FORMAT CHANGE (e.g. a new setting field):
///   1. Bump CurrentVersion by 1 (2 -> 3).
///   2. Add a Migrate2To3 method that edits the JSON tree (see Migrate1To2).
///   3. Append it to the Migrations[] array (index = old version - 1).
///   4. Update SettingsData/PointData plus BuildDocument()/Save() for the new
///      shape, so current files write and read the new field.
/// That's it — files of ANY older version chain through the new migration on
/// load, and the Open dialog offers to upgrade them.
///
/// MIGRATION RULES:
///   - Bridge exactly one step: assume the file is already at version N and
///     produce N+1 (all previous migrations have already run).
///   - Guard additions with `if (obj["key"] == null)` so re-running is safe.
///   - Fill new fields with their OLD-behavior default so an upgraded file
///     still represents the exact same road.</summary>
public static class TrackFile
{
    /// <summary>The version this build writes to disk. Bump whenever the format changes.</summary>
    public const int CurrentVersion = 2;

    /// <summary>One entry per historical version gap. Index 0 migrates v1->v2,
    /// index 1 migrates v2->v3, and so on.</summary>
    private static readonly Action<JsonObject>[] Migrations =
    {
        Migrate1To2
        // Migrate2To3, Migrate3To4, ... append future migrations here.
    };

    public sealed class TrackLoadResult
    {
        public RoadDocument Document = new RoadDocument();
        public bool NeedsUpgrade;
        public int FromVersion;
        public int ToVersion;
    }

    private sealed class TrackData
    {
        public int Version { get; set; } = 1;
        public SettingsData Settings { get; set; } = new SettingsData();
        public List<PointData> Points { get; set; } = new List<PointData>();
    }

    private sealed class SettingsData
    {
        public int Power { get; set; }
        public string Material { get; set; } = "CONCRETE/CONCRETEFLOOR005A";
        public double Thickness { get; set; }
        // Always present after migration; false is only a fallback for malformed files.
        public bool SolidLeft { get; set; }
        public bool SolidRight { get; set; }
        public bool SolidBottom { get; set; }
        public double SegmentLength { get; set; }
        public double TextureScale { get; set; }
        public int LightmapScale { get; set; }
        public double Snap { get; set; }
        public bool IncUseGridX { get; set; } = true;
        public bool IncUseGridY { get; set; } = true;
        public bool IncUseGridZ { get; set; } = true;
        public bool IncUseGridWidth { get; set; } = true;
        public bool IncUseGridBank { get; set; } = false;
        public double IncCustomX { get; set; } = 64;
        public double IncCustomY { get; set; } = 64;
        public double IncCustomZ { get; set; } = 64;
        public double IncCustomWidth { get; set; } = 64;
        public double IncCustomBank { get; set; } = 4;
    }

    private sealed class PointData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Width { get; set; }
        public double Bank { get; set; }
    }

    public static void Save(RoadDocument doc, string path)
    {
        var data = new TrackData
        {
            Version = CurrentVersion
        };
        data.Settings = new SettingsData
        {
            Power = doc.Settings.Power,
            Material = doc.Settings.Material,
            Thickness = doc.Settings.Thickness,
            SolidLeft = doc.Settings.SolidLeft,
            SolidRight = doc.Settings.SolidRight,
            SolidBottom = doc.Settings.SolidBottom,
            SegmentLength = doc.Settings.SegmentLength,
            TextureScale = doc.Settings.TextureScale,
            LightmapScale = doc.Settings.LightmapScale,
            Snap = doc.Settings.Snap,
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
            data.Points.Add(new PointData
            {
                X = p.Position.X,
                Y = p.Position.Y,
                Z = p.Position.Z,
                Width = p.Width,
                Bank = p.BankDegrees
            });
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(data, options));
    }

    public static TrackLoadResult Load(string path)
    {
        string json = File.ReadAllText(path);
        JsonObject root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("The track file is empty or invalid.");

        int version = root["Version"]?.GetValue<int>() ?? 1;
        bool needsUpgrade = version < CurrentVersion;

        // Chain every migration from the file's version up to the current one.
        for (int v = version; v < CurrentVersion; v++)
        {
            Migrations[v - 1](root);
        }

        root["Version"] = CurrentVersion;

        var data = JsonSerializer.Deserialize<TrackData>(root)
            ?? throw new InvalidDataException("The track file is invalid.");

        return new TrackLoadResult
        {
            Document = BuildDocument(data),
            NeedsUpgrade = needsUpgrade,
            FromVersion = version,
            ToVersion = CurrentVersion
        };
    }

    private static RoadDocument BuildDocument(TrackData data)
    {
        var doc = new RoadDocument();
        if (data.Settings != null)
        {
            doc.Settings.Power = data.Settings.Power;
            doc.Settings.Material = string.IsNullOrWhiteSpace(data.Settings.Material)
                ? "CONCRETE/CONCRETEFLOOR005A"
                : data.Settings.Material;
            doc.Settings.Thickness = data.Settings.Thickness;
            doc.Settings.SolidLeft = data.Settings.SolidLeft;
            doc.Settings.SolidRight = data.Settings.SolidRight;
            doc.Settings.SolidBottom = data.Settings.SolidBottom;
            doc.Settings.SegmentLength = data.Settings.SegmentLength;
            doc.Settings.TextureScale = data.Settings.TextureScale;
            doc.Settings.LightmapScale = data.Settings.LightmapScale;
            doc.Settings.Snap = data.Settings.Snap;
            doc.Settings.IncUseGridX = data.Settings.IncUseGridX;
            doc.Settings.IncUseGridY = data.Settings.IncUseGridY;
            doc.Settings.IncUseGridZ = data.Settings.IncUseGridZ;
            doc.Settings.IncUseGridWidth = data.Settings.IncUseGridWidth;
            doc.Settings.IncUseGridBank = data.Settings.IncUseGridBank;
            doc.Settings.IncCustomX = data.Settings.IncCustomX;
            doc.Settings.IncCustomY = data.Settings.IncCustomY;
            doc.Settings.IncCustomZ = data.Settings.IncCustomZ;
            doc.Settings.IncCustomWidth = data.Settings.IncCustomWidth;
            doc.Settings.IncCustomBank = data.Settings.IncCustomBank;
        }

        if (data.Points != null)
        {
            foreach (PointData p in data.Points)
            {
                doc.Points.Add(new RoadPoint(new Vec3(p.X, p.Y, p.Z), p.Width, p.Bank));
            }
        }

        return doc;
    }

    /// <summary>v1 -> v2: adds the fields introduced with Solid Roads and the
    /// increment/decrement interval settings. Old tracks had no walls/bottom, so
    /// the Solid Roads keys default to off; the increment keys use their standard
    /// defaults.</summary>
    private static void Migrate1To2(JsonObject root)
    {
        if (root["Settings"] is not JsonObject s)
        {
            return;
        }

        if (s["SolidLeft"] == null) s["SolidLeft"] = false;
        if (s["SolidRight"] == null) s["SolidRight"] = false;
        if (s["SolidBottom"] == null) s["SolidBottom"] = false;

        if (s["IncUseGridX"] == null) s["IncUseGridX"] = true;
        if (s["IncUseGridY"] == null) s["IncUseGridY"] = true;
        if (s["IncUseGridZ"] == null) s["IncUseGridZ"] = true;
        if (s["IncUseGridWidth"] == null) s["IncUseGridWidth"] = true;
        if (s["IncUseGridBank"] == null) s["IncUseGridBank"] = false;
        if (s["IncCustomX"] == null) s["IncCustomX"] = 64.0;
        if (s["IncCustomY"] == null) s["IncCustomY"] = 64.0;
        if (s["IncCustomZ"] == null) s["IncCustomZ"] = 64.0;
        if (s["IncCustomWidth"] == null) s["IncCustomWidth"] = 64.0;
        if (s["IncCustomBank"] == null) s["IncCustomBank"] = 4.0;
    }
}
