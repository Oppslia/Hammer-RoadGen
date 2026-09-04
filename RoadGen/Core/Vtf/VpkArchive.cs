using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RoadGen.Core.Vtf;

/// <summary>Reads a Source 1 game's "_dir.vpk" directory file (the current Valve VPK v2
/// layout) and serves individual file bytes from its chunk files on demand, so packed stock
/// materials (VTFs/VMTs) can be decoded just like loose files on disk.
///
/// Format (verified against real TF2 archives): a 28-byte header (signature "VPVP",
/// version 2, tree size) followed by a directory tree of null-terminated strings:
///   ext\0  ->  path\0  ->  ( name\0 + 18-byte entry )*  ->  empty name  ->  ...  ->  empty path  ->  next ext
/// Each 18-byte entry is: crc(4) preloadBytes(2) archiveIndex(2) entryOffset(4) entryLength(4)
/// then a constant 0xFFFF(2). File data lives in &lt;base&gt;_&lt;index&gt;.vpk chunk files at
/// entryOffset (archiveIndex 0x7FFF means the data is embedded in the dir file itself).</summary>
public sealed class VpkArchive : IDisposable
{
    private const ushort EmbeddedArchive = 0x7FFF;

    private readonly struct VpkEntry
    {
        public readonly ushort ArchiveIndex;
        public readonly int Offset;
        public readonly int Length;

        public VpkEntry(ushort archiveIndex, int offset, int length)
        {
            ArchiveIndex = archiveIndex;
            Offset = offset;
            Length = length;
        }
    }

    private readonly Dictionary<string, VpkEntry> _entries =
        new Dictionary<string, VpkEntry>(StringComparer.OrdinalIgnoreCase);

    private readonly string _dirPath;   // full path of the *_dir.vpk
    private readonly string _basePath;  // dir path minus "_dir.vpk" (chunks share this base)
    private readonly Dictionary<ushort, FileStream> _chunkStreams = new Dictionary<ushort, FileStream>();
    private readonly object _ioLock = new object();

    /// <summary>Number of files catalogued in this archive's directory.</summary>
    public int EntryCount => _entries.Count;

    /// <summary>Base name used for chunk files, e.g. "tf2_textures" for tf2_textures_dir.vpk.</summary>
    public string BaseName => Path.GetFileName(_basePath);

    /// <summary>All content-relative file keys catalogued in this archive (forward slashes,
    /// e.g. "materials/models/props/foo.vtf"). A material browser uses this to list what the
    /// archive can serve without touching the chunk files.</summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>Parses a *_dir.vpk directory file. Never throws for unreadable archives;
    /// such an archive simply catalogues nothing.</summary>
    public VpkArchive(string dirVpkPath)
    {
        _dirPath = dirVpkPath;
        _basePath = dirVpkPath.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase)
            ? dirVpkPath.Substring(0, dirVpkPath.Length - "_dir.vpk".Length)
            : Path.Combine(Path.GetDirectoryName(dirVpkPath) ?? "", Path.GetFileNameWithoutExtension(dirVpkPath));
        Parse();
    }

    private void Parse()
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(_dirPath);
        }
        catch (Exception)
        {
            return;
        }

        if (data.Length < 28 || BitConverter.ToUInt32(data, 0) != 0x55AA1234)
        {
            return;
        }

        uint version = BitConverter.ToUInt32(data, 4);
        int headerSize = version >= 2 ? 28 : 12;
        if (data.Length < headerSize + 4)
        {
            return;
        }

        // v1 archives store each entry's preload data inline right after the 18-byte header;
        // v2 archives keep preload data elsewhere (in the dir file's data section).
        bool skipInlinePreload = version < 2;

        int treeSize = (int)BitConverter.ToUInt32(data, 8);
        int treeEnd = Math.Min(headerSize + treeSize, data.Length);

        int pos = headerSize;
        while (pos < treeEnd)
        {
            string ext = ReadNullString(data, ref pos, treeEnd);
            if (ext == null)
            {
                break;
            }

            if (ext.Length == 0)
            {
                break; // end of the directory tree
            }

            while (pos < treeEnd)
            {
                string path = ReadNullString(data, ref pos, treeEnd);
                if (path == null)
                {
                    break;
                }

                if (path.Length == 0)
                {
                    break; // end of this extension's entries
                }

                while (pos < treeEnd)
                {
                    string name = ReadNullString(data, ref pos, treeEnd);
                    if (name == null)
                    {
                        break;
                    }

                    if (name.Length == 0)
                    {
                        break; // end of this path's files
                    }

                    if (pos + 18 > treeEnd)
                    {
                        return;
                    }

                    ushort preload = BitConverter.ToUInt16(data, pos + 4);
                    ushort archiveIndex = BitConverter.ToUInt16(data, pos + 6);
                    int entryOffset = BitConverter.ToInt32(data, pos + 8);
                    int entryLength = BitConverter.ToInt32(data, pos + 12);
                    pos += 18;
                    if (skipInlinePreload)
                    {
                        pos += preload;
                    }

                    // Entry full path uses forward slashes, matching the material lookup key.
                    string full = path + "/" + name + "." + ext;
                    _entries[full] = new VpkEntry(archiveIndex, entryOffset, entryLength);
                }
            }
        }
    }

    private static string ReadNullString(byte[] data, ref int pos, int end)
    {
        int start = pos;
        while (pos < end && data[pos] != 0)
        {
            pos++;
        }

        if (pos >= end)
        {
            return null;
        }

        string s = Encoding.ASCII.GetString(data, start, pos - start);
        pos++; // skip the terminator
        return s;
    }

    /// <summary>Reads the bytes of a file by its VPK path (forward slashes, e.g.
    /// "materials/models/props/foo.vtf"). Returns false if not present or unreadable.</summary>
    public bool TryRead(string fullPath, out byte[] data)
    {
        data = null;
        if (!_entries.TryGetValue(fullPath, out VpkEntry entry))
        {
            return false;
        }

        if (entry.Length <= 0)
        {
            return false;
        }

        lock (_ioLock)
        {
            try
            {
                if (entry.ArchiveIndex == EmbeddedArchive)
                {
                    using FileStream dir = File.OpenRead(_dirPath);
                    data = ReadRange(dir, entry.Offset, entry.Length);
                    return data != null;
                }

                string chunkPath = _basePath + "_" + entry.ArchiveIndex.ToString("D3") + ".vpk";
                if (!_chunkStreams.TryGetValue(entry.ArchiveIndex, out FileStream stream))
                {
                    if (!File.Exists(chunkPath))
                    {
                        return false;
                    }

                    stream = File.OpenRead(chunkPath);
                    _chunkStreams[entry.ArchiveIndex] = stream;
                }

                data = ReadRange(stream, entry.Offset, entry.Length);
                return data != null;
            }
            catch (Exception)
            {
                data = null;
                return false;
            }
        }
    }

    private static byte[] ReadRange(FileStream stream, int offset, int length)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[length];
        int total = 0;
        while (total < length)
        {
            int read = stream.Read(buffer, total, length - total);
            if (read <= 0)
            {
                return null;
            }

            total += read;
        }

        return buffer;
    }

    public void Dispose()
    {
        lock (_ioLock)
        {
            foreach (FileStream stream in _chunkStreams.Values)
            {
                stream.Dispose();
            }

            _chunkStreams.Clear();
        }
    }
}
