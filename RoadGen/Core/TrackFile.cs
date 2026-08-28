using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RoadGen.Core;

/// <summary>Reads and writes the native RoadGen track format (.trk).
/// This is a small JSON document holding the control points and road settings,
/// independent of the VMF output.</summary>
public static class TrackFile
{
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
        public bool SolidLeft { get; set; } = true;
        public bool SolidRight { get; set; } = true;
        public bool SolidBottom { get; set; } = true;
        public double SegmentLength { get; set; }
        public double TextureScale { get; set; }
        public int LightmapScale { get; set; }
        public double Snap { get; set; }
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
        var data = new TrackData();
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
            Snap = doc.Settings.Snap
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

    public static RoadDocument Load(string path)
    {
        string json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<TrackData>(json)
            ?? throw new InvalidDataException("The track file is empty or invalid.");

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
}
