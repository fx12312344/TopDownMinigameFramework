using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RaidPrototype;
using RaidPrototype.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

static partial class Suite
{
    private const string DocksPack = "example.raiddocks", DocksMap = DocksPack + ".warehouse";
    private static string Fixture(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
    // Test-only transport decoder for the single-record CSV fixture. This is NOT the
    // game's CSV implementation; production JSON parsing below uses its real Newtonsoft DLL.
    private static (string Key, string Value) ReadSingleCsvRecord(string text)
    {
        var cells = new List<string>(); var value = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { cells.Add(value.ToString()); value.Clear(); }
            else if ((c == '\r' || c == '\n') && !quoted)
            {
                if (text.Substring(i).Trim().Length != 0) throw new InvalidOperationException("CSV fixture contains more than one record.");
                break;
            }
            else value.Append(c);
        }
        if (quoted) throw new InvalidOperationException("Unterminated CSV quoted field.");
        cells.Add(value.ToString());
        if (cells.Count != 2) throw new InvalidOperationException("CSV fixture must contain exactly a key and payload.");
        return (cells[0].TrimStart('\ufeff'), cells[1]);
    }
    private static LoadedMod FixtureProvider(string json)
    {
        var provider = new LoadedMod { Manifest = new ModManifest { ID = DocksPack } };
        provider.Localization["en"] = new() { [ContentBridge.PayloadKey] = json };
        ModLoader.Mods.Add(provider); return provider;
    }
    private static void VerifyDocksButtonPath(string json)
    {
        FixtureProvider(json); var h = new Harness(); h.Open();
        var pack = ContentRegistry.Packs.Single(p => p.Id == DocksPack);
        Check(pack.Maps.Length == 1 && pack.Weapons.Length == 1 && pack.Items.Length == 2 && pack.Skins.Length == 1, "registered id alone is insufficient: all Night Docks definition arrays must survive JSON parsing");
        Check(ContentRegistry.Maps.Count == 2, "Night Docks registration must add a second selectable map");
        Check(Debug.Logs.Any(log => log.Contains("Registered CSV content provider " + DocksPack) && log.Contains("maps=1, weapons=1, items=2, skins=1") && log.Contains(DocksMap)), "success log must report actual parsed array counts and map id");
        OpenMapPicker(h);
        Check(VisibleMapIds(h).SequenceEqual(new[] { DocksMap, DefaultContent.DefaultMapId }.OrderBy(id => id, StringComparer.Ordinal)), "actual picker must show both Night Docks and built-in maps");
        var choice = MapChoice(h, DocksMap);
        Check(ButtonText(choice).Contains("Night Docks"), "Night Docks button must expose parsed display name");
        choice.Press();
        Check(h.Model.Map.Id == DocksMap && h.Model.Map.Width == 24 && h.Model.Map.Height == 16, "real Night Docks button did not switch map/model bounds");
        Check(h.Model.CurrentWeapon.Id == DocksPack + ".carbine" && h.Model.CurrentWeapon.Damage == 24 && h.Model.CurrentWeapon.Automatic, "Night Docks weapon metadata was lost");
        Check(h.Model.Skin.Id == DocksPack + ".night" && h.Model.Skin.WallKey == DocksPack + ":dock_wall", "Night Docks nested skin/sprite data was lost");
        Check(h.Model.Enemies.Count == 3 && h.Model.Boxes.Count == 3 && h.Model.Obstacles.Count == 3, "Night Docks nested entity arrays were lost");
        Check(h.Model.Boxes[1].Loot.Count == 2 && h.Model.Boxes[1].Loot[0].ItemId == DocksPack + ".medical", "Night Docks nested loot arrays were lost");
        Check(h.Field<GameObject>("weaponA").GetComponentInChildren<TextMeshProUGUI>(true).text == "Harbor carbine" && !h.Field<GameObject>("weaponB").activeSelf, "Night Docks preparation weapon controls incorrect");
        Check(h.Field<GameObject>("preparation").activeInHierarchy && !h.Field<GameObject>("mapPicker").activeSelf, "Night Docks selection must return to preparation");
        MapPosition(h, 1, 2); StartCurrent(h);
        Check(h.Field<bool>("started") && h.Model.Map.Id == DocksMap && h.Model.CurrentWeapon.Id == DocksPack + ".carbine", "actual starting-weapon button did not begin selected Night Docks raid");
        Check(h.Driver.enabled && Debug.Errors.Count == 0, "Night Docks real parser/button path threw");
    }
    private static void RegisterRealJsonCsvIntegration()
    {
        Case("Real game Newtonsoft assembly replaces JsonUtility substitution", () =>
        {
            var assembly = typeof(JObject).Assembly;
            Check(assembly.GetName().Name == "Newtonsoft.Json" && assembly != typeof(Suite).Assembly, "JSON parsing must use external Newtonsoft DLL, not test stub");
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Newtonsoft.Json.dll")), "Private=true must copy game Newtonsoft DLL beside test executable");
            Check(typeof(Suite).Assembly.GetTypes().All(type => type.Name != "JsonUtility"), "legacy JsonUtilityStub must be excluded from compilation");
            Console.WriteLine("PARSER " + assembly.FullName);
        });
        Case("Actual Night Docks quoted CSV from disk registers complete arrays and selects real UI map button", () =>
        {
            string raw = File.ReadAllText(Fixture("en.csv"));
            Check(raw.Contains("\"\"maps\"\"") && raw.Contains("\"\"items\"\""), "disk fixture must remain actual CSV quoted JSON, not raw JSON substituted as CSV");
            var row = ReadSingleCsvRecord(raw);
            Check(row.Key == ContentBridge.PayloadKey && !row.Value.Contains('\r') && !row.Value.Contains('\n'), "CSV decode must produce exact single-line payload key/value");
            Check(JToken.DeepEquals(JToken.Parse(row.Value), JToken.Parse(File.ReadAllText(Fixture("pack.json")))), "actual CSV payload must match maintained complete pack.json");
            VerifyDocksButtonPath(row.Value);
        });
        Case("Full pack JSON generated quoted CSV roundtrip preserves arrays through bridge and actual buttons", () =>
        {
            var root = JObject.Parse(File.ReadAllText(Fixture("pack.json")));
            string json = root.ToString(Formatting.None);
            var row = ReadSingleCsvRecord(ContentBridge.PayloadKey + ",\"" + json.Replace("\"", "\"\"") + "\"\r\n");
            Check(row.Key == ContentBridge.PayloadKey && row.Value == json, "generated CSV escaping roundtrip changed payload");
            VerifyDocksButtonPath(row.Value);
        });
        Case("Real JSON loader rejects empty packs and duplicate keys atomically", () =>
        {
            string empty = "{\"apiVersion\":1,\"id\":\"test.empty\",\"nameZh\":\"空包\",\"nameEn\":\"Empty pack\"}";
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJson(empty)), "id-only empty pack must not report successful registration");
            string duplicate = Payload().Replace("\"apiVersion\":1", "\"apiVersion\":1,\"apiVersion\":1");
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJson(duplicate)), "duplicate root property must reject rather than last-write-win");
            duplicate = Payload().Replace("\"damage\":15", "\"damage\":15,\"damage\":15");
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJson(duplicate)), "duplicate nested weapon property must reject");
            Check(ContentRegistry.Packs.Count == 1 && !ContentRegistry.IsRegistered("test.empty") && !ContentRegistry.IsRegistered(Pack), "failed real-parser registration partially changed registry");
        });
        Case("Real JSON loader preserves exact key and provider ID casing", () =>
        {
            string json = Payload();
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJsonForProvider(json, "TEST.THIRDPARTY")), "provider manifest ID comparison must be ordinal case-sensitive");
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJson(json.Replace("\"apiVersion\":1", "\"ApiVersion\":1"))), "wrong-case required apiVersion key must not bind");
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJson(json.Replace("\"id\":\"" + Pack + "\"", "\"Id\":\"" + Pack + "\""))), "wrong-case required id key must not bind");
            Check(ContentRegistry.Packs.Count == 1, "case mismatch failures must be atomic");
            Check(ContentLoader.RegisterJsonForProvider(json, Pack) == Pack && ContentRegistry.GetMap(MapId).Loadout.Single() == GunId, "correct-case complete payload must still parse");
        });
    }
}
