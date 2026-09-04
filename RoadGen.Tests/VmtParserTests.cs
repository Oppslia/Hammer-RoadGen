using System;
using System.Collections.Generic;
using System.Text;

namespace RoadGen.Tests;

/// <summary>Regression tests for VtfMaterialCache.ParseVmtBaseTexture — the parser that reads
/// the $basetexture value out of a .vmt. Kept in its own file (its own "area") so the VMT
/// resolution edge cases can grow without touching the road-math tests in Program.cs.
///
/// The method under test is private, so it is driven via reflection rather than widening the
/// production API. Each case here is a real bug that shipped: (1) quoted key/value returned
/// the gap between the two quotes; (2) bare-key .vmts read past the value into the next
/// $ key ("$surfaceprop metal").</summary>
public static class VmtParserTests
{
    /// <summary>The (name, body) cases this area contributes to the runner in Program.cs.</summary>
    public static IEnumerable<(string Name, Action Body)> Cases()
    {
        yield return ("VMT: quoted key/value reads the real texture (not the gap between quotes)", TestVmtQuotedKeyValue);
        yield return ("VMT: bare key + quoted value doesn't bleed into the next $ key", TestVmtBareKeyQuotedValue);
        yield return ("VMT: bare key + bare (unquoted) value is parsed", TestVmtBareKeyBareValue);
        yield return ("VMT: $basetexture key is matched case-insensitively", TestVmtKeyCaseInsensitive);
        yield return ("VMT: $basetexture found even when other $ keys come first", TestVmtFindsAfterOtherKeys);
        yield return ("VMT: absent $basetexture returns null", TestVmtMissingReturnsNull);
        yield return ("VMT: present-but-empty $basetexture returns empty string", TestVmtEmptyValue);
        yield return ("VMT: first (outer) $basetexture wins over nested blocks", TestVmtFirstWins);
        yield return ("VMT: whitespace around the value is ignored/trimmed", TestVmtWhitespaceTrim);
    }

    private static string ParseVmtBaseTexture(string vmtText)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        System.Reflection.MethodInfo method =
            typeof(RoadGen.Core.Vtf.VtfMaterialCache).GetMethod("ParseVmtBaseTexture", flags);
        Checks.AssertTrue(method != null, "ParseVmtBaseTexture not found via reflection");
        object result = method.Invoke(null, new object[] { Encoding.ASCII.GetBytes(vmtText) });
        return result as string;
    }

    private static void TestVmtQuotedKeyValue()
    {
        // Regression #1: quoted key + quoted value. The old parser returned the space
        // between the key's closing quote and the value's opening quote ("no $basetexture").
        string vmt = """
            "lightmappedGeneric"
            {
            	"$baseTexture" "de_aztec/ground01grass"
            	"%keywords" "cstrike"
            	"$surfaceprop" "dirt"
            }
            """;
        Checks.AssertEqual("de_aztec/ground01grass", ParseVmtBaseTexture(vmt),
            "$basetexture value must be the real texture path, not the quote gap");
    }

    private static void TestVmtBareKeyQuotedValue()
    {
        // Regression #2: Valve .vmt with BARE keys ($basetexture "Metal/metalwall048c").
        // The old parser read past the value's closing quote to the next stray quote in the
        // file ("%keywords"), returning "$surfaceprop metal" instead of the texture path.
        string vmt = """
            LightmappedGeneric
            {
            $basetexture "Metal/metalwall048c"
            $surfaceprop metal
            "%keywords" portal
            "%noportal" 1
            $envmap env_cubemap
            $envmapcontrast 1
            $envmapsaturation 1
            $envmaptint "[ 0.75 0.5 0.5 ]"
            LightmappedGeneric_HDR_DX9
            {
            $bumpmap "metal/metalwall048b_height-ssbump"
            $ssbump 1
            }
            LightmappedGeneric_NoBump_DX8
            {
            $basetexture "Metal/metalwall048c"
            $basealphaenvmapmask 1
            }
            }
            """;
        Checks.AssertEqual("Metal/metalwall048c", ParseVmtBaseTexture(vmt),
            "bare-key $basetexture must not bleed into the following $surfaceprop line");
    }

    private static void TestVmtBareKeyBareValue()
    {
        string vmt = """
            LightmappedGeneric
            {
            $basetexture metal/metalwall048c
            $surfaceprop metal
            }
            """;
        Checks.AssertEqual("metal/metalwall048c", ParseVmtBaseTexture(vmt),
            "bare key + bare (unquoted) value is parsed");
    }

    private static void TestVmtKeyCaseInsensitive()
    {
        string vmt = """
            "LightmappedGeneric"
            {
            	"$BASEtexture" "props/crate_c"
            }
            """;
        Checks.AssertEqual("props/crate_c", ParseVmtBaseTexture(vmt),
            "$basetexture key matched case-insensitively");
    }

    private static void TestVmtFindsAfterOtherKeys()
    {
        string vmt = """
            "LightmappedGeneric"
            {
            	"$surfaceprop" "dirt"
            	"%keywords" "cstrike"
            	"$basetexture" "nature/blendgrass004a"
            }
            """;
        Checks.AssertEqual("nature/blendgrass004a", ParseVmtBaseTexture(vmt),
            "$basetexture found after other $ keys");
    }

    private static void TestVmtMissingReturnsNull()
    {
        string vmt = """
            "LightmappedGeneric"
            {
            	"$surfaceprop" "dirt"
            	"%keywords" "cstrike"
            }
            """;
        Checks.AssertEqual<string>(null, ParseVmtBaseTexture(vmt),
            "absent $basetexture returns null");
    }

    private static void TestVmtEmptyValue()
    {
        string vmt = """
            "LightmappedGeneric"
            {
            	"$basetexture" ""
            }
            """;
        Checks.AssertEqual("", ParseVmtBaseTexture(vmt),
            "present-but-empty $basetexture returns empty string (not null)");
    }

    private static void TestVmtFirstWins()
    {
        string vmt = """
            "LightmappedGeneric"
            {
            	"$basetexture" "cs_havana/outer"
            	"%keywords" "cstrike"
            LightmappedGeneric_DX9
            {
            	"$basetexture" "cs_havana/inner"
            }
            }
            """;
        Checks.AssertEqual("cs_havana/outer", ParseVmtBaseTexture(vmt),
            "first (outer) $basetexture wins over nested fallback blocks");
    }

    private static void TestVmtWhitespaceTrim()
    {
        string vmt = """
            "LightmappedGeneric"
            {
            	"$basetexture"    "   props/foo_d   "
            }
            """;
        Checks.AssertEqual("props/foo_d", ParseVmtBaseTexture(vmt),
            "whitespace between key and value is ignored and the value is trimmed");
    }
}
