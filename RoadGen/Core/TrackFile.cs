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
    public const int CurrentVersion = 6;

    /// <summary>One entry per historical version gap. Index 0 migrates v1->v2,
    /// index 1 migrates v2->v3, and so on.</summary>
    private static readonly Action<JsonObject>[] Migrations =
    {
        Migrate1To2,
        Migrate2To3,
        Migrate3To4,
        Migrate4To5,
        Migrate5To6
        // Migrate6To7, ... append future migrations here.
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
        public List<TrackItemData> Tracks { get; set; } = new List<TrackItemData>();
    }

    private sealed class TrackItemData
    {
        public string Name { get; set; } = "Track";
        // Nullable so files saved before this flag existed load as enabled.
        public bool? EnableJoining { get; set; }
        public SettingsData Settings { get; set; } = new SettingsData();
        public List<PointData> Points { get; set; } = new List<PointData>();
        public List<EdgeFeatureData> EdgeFeatures { get; set; } = new List<EdgeFeatureData>();
    }

    private sealed class EdgeFeatureData
    {
        public string Kind { get; set; } = "Sidewalk";
        public bool LeftSide { get; set; } = true;
        public double Offset { get; set; }
        public bool SolidBottom { get; set; } = true;
        public bool SolidInner { get; set; } = true;
        public bool SolidOuter { get; set; } = true;
        public string Material { get; set; } = "CONCRETE/CONCRETEFLOOR005A";
        public List<EdgeFeaturePointData> Points { get; set; } = new List<EdgeFeaturePointData>();
        // Null when every point is covered (files saved before per-point coverage
        // existed load as full coverage).
        public List<bool> Enabled { get; set; } = new List<bool>();
    }

    private sealed class EdgeFeaturePointData
    {
        public double Width { get; set; } = 128;
        public double BottomOffset { get; set; }
        public double TopOffset { get; set; } = 64;
        public double Bank { get; set; }
    }

    private sealed class SettingsData
    {
        public int Power { get; set; }
        public string Material { get; set; } = "CONCRETE/CONCRETEFLOOR005A";
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
        // Nullable so files saved before per-point thickness load as the default.
        public double? Thickness { get; set; }
    }

    public static void Save(RoadDocument document, string path)
    {
        TrackData data = new TrackData
        {
            Version = CurrentVersion
        };

        foreach (Track track in document.Tracks)
        {
            TrackItemData trackItem = new TrackItemData
            {
                Name = track.Name,
                EnableJoining = track.EnableJoining,
                Settings = new SettingsData
                {
                    Power = track.Settings.Power,
                    Material = track.Settings.Material,
                    SolidLeft = track.Settings.SolidLeft,
                    SolidRight = track.Settings.SolidRight,
                    SolidBottom = track.Settings.SolidBottom,
                    SegmentLength = track.Settings.SegmentLength,
                    TextureScale = track.Settings.TextureScale,
                    LightmapScale = track.Settings.LightmapScale,
                    Snap = track.Settings.Snap,
                    IncUseGridX = track.Settings.IncUseGridX,
                    IncUseGridY = track.Settings.IncUseGridY,
                    IncUseGridZ = track.Settings.IncUseGridZ,
                    IncUseGridWidth = track.Settings.IncUseGridWidth,
                    IncUseGridBank = track.Settings.IncUseGridBank,
                    IncCustomX = track.Settings.IncCustomX,
                    IncCustomY = track.Settings.IncCustomY,
                    IncCustomZ = track.Settings.IncCustomZ,
                    IncCustomWidth = track.Settings.IncCustomWidth,
                    IncCustomBank = track.Settings.IncCustomBank
                }
            };

            foreach (RoadPoint point in track.Points)
            {
                trackItem.Points.Add(new PointData
                {
                    X = point.Position.X,
                    Y = point.Position.Y,
                    Z = point.Position.Z,
                    Width = point.Width,
                    Bank = point.BankDegrees,
                    Thickness = point.Thickness
                });
            }

            foreach (EdgeFeature feature in track.EdgeFeatures)
            {
                EdgeFeatureData featureData = new EdgeFeatureData
                {
                    Kind = feature.Kind.ToString(),
                    LeftSide = feature.LeftSide,
                    Offset = feature.Offset,
                    SolidBottom = feature.SolidBottom,
                    SolidInner = feature.SolidInner,
                    SolidOuter = feature.SolidOuter,
                    Material = feature.Material
                };

                foreach (EdgeFeaturePoint point in feature.Points)
                {
                    featureData.Points.Add(new EdgeFeaturePointData
                    {
                        Width = point.Width,
                        BottomOffset = point.BottomOffset,
                        TopOffset = point.TopOffset,
                        Bank = point.BankDegrees
                    });
                }

                if (feature.Enabled.Count > 0)
                {
                    featureData.Enabled = new List<bool>(feature.Enabled);
                }
                else
                {
                    featureData.Enabled = null;
                }

                trackItem.EdgeFeatures.Add(featureData);
            }

            data.Tracks.Add(trackItem);
        }

        JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
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
        RoadDocument document = new RoadDocument();
        document.Tracks.Clear();

        if (data.Tracks != null && data.Tracks.Count > 0)
        {
            foreach (TrackItemData trackItem in data.Tracks)
            {
                string name = string.IsNullOrWhiteSpace(trackItem.Name) ? "Track" : trackItem.Name;
                Track track = new Track(name);
                track.EnableJoining = trackItem.EnableJoining ?? true;
                ApplySettings(track.Settings, trackItem.Settings);

                if (trackItem.Points != null)
                {
                    foreach (PointData point in trackItem.Points)
                    {
                        track.Points.Add(new RoadPoint(new Vec3(point.X, point.Y, point.Z), point.Width, point.Bank, point.Thickness ?? 64));
                    }
                }

                if (trackItem.EdgeFeatures != null)
                {
                    foreach (EdgeFeatureData featureData in trackItem.EdgeFeatures)
                    {
                        EdgeFeature feature = new EdgeFeature
                        {
                            Kind = ParseEdgeFeatureKind(featureData.Kind),
                            LeftSide = featureData.LeftSide,
                            Offset = featureData.Offset,
                            SolidBottom = featureData.SolidBottom,
                            SolidInner = featureData.SolidInner,
                            SolidOuter = featureData.SolidOuter,
                            Material = string.IsNullOrWhiteSpace(featureData.Material) ? "CONCRETE/CONCRETEFLOOR005A" : featureData.Material
                        };

                        if (featureData.Points != null)
                        {
                            foreach (EdgeFeaturePointData pointData in featureData.Points)
                            {
                                feature.Points.Add(new EdgeFeaturePoint
                                {
                                    Width = pointData.Width,
                                    BottomOffset = pointData.BottomOffset,
                                    TopOffset = pointData.TopOffset,
                                    BankDegrees = pointData.Bank
                                });
                            }
                        }

                        if (featureData.Enabled != null)
                        {
                            foreach (bool enabled in featureData.Enabled)
                            {
                                feature.Enabled.Add(enabled);
                            }
                        }

                        track.EdgeFeatures.Add(feature);
                    }
                }

                document.Tracks.Add(track);
            }
        }

        if (document.Tracks.Count == 0)
        {
            document.Tracks.Add(new Track("Track 1"));
        }

        document.ActiveTrackIndex = 0;
        return document;
    }

    private static void ApplySettings(RoadSettings settings, SettingsData data)
    {
        if (data == null)
        {
            return;
        }

        settings.Power = data.Power;
        settings.Material = string.IsNullOrWhiteSpace(data.Material) ? "CONCRETE/CONCRETEFLOOR005A" : data.Material;
        settings.SolidLeft = data.SolidLeft;
        settings.SolidRight = data.SolidRight;
        settings.SolidBottom = data.SolidBottom;
        settings.SegmentLength = data.SegmentLength;
        settings.TextureScale = data.TextureScale;
        settings.LightmapScale = data.LightmapScale;
        settings.Snap = data.Snap;
        settings.IncUseGridX = data.IncUseGridX;
        settings.IncUseGridY = data.IncUseGridY;
        settings.IncUseGridZ = data.IncUseGridZ;
        settings.IncUseGridWidth = data.IncUseGridWidth;
        settings.IncUseGridBank = data.IncUseGridBank;
        settings.IncCustomX = data.IncCustomX;
        settings.IncCustomY = data.IncCustomY;
        settings.IncCustomZ = data.IncCustomZ;
        settings.IncCustomWidth = data.IncCustomWidth;
        settings.IncCustomBank = data.IncCustomBank;
    }

    private static EdgeFeatureKind ParseEdgeFeatureKind(string value)
    {
        if (Enum.TryParse<EdgeFeatureKind>(value, out EdgeFeatureKind kind))
        {
            return kind;
        }

        return EdgeFeatureKind.Sidewalk;
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

    /// <summary>v2 -> v3: wrap the single top-level road into a Tracks array of one
    /// track, matching the multi-track document shape.</summary>
    private static void Migrate2To3(JsonObject root)
    {
        if (root["Tracks"] != null)
        {
            return; // already v3+
        }

        JsonNode settingsNode = root["Settings"];
        JsonNode pointsNode = root["Points"];

        JsonObject track = new JsonObject
        {
            ["Name"] = "Track 1"
        };

        if (settingsNode != null)
        {
            track["Settings"] = settingsNode.DeepClone();
        }

        if (pointsNode != null)
        {
            track["Points"] = pointsNode.DeepClone();
        }

        root["Tracks"] = new JsonArray(track);
        root.Remove("Settings");
        root.Remove("Points");
    }

    /// <summary>v3 -> v4: adds optional per-track edge features. Nothing to migrate —
    /// old tracks simply have an empty EdgeFeatures list.</summary>
    private static void Migrate3To4(JsonObject root)
    {
        // Edge features are a new optional field; no structural change is needed.
    }

    /// <summary>v4 -> v5: edge feature width/thickness/bank become per-control-point
    /// values. The old scalar Width/BottomOffset/TopOffset are expanded into a Points
    /// list with one entry per road control point (bank defaults to 0).</summary>
    private static void Migrate4To5(JsonObject root)
    {
        if (root["Tracks"] is not JsonArray tracks)
        {
            return;
        }

        foreach (JsonNode trackNode in tracks)
        {
            if (trackNode is not JsonObject track)
            {
                continue;
            }

            int roadPointCount = track["Points"] is JsonArray roadPoints ? roadPoints.Count : 0;
            if (track["EdgeFeatures"] is not JsonArray edgeFeatures)
            {
                continue;
            }

            foreach (JsonNode featureNode in edgeFeatures)
            {
                if (featureNode is not JsonObject feature || feature["Points"] != null)
                {
                    continue;
                }

                double width = feature["Width"]?.GetValue<double>() ?? 128.0;
                double bottomOffset = feature["BottomOffset"]?.GetValue<double>() ?? 0.0;
                double topOffset = feature["TopOffset"]?.GetValue<double>() ?? 64.0;

                JsonArray points = new JsonArray();
                for (int pointIndex = 0; pointIndex < roadPointCount; pointIndex++)
                {
                    points.Add(new JsonObject
                    {
                        ["Width"] = width,
                        ["BottomOffset"] = bottomOffset,
                        ["TopOffset"] = topOffset,
                        ["Bank"] = 0.0
                    });
                }

                feature["Points"] = points;
                feature.Remove("Width");
                feature.Remove("BottomOffset");
                feature.Remove("TopOffset");
            }
        }
    }

    /// <summary>v5 -> v6: adds an optional per-point coverage mask to edge features.
    /// Nothing to migrate — an absent mask means full coverage, which matches the old
    /// behaviour exactly.</summary>
    private static void Migrate5To6(JsonObject root)
    {
        // Enabled is a new optional field; no structural change is needed.
    }
}
