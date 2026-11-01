using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

internal static class AssetProgram
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("FAIL: " + message);
    }
    // The original game splits on the FIRST comma, then applies exactly this order.
    private static string ParseCsvValue(string value)
    {
        value = value.Trim();
        if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
            value = value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
        return value.Replace("\\n", "\n");
    }
    private static bool SameJson(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = left.EnumerateObject().ToArray();
                return properties.Length == right.EnumerateObject().Count() && properties.All(p => right.TryGetProperty(p.Name, out var other) && SameJson(p.Value, other));
            case JsonValueKind.Array:
                return left.GetArrayLength() == right.GetArrayLength() && left.EnumerateArray().Zip(right.EnumerateArray(), SameJson).All(equal => equal);
            case JsonValueKind.String: return left.GetString() == right.GetString();
            case JsonValueKind.Number: return left.GetDouble() == right.GetDouble();
            default: return left.GetRawText() == right.GetRawText();
        }
    }
    private static void CheckCsv(string csvPath, JsonElement source)
    {
        var bytes = File.ReadAllBytes(csvPath);
        Check(!(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf), "UTF-8 CSV has no BOM before key");
        string[] lines = File.ReadAllLines(csvPath, new UTF8Encoding(false, true));
        Check(lines.Length == 1, "CSV contains exactly one physical data row (no header)");
        int comma = lines[0].IndexOf(',');
        Check(comma > 0 && lines[0].Substring(0, comma) == "raid_framework_pack_v1", "first-comma split preserves exact bridge key");
        string rawValue = lines[0].Substring(comma + 1);
        Check(rawValue.StartsWith("\"") && rawValue.EndsWith("\""), "minified JSON is CSV outer-quoted");
        string restored = ParseCsvValue(rawValue);
        using var document = JsonDocument.Parse(restored);
        Check(SameJson(source, document.RootElement), "original ParseCsvValue restores legal JSON structurally identical to pack.json");
        Check(!restored.Contains('\r') && !restored.Contains('\n'), "game parser result is still a single minified line");
    }
    public static void Main(string[] args)
    {
        string example = Path.GetFullPath(args.Length == 0 ? "RaidPrototype/Examples/RaidDocks" : args[0]);
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(example, "pack.json")));
        string dist = Path.Combine(example, "dist", "RaidDocksExample");
        CheckCsv(Path.Combine(example, "Localization", "en.csv"), source.RootElement);
        CheckCsv(Path.Combine(dist, "Localization", "en.csv"), source.RootElement);
        Check(File.ReadAllBytes(Path.Combine(example, "Localization", "en.csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(dist, "Localization", "en.csv"))), "distribution CSV equals generated CSV");
        var manifest = XDocument.Load(Path.Combine(dist, "manifest.xml")).Root;
        Check(manifest.Element("ID").Value == source.RootElement.GetProperty("id").GetString(), "manifest.ID equals pack.id exactly");
        Check(manifest.Element("ModVersion").Value == "1.0.1", "manifest version 1.0.1");
        Check(manifest.Element("Prerequisites").Elements("Mod").Select(e => e.Value).SequenceEqual(new[] { "local.raidprototype" }), "manifest Prerequisites preserved");
        Check(!File.Exists(Path.Combine(dist, "RaidPrototype.dll")), "distribution contains no private framework DLL");
        var sourcePngs = Directory.GetFiles(Path.Combine(example, "Textures", "Items"), "*.png");
        Check(sourcePngs.Length == 7 && Directory.GetFiles(Path.Combine(dist, "Textures", "Items"), "*.png").Length == 7, "seven original PNG assets");
        foreach (string png in sourcePngs)
            Check(File.ReadAllBytes(png).SequenceEqual(File.ReadAllBytes(Path.Combine(dist, "Textures", "Items", Path.GetFileName(png)))), "PNG copied unchanged: " + Path.GetFileName(png));
        string dll = Path.Combine(dist, "RaidDocksExample.dll");
        // Read ECMA-335 metadata only: never Assembly.Load/LoadFrom or execute game DLLs.
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var assembly = metadata.GetAssemblyDefinition();
        Check(metadata.GetString(assembly.Name) == "RaidDocksExample" && assembly.Version == new Version(1, 0, 1, 0), "real built assembly identity is RaidDocksExample 1.0.1.0");
        var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Check(!references.Contains("RaidPrototype", StringComparer.OrdinalIgnoreCase), "built DLL has NO RaidPrototype AssemblyRef");
        Check(references.SequenceEqual(new[] { "Assembly-CSharp", "netstandard" }), "built DLL references only game and netstandard");
        var entry = metadata.TypeDefinitions.Select(h => metadata.GetTypeDefinition(h)).Single(t => metadata.GetString(t.Name) == "Entry" && metadata.GetString(t.Namespace) == "RaidDocksExample");
        var implemented = entry.GetInterfaceImplementations().Select(h => metadata.GetInterfaceImplementation(h).Interface).ToArray();
        Check(implemented.Length == 1 && implemented[0].Kind == HandleKind.TypeReference, "Entry implements one externally defined interface");
        var imod = metadata.GetTypeReference((TypeReferenceHandle)implemented[0]);
        Check(metadata.GetString(imod.Name) == "IMod" && imod.ResolutionScope.Kind == HandleKind.AssemblyReference && metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)imod.ResolutionScope).Name) == "Assembly-CSharp", "Entry implements the REAL game's IMod, not a test stub");
        foreach (string methodName in new[] { "Init", "OnEnable", "OnDisable" })
        {
            var method = entry.GetMethods().Select(h => metadata.GetMethodDefinition(h)).Single(m => metadata.GetString(m.Name) == methodName);
            Check(pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes().SequenceEqual(new byte[] { 0x2a }), "Entry." + methodName + " IL is only ret (empty)");
        }
        Console.WriteLine("Assembly references (read-only PE metadata): " + string.Join(", ", references));
        Console.WriteLine("PASS: " + assertions + " CSV/manifest/PNG/game-only DLL metadata assertions; no game code executed.");
    }
}
