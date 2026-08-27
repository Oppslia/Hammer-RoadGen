using System;
using System.Collections.Generic;
using System.Text;

namespace RoadGen.Core;

/// <summary>A block in a parsed VMF document: a name, key/value properties and
/// nested child blocks.</summary>
public sealed class VmfBlock
{
    public string Name = "";
    public readonly Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<VmfBlock> Children = new List<VmfBlock>();
}

/// <summary>A small recursive-descent parser for Valve's VMF text format.</summary>
public static class VmfParser
{
    public static VmfBlock Parse(string text)
    {
        int pos = 0;
        var root = new VmfBlock { Name = "root" };

        while (true)
        {
            SkipWhitespace(text, ref pos);
            if (pos >= text.Length)
            {
                break;
            }

            string token = ReadToken(text, ref pos);
            SkipWhitespace(text, ref pos);
            if (pos < text.Length && text[pos] == '{')
            {
                root.Children.Add(ParseBlock(text, ref pos, token));
            }
        }

        return root;
    }

    private static VmfBlock ParseBlock(string text, ref int pos, string name)
    {
        var block = new VmfBlock { Name = name };

        // Caller has positioned us on '{'.
        if (pos < text.Length && text[pos] == '{')
        {
            pos++;
        }

        while (pos < text.Length)
        {
            SkipWhitespace(text, ref pos);
            if (pos >= text.Length)
            {
                break;
            }

            if (text[pos] == '}')
            {
                pos++;
                break;
            }

            string key = ReadToken(text, ref pos);
            SkipWhitespace(text, ref pos);

            if (pos < text.Length && text[pos] == '{')
            {
                block.Children.Add(ParseBlock(text, ref pos, key));
            }
            else
            {
                string value = ReadToken(text, ref pos);
                block.Properties[key] = value;
            }
        }

        return block;
    }

    private static void SkipWhitespace(string text, ref int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
        {
            pos++;
        }
    }

    private static string ReadToken(string text, ref int pos)
    {
        SkipWhitespace(text, ref pos);
        if (pos >= text.Length)
        {
            return string.Empty;
        }

        if (text[pos] == '"')
        {
            pos++;
            var sb = new StringBuilder();
            while (pos < text.Length && text[pos] != '"')
            {
                sb.Append(text[pos]);
                pos++;
            }

            if (pos < text.Length)
            {
                pos++; // closing quote
            }

            return sb.ToString();
        }

        int start = pos;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '{' && text[pos] != '}')
        {
            pos++;
        }

        return text.Substring(start, pos - start);
    }
}
