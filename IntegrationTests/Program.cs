using System.Reflection;
using RaidPrototype;
using RaidPrototype.Content;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UObj = UnityEngine.Object;
using URect = UnityEngine.Rect;

// These tests execute linked production C# against explicitly limited CLR doubles.
// Passing here does not establish Unity lifecycle, player input, layout or rendering correctness.
return Suite.Run();

static partial class Suite
{
    private static int assertions;
    private static readonly List<(string Name, Action Body)> Cases = new();
    private static void Case(string name, Action body) => Cases.Add((name, body));
    private static void Check(bool condition, string reason)
    {
        assertions++; if (!condition) throw new InvalidOperationException(reason);
    }
    private static void Near(float actual, float expected, string reason) => Check(MathF.Abs(actual - expected) < .001f, $"{reason}: actual {actual}, expected {expected}");
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Call(object target, string method, params object[] args) => StubLifecycle.Call(target, method, args);
    private static void Reset()
    {
        ContentBridge.Stop(); GameObject.ResetAll(); MainMenuUIController.Instance = null; EventSystem.current = null;
        SceneManager.ActiveScene = new Scene(1); Input.Reset(); Time.unscaledDeltaTime = .02f;
        LocHelper.Locale = "en"; FontLoader.Instance = null; TMP_Settings.defaultFontAsset = new TMP_FontAsset();
        Debug.Logs.Clear(); Debug.Errors.Clear(); Debug.Warnings.Clear(); ModLoader.Mods.Clear();
        var registered = ContentRegistry.Packs.ToList();
        while (registered.Count > 0)
        {
            var leaf = registered.First(p => !registered.Any(q => q.Requires.Contains(p.Id)));
            ContentRegistry.Unregister(leaf.Id); registered.Remove(leaf);
        }
        ((HashSet<string>)typeof(VisualBridge).GetField("warned", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        DefaultContent.EnsureRegistered();
    }
    private static GameObject Named(string name) => GameObject.All.Single(x => x != null && x.name == name);
    private static Button ButtonNamed(string text) => Named(text).GetComponent<Button>();
    private static MainMenuUIController Menu()
    {
        var menu = new GameObject("Main menu").AddComponent<MainMenuUIController>(); MainMenuUIController.Instance = menu; return menu;
    }
    private sealed class Harness
    {
        public Entry Entry { get; } = new();
        public RaidDriver Driver { get; }
        public MainMenuUIController Menu { get; }
        public EventSystem Events { get; }
        public GameObject Selection { get; }
        public Harness(bool navigation = true)
        {
            Menu = Suite.Menu(); Events = new GameObject("EventSystem").AddComponent<EventSystem>(); EventSystem.current = Events;
            Events.sendNavigationEvents = navigation; Selection = new GameObject("Original selection"); Events.SetSelectedGameObject(Selection);
            Entry.Init(new ModManifest()); Entry.OnEnable(); Driver = Get<GameObject>(Entry, "driver").GetComponent<RaidDriver>(); Tick();
        }
        public T Field<T>(string name) => Get<T>(Driver, name);
        public RaidModel Model => Field<RaidModel>("model");
        public RaidGraphic Graphic => Field<RaidGraphic>("field");
        public void Tick(params KeyCode[] down)
        {
            Input.Down.Clear(); foreach (var key in down) Input.Down.Add(key); Call(Driver, "Update"); Input.Down.Clear(); Input.MouseDown = false;
        }
        public void Open() => Field<GameObject>("launcher").GetComponent<Button>().Press();
        public void Start(Weapon weapon = Weapon.Pistol)
        {
            Open(); Field<GameObject>(weapon == Weapon.Pistol ? "weaponA" : "weaponB").GetComponent<Button>().Press();
        }
        public void Close() => ButtonNamed("Close sandbox").Press();
    }
    public static int Run()
    {
        RegisterEntry(); RegisterSimulation(); RegisterGraphic(); RegisterContentIntegration(); RegisterMapPickerIntegration(); RegisterRenderingRegressions(); RegisterBridgeIntegration(); RegisterRealJsonCsvIntegration(); RegisterFontAndOpacityRegressions();
        Console.WriteLine("RaidPrototype net8 substitution integration tests");
        Console.WriteLine("Linked production: RaidEntry.cs, RaidGraphic.cs, RaidModel.cs, Vec2.cs, Content.cs, DefaultContent.cs, VisualBridge.cs, ContentBridge.cs, ContentLoader.cs");
        Console.WriteLine("NOT Unity engine / PlayMode / rendering tests. Stub Destroy is immediate; layout and screen conversion are simplified.");
        int failures = 0;
        foreach (var test in Cases)
        {
            try { Reset(); test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + "\n" + ex); }
        }
        Reset();
        Console.WriteLine($"RESULT: {Cases.Count - failures}/{Cases.Count} scenarios passed; {assertions} assertions; {failures} failures.");
        return failures == 0 ? 0 : 1;
    }
    private static void RegisterEntry()
    {
        Case("Entry enable is idempotent and driver is persistent", () =>
        {
            var entry = new Entry(); entry.Init(new ModManifest()); entry.OnEnable();
            var driver = Get<GameObject>(entry, "driver"); entry.OnEnable();
            Check(ReferenceEquals(driver, Get<GameObject>(entry, "driver")), "duplicate driver");
            Check(driver.Persistent && driver.GetComponent<RaidDriver>() != null, "driver setup");
            Check(GameObject.All.Count(x => x != null && x.name == "RaidPrototype Driver") == 1, "one driver");
        });
        Case("Entry disable before enable and repeated disable are safe; re-enable creates fresh driver", () =>
        {
            var entry = new Entry(); entry.OnDisable(); entry.OnEnable(); var old = Get<GameObject>(entry, "driver");
            entry.OnDisable(); entry.OnDisable(); Check(old == null, "driver destroyed"); Check(Get<GameObject>(entry, "driver") == null, "driver cleared");
            entry.OnEnable(); Check(!ReferenceEquals(old, Get<GameObject>(entry, "driver")), "new driver after re-enable");
        });
        Case("No interface without active main menu in active scene", () =>
        {
            var entry = new Entry(); entry.OnEnable(); var driver = Get<GameObject>(entry, "driver").GetComponent<RaidDriver>();
            Call(driver, "Update"); Check(Get<GameObject>(driver, "canvasObject") == null, "no menu should not create UI");
            var menu = Menu(); menu.gameObject.SetActive(false); Call(driver, "Update"); Check(Get<GameObject>(driver, "canvasObject") == null, "inactive menu");
            menu.gameObject.SetActive(true); menu.gameObject.scene = new Scene(99); Call(driver, "Update"); Check(Get<GameObject>(driver, "canvasObject") == null, "foreign scene menu");
            menu.gameObject.scene = SceneManager.GetActiveScene(); Call(driver, "Update"); Check(Get<GameObject>(driver, "canvasObject") != null, "valid main menu creates UI");
        });
        Case("Interface creation is once per menu and starts closed", () =>
        {
            var h = new Harness(); var canvas = h.Field<GameObject>("canvasObject"); h.Tick();
            Check(ReferenceEquals(canvas, h.Field<GameObject>("canvasObject")), "UI recreated every frame");
            Check(canvas.GetComponent<Canvas>().sortingOrder == 950, "overlay ordering");
            Check(h.Field<GameObject>("launcher").activeSelf && !h.Field<GameObject>("panel").activeSelf, "initial visibility");
            Check(h.Model == null && !h.Field<bool>("opened"), "initial model/open flags");
            Check(canvas.GetComponentsInChildren<Button>(true).All(x => x.navigation.mode == Navigation.Mode.None), "sandbox navigation disabled on all buttons");
            Check(Debug.Errors.Count == 0, "creation error");
        });
        Case("Open requires event system; accepted open captures and disables navigation", () =>
        {
            var h = new Harness(); EventSystem.current = null; h.Open(); Check(!h.Field<bool>("opened") && h.Model == null, "open without EventSystem");
            EventSystem.current = h.Events; h.Open();
            Check(h.Field<bool>("opened") && !h.Field<bool>("started"), "opened preparation");
            Check(!h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == null, "navigation captured");
            Check(!h.Field<GameObject>("launcher").activeSelf && h.Field<GameObject>("panel").activeSelf, "open visibility");
            Check(h.Model != null && ReferenceEquals(h.Model, h.Graphic.Model), "real model connected to real graphic");
            Check(h.Field<GameObject>("preparation").activeSelf && !h.Field<GameObject>("resultPanel").activeSelf, "preparation/result visibility");
            Call(h.Driver, "Open"); h.Close(); Check(h.Events.currentSelectedGameObject == h.Selection, "duplicate Open must not overwrite captured selection");
        });
        Case("Close clears session and restores selection and navigation; reopen gets fresh model", () =>
        {
            var h = new Harness(); h.Start(); var old = h.Model; h.Close();
            Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "navigation restoration");
            Check(h.Model == null && h.Graphic.Model == null, "session/graphic model cleared");
            Check(!h.Field<bool>("opened") && !h.Field<bool>("started") && !h.Field<bool>("paused"), "session flags cleared");
            Check(h.Field<GameObject>("launcher").activeSelf && !h.Field<GameObject>("panel").activeSelf, "closed visibility");
            h.Open(); Check(!ReferenceEquals(old, h.Model) && h.Model.LootCount == 0, "fresh preparation model");
        });
        Case("Originally disabled navigation remains disabled on close", () =>
        {
            var h = new Harness(false); h.Open(); h.Close(); Check(!h.Events.sendNavigationEvents, "must restore false rather than force true");
        });
        Case("Inactive or destroyed prior selection is not restored", () =>
        {
            var h = new Harness(); h.Open(); h.Selection.SetActive(false); h.Close(); Check(h.Events.currentSelectedGameObject == null, "inactive selection restored");
            h.Selection.SetActive(true); h.Events.SetSelectedGameObject(h.Selection); h.Open(); UObj.Destroy(h.Selection); h.Close();
            Check(h.Events.currentSelectedGameObject == null && h.Events.sendNavigationEvents, "destroyed selection restoration");
        });
        Case("Restoration targets captured EventSystem, not replacement current", () =>
        {
            var h = new Harness(); h.Open(); var other = new GameObject("Replacement event system").AddComponent<EventSystem>(); other.sendNavigationEvents = false; EventSystem.current = other;
            h.Close(); Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "original event system restoration");
            Check(!other.sendNavigationEvents && other.currentSelectedGameObject == null, "replacement must remain untouched");
        });
        Case("Destroyed captured EventSystem is safely ignored", () =>
        {
            var h = new Harness(); h.Open(); UObj.Destroy(h.Events.gameObject); EventSystem.current = null;
            h.Close(); Check(h.Model == null && h.Field<EventSystem>("capturedEventSystem") == null, "close after event system destruction");
        });
        Case("Menu replacement cleans old UI/session and restores input", () =>
        {
            var h = new Harness(); h.Start(); var oldCanvas = h.Field<GameObject>("canvasObject"); var oldModel = h.Model;
            var next = Menu(); h.Tick();
            Check(oldCanvas == null && !oldCanvas.activeSelf, "old overlay destroyed and inactive");
            Check(ReferenceEquals(next, h.Field<MainMenuUIController>("menu")) && h.Field<GameObject>("canvasObject") != null, "replacement menu UI");
            Check(h.Model == null && !h.Field<bool>("opened") && oldModel != null, "old session dropped");
            Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "input released on menu replacement");
        });
        foreach (var kind in new[] { "inactive", "foreign-scene", "missing" })
            Case("Menu loss cleanup: " + kind, () =>
            {
                var h = new Harness(); h.Start(); var canvas = h.Field<GameObject>("canvasObject");
                if (kind == "inactive") h.Menu.gameObject.SetActive(false);
                else if (kind == "foreign-scene") SceneManager.ActiveScene = new Scene(2);
                else MainMenuUIController.Instance = null;
                h.Tick(); Check(canvas == null && !canvas.activeSelf, "canvas must be removed");
                Check(h.Model == null && h.Field<RaidGraphic>("field") == null && !h.Field<bool>("opened"), "session cleanup");
                Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "menu loss input restoration");
                h.Tick(); Check(Debug.Errors.Count == 0, "repeated cleanup error");
            });
        Case("Stale UI callbacks cannot open or start outside menu", () =>
        {
            var h = new Harness(); h.Open(); var old = h.Model; SceneManager.ActiveScene = new Scene(2);
            Call(h.Driver, "StartRaid", Weapon.Rifle); Check(ReferenceEquals(old, h.Model) && !h.Field<bool>("started"), "start bypassed menu guard");
            Call(h.Driver, "Prepare"); Check(ReferenceEquals(old, h.Model), "prepare bypassed menu guard");
            Call(h.Driver, "Close"); Call(h.Driver, "Open"); Check(!h.Field<bool>("opened"), "open bypassed menu guard");
        });
        Case("Mod disable and destroy cleanup restore navigation, idempotently", () =>
        {
            var h = new Harness(); h.Start(); var canvas = h.Field<GameObject>("canvasObject"); var driver = h.Driver;
            h.Entry.OnDisable(); Check(canvas == null && !canvas.activeSelf && driver == null, "objects destroyed");
            Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "disable input restoration");
            Call(driver, "OnDisable"); Call(driver, "OnDestroy"); h.Entry.OnDisable(); Check(Get<RaidModel>(driver, "model") == null, "repeat cleanup");
        });
        Case("Direct driver disable restores navigation and removes canvas", () =>
        {
            var h = new Harness(); h.Open(); h.Driver.enabled = false;
            Check(h.Model == null && h.Field<GameObject>("canvasObject") == null, "driver disabled cleanup");
            Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "driver disabled restoration");
        });
        Case("Locale and font fallback surfaces work without game services", () =>
        {
            FontLoader.Instance = new FontLoader { simplifiedChineseFont = new TMP_FontAsset() };
            LocHelper.Locale = "ZH-CN"; var h = new Harness(); Check(h.Field<GameObject>("launcher").name == "搜打撤原型（独立沙盒）", "Chinese locale prefix");
            Check(ReferenceEquals(h.Field<TMP_FontAsset>("font"), FontLoader.Instance.simplifiedChineseFont), "Chinese direct loaded font");
            h.Entry.OnDisable(); LocHelper.Locale = null; FontLoader.Instance = null;
            var second = new Harness(); Check(second.Field<GameObject>("launcher").name.Contains("Sandbox"), "null locale falls back to English");
            Check(ReferenceEquals(second.Field<TMP_FontAsset>("font"), TMP_Settings.defaultFontAsset), "English default font fallback");
        });
        Case("Body then version then default font fallback, not decorative title", () =>
        {
            var h = new Harness(); h.Entry.OnDisable();
            h.Menu.title = MenuLabel(h.Menu, "Decorative title", new TMP_FontAsset());
            h.Menu.body = MenuLabel(h.Menu, "Body", new TMP_FontAsset());
            h.Menu.versionTMP = MenuLabel(h.Menu, "Version", new TMP_FontAsset());
            void ExpectFallback(TMP_FontAsset expected, string label)
            {
                h.Entry.OnEnable(); var driver = Get<GameObject>(h.Entry, "driver").GetComponent<RaidDriver>(); Call(driver, "Update");
                Check(ReferenceEquals(Get<TMP_FontAsset>(driver, "font"), expected), label);
                Check(Get<GameObject>(driver, "canvasObject").GetComponentsInChildren<TextMeshProUGUI>(true).All(t => ReferenceEquals(t.font, expected) && !ReferenceEquals(t.font, h.Menu.title.font)), label + " on all labels, not decorative title");
                h.Entry.OnDisable();
            }
            ExpectFallback(h.Menu.body.font, "body preferred over version and decorative title");
            h.Menu.body.font = null; ExpectFallback(h.Menu.versionTMP.font, "null body font falls back to version");
            h.Menu.body = null; h.Menu.versionTMP.font = null; ExpectFallback(TMP_Settings.defaultFontAsset, "null version font falls back to TMP default, never title");
            h.Menu.versionTMP = null; ExpectFallback(TMP_Settings.defaultFontAsset, "absent body/version falls back to TMP default");
        });
    }
    private static void RegisterSimulation()
    {
        Case("Starting either weapon uses real fresh model; preparation does not simulate", () =>
        {
            var h = new Harness(); h.Open(); var preparationModel = h.Model; Input.Held.Add(KeyCode.D); h.Tick();
            Check(h.Model.PlayerPosition == new Vec2(2, 2), "preparation moved");
            h.Field<GameObject>("weaponB").GetComponent<Button>().Press(); Check(!ReferenceEquals(preparationModel, h.Model) && h.Model.SelectedWeapon == Weapon.Rifle, "rifle start");
            Check(h.Field<bool>("started") && !h.Field<GameObject>("preparation").activeSelf, "start flags");
            h.Close(); h.Start(); Check(h.Model.SelectedWeapon == Weapon.Pistol && h.Model.PlayerHealth == 100 && h.Model.LootCount == 0, "pistol fresh model");
        });
        Case("Unscaled delta clamped to 50ms and negative delta to zero", () =>
        {
            var h = new Harness(); h.Start(); Input.Held.Add(KeyCode.D); Time.unscaledDeltaTime = 1; h.Tick(); Near(h.Model.PlayerPosition.X, 2.2f, "clamped movement");
            Time.unscaledDeltaTime = -2; h.Tick(); Near(h.Model.PlayerPosition.X, 2.2f, "negative delta movement");
            h.Tick(KeyCode.Alpha2); Check(h.Model.SelectedWeapon == Weapon.Rifle, "rifle selection"); h.Tick(KeyCode.Alpha1); Check(h.Model.SelectedWeapon == Weapon.Pistol, "pistol selection");
        });
        Case("Esc and B pause/resume active raid; preparation Esc closes", () =>
        {
            var h = new Harness(); h.Open(); h.Tick(KeyCode.Escape); Check(!h.Field<bool>("opened"), "Esc preparation close"); h.Start();
            Input.Held.Add(KeyCode.D); var before = h.Model.PlayerPosition; h.Tick(KeyCode.Escape);
            Check(h.Field<bool>("paused") && h.Model.PlayerPosition == before && h.Field<TextMeshProUGUI>("pauseNotice").text.Contains("PAUSED"), "Esc pause");
            h.Tick(KeyCode.B); Check(!h.Field<bool>("paused") && h.Model.PlayerPosition.X > before.X, "B resume");
            ButtonNamed("Pause / bag").Press(); Check(h.Field<bool>("paused"), "pause button"); h.Tick(KeyCode.B); Check(!h.Field<bool>("paused"), "B toggle");
        });
        Case("Focus loss pauses; refocus does not auto-resume or simulate unfocused", () =>
        {
            var h = new Harness(); h.Start(); Input.Held.Add(KeyCode.D); var before = h.Model.PlayerPosition;
            Call(h.Driver, "OnApplicationFocus", false); h.Tick(); Check(h.Field<bool>("paused") && h.Model.PlayerPosition == before, "focus pause");
            h.Tick(KeyCode.B); Check(!h.Field<bool>("paused") && h.Model.PlayerPosition == before, "unfocused simulation must stay blocked even if unpaused");
            Call(h.Driver, "OnApplicationFocus", false); Call(h.Driver, "OnApplicationFocus", true); h.Tick();
            Check(h.Field<bool>("paused") && h.Model.PlayerPosition == before, "refocus must not resume");
            h.Tick(KeyCode.Escape); Check(h.Model.PlayerPosition.X > before.X, "explicit resume");
        });
        Case("Pause freezes firing, weapon switch, AI and movement", () =>
        {
            var h = new Harness(); h.Start(); h.Tick(KeyCode.B); var enemy = h.Model.Enemies[0].Position; var player = h.Model.PlayerPosition;
            Input.Held.Add(KeyCode.W); Input.MouseHeld = Input.MouseDown = true; h.Tick(KeyCode.Alpha2, KeyCode.E);
            Check(h.Model.PlayerPosition == player && h.Model.Enemies[0].Position == enemy && h.Model.ShotSerial == 0 && h.Model.SelectedWeapon == Weapon.Pistol && h.Model.LootCount == 0, "pause mutation");
        });
        Case("Driver movement and E input reach actual box and update inventory", () =>
        {
            var h = new Harness(); h.Start(); Time.unscaledDeltaTime = .05f; Input.Held.Add(KeyCode.D); Input.Held.Add(KeyCode.W); h.Tick(); h.Tick(); h.Tick();
            Input.Held.Clear(); h.Tick(KeyCode.E); Check(h.Model.LootCount == 1 && h.Model.Boxes[0].Opened, "actual model interaction");
            Check(h.Field<TextMeshProUGUI>("inventory").text.Contains("Loot × 1"), "bag text"); h.Tick(KeyCode.E); Check(h.Model.LootCount == 1, "box cannot repeat");
        });
        Case("Pistol needs mouse down; rifle accepts hold; off-board target rejects shot", () =>
        {
            var h = new Harness(); h.Start(); Input.mousePosition = new Vector2(90, -50); Input.MouseHeld = true; h.Tick(); Check(h.Model.ShotSerial == 0, "pistol fired on hold");
            Input.MouseDown = true; h.Tick(); Check(h.Model.ShotSerial == 1 && h.Graphic.ShotFlash > 0, "pistol click/flash");
            h.Close(); Input.MouseHeld = false; h.Start(Weapon.Rifle); Input.MouseHeld = true; h.Tick(); Check(h.Model.ShotSerial == 1, "rifle held shot");
            for (int i = 0; i < 8; i++) h.Tick(); Check(h.Model.ShotSerial >= 2, "rifle cooldown/automatic repeat");
            long serial = h.Model.ShotSerial; Input.mousePosition = new Vector2(101, 0); for (int i = 0; i < 8; i++) h.Tick(); Check(h.Model.ShotSerial == serial, "off-board shot");
        });
        Case("Death state draws result, freezes driver simulation and Esc exits", () =>
        {
            var h = new Harness(); h.Start(); h.Model.Update(20, Vec2.Zero); Check(h.Model.State == RaidState.Dead, "real model death setup"); h.Tick();
            Check(h.Field<GameObject>("resultPanel").activeSelf && h.Field<TextMeshProUGUI>("result").text.Contains("RAID FAILED"), "death result");
            var position = h.Model.PlayerPosition; Input.Held.Add(KeyCode.D); h.Tick(); Check(h.Model.PlayerPosition == position, "dead movement");
            h.Tick(KeyCode.Escape); Check(h.Model == null && h.Events.sendNavigationEvents, "dead Esc closes");
        });
        Case("Extraction result and Back to preparation clear real model run", () =>
        {
            var h = new Harness(); h.Start(Weapon.Rifle); var model = h.Model;
            // Controlled fixture uses real model internal enemy setters, not a copied simulation.
            foreach (var enemy in model.Enemies) enemy.Health = 0;
            model.Update(2.5f, Vec2.UnitY); model.Update(4f, Vec2.UnitX); model.Update(3.1f, Vec2.Zero);
            Check(model.State == RaidState.Extracted, "real extraction route"); h.Tick();
            Check(h.Field<GameObject>("resultPanel").activeSelf && h.Field<TextMeshProUGUI>("result").text.Contains("EXTRACTED"), "success result");
            h.Field<GameObject>("resultPanel").GetComponentsInChildren<Button>().Single(b => ButtonText(b) == "Back to preparation").Press();
            Check(!ReferenceEquals(model, h.Model) && h.Model.State == RaidState.Playing && h.Model.SelectedWeapon == Weapon.Pistol && h.Model.LootCount == 0, "preparation resets model");
            Check(!h.Field<bool>("started") && h.Field<GameObject>("preparation").activeSelf && !h.Field<GameObject>("resultPanel").activeSelf, "return visibility");
        });
        Case("Update exception fails closed, disables driver and restores navigation", () =>
        {
            var h = new Harness(); h.Start(); Time.unscaledDeltaTime = float.NaN; h.Tick();
            Check(!h.Driver.enabled && h.Model == null && h.Field<GameObject>("canvasObject") == null, "exception cleanup");
            Check(h.Events.sendNavigationEvents && h.Events.currentSelectedGameObject == h.Selection, "exception input restoration");
            Check(Debug.Errors.Count == 1 && Debug.Errors[0].Contains("Closed after error"), "exception logged");
        });
    }
    private static RaidGraphic Graphic(float width = 200, float height = 140)
    {
        var graphic = new GameObject("Standalone graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(RaidGraphic)).GetComponent<RaidGraphic>();
        graphic.rectTransform.rect = new URect(-width / 2, -height / 2, width, height); return graphic;
    }
    private static VertexHelper Mesh(RaidGraphic graphic)
    {
        var helper = new VertexHelper(); Call(graphic, "OnPopulateMesh", helper); return helper;
    }
    private static void RegisterGraphic()
    {
        Case("Real Graphic maps arena corners and rejects outside coordinates", () =>
        {
            var g = Graphic(); Check(g.MouseWorld(new Vector2(-100, -70), out var a), "minimum corner"); Near(a.X, 0, "minimum x"); Near(a.Y, 0, "minimum y");
            Check(g.MouseWorld(new Vector2(100, 70), out var b), "maximum corner"); Near(b.X, 20, "maximum x"); Near(b.Y, 14, "maximum y");
            Check(g.MouseWorld(Vector2.zero, out var mid), "center"); Near(mid.X, 10, "middle x"); Near(mid.Y, 7, "middle y");
            Check(!g.MouseWorld(new Vector2(101, 0), out _) && !g.MouseWorld(new Vector2(0, -71), out _), "outside coordinates");
        });
        Case("Real Graphic letterboxes wide and tall rectangles", () =>
        {
            var wide = Graphic(400, 140); Check(!wide.MouseWorld(new Vector2(150, 0), out _), "wide letterbox"); Check(wide.MouseWorld(new Vector2(100, 70), out var a), "wide corner"); Near(a.X, 20, "wide x");
            var tall = Graphic(200, 280); Check(!tall.MouseWorld(new Vector2(0, 100), out _), "tall letterbox"); Check(tall.MouseWorld(new Vector2(-100, -70), out var b), "tall corner"); Near(b.Y, 0, "tall y");
        });
        Case("Empty Graphic clears prior mesh and Refresh marks vertices dirty", () =>
        {
            var g = Graphic(); var vh = new VertexHelper(); vh.AddVert(Vector2.zero, new Color32(), Vector2.zero); vh.AddTriangle(0, 0, 0);
            Call(g, "OnPopulateMesh", vh); Check(vh.currentVertCount == 0 && vh.Triangles.Count == 0, "null model clears mesh");
            int before = g.DirtyCount; g.Refresh(); Check(g.DirtyCount == before + 1, "refresh dirty request");
        });
        Case("Real Graphic emits finite indexed representative arena geometry", () =>
        {
            var g = Graphic(); g.Model = new RaidModel(); g.Aim = new Vector2(10, 7); var mesh = Mesh(g);
            Check(mesh.currentVertCount > 400 && mesh.Triangles.Count > 200, $"nonempty arena geometry: {mesh.currentVertCount} vertices / {mesh.Triangles.Count} triangles");
            Check(mesh.Vertices.All(x => float.IsFinite(x.Position.x) && float.IsFinite(x.Position.y)), "nonfinite vertex");
            Check(mesh.Triangles.All(t => t.A >= 0 && t.B >= 0 && t.C >= 0 && t.A < mesh.currentVertCount && t.B < mesh.currentVertCount && t.C < mesh.currentVertCount), "invalid triangle index");
            Check(mesh.Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.Player)), "player geometry missing");
            Check(mesh.Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.Enemy)), "enemy geometry missing");
            Check(mesh.Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.Wall)), "obstacle geometry missing");
        });
        Case("Real Graphic reflects opened loot, dead enemy and shot-flash branches", () =>
        {
            var g = Graphic(); g.Model = new RaidModel(); g.Aim = new Vector2(10, 2); var before = Mesh(g);
            g.Model.Update(.2f, Vec2.One); Check(g.Model.Interact(), "real opened box setup"); g.Model.Enemies[0].Health = 0;
            Check(g.Model.Fire(new Vec2(19, 2)), "real shot setup"); g.ShotFlash = .07f; var after = Mesh(g);
            Check(!before.Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.OpenCrate)) && after.Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.OpenCrate)), "opened box branch");
            Check(after.Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.OpenCrate)), "dead enemy branch");
            Check(after.Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.Bullet)), "shot flash branch");
            g.ShotFlash = 0; Check(!Mesh(g).Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.Bullet)), "shot flash reset");
        });
        Case("Real Graphic extraction progress adds extraction-fill geometry", () =>
        {
            var g = Graphic(); g.Model = new RaidModel(); foreach (var enemy in g.Model.Enemies) enemy.Health = 0;
            g.Model.Update(2.5f, Vec2.UnitY); g.Model.Update(4f, Vec2.UnitX); g.Model.Update(.5f, Vec2.Zero);
            Check(g.Model.ExtractionProgress > 0, "real extraction progress setup");
            Check(Mesh(g).Vertices.Any(x => x.Color == VisualBridge.Color(g.Model.Skin.ExitEdge)), "extraction progress mesh branch");
        });
    }
}
