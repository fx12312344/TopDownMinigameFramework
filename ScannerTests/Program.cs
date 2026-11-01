using System.Reflection;
using System.Text.Json;

// The game DLL and scanner are unmodified. Only the offline process's initial
// game-assembly-name set is populated from the actual Managed DLL file names.
// We deliberately do not execute ModLoader.Awake (Steam/trust/save side effects).
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ScannerTests <GameManaged> <pack-folder> [pack-folder ...]");
    return 2;
}
RunCsvValueCases();
string managed = Path.GetFullPath(args[0]);
if (!File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")))
    throw new DirectoryNotFoundException("GameManaged must contain the current game's Assembly-CSharp.dll.");
string sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(managed, "Assembly-CSharp.dll"))));
string loadedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ModScanner).Assembly.Location)));
if (sourceHash != loadedHash)
    throw new InvalidOperationException("Loaded scanner DLL differs from GameManaged; rebuild with the matching GameManaged property.");
var gameNames = (HashSet<string>?)typeof(ModLoader)
    .GetField("gameAssemblyNames", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
    ?? throw new InvalidOperationException("Current game no longer exposes expected assembly-name set.");
foreach (string file in System.IO.Directory.GetFiles(managed, "*.dll", SearchOption.AllDirectories))
    gameNames.Add(Path.GetFileNameWithoutExtension(file));
int refused = 0;
foreach (string input in args.Skip(1))
{
    string folder = Path.GetFullPath(input);
    ModManifest manifest = ModLoader.ParseManifestForTools(folder, out string problem)
        ?? throw new InvalidDataException("Original manifest parser refused: " + problem);
    string[] dlls = ModLoader.ListedAssemblyPaths(folder, manifest).ToArray();
    string[] unlisted = ModLoader.UnlistedAssemblyFiles(folder, manifest).ToArray();
    var scan = ModScanner.Scan(dlls, manifest.Assemblies.Select(Path.GetFileNameWithoutExtension)!, folder, true);
    string[] undeclared = ModCapability.Undeclared(scan.Capabilities, manifest.Capabilities).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Folder = folder,
        ManifestId = manifest.ID,
        UnlistedDlls = unlisted,
        UndeclaredCapabilities = undeclared,
        ScannerVersion = ModScanner.VERSION,
        ScannerAssembly = typeof(ModScanner).Assembly.Location,
        ScannerAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ModScanner).Assembly.Location))),
        scan.Refused,
        scan.Error,
        scan.MethodsScanned,
        Capabilities = scan.Capabilities.OrderBy(s => s).ToArray(),
        HardFlags = scan.HardFlags.Select(f => new { f.Kind, f.Location, f.Target }).ToArray(),
        Findings = scan.Findings.Select(f => new { f.Kind, f.Location, f.Target }).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
    if (scan.Refused || unlisted.Length != 0 || undeclared.Length != 0) refused++;
}
return refused == 0 ? 0 : 1;

static void RunCsvValueCases()
{
    var parse = typeof(ModLoader).GetMethod("ParseCsvValue", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("ModLoader.ParseCsvValue");
    (string Raw, string Expected, string Name)[] cases =
    {
        ("  {\"id\":\"x\",\"value\":2}  ", "{\"id\":\"x\",\"value\":2}", "Raw JSON and trim"),
        ("\"{\"\"id\"\":\"\"x\"\"}\"", "{\"id\":\"x\"}", "CSV quoted JSON"),
        ("{\"text\":\"a\\nb\"}", "{\"text\":\"a\nb\"}", "Backslash-n is destructive for JSON"),
        ("{\"text\":\"a\\u000Ab\"}", "{\"text\":\"a\\u000Ab\"}", "Unicode escaped newline is preserved"),
        ("{\"text\":\"a\\u005Cnb\"}", "{\"text\":\"a\\u005Cnb\"}", "Unicode escaped literal backslash is preserved")
    };
    foreach (var item in cases)
    {
        var actual = (string?)parse.Invoke(null, new object[] { item.Raw });
        if (actual != item.Expected)
            throw new InvalidOperationException("Original ParseCsvValue case failed: " + item.Name);
    }
    Console.WriteLine("Original ModLoader.ParseCsvValue: " + cases.Length + " cases passed.");
}
