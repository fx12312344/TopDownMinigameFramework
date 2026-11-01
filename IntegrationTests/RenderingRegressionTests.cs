using RaidPrototype;
using RaidPrototype.Content;
using UnityEngine;
using UnityEngine.UI;

static partial class Suite
{
    private static VertexHelper ForegroundMesh(RaidForeground foreground)
    {
        var helper = new VertexHelper(); Call(foreground, "OnPopulateMesh", helper); return helper;
    }
    private static void ValidMesh(VertexHelper mesh, string label)
    {
        Check(mesh.currentVertCount < 65000, $"{label} vertex limit: {mesh.currentVertCount}");
        Check(mesh.Vertices.All(v => float.IsFinite(v.Position.x) && float.IsFinite(v.Position.y)), label + " nonfinite vertex");
        Check(mesh.Triangles.All(t => t.A >= 0 && t.B >= 0 && t.C >= 0 && t.A < mesh.currentVertCount && t.B < mesh.currentVertCount && t.C < mesh.currentVertCount), label + " invalid triangle");
    }
    private static void RegisterRenderingRegressions()
    {
        Case("Background precedes arena and cannot replace world geometry; foreground follows all sprites", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); var g = h.Graphic;
            var bg = Get<GameObject>(g, "backgroundObject"); var fg = Get<RaidForeground>(g, "foreground");
            Check(bg.transform.parent == g.transform.parent && bg.transform.GetSiblingIndex() < g.transform.GetSiblingIndex(), "background not behind arena");
            Check(fg.transform.parent == g.transform && Views(g).All(v => v.transform.GetSiblingIndex() < fg.transform.GetSiblingIndex()), "foreground not after sprites");
            Check(!fg.raycastTarget && ReferenceEquals(fg.Owner, g), "foreground owner/input");
            var world = Mesh(g); Check(world.Vertices.Any(v => v.Color == VisualBridge.Color(g.Model.Skin.Wall)) && world.Vertices.Any(v => v.Color == VisualBridge.Color(g.Model.Skin.Player)), "background suppressed actual world");
            Check(!world.Vertices.Any(v => v.Color == VisualBridge.Color(g.Model.Skin.Floor)), "resolved background should replace fallback floor only");
            ModLoader.Mods.Clear(); bg.GetComponent<Image>().sprite = null; g.Refresh();
            Check(Mesh(g).Vertices.Any(v => v.Color == VisualBridge.Color(g.Model.Skin.Floor)), "missing background must recover fallback floor");
        });
        Case("Foreground owns shot/crosshair mesh above sprites and refresh marks it dirty", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); StartCurrent(h); var g = h.Graphic; var fg = Get<RaidForeground>(g, "foreground");
            Check(h.Model.Fire(new Vec2(25, 2)), "foreground shot setup"); g.Aim = new Vector2(25, 2); g.ShotFlash = .07f; int before = fg.DirtyCount; g.Refresh();
            Check(fg.DirtyCount > before, "foreground not dirtied"); var mesh = ForegroundMesh(fg);
            Check(mesh.Vertices.Any(v => v.Color == VisualBridge.Color(g.Model.Skin.Bullet)), "foreground shot missing");
            Check(!Mesh(g).Vertices.Any(v => v.Color == VisualBridge.Color(g.Model.Skin.Bullet)), "shot must not remain under sprite layer");
            Check(mesh.Vertices.Any(v => v.Color == VisualBridge.Color(g.Model.Skin.UIText)), "foreground cursor/aim missing"); ValidMesh(mesh, "foreground");
        });
        Case("Sprite tint distinguishes factions and survives ApplyTheme", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); var skin = h.Model.Skin;
            var views = Views(h.Graphic); var player = views.Single(v => v.Key.EndsWith(":player")).GetComponent<Image>(); var enemy = views.Single(v => v.Key.EndsWith(":enemy")).GetComponent<Image>();
            Check(player.color == VisualBridge.Color(skin.Player) && enemy.color == VisualBridge.Color(skin.Enemy) && player.color != enemy.color, "faction tint");
            Check(views.Single(v => v.Key.EndsWith(":crate")).GetComponent<Image>().color == VisualBridge.Color(skin.Crate), "crate tint");
            Check(views.Single(v => v.Key.EndsWith(":wall")).GetComponent<Image>().color == VisualBridge.Color(skin.Wall), "wall tint");
            Call(h.Driver, "ApplyTheme"); Check(player.color == VisualBridge.Color(skin.Player) && enemy.color == VisualBridge.Color(skin.Enemy), "UI theme overwrote sprite tint");
        });
        Case("Close immediately releases rendered model/cache and destroys background/foreground/sprites", () =>
        {
            Provider("background", "player", "enemy", "crate", "wall"); var h = OpenThirdParty(sprites: true); StartCurrent(h); var g = h.Graphic; var views = Views(g).ToArray();
            var bg = Get<GameObject>(g, "backgroundObject"); var fg = Get<RaidForeground>(g, "foreground"); h.Close();
            Check(g.Model == null && Get<RaidModel>(g, "renderedModel") == null, "close retained session model");
            Check(Get<MapDefinition>(g, "mapData") == null && Get<SkinDefinition>(g, "skinData") == null && !Get<bool>(g, "spritesBuilt"), "close retained content cache");
            Check(bg == null && fg == null && views.All(v => v == null) && Views(g).Count == 0, "close retained render objects");
            Check(Get<List<Action>>(g, "placeSprites").Count == 0 && h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "close retained delegates/navigation");
        });
        Case("Unregister before StartRaid returns to default preparation without throwing or starting", () =>
        {
            var h = OpenThirdParty(); ContentRegistry.Unregister(Pack); StartCurrent(h);
            Check(h.Driver.enabled && h.Field<bool>("opened") && !h.Field<bool>("started"), "missing map started run/disabled driver");
            Check(h.Model.Map.Id == DefaultContent.DefaultMapId && h.Field<GameObject>("preparation").activeSelf, "missing map preparation fallback");
            Check(!h.Events.sendNavigationEvents && Debug.Errors.Count == 0, "fallback navigation/error"); h.Close(); Check(h.Events.sendNavigationEvents, "fallback close restore");
        });
        Case("Weapon/item icons resolve sprite keys, honor skin fallback and hide unresolved textures", () =>
        {
            var pack = ThirdParty(singleWeapon: false); pack.Weapons[0].SpriteKey = Pack + ":gunIcon";
            pack.Weapons[1].SpriteKey = Pack + ":missingGunIcon"; pack.Items[0].SpriteKey = Pack + ":gemIcon"; pack.Skins[0].ItemKey = Pack + ":fallbackIcon";
            ContentRegistry.Register(pack); var provider = Provider("gunIcon", "gemIcon", "fallbackIcon"); var h = new Harness(); h.Open(); PickMap(h, MapId);
            var weapon = h.Field<Image>("weaponIcon"); var loot = h.Field<Image>("lootIcon");
            Check(weapon.enabled && ReferenceEquals(weapon.sprite, provider.Sprites["gunIcon"]) && !loot.enabled, "weapon icon before loot");
            Check(weapon.preserveAspect && loot.preserveAspect && !weapon.raycastTarget && !loot.raycastTarget, "icon image settings");
            StartCurrent(h); h.Tick(KeyCode.E); Check(loot.enabled && ReferenceEquals(loot.sprite, provider.Sprites["gemIcon"]), "explicit item key wins over skin fallback");
            h.Tick(KeyCode.Alpha2); Check(!weapon.enabled && loot.enabled && h.Driver.enabled, "missing weapon sprite must hide icon, not close run");
            provider.Sprites.Remove("gemIcon"); h.Tick(); Check(!loot.enabled && h.Field<TMPro.TextMeshProUGUI>("inventory").text.Contains("Test gem × 3"), "unresolved explicit texture must preserve text fallback");
            h.Close(); ContentRegistry.Unregister(Pack); pack.Items[0].SpriteKey = null; ContentRegistry.Register(pack); h.Open(); StartCurrent(h); h.Tick(KeyCode.E);
            Check(loot.enabled && ReferenceEquals(loot.sprite, provider.Sprites["fallbackIcon"]), "missing item key falls back to skin.ItemKey");
            Call(h.Driver, "ApplyTheme"); h.Tick(); Check(weapon.color == (Color32)Color.white && loot.color == (Color32)Color.white, "DrawStatus restores white icon tint");
            provider.Sprites.Remove("fallbackIcon"); h.Tick(); Check(!loot.enabled && h.Driver.enabled && Debug.Errors.Count == 0, "missing fallback sprite must keep UI usable");
        });
        Case("Maximum legal 10000-square map and entity limits stay below 65000 vertices", () =>
        {
            var pack = ThirdParty(); var map = pack.Maps[0]; map.Width = map.Height = 10000;
            map.Enemies = Enumerable.Range(0, ContentRegistry.MaxEnemiesPerMap).Select(i => new EnemySpawn { Position = new Vec2(20 + i * 2, 20), Speed = 0 }).ToArray();
            map.Boxes = Enumerable.Range(0, ContentRegistry.MaxBoxesPerMap).Select(i => new BoxSpawn { Position = new Vec2(20 + i * 2, 30) }).ToArray();
            map.Obstacles = Enumerable.Range(0, ContentRegistry.MaxObstaclesPerMap).Select(i => new RaidPrototype.Rect(100 + i * 2, 100, 101 + i * 2, 101)).ToArray();
            map.HasExtraction = true; map.ExtractionPoint = new Vec2(9990, 9990); ContentRegistry.Register(pack);
            var g = Graphic(); g.Model = new RaidModel(MapId); g.Refresh(); var mesh = Mesh(g); var foreground = ForegroundMesh(Get<RaidForeground>(g, "foreground"));
            Check(g.Model.Enemies.Count == 64 && g.Model.Boxes.Count == 128 && g.Model.Obstacles.Count == 256, "maximum fixture counts");
            Check(mesh.currentVertCount > 5000, "max fixture underpopulated"); ValidMesh(mesh, "max world"); ValidMesh(foreground, "max foreground");
            Console.WriteLine($"METRIC max legal map 10000x10000 / enemy=64 box=128 wall=256: world={mesh.currentVertCount} vertices, foreground={foreground.currentVertCount} vertices.");
        });
    }
}
