using System.Text.RegularExpressions;
using RaidPrototype;
using RaidPrototype.Content;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

static partial class Suite
{
    // Exercise the listeners attached to the real linked UI. Never invoke SelectMap as
    // a shortcut for user selection, or relax the stub's inactive-button rejection.
    private static string ButtonText(Button button) => button.GetComponentInChildren<TextMeshProUGUI>(true).text;
    private static void OpenMapPicker(Harness h)
    {
        var preparation = h.Field<GameObject>("preparation");
        Check(preparation.activeInHierarchy, "map entry must be reached from visible preparation");
        var entry = preparation.GetComponentsInChildren<Button>()
            .Single(b => b.gameObject != h.Field<GameObject>("weaponA") && b.gameObject != h.Field<GameObject>("weaponB"));
        var before = h.Model;
        entry.Press();
        Check(h.Field<GameObject>("mapPicker").activeInHierarchy, "map entry opens the real picker overlay");
        Check(ReferenceEquals(before, h.Model), "opening picker must not silently cycle or replace the selected model");
    }
    private static Button MapChoice(Harness h, string mapId) => h.Field<GameObject>("mapPicker")
        .GetComponentsInChildren<Button>().Single(b => ButtonText(b).Split('\n').Any(line => line.Trim() == mapId));
    private static string[] VisibleMapIds(Harness h)
    {
        var ids = ContentRegistry.Maps.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return h.Field<GameObject>("mapPicker").GetComponentsInChildren<Button>()
            .SelectMany(b => ButtonText(b).Split('\n').Select(line => line.Trim()).Where(ids.Contains)).ToArray();
    }
    private static void PickMap(Harness h, string mapId)
    {
        OpenMapPicker(h);
        MapChoice(h, mapId).Press();
        Check(h.Model.Map.Id == mapId && ReferenceEquals(h.Graphic.Model, h.Model), "actual map button must select the requested model: " + mapId);
        Check(!h.Field<GameObject>("mapPicker").activeSelf && h.Field<GameObject>("preparation").activeInHierarchy, "selection returns to preparation, not raid");
        Check(!h.Field<bool>("started"), "map selection must not start a raid");
    }
    private static void MapPosition(Harness h, int index, int count)
    {
        Check(Regex.IsMatch(h.Field<TextMeshProUGUI>("intro").text, $@"\b{index}\s*/\s*{count}\b"), $"preparation must show current map index/total {index}/{count}");
    }
    private static void MapPickerRefreshPeriod(Harness h)
    {
        Time.unscaledDeltaTime = .1f;
        for (int i = 0; i < 12; i++) h.Tick();
        Time.unscaledDeltaTime = .02f;
    }
    private static void PickerControl(Harness h, string text) => h.Field<GameObject>("mapPicker")
        .GetComponentsInChildren<Button>().Single(b => ButtonText(b) == text).Press();
    private static ContentPack PagedPack()
    {
        var pack = ThirdParty();
        var template = pack.Maps[0];
        // Registry insertion order deliberately differs from map-id order.
        pack.Maps = new[] { 8, 2, 6, 0, 7, 1, 5, 3, 4 }.Select(i => new MapDefinition
        {
            Id = Pack + ".page" + i, NameZh = "分页地图 " + i, NameEn = "Page map " + i,
            Width = template.Width, Height = template.Height, PlayerSpawn = template.PlayerSpawn,
            HasExtraction = false, SkinId = SkinId, Loadout = template.Loadout
        }).ToArray();
        return pack;
    }
    private static void RegisterMapPickerIntegration()
    {
        Case("Map picker lists both registered maps and real buttons replace model, skin and weapon metadata", () =>
        {
            ContentRegistry.Register(ThirdParty()); var h = new Harness(); h.Open(); var original = h.Model;
            MapPosition(h, 1, 2); OpenMapPicker(h);
            Check(VisibleMapIds(h).SequenceEqual(new[] { DefaultContent.DefaultMapId, MapId }.OrderBy(id => id, StringComparer.Ordinal)), "both map ids must appear in sorted picker");
            var choices = h.Field<GameObject>("mapPicker").GetComponentsInChildren<Button>().Select(ButtonText).ToArray();
            Check(choices.Any(t => t.Contains(ContentRegistry.GetMap(DefaultContent.DefaultMapId).NameEn)) && choices.Any(t => t.Contains("Test arena")), "both map names must be visible, not just registry entries");
            MapChoice(h, MapId).Press();
            Check(!ReferenceEquals(original, h.Model) && h.Model.Map.Id == MapId && ReferenceEquals(h.Graphic.Model, h.Model), "external button must create and bind a fresh real model");
            Check(h.Model.Skin.Id == SkinId && h.Model.CurrentWeapon.Id == GunId, "selected map must supply real skin and weapon");
            Check(h.Field<GameObject>("weaponA").GetComponentInChildren<TextMeshProUGUI>(true).text == "Test auto" && !h.Field<GameObject>("weaponB").activeSelf, "selection updates weapon labels/visibility");
            Check(h.Field<GameObject>("preparation").activeInHierarchy && !h.Field<GameObject>("mapPicker").activeSelf, "selection closes picker and restores preparation");
            MapPosition(h, 2, 2);
            Check(Mesh(h.Graphic).Vertices.Any(v => v.Color == VisualBridge.Color(h.Model.Skin.Floor)), "selected map reaches actual graphic mesh");
            PickMap(h, DefaultContent.DefaultMapId); MapPosition(h, 1, 2);
            Check(h.Model.Skin.Id != SkinId && h.Model.CurrentWeapon.Id != GunId && h.Field<GameObject>("weaponB").activeSelf, "built-in button restores original skin/loadout");
        });
        Case("Map picker updates visible choices after late registration without cycling selection", () =>
        {
            var h = new Harness(); h.Open(); OpenMapPicker(h); var original = h.Model;
            Check(VisibleMapIds(h).SequenceEqual(new[] { DefaultContent.DefaultMapId }), "initial default-only picker");
            ContentRegistry.Register(ThirdParty()); MapPickerRefreshPeriod(h);
            Check(VisibleMapIds(h).SequenceEqual(new[] { DefaultContent.DefaultMapId, MapId }.OrderBy(id => id, StringComparer.Ordinal)), "late registered map missing from open picker");
            Check(ReferenceEquals(original, h.Model), "late registration changed current preparation selection");
            MapChoice(h, MapId).Press(); MapPosition(h, 2, 2);
            Check(h.Model.Map.Id == MapId && h.Model.CurrentWeapon.Id == GunId, "late map actual button failed selection");
        });
        Case("Map picker missing selected content falls back when user reopens map selection", () =>
        {
            var h = OpenThirdParty(); ContentRegistry.Unregister(Pack); OpenMapPicker(h);
            Check(VisibleMapIds(h).SequenceEqual(new[] { DefaultContent.DefaultMapId }), "unloaded map retained in picker");
            MapChoice(h, DefaultContent.DefaultMapId).Press(); MapPosition(h, 1, 1);
            Check(h.Model.Map.Id == DefaultContent.DefaultMapId && h.Driver.enabled && !h.Field<bool>("started"), "unloaded selection did not return safely to default preparation");
            Check(Debug.Errors.Count == 0 && !h.Events.sendNavigationEvents, "fallback threw or released overlay input early");
        });
        Case("Map picker selection tolerates content disappearing between draw and button press", () =>
        {
            ContentRegistry.Register(ThirdParty()); var h = new Harness(); h.Open(); OpenMapPicker(h);
            var stale = MapChoice(h, MapId); ContentRegistry.Unregister(Pack);
            // No intervening Update: the user is pressing a still-visible, genuinely wired button.
            stale.Press();
            Check(h.Model.Map.Id == DefaultContent.DefaultMapId && h.Driver.enabled && Debug.Errors.Count == 0, "stale map button must fall back instead of throwing");
            Check(h.Field<GameObject>("preparation").activeInHierarchy && !h.Field<GameObject>("mapPicker").activeSelf, "stale selection must restore preparation");
            MapPosition(h, 1, 1);
        });
        Case("Map picker sorts all ids into four-item pages and actual page controls select their displayed map", () =>
        {
            ContentRegistry.Register(PagedPack()); var h = new Harness(); h.Open(); OpenMapPicker(h); var original = h.Model;
            var sorted = ContentRegistry.Maps.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Check(sorted.Length == 10 && VisibleMapIds(h).SequenceEqual(sorted.Take(4)), "first page must show exactly four maps in id order");
            PickerControl(h, "Previous page"); Check(VisibleMapIds(h).SequenceEqual(sorted.Take(4)), "previous at first page must clamp");
            PickerControl(h, "Next page"); Check(VisibleMapIds(h).SequenceEqual(sorted.Skip(4).Take(4)), "second page has wrong ids/order");
            PickerControl(h, "Next page"); Check(VisibleMapIds(h).SequenceEqual(sorted.Skip(8)), "last page must hide unused map slots");
            Check(h.Field<GameObject[]>("mapChoices").Count(go => go.activeSelf) == 2, "last page must not leave stale clickable map choices");
            PickerControl(h, "Next page"); Check(VisibleMapIds(h).SequenceEqual(sorted.Skip(8)), "next at last page must clamp");
            PickerControl(h, "Previous page"); Check(VisibleMapIds(h).SequenceEqual(sorted.Skip(4).Take(4)), "backward paging did not restore second page");
            Check(ReferenceEquals(original, h.Model), "paging must not replace the selected model");
            MapChoice(h, sorted[6]).Press(); MapPosition(h, 7, 10);
            Check(h.Model.Map.Id == sorted[6] && h.Model.CurrentWeapon.Id == GunId && h.Model.Skin.Id == SkinId, "page-two button listener selected the wrong map");
            OpenMapPicker(h); Check(VisibleMapIds(h).SequenceEqual(sorted.Take(4)), "reopening should reset to first page");
            PickerControl(h, "Next page"); PickerControl(h, "Next page"); ContentRegistry.Unregister(Pack); MapPickerRefreshPeriod(h);
            Check(VisibleMapIds(h).SequenceEqual(new[] { DefaultContent.DefaultMapId }), "unload while on last page must clamp and remove obsolete choices");
            MapChoice(h, DefaultContent.DefaultMapId).Press(); MapPosition(h, 1, 1);
            Check(h.Model.Map.Id == DefaultContent.DefaultMapId && Debug.Errors.Count == 0, "unload/page shrink fallback failed");
        });
        Case("Map picker back and Escape restore preparation without replacing selection or releasing input", () =>
        {
            var h = OpenThirdParty(); var selected = h.Model; OpenMapPicker(h);
            PickerControl(h, "Back to preparation");
            Check(!h.Field<GameObject>("mapPicker").activeSelf && h.Field<GameObject>("preparation").activeInHierarchy, "back button did not restore preparation");
            Check(ReferenceEquals(selected, h.Model) && !h.Events.sendNavigationEvents && h.Field<bool>("opened"), "back changed selection or closed sandbox");
            OpenMapPicker(h); h.Tick(KeyCode.Escape);
            Check(!h.Field<GameObject>("mapPicker").activeSelf && h.Field<GameObject>("preparation").activeInHierarchy && ReferenceEquals(selected, h.Model), "Escape must close picker only");
            Check(h.Field<bool>("opened") && !h.Events.sendNavigationEvents, "picker Escape released sandbox input");
        });
        Case("Legacy private CycleMap remains compatible while the map entry only opens picker", () =>
        {
            ContentRegistry.Register(ThirdParty()); var h = new Harness(); h.Open();
            var method = h.Driver.GetType().GetMethod("CycleMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check(method != null && method.IsPrivate, "legacy private CycleMap missing");
            Call(h.Driver, "CycleMap"); Check(h.Model.Map.Id == MapId, "legacy forward cycle");
            Call(h.Driver, "CycleMap"); Check(h.Model.Map.Id == DefaultContent.DefaultMapId, "legacy cycle wrap");
            OpenMapPicker(h); Check(h.Model.Map.Id == DefaultContent.DefaultMapId, "new entry still directly cycles");
        });
        Case("Scene loss with map picker open destroys picker/buttons and restores captured navigation", () =>
        {
            ContentRegistry.Register(ThirdParty()); var h = new Harness(); h.Open(); OpenMapPicker(h);
            var canvas = h.Field<GameObject>("canvasObject"); var picker = h.Field<GameObject>("mapPicker");
            var buttons = picker.GetComponentsInChildren<Button>(true).ToArray();
            SceneManager.ActiveScene = new Scene(27); h.Tick();
            Check(canvas == null && picker == null && buttons.All(b => b == null && !b.gameObject.activeInHierarchy), "scene loss retained map-picker UI or live callbacks");
            Check(h.Model == null && !h.Field<bool>("opened") && h.Field<GameObject>("canvasObject") == null, "scene loss retained map session");
            Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "scene loss failed to restore navigation/selection");
            h.Tick(); Check(Debug.Errors.Count == 0, "repeat picker cleanup error");
        });
    }
}
