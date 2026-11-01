using System.Text.Json;
using RaidPrototype;
using RaidPrototype.Content;
using UnityEngine;
using UnityEngine.UI;

static partial class Suite
{
    private static string Payload(string id = Pack, string name = "CSV arena", string[] requires = null)
    {
        // Explicit fields keep this controlled fixture independent of parser/default changes.
        return JsonSerializer.Serialize(new
        {
            apiVersion = 1, id, nameZh = "桥接测试", nameEn = "Bridge test", requires = requires ?? Array.Empty<string>(),
            items = new[] { new { id = id + ".gem", nameZh = "桥接物资", nameEn = "CSV gem", width = 1, height = 1, maxStack = 9 } },
            weapons = new[] { new { id = id + ".gun", nameZh = "桥接武器", nameEn = "CSV gun", damage = 15, cooldown = .1f, range = 20, automatic = true } },
            skins = new[] { new { id = id + ".skin", nameZh = "桥接皮肤", nameEn = "CSV skin" } },
            maps = new[] { new
            {
                id = id + ".arena", nameZh = "桥接地图", nameEn = name, width = 30, height = 10,
                playerSpawn = new { x = 2, y = 2 }, hasExtraction = false, extractionRadius = 1.2f, extractionSeconds = 3,
                skinId = id + ".skin", loadout = new[] { id + ".gun" },
                boxes = new[] { new { position = new { x = 2.7f, y = 2 }, loot = new[] { new { itemId = id + ".gem", count = 2 } } } }
            } }
        });
    }
    private static LoadedMod CsvProvider(string id = Pack, string json = null, bool enabled = true, bool payload = true)
    {
        var provider = new LoadedMod { Manifest = new ModManifest { ID = id }, IsEnabled = enabled };
        if (payload) provider.Localization["en"] = new Dictionary<string, string> { [ContentBridge.PayloadKey] = json ?? Payload(id) };
        ModLoader.Mods.Add(provider); return provider;
    }
    private static void RegisterBridgeIntegration()
    {
        Case("Bridge Entry enable imports loaded CSV payload once and disable unsubscribes/removes", () =>
        {
            CsvProvider(); var h = new Harness();
            Check(ContentRegistry.IsRegistered(Pack) && ContentRegistry.GetMap(MapId).NameEn == "CSV arena", "Entry did not import provider");
            Check(ModHook.SubscriberCount == 1, "bridge event subscription"); h.Entry.OnEnable(); ContentBridge.Start();
            Check(ModHook.SubscriberCount == 1 && ContentRegistry.Packs.Count == 2, "duplicate enable/import");
            h.Entry.OnDisable(); Check(ModHook.SubscriberCount == 0 && !ContentRegistry.IsRegistered(Pack) && ContentRegistry.IsRegistered(DefaultContent.PackId), "disable cleanup");
            h.Entry.OnDisable(); Check(ModHook.SubscriberCount == 0, "repeat disable subscription leak");
        });
        Case("Bridge assets event imports provider whose localization arrives late", () =>
        {
            var provider = CsvProvider(payload: false); var h = new Harness(); Check(!ContentRegistry.IsRegistered(Pack), "premature import");
            provider.Localization["en"] = new() { [ContentBridge.PayloadKey] = Payload() }; ModHook.RaiseAssetsLoaded(provider);
            Check(ContentRegistry.IsRegistered(Pack), "late assets event import"); h.Open(); PickMap(h, MapId); Check(h.Model.Map.Id == MapId, "CSV map missing from actual UI cycle");
        });
        Case("Bridge periodic Update refresh imports late provider even outside main menu", () =>
        {
            var h = new Harness(); MainMenuUIController.Instance = null; CsvProvider(); Time.unscaledDeltaTime = .1f;
            for (int i = 0; i < 12; i++) h.Tick();
            Check(ContentRegistry.IsRegistered(Pack) && h.Driver.enabled && Debug.Errors.Count == 0, "periodic bridge refresh before menu gate");
        });
        Case("Bridge duplicate refresh is idempotent and malformed provider does not block good provider", () =>
        {
            CsvProvider("test.bad", "{not json}"); CsvProvider(); ContentBridge.Start();
            Check(ContentRegistry.IsRegistered(Pack) && !ContentRegistry.IsRegistered("test.bad"), "bad provider blocked good provider");
            int warnings = Debug.Warnings.Count; for (int i = 0; i < 3; i++) { ContentBridge.Refresh(); ModHook.RaiseAssetsLoaded(null); }
            Check(ContentRegistry.Packs.Count == 2 && Debug.Warnings.Count == warnings && warnings == 1, "repeat import/warning dedup");
        });
        Case("Bridge mismatched provider ID is isolated; correcting payload recovers", () =>
        {
            var bad = CsvProvider("test.host", Payload("test.impersonated")); CsvProvider(); ContentBridge.Start();
            Check(!ContentRegistry.IsRegistered("test.impersonated") && !ContentRegistry.IsRegistered("test.host") && ContentRegistry.IsRegistered(Pack), "provider ID authority");
            Check(Debug.Warnings.Any(x => x.Contains("must equal its provider")), "identity rejection warning");
            bad.Localization["en"][ContentBridge.PayloadKey] = Payload("test.host"); ContentBridge.Refresh();
            Check(ContentRegistry.IsRegistered("test.host"), "corrected provider recovery");
        });
        Case("Loader provider ID checks and malformed JSON registration remain atomic", () =>
        {
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJsonForProvider(Payload(), " ")), "empty provider ID accepted");
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJsonForProvider(Payload(), "wrong.id")), "provider mismatch accepted");
            Check(Throws<ArgumentException>(() => ContentLoader.RegisterJson("{}")) && Throws<ArgumentException>(() => ContentLoader.RegisterJson("{\"apiVersion\":1,\"id\":]")), "invalid schema/json accepted");
            Check(ContentRegistry.Packs.Count == 1, "failed loader registration mutated registry");
            Check(ContentLoader.RegisterJsonForProvider(Payload(), Pack) == Pack && ContentRegistry.IsRegistered(Pack), "real loader happy path");
        });
        Case("Bridge ignores framework/disabled/non-English/missing/null provider surfaces", () =>
        {
            CsvProvider("local.raidprototype"); CsvProvider("test.disabled", enabled: false);
            var locale = CsvProvider("test.locale", payload: false); locale.Localization["zh"] = new() { [ContentBridge.PayloadKey] = Payload("test.locale") };
            var nullTable = CsvProvider("test.nulltable", payload: false); nullTable.Localization["en"] = null;
            var nullLocalization = CsvProvider("test.nulllocale", payload: false); nullLocalization.Localization = null;
            ModLoader.Mods.Add(null); ModLoader.Mods.Add(new LoadedMod { Manifest = null }); ContentBridge.Start();
            Check(ContentRegistry.Packs.Count == 1 && Debug.Warnings.Count == 0, "ignored provider unexpectedly imported/rejected");
        });
        Case("Bridge provider disable unregisters pack while active UI model snapshot remains usable", () =>
        {
            var provider = CsvProvider(); var h = new Harness(); h.Open(); PickMap(h, MapId); StartCurrent(h); h.Tick(KeyCode.E); var model = h.Model;
            Check(model.LootCount == 2, "CSV model loot setup"); provider.IsEnabled = false; ContentBridge.Refresh();
            Check(!ContentRegistry.IsRegistered(Pack) && ReferenceEquals(model, h.Model), "provider disable session handling");
            h.Tick(); Check(h.Driver.enabled && model.GetItem(ItemId).NameEn == "CSV gem" && h.Field<TMPro.TextMeshProUGUI>("inventory").text.Contains("CSV gem × 2"), "unloaded CSV session unusable");
            model.Reset(); Check(model.State == RaidState.Playing && model.Boxes.Count == 1, "snapshot Reset after unload");
            h.Close(); h.Open(); Check(h.Model.Map.Id == DefaultContent.DefaultMapId, "unloaded map reopen fallback");
        });
        Case("Bridge payload changes atomically replace future content without mutating active session", () =>
        {
            var provider = CsvProvider(); var h = new Harness(); h.Open(); PickMap(h, MapId); var session = h.Model;
            provider.Localization["en"][ContentBridge.PayloadKey] = Payload(name: "Replacement CSV arena"); ContentBridge.Refresh();
            Check(ContentRegistry.GetMap(MapId).NameEn == "Replacement CSV arena" && session.Map.NameEn == "CSV arena", "payload replacement snapshot");
            Call(h.Driver, "Prepare"); Check(h.Model.Map.NameEn == "Replacement CSV arena" && !ReferenceEquals(session, h.Model), "new session missed replacement");
        });
        Case("Bridge reverse dependency order registers and disable retires dependants first", () =>
        {
            var child = CsvProvider("test.child", Payload("test.child", requires: new[] { "test.base" })); var dependency = CsvProvider("test.base");
            ContentBridge.Start(); Check(ContentRegistry.IsRegistered("test.base") && ContentRegistry.IsRegistered("test.child"), "dependency retry import");
            child.IsEnabled = dependency.IsEnabled = false; ContentBridge.Refresh();
            Check(!ContentRegistry.IsRegistered("test.base") && !ContentRegistry.IsRegistered("test.child"), "dependency removal order");
        });
        Case("Bridge provider instance replacement and localization removal retire original binding", () =>
        {
            var first = CsvProvider(); ContentBridge.Start(); ModLoader.Mods.Remove(first); var second = CsvProvider(json: Payload(name: "New provider instance")); ContentBridge.Refresh();
            Check(ContentRegistry.GetMap(MapId).NameEn == "New provider instance", "provider identity replacement");
            second.Localization["en"].Remove(ContentBridge.PayloadKey); ContentBridge.Refresh(); Check(!ContentRegistry.IsRegistered(Pack), "removed localization key retained content");
            ModHook.RaiseAssetsLoaded(first); Check(!ContentRegistry.IsRegistered(Pack), "old provider event reimported removed provider");
        });
        Case("Bridge invalidates enabled dependant closure when base provider disables then recovers", () =>
        {
            CsvProvider("test.leaf", Payload("test.leaf", requires: new[] { "test.child" }));
            var child = CsvProvider("test.child", Payload("test.child", requires: new[] { "test.base" })); var dependency = CsvProvider("test.base");
            ContentBridge.Start(); var session = new RaidModel("test.leaf.arena"); dependency.IsEnabled = false; ContentBridge.Refresh();
            Check(child.IsEnabled && !ContentRegistry.IsRegistered("test.base") && !ContentRegistry.IsRegistered("test.child") && !ContentRegistry.IsRegistered("test.leaf"), "disabled base left enabled dependant closure registered");
            Check(session.Map.Id == "test.leaf.arena" && session.Interact() && session.LootCount == 2, "invalidated closure destroyed running snapshot");
            dependency.IsEnabled = true; ContentBridge.Refresh();
            Check(ContentRegistry.IsRegistered("test.base") && ContentRegistry.IsRegistered("test.child") && ContentRegistry.IsRegistered("test.leaf"), "closure failed recovery after base re-enabled");
        });
        Case("Bridge changed base payload retires enabled dependant sessions and reimports valid closure", () =>
        {
            CsvProvider("test.child", Payload("test.child", requires: new[] { "test.base" })); var dependency = CsvProvider("test.base"); ContentBridge.Start(); var old = new RaidModel("test.base.arena");
            dependency.Localization["en"][ContentBridge.PayloadKey] = Payload("test.base", name: "Updated dependency"); ContentBridge.Refresh();
            Check(ContentRegistry.GetMap("test.base.arena").NameEn == "Updated dependency" && ContentRegistry.IsRegistered("test.child"), "base replacement/dependant reimport");
            Check(old.Map.NameEn == "CSV arena", "base payload replacement mutated prior session");
        });
        Case("Bridge rejected payload caches failure state, deduplicates logs and retries changed provider/payload", () =>
        {
            var bad = CsvProvider("test.bad", "{not json}"); ContentBridge.Start();
            var type = typeof(ContentBridge); const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var payloads = (Dictionary<string, string>)type.GetField("failedPayloads", flags).GetValue(null);
            var providers = (Dictionary<string, LoadedMod>)type.GetField("failedProviders", flags).GetValue(null);
            var generations = (Dictionary<string, int>)type.GetField("failedGenerations", flags).GetValue(null);
            var messages = (Dictionary<string, string>)type.GetField("failures", flags).GetValue(null);
            Check(payloads["test.bad"] == "{not json}" && ReferenceEquals(providers["test.bad"], bad) && messages.ContainsKey("test.bad"), "malformed payload failure not cached");
            int warnings = Debug.Warnings.Count, logs = Debug.Logs.Count, generation = generations["test.bad"];
            Check(warnings == 1 && !ContentRegistry.IsRegistered("test.bad"), "initial bad provider must reject exactly once");
            for (int i = 0; i < 4; i++) ContentBridge.Refresh();
            Check(Debug.Warnings.Count == warnings && Debug.Logs.Count == logs && generations["test.bad"] == generation && ReferenceEquals(providers["test.bad"], bad), "unchanged failure cache/log state changed");
            ModLoader.Mods.Remove(bad); bad = CsvProvider("test.bad", "{not json}"); ContentBridge.Refresh();
            Check(ReferenceEquals(providers["test.bad"], bad) && !ContentRegistry.IsRegistered("test.bad"), "replacement provider did not update rejected binding");
            ContentBridge.Refresh(); Check(Debug.Warnings.Count == warnings && Debug.Logs.Count == logs, "unchanged replacement rejection repeated logs");
            bad.Localization["en"][ContentBridge.PayloadKey] = Payload("test.bad"); ContentBridge.Refresh();
            Check(ContentRegistry.IsRegistered("test.bad") && !payloads.ContainsKey("test.bad") && !providers.ContainsKey("test.bad") && !generations.ContainsKey("test.bad") && !messages.ContainsKey("test.bad"), "corrected payload failed registration or retained rejected cache");
            Check(Debug.Logs.Count == logs + 1, "corrected provider should emit one successful registration log");
        });
        Case("Bridge missing dependency is retried when later provider registration succeeds", () =>
        {
            CsvProvider("test.child", Payload("test.child", requires: new[] { "test.base" })); ContentBridge.Start(); Check(!ContentRegistry.IsRegistered("test.child"), "missing dependency accepted");
            CsvProvider("test.base"); ContentBridge.Refresh(); Check(ContentRegistry.IsRegistered("test.base") && ContentRegistry.IsRegistered("test.child"), "cached missing dependency never retried");
        });
        Case("Bridge Stop removes dependency chains fully and leaves no retained bindings", () =>
        {
            CsvProvider("test.child", Payload("test.child", requires: new[] { "test.base" })); CsvProvider("test.base");
            ContentBridge.Start(); Check(ContentRegistry.IsRegistered("test.child") && ContentRegistry.IsRegistered("test.base"), "dependency Stop setup");
            ContentBridge.Stop(); Check(!ContentRegistry.IsRegistered("test.child") && !ContentRegistry.IsRegistered("test.base"), "Stop leaked required provider pack");
            var bindings = (System.Collections.IDictionary)typeof(ContentBridge).GetField("bindings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            Check(bindings.Count == 0 && ModHook.SubscriberCount == 0, "Stop retained binding/event state");
        });
        Case("Bridge removed provider withdraws pack and invalid replacement does not block others", () =>
        {
            var provider = CsvProvider(); CsvProvider("test.other"); ContentBridge.Start(); ModLoader.Mods.Remove(provider); ContentBridge.Refresh();
            Check(!ContentRegistry.IsRegistered(Pack) && ContentRegistry.IsRegistered("test.other"), "removed provider cleanup");
            CsvProvider(json: "[]"); ContentBridge.Refresh(); Check(!ContentRegistry.IsRegistered(Pack) && ContentRegistry.IsRegistered("test.other"), "invalid replacement affected other provider");
        });
        Case("Bridge Stop prevents event/refresh import and restart can recover provider", () =>
        {
            var provider = CsvProvider(); ContentBridge.Start(); ContentBridge.Stop();
            ModHook.RaiseAssetsLoaded(provider); ContentBridge.Refresh(); Check(!ContentRegistry.IsRegistered(Pack) && ModHook.SubscriberCount == 0, "stopped bridge still imports");
            ContentBridge.Start(); Check(ContentRegistry.IsRegistered(Pack) && ModHook.SubscriberCount == 1, "restart import");
        });
    }
}
