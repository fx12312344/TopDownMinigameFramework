using System.Reflection;
using RaidPrototype;
using RaidPrototype.Content;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UObj = UnityEngine.Object;

static partial class Suite
{
    private const string Pack = "test.thirdparty", MapId = Pack + ".arena", ItemId = Pack + ".gem", SkinId = Pack + ".skin", GunId = Pack + ".gun";
    private static ContentPack ThirdParty(bool sprites = false, bool singleWeapon = true)
    {
        var skin = new SkinDefinition
        {
            Id = SkinId, NameZh = "测试主题", NameEn = "Test theme",
            Floor = new RGBA(.2f, .1f, .4f), Player = new RGBA(.8f, .3f, .2f),
            UIBackground = new RGBA(.11f, .22f, .33f), UIText = new RGBA(.77f, .88f, .99f), UIButton = new RGBA(.44f, .55f, .66f)
        };
        if (sprites) { skin.BackgroundKey = Pack + ":background"; skin.PlayerKey = Pack + ":player"; skin.EnemyKey = Pack + ":enemy"; skin.CrateKey = Pack + ":crate"; skin.WallKey = Pack + ":wall"; }
        return new ContentPack
        {
            Id = Pack, NameZh = "第三方测试包", NameEn = "Third party test pack",
            Items = new[] { new ItemDefinition { Id = ItemId, NameZh = "测试宝石", NameEn = "Test gem", MaxStack = 5 } },
            Weapons = singleWeapon ? new[] { Gun(GunId, "Test auto", true) } : new[] { Gun(GunId, "Test auto", true), Gun(Pack + ".second", "Test single", false) },
            Skins = new[] { skin },
            Maps = new[] { new MapDefinition
            {
                Id = MapId, NameZh = "测试竞技场", NameEn = "Test arena", Width = 30, Height = 10, PlayerSpawn = new Vec2(2, 2),
                HasExtraction = false, SkinId = SkinId, Loadout = singleWeapon ? new[] { GunId } : new[] { GunId, Pack + ".second" },
                Boxes = new[] { new BoxSpawn { Position = new Vec2(2.7f, 2), Loot = new[] { new LootEntry { ItemId = ItemId, Count = 3 } } } },
                Enemies = new[] { new EnemySpawn { Position = new Vec2(20, 5), Speed = 0, Damage = 0 } },
                Obstacles = new[] { new RaidPrototype.Rect(10, 3, 11, 6) }
            } }
        };
    }
    private static WeaponDefinition Gun(string id, string name, bool automatic) => new() { Id = id, NameZh = "测试武器", NameEn = name, Damage = 11, Cooldown = .07f, Range = 30, Automatic = automatic };
    private static Harness OpenThirdParty(bool sprites = false, bool singleWeapon = true)
    {
        ContentRegistry.Register(ThirdParty(sprites, singleWeapon)); var h = new Harness(); h.Open();
        PickMap(h, MapId); Check(h.Model.Map.Id == MapId, "third party map selected"); return h;
    }
    private static void StartCurrent(Harness h, int index = 0) => h.Field<GameObject>(index == 0 ? "weaponA" : "weaponB").GetComponent<Button>().Press();
    private static StubMod Provider(params string[] names)
    {
        var mod = new StubMod { Manifest = new ModManifest { ID = Pack } };
        foreach (var name in names) mod.Sprites.Add(name, new Sprite { Name = name }); ModLoader.Mods.Add(mod); return mod;
    }
    private static List<RaidSpriteView> Views(RaidGraphic g) => Get<List<RaidSpriteView>>(g, "views");
    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }
    private static void RegisterContentIntegration()
    {
        Case("Entry Init registers built-in pack idempotently", () =>
        {
            ContentRegistry.Unregister(DefaultContent.PackId); var entry = new Entry(); entry.Init(new ModManifest()); entry.Init(new ModManifest());
            Check(ContentRegistry.IsRegistered(DefaultContent.PackId) && ContentRegistry.Packs.Count == 1, "default content registration");
        });
        Case("Map picker selection displays external metadata and restores built-in weapon controls", () =>
        {
            var h = OpenThirdParty(); Check(h.Field<TextMeshProUGUI>("intro").text.Contains("Test arena") && h.Field<TextMeshProUGUI>("intro").text.Contains("30 × 10"), "map introduction");
            Check(h.Field<GameObject>("weaponA").GetComponentInChildren<TextMeshProUGUI>().text == "Test auto", "dynamic weapon label");
            Check(!h.Field<GameObject>("weaponB").activeSelf, "single weapon button hidden");
            PickMap(h, DefaultContent.DefaultMapId); Check(h.Model.Map.Id == DefaultContent.DefaultMapId && h.Field<GameObject>("weaponB").activeSelf, "explicit built-in selection resets second weapon");
        });
        Case("Map cycle cannot change active run", () =>
        {
            var h = OpenThirdParty(); StartCurrent(h); var model = h.Model; Call(h.Driver, "CycleMap"); Check(ReferenceEquals(model, h.Model) && h.Model.Map.Id == MapId, "mid-run map change");
        });
        Case("Content theme applies to all active/inactive UI and real mesh", () =>
        {
            var h = OpenThirdParty(); var skin = h.Model.Skin; var panel = h.Field<GameObject>("panel");
            Check(panel.GetComponentsInChildren<Image>(true).Where(i => i.gameObject.name != "WeaponIcon" && i.gameObject.name != "LootIcon").All(i => i.color == VisualBridge.Color(i.GetComponent<Button>() != null ? skin.UIButton : skin.UIBackground)), "all themed image colors except untinted item/weapon icons");
            Check(h.Field<Image>("weaponIcon").color == (Color32)Color.white && h.Field<Image>("lootIcon").color == (Color32)Color.white, "icons remain white after DrawStatus");
            Check(panel.GetComponentsInChildren<TextMeshProUGUI>(true).All(t => t.color == VisualBridge.Color(skin.UIText)), "all TMP theme colors");
            Check(h.Field<TextMeshProUGUI>("heading").text.Contains("Test arena"), "heading theme map");
            Check(Mesh(h.Graphic).Vertices.Any(v => v.Color == VisualBridge.Color(skin.Floor)), "mesh theme floor");
        });
        Case("Chinese content labels and weapon names come from registered DTOs", () =>
        {
            ContentRegistry.Register(ThirdParty()); LocHelper.Locale = "zh-Hans";
            FontLoader.Instance = new FontLoader { simplifiedChineseFont = new TMP_FontAsset() };
            var h = new Harness(); h.Open(); PickMap(h, MapId);
            Check(h.Field<TextMeshProUGUI>("intro").text.Contains("测试竞技场"), "Chinese map title");
            Check(h.Field<GameObject>("weaponA").GetComponentInChildren<TextMeshProUGUI>().text == "测试武器", "Chinese weapon");
            StartCurrent(h); h.Tick(KeyCode.E); Check(h.Field<TextMeshProUGUI>("inventory").text.Contains("测试宝石 × 3"), "Chinese session item label");
        });
        Case("External one-weapon loadout ignores Alpha2 and uses Automatic metadata", () =>
        {
            var h = OpenThirdParty(); StartCurrent(h); h.Tick(KeyCode.Alpha2); Check(h.Model.CurrentWeapon.Id == GunId, "single weapon Alpha2 guard");
            Input.mousePosition = new Vector2(50, 0); Input.MouseHeld = true; h.Tick(); Check(h.Model.ShotSerial == 1, "external automatic fire");
            for (int i = 0; i < 6; i++) h.Tick(); Check(h.Model.ShotSerial > 1 && Debug.Errors.Count == 0, "external cooldown/repeat");
        });
        Case("Two external weapons use indexed buttons and keyboard selection", () =>
        {
            var h = OpenThirdParty(singleWeapon: false); Check(h.Field<GameObject>("weaponB").activeSelf, "second external weapon button"); StartCurrent(h, 1);
            Check(h.Model.CurrentWeapon.Id == Pack + ".second", "index one starting weapon"); h.Tick(KeyCode.Alpha1); Check(h.Model.CurrentWeapon.Id == GunId, "Alpha1 external");
            h.Tick(KeyCode.Alpha2); Check(h.Model.CurrentWeapon.Id == Pack + ".second", "Alpha2 external");
        });
        Case("No-extraction map status and mesh omit extraction objective", () =>
        {
            var h = OpenThirdParty(); Check(h.Field<TextMeshProUGUI>("status").text.Contains("No extraction objective"), "status no exit");
            Check(!Mesh(h.Graphic).Vertices.Any(v => v.Color == VisualBridge.Color(h.Model.Skin.Exit)), "unexpected extraction geometry");
            StartCurrent(h); h.Model.Update(10, Vec2.Zero); h.Tick(); Check(h.Model.State == RaidState.Playing && !h.Field<GameObject>("resultPanel").activeSelf, "sandbox should not auto-extract");
        });
        Case("Graphic mapping and WorldLocal/WorldSize follow external MapBounds", () =>
        {
            var h = OpenThirdParty(); var g = h.Graphic;
            Check(g.MouseWorld(new Vector2(100, 0), out var right), "wide map right edge"); Near(right.X, 30, "map right x"); Near(right.Y, 5, "map right y");
            Check(!g.MouseWorld(new Vector2(0, 40), out _), "non-default map letterbox");
            var center = g.WorldLocal(new Vec2(15, 5)); Near(center.x, 0, "world local x"); Near(center.y, 0, "world local y");
            var size = g.WorldSize(new Vector2(3, 3)); Near(size.x, 20, "world size scaling");
        });
        Case("Session item lookup and inventory survive unregister and conflicting re-registration", () =>
        {
            var h = OpenThirdParty(); StartCurrent(h); h.Tick(KeyCode.E); var old = h.Model; Check(old.LootCount == 3, "external loot acquired");
            ContentRegistry.Unregister(Pack); var replacement = ThirdParty(); replacement.Items[0].NameEn = "Changed gem"; replacement.Items[0].MaxStack = 1; replacement.Maps[0].Boxes[0].Loot[0].Count = 1; ContentRegistry.Register(replacement);
            h.Tick(); Check(h.Driver.enabled && ReferenceEquals(old, h.Model), "unload must not invalidate active run");
            Check(old.GetItem(ItemId).NameEn == "Test gem" && old.GetItem(ItemId).MaxStack == 5, "session snapshot item");
            Check(h.Field<TextMeshProUGUI>("inventory").text.Contains("Test gem × 3"), "status must not read replacement registry item");
            old.GetItem(ItemId).NameEn = "mutated"; old.Map.NameEn = "mutated"; var skinCopy = old.Skin; skinCopy.UIText.R = 0;
            Check(old.GetItem(ItemId).NameEn == "Test gem" && old.Map.NameEn == "Test arena", "outward DTO alias");
        });
        Case("Session GetItem rejects unknown/null and returned inventory is detached", () =>
        {
            var h = OpenThirdParty(); StartCurrent(h); h.Tick(KeyCode.E); Check(Throws<ArgumentNullException>(() => h.Model.GetItem(null)), "null item id");
            Check(Throws<KeyNotFoundException>(() => h.Model.GetItem("missing.item")), "unknown session item");
            h.Model.Inventory[0].Count = 999; Check(h.Model.Inventory[0].Count == 3, "inventory copy mutation");
            h.Close(); h.Open(); Check(h.Model.LootCount == 0 && h.Model.Inventory.Count == 0, "new run inventory isolation");
        });
        Case("Removed selected content falls back safely when preparation is requested", () =>
        {
            var h = OpenThirdParty(); ContentRegistry.Unregister(Pack); Call(h.Driver, "Prepare");
            Check(h.Model.Map.Id == DefaultContent.DefaultMapId && h.Driver.enabled && h.Field<bool>("opened"), "removed content fallback");
            h.Close(); Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "fallback close releases navigation");
        });
        Case("VisualBridge resolves only enabled provider and warns once per missing key", () =>
        {
            var provider = Provider("player"); Check(ReferenceEquals(VisualBridge.Find(Pack + ":player"), provider.Sprites["player"]), "provider sprite lookup");
            Check(VisualBridge.Find(null) == null && VisualBridge.Find(" ") == null && Debug.Warnings.Count == 0, "empty key");
            provider.IsEnabled = false; Check(VisualBridge.Find(Pack + ":player") == null, "disabled provider used"); VisualBridge.Find(Pack + ":player"); Check(Debug.Warnings.Count == 1, "missing warning repeated");
            provider.IsEnabled = true; UObj.Destroy(provider.Sprites["player"]); Check(VisualBridge.Find(Pack + ":player") == null, "destroyed sprite used");
            Check(VisualBridge.Find("invalid") == null && Debug.Warnings.Count == 2, "malformed key fallback");
        });
        Case("Content sprite views place actual provider sprites and skip missing fallback", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); var views = Views(h.Graphic);
            Check(views.Count == 4, "wall/crate/enemy/player sprite count; background is separate sibling");
            Check(views.All(v => v.GetComponent<Image>().enabled && !v.GetComponent<Image>().raycastTarget && v.GetComponent<Image>().sprite != null), "sprite enabled/raycast settings");
            var background = Get<GameObject>(h.Graphic, "backgroundObject").GetComponent<Image>();
            Check(background.transform.parent == h.Graphic.transform.parent && background.transform.GetSiblingIndex() < h.Graphic.transform.GetSiblingIndex(), "background must precede world mesh");
            Check(background.enabled && !background.raycastTarget && background.sprite != null, "background provider sprite");
            Near(background.rectTransform.anchorMin.x, h.Graphic.rectTransform.anchorMin.x, "background anchors");
            Near(background.rectTransform.offsetMax.y, h.Graphic.rectTransform.offsetMax.y - (140 - 10 * (200f / 30)) / 2, "letterboxed background upper offset");
            Near(background.rectTransform.offsetMin.y, h.Graphic.rectTransform.offsetMin.y + (140 - 10 * (200f / 30)) / 2, "letterboxed background lower offset");
            Near(background.rectTransform.offsetMax.x, h.Graphic.rectTransform.offsetMax.x, "background horizontal offset");
            var player = views.Single(v => v.Key.EndsWith(":player")).GetComponent<Image>().rectTransform;
            Near(player.anchoredPosition.x, -100 + 2 * (200f / 30), "player sprite x");
            Check(Debug.Warnings.Count == 0, "resolved sprites should not warn");
        });
        Case("Missing sprite creates disabled view and supports late provider availability", () =>
        {
            var h = OpenThirdParty(sprites: true); Check(Views(h.Graphic).All(v => !v.GetComponent<Image>().enabled), "missing sprites visible");
            Check(Debug.Warnings.Count == 5 && Mesh(h.Graphic).currentVertCount > 0, "fallback warning/geometry"); h.Tick(); Check(Debug.Warnings.Count == 5, "warning deduplication");
            Provider("player"); h.Tick(); Check(Views(h.Graphic).Single(v => v.Key.EndsWith(":player")).GetComponent<Image>().enabled, "late sprite recovery");
        });
        Case("Sprite box/enemy visibility follows real model state without reallocating", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); StartCurrent(h); var views = Views(h.Graphic).ToArray();
            h.Tick(KeyCode.E); h.Model.Enemies[0].Health = 0; h.Tick();
            Check(views.SequenceEqual(Views(h.Graphic)), "sprite views rebuilt unnecessarily");
            Check(!views.Single(v => v.Key.EndsWith(":crate")).GetComponent<Image>().enabled && !views.Single(v => v.Key.EndsWith(":enemy")).GetComponent<Image>().enabled, "opened crate/dead enemy sprite visibility");
        });
        Case("Model replacement and explicit Graphic null refresh dispose prior sprite views", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); var old = Views(h.Graphic).ToArray(); StartCurrent(h);
            Check(old.All(v => v == null && !v.gameObject.activeSelf), "previous preparation sprites destroyed");
            var active = Views(h.Graphic).ToArray(); h.Graphic.Model = null; h.Graphic.Refresh();
            Check(Views(h.Graphic).Count == 0 && active.All(v => v == null), "null model sprite cleanup");
        });
        Case("Scene loss destroys third-party sprite children and restores menu navigation", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); StartCurrent(h); var views = Views(h.Graphic).ToArray(); var graphic = h.Graphic;
            var background = Get<GameObject>(graphic, "backgroundObject"); SceneManager.ActiveScene = new Scene(8); h.Tick();
            Check(graphic == null && background == null && views.All(v => v == null && !v.gameObject.activeSelf), "scene sprite/background teardown");
            Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection && h.Model == null, "scene input/session cleanup");
        });
        Case("Model Reset refresh rebuilds sprite views bound to replacement entities", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); StartCurrent(h);
            h.Tick(KeyCode.E); h.Model.Enemies[0].Health = 0; h.Tick(); var previous = Views(h.Graphic).ToArray(); var background = Get<GameObject>(h.Graphic, "backgroundObject");
            h.Model.Reset(); h.Graphic.Refresh(); Check(previous.All(v => v == null) && background == null, "reset must discard old entity bindings/background");
            Check(Views(h.Graphic).Single(v => v.Key.EndsWith(":crate")).GetComponent<Image>().enabled && Views(h.Graphic).Single(v => v.Key.EndsWith(":enemy")).GetComponent<Image>().enabled, "reset sprites follow new entities");
        });
        Case("Map picker selection clears third-party background sibling and optional views", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); var background = Get<GameObject>(h.Graphic, "backgroundObject"); var old = Views(h.Graphic).ToArray();
            PickMap(h, DefaultContent.DefaultMapId);
            Check(background == null && old.All(v => v == null) && Get<GameObject>(h.Graphic, "backgroundObject") == null && Views(h.Graphic).Count == 0, "default map retained old content sprites");
        });
        Case("Graphic OnDestroy clears view bookkeeping idempotently", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); var graphic = h.Graphic; var old = Views(graphic).ToArray(); UObj.Destroy(graphic.gameObject);
            Check(old.All(v => v == null) && Views(graphic).Count == 0 && Get<List<Action>>(graphic, "placeSprites").Count == 0, "OnDestroy bookkeeping");
            Call(graphic, "OnDestroy"); Check(Views(graphic).Count == 0, "repeated Graphic destruction");
        });
        Case("Invalid registry input is atomic and leaves menu usable", () =>
        {
            var h = new Harness(); h.Open(); var bad = ThirdParty(); bad.Maps[0].Loadout = new[] { "missing.weapon" };
            Check(Throws<ArgumentException>(() => ContentRegistry.Register(bad)) && !ContentRegistry.IsRegistered(Pack), "invalid content partially registered");
            PickMap(h, DefaultContent.DefaultMapId); Check(h.Model.Map.Id == DefaultContent.DefaultMapId && h.Driver.enabled, "invalid content broke menu");
        });
    }
}
