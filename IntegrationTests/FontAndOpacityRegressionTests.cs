using RaidPrototype;
using RaidPrototype.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

static partial class Suite
{
    private static TextMeshProUGUI MenuLabel(MainMenuUIController menu, string name, TMP_FontAsset font)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(menu.transform, false); label.font = font; return label;
    }
    private static void FontRefreshPeriod(Harness h)
    {
        // Drive Update rather than invoking RefreshFont directly. Extra ticks avoid float-boundary assumptions.
        float previous = Time.unscaledDeltaTime; Time.unscaledDeltaTime = .1f;
        for (int i = 0; i < 12; i++) h.Tick();
        Time.unscaledDeltaTime = previous;
    }
    private static void AllLabelFonts(Harness h, TMP_FontAsset expected, string context)
    {
        var canvas = h.Field<GameObject>("canvasObject");
        Check(canvas != null && expected != null, context + ": canvas and expected font exist");
        var labels = canvas.GetComponentsInChildren<TextMeshProUGUI>(true).ToArray();
        Check(labels.Length >= 14, context + ": all generated labels included");
        Check(labels.Any(t => !t.gameObject.activeInHierarchy), context + ": inactive coverage");
        foreach (var label in labels)
            Check(ReferenceEquals(label.font, expected), context + ": label font for " + label.transform.parent.gameObject.name);
        Check(ReferenceEquals(h.Field<TMP_FontAsset>("font"), expected), context + ": cached font");
    }
    private static void RegisterFontAndOpacityRegressions()
    {
        Case("Chinese loaded FontLoader font is used by every generated active/inactive label", () =>
        {
            LocHelper.Locale = "zh-Hans";
            FontLoader.Instance = new FontLoader
            {
                simplifiedChineseFont = new TMP_FontAsset(), japaneseFont = new TMP_FontAsset(),
                koreanFont = new TMP_FontAsset(), notoSans = new TMP_FontAsset()
            };
            var h = new Harness();
            h.Menu.title = MenuLabel(h.Menu, "Decorative title", new TMP_FontAsset());
            h.Menu.body = MenuLabel(h.Menu, "Body", new TMP_FontAsset());
            h.Menu.versionTMP = MenuLabel(h.Menu, "Version", new TMP_FontAsset());
            AllLabelFonts(h, FontLoader.Instance.simplifiedChineseFont, "closed Chinese overlay");
            h.Open(); AllLabelFonts(h, FontLoader.Instance.simplifiedChineseFont, "Chinese preparation");
            Check(h.Field<TextMeshProUGUI>("intro").text.Contains("演示区域"), "Chinese introduction text");
            StartCurrent(h); h.Tick(KeyCode.B);
            Check(h.Field<TextMeshProUGUI>("pauseNotice").text.Contains("已暂停"), "Chinese pause text");
            AllLabelFonts(h, FontLoader.Instance.simplifiedChineseFont, "Chinese running/paused overlay");
            Check(h.Menu.title.font != FontLoader.Instance.simplifiedChineseFont, "decorative title not selected or mutated");
            Check(Debug.Errors.Count == 0 && h.Driver.enabled, "Chinese UI stays usable");
        });
        foreach (bool loaderPresent in new[] { false, true })
            Case("Chinese UI waits for late font without losing menu: " + (loaderPresent ? "loader asset absent" : "loader absent"), () =>
            {
                LocHelper.Locale = "ZH-TW";
                if (loaderPresent) FontLoader.Instance = new FontLoader
                {
                    notoSans = new TMP_FontAsset(), japaneseFont = new TMP_FontAsset(), koreanFont = new TMP_FontAsset()
                };
                var h = new Harness();
                h.Menu.title = MenuLabel(h.Menu, "Decorative title", new TMP_FontAsset());
                h.Menu.body = MenuLabel(h.Menu, "Body", new TMP_FontAsset());
                h.Menu.versionTMP = MenuLabel(h.Menu, "Version", new TMP_FontAsset());
                FontRefreshPeriod(h);
                Check(h.Field<GameObject>("canvasObject") == null && h.Field<GameObject>("launcher") == null, "no partial UI before Chinese font exists");
                Check(ReferenceEquals(h.Field<MainMenuUIController>("menu"), h.Menu), "valid menu retained while waiting");
                Check(h.Driver.enabled && h.Menu.gameObject.activeInHierarchy && MainMenuUIController.Instance == h.Menu, "waiting must not disable driver/menu");
                Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection && h.Model == null, "waiting must not capture input or create session");
                Check(!GameObject.All.Any(go => go != null && go.name == "RaidPrototype Canvas"), "no leaked waiting overlays");
                FontLoader.Instance ??= new FontLoader(); FontLoader.Instance.simplifiedChineseFont = new TMP_FontAsset();
                FontRefreshPeriod(h);
                AllLabelFonts(h, FontLoader.Instance.simplifiedChineseFont, "Chinese late-loaded font");
                var canvas = h.Field<GameObject>("canvasObject"); FontRefreshPeriod(h);
                Check(ReferenceEquals(canvas, h.Field<GameObject>("canvasObject")) && GameObject.All.Count(go => go != null && go.name == "RaidPrototype Canvas") == 1, "late font creates UI exactly once");
                h.Open(); Check(h.Field<bool>("opened") && h.Model != null && !h.Events.sendNavigationEvents, "late font overlay opens normally");
                Call(h.Driver, "Close"); Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "late font overlay restores input");
                Check(Debug.Errors.Count == 0, "no waiting/late-load errors");
            });
        foreach (var locale in new[] { "ja", "ko" })
            Case("CJK locale waits for matching late font rather than falling back: " + locale, () =>
            {
                LocHelper.Locale = locale; FontLoader.Instance = new FontLoader
                {
                    simplifiedChineseFont = new TMP_FontAsset(), notoSans = new TMP_FontAsset()
                };
                var h = new Harness(); h.Menu.body = MenuLabel(h.Menu, "Body", new TMP_FontAsset()); FontRefreshPeriod(h);
                Check(h.Field<GameObject>("canvasObject") == null && h.Driver.enabled, "must not use another language or menu/default font");
                Check(ReferenceEquals(h.Field<MainMenuUIController>("menu"), h.Menu) && h.Events.sendNavigationEvents, "waiting keeps menu/navigation");
                var loaded = new TMP_FontAsset();
                if (locale == "ja") FontLoader.Instance.japaneseFont = loaded; else FontLoader.Instance.koreanFont = loaded;
                FontRefreshPeriod(h); AllLabelFonts(h, loaded, locale + " late matching font");
                Check(Debug.Errors.Count == 0, "CJK missing font is not an exception");
            });
        Case("Periodic font refresh updates inactive and later-added TMP labels without locale change", () =>
        {
            LocHelper.Locale = "zh-CN"; FontLoader.Instance = new FontLoader { simplifiedChineseFont = new TMP_FontAsset() };
            var h = new Harness(); h.Open(); StartCurrent(h);
            var canvas = h.Field<GameObject>("canvasObject"); var session = h.Model;
            var late = new GameObject("Late inactive label", typeof(RectTransform), typeof(TextMeshProUGUI));
            late.transform.SetParent(h.Field<GameObject>("resultPanel").transform, false);
            late.GetComponent<TextMeshProUGUI>().font = new TMP_FontAsset();
            Check(!late.activeInHierarchy && !h.Field<GameObject>("preparation").activeInHierarchy, "inactive label and preparation fixture");
            FontLoader.Instance.simplifiedChineseFont = new TMP_FontAsset(); FontRefreshPeriod(h);
            AllLabelFonts(h, FontLoader.Instance.simplifiedChineseFont, "same-locale replacement");
            late.GetComponent<TextMeshProUGUI>().font = new TMP_FontAsset(); FontRefreshPeriod(h);
            AllLabelFonts(h, FontLoader.Instance.simplifiedChineseFont, "unchanged resolved font repairs late inactive label");
            Check(ReferenceEquals(canvas, h.Field<GameObject>("canvasObject")) && ReferenceEquals(session, h.Model), "refresh must not rebuild overlay or reset session");
            Check(h.Field<bool>("started") && !h.Events.sendNavigationEvents && Debug.Errors.Count == 0, "refresh preserves active run/navigation");
        });
        Case("Locale switching selects direct loaded zh/ja/ko/noto fonts and keeps the current run", () =>
        {
            FontLoader.Instance = new FontLoader
            {
                simplifiedChineseFont = new TMP_FontAsset(), japaneseFont = new TMP_FontAsset(),
                koreanFont = new TMP_FontAsset(), notoSans = new TMP_FontAsset()
            };
            var h = new Harness();
            h.Menu.title = MenuLabel(h.Menu, "Decorative title", new TMP_FontAsset());
            h.Menu.body = MenuLabel(h.Menu, "Body", new TMP_FontAsset());
            h.Menu.versionTMP = MenuLabel(h.Menu, "Version", new TMP_FontAsset());
            h.Start(); var canvas = h.Field<GameObject>("canvasObject"); var session = h.Model;
            var labels = canvas.GetComponentsInChildren<TextMeshProUGUI>(true).ToArray();
            foreach (var step in new (string Locale, TMP_FontAsset Font)[]
            {
                ("en", FontLoader.Instance.notoSans), ("zh-Hans", FontLoader.Instance.simplifiedChineseFont),
                ("ja", FontLoader.Instance.japaneseFont), ("ko", FontLoader.Instance.koreanFont),
                ("fr", FontLoader.Instance.notoSans), ("ZH-TW", FontLoader.Instance.simplifiedChineseFont),
                (null, FontLoader.Instance.notoSans)
            })
            {
                LocHelper.Locale = step.Locale; FontRefreshPeriod(h);
                AllLabelFonts(h, step.Font, "locale " + (step.Locale ?? "null"));
                Check(ReferenceEquals(canvas, h.Field<GameObject>("canvasObject")) && ReferenceEquals(session, h.Model), "locale switch retains canvas/session");
                Check(labels.SequenceEqual(canvas.GetComponentsInChildren<TextMeshProUGUI>(true)), "locale switch does not replace TMP children");
                Check(h.Field<bool>("opened") && h.Field<bool>("started") && !h.Events.sendNavigationEvents, "locale switch preserves run/input capture");
            }
            Check(Debug.Errors.Count == 0 && h.Driver.enabled, "locale switch driver health");
        });
        Case("English UI waits safely when even TMP default font is unavailable and retries late fallback", () =>
        {
            TMP_Settings.defaultFontAsset = null; var h = new Harness();
            Check(h.Field<GameObject>("canvasObject") == null && h.Driver.enabled, "missing all fonts must delay UI, not fail closed");
            Check(ReferenceEquals(h.Field<MainMenuUIController>("menu"), h.Menu) && h.Events.sendNavigationEvents, "missing English font retains menu/input");
            h.Menu.body = MenuLabel(h.Menu, "Late body", new TMP_FontAsset()); FontRefreshPeriod(h);
            AllLabelFonts(h, h.Menu.body.font, "late English body fallback");
            Check(Debug.Errors.Count == 0, "missing English font must not log exception");
        });
        Case("ApplyTheme makes only root panel fully opaque while retaining skin RGB and nested alpha", () =>
        {
            var pack = ThirdParty(); pack.Skins[0].UIBackground = new RGBA(.11f, .22f, .33f, .24f);
            pack.Skins[0].UIButton = new RGBA(.44f, .55f, .66f, .53f); ContentRegistry.Register(pack);
            var h = new Harness(); h.Open(); PickMap(h, MapId);
            var skin = h.Model.Skin; var expectedBackground = VisualBridge.Color(skin.UIBackground);
            var expectedButton = VisualBridge.Color(skin.UIButton); var panel = h.Field<GameObject>("panel");
            Check(expectedBackground.a < 255 && expectedButton.a < 255, "transparent skin fixture");
            void VerifyTheme(string context)
            {
                var root = panel.GetComponent<Image>().color;
                Check(root.r == expectedBackground.r && root.g == expectedBackground.g && root.b == expectedBackground.b && root.a == 255, context + ": root uses skin RGB with alpha 255");
                var nestedPanels = new[] { h.Field<GameObject>("preparation"), h.Field<GameObject>("resultPanel") };
                Check(nestedPanels.All(p => p.GetComponent<Image>().color == expectedBackground), context + ": nested panel alpha preserved including inactive result");
                Check(panel.GetComponentsInChildren<Button>(true).All(b => b.GetComponent<Image>().color == expectedButton), context + ": button alpha preserved");
                Check(panel.GetComponentsInChildren<TextMeshProUGUI>(true).All(t => t.color == VisualBridge.Color(skin.UIText)), context + ": text theme unchanged");
            }
            VerifyTheme("preparation"); StartCurrent(h); Call(h.Driver, "ApplyTheme"); VerifyTheme("run with inactive preparation");
            Call(h.Driver, "Close"); h.Open(); VerifyTheme("reopened preparation");
            Check(Debug.Errors.Count == 0 && h.Driver.enabled, "transparent skin remains usable");
        });
    }
}
