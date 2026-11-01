// Deliberately limited CLR test doubles, NOT a Unity runtime or rendering implementation.
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public bool Persistent { get; internal set; }
        private static bool Missing(Object value) => ReferenceEquals(value, null) || value.Destroyed;
        public static bool operator ==(Object a, Object b) => Missing(a) ? Missing(b) : !Missing(b) && ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void DontDestroyOnLoad(Object target) => target.Persistent = true;
        // Immediate destruction is intentional. Real Unity defers Destroy until end of frame.
        public static void Destroy(Object target)
        {
            if (target == null) return;
            if (target is GameObject go) go.DestroyNow();
            else target.Destroyed = true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component => gameObject.GetComponentInChildren<T>(includeInactive);
        public IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class MonoBehaviour : Component
    {
        private bool isEnabled = true;
        public bool enabled
        {
            get => isEnabled;
            set { bool notify = isEnabled && !value && gameObject.activeInHierarchy; isEnabled = value; if (notify) StubLifecycle.Call(this, "OnDisable"); }
        }
    }
    public class Transform : Component
    {
        public Transform parent { get; private set; }
        public List<Transform> Children { get; } = new();
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public void SetSiblingIndex(int index)
        {
            if (parent == null) return;
            parent.Children.Remove(this); parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), this);
        }
        public void SetParent(Transform next, bool worldPositionStays)
        {
            parent?.Children.Remove(this); parent = next; parent?.Children.Add(this);
        }
    }
    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax;
        // Layout is NOT solved. Tests explicitly supply a local rectangle for Graphic mapping.
        public Rect rect = new(-100, -70, 200, 140);
    }
    public class GameObject : Object
    {
        public static List<GameObject> All { get; } = new();
        private readonly List<Component> components = new();
        public string name;
        public Transform transform { get; }
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public SceneManagement.Scene scene { get; set; }
        public GameObject(string name, params Type[] types)
        {
            this.name = name; scene = SceneManagement.SceneManager.GetActiveScene();
            transform = types.Contains(typeof(RectTransform)) ? new RectTransform() : new Transform();
            transform.gameObject = this; components.Add(transform); All.Add(this);
            foreach (var type in types) if (type != typeof(RectTransform) && type != typeof(Transform)) AddComponent(type);
        }
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        private Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type); component.gameObject = this; components.Add(component);
            StubLifecycle.Call(component, "Awake"); if (activeInHierarchy) StubLifecycle.Call(component, "OnEnable");
            return component;
        }
        public T GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault(x => x != null);
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : Component =>
            components.OfType<T>().Where(x => x != null && (includeInactive || activeInHierarchy))
                .Concat(transform.Children.ToArray().SelectMany(x => x.gameObject.GetComponentsInChildren<T>(includeInactive)));
        public void SetActive(bool value)
        {
            var prior = Descendants().ToDictionary(x => x, x => x.activeInHierarchy);
            activeSelf = value;
            foreach (var go in Descendants()) if (prior[go] != go.activeInHierarchy)
                foreach (var component in go.components.ToArray())
                    if (component is not MonoBehaviour behaviour || behaviour.enabled)
                        StubLifecycle.Call(component, go.activeInHierarchy ? "OnEnable" : "OnDisable");
        }
        private IEnumerable<GameObject> Descendants() => new[] { this }.Concat(transform.Children.ToArray().SelectMany(x => x.gameObject.Descendants()));
        internal void DestroyNow()
        {
            SetActive(false);
            foreach (var child in transform.Children.ToArray()) Object.Destroy(child.gameObject);
            foreach (var component in components.ToArray()) { StubLifecycle.Call(component, "OnDestroy"); component.Destroyed = true; }
            Destroyed = true; transform.SetParent(null, false);
        }
        public static void ResetAll()
        {
            foreach (var go in All.ToArray()) Object.Destroy(go);
            All.Clear();
        }
    }
    public static class StubLifecycle
    {
        public static void Call(object instance, string method, params object[] args)
        {
            var info = instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null) return;
            try { info.Invoke(instance, args); }
            catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw(); }
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized => sqrMagnitude > 0 ? this / MathF.Sqrt(sqrMagnitude) : zero;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
    }
    public readonly struct Rect
    {
        public readonly float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Vector2 center => new(x + width / 2, y + height / 2);
    }
    // Unity's Color32 channels are mutable; retain value equality for assertions.
    public record struct Color32(byte r, byte g, byte b, byte a);
    public readonly record struct Color(float r, float g, float b, float a)
    {
        public static Color white => new(1, 1, 1, 1);
        public static implicit operator Color32(Color c) => new(
            (byte)MathF.Round(Math.Clamp(c.r, 0, 1) * 255), (byte)MathF.Round(Math.Clamp(c.g, 0, 1) * 255),
            (byte)MathF.Round(Math.Clamp(c.b, 0, 1) * 255), (byte)MathF.Round(Math.Clamp(c.a, 0, 1) * 255));
    }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
        public static int CeilToInt(float v) => (int)MathF.Ceiling(v);
        public static int RoundToInt(float v) => (int)MathF.Round(v);
        public static float Cos(float a) => MathF.Cos(a);
        public static float Sin(float a) => MathF.Sin(a);
    }
    public static class RectTransformUtility
    {
        // Identity conversion only. Camera, scaling, screen transforms and Canvas layout are untested.
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screen, object camera, out Vector2 local) { local = screen; return true; }
    }
    public enum RenderMode { ScreenSpaceOverlay }
    public sealed class Canvas : Component { public RenderMode renderMode; public int sortingOrder; }
    public sealed class CanvasRenderer : Component { }
    public sealed class Sprite : Object { public string Name; }
    public enum KeyCode { Escape, B, Alpha1, Alpha2, D, A, W, S, E }
    public static class Input
    {
        public static HashSet<KeyCode> Held { get; } = new();
        public static HashSet<KeyCode> Down { get; } = new();
        public static Vector2 mousePosition;
        public static bool MouseHeld, MouseDown;
        public static bool GetKey(KeyCode code) => Held.Contains(code);
        public static bool GetKeyDown(KeyCode code) => Down.Contains(code);
        public static bool GetMouseButton(int button) => button == 0 && MouseHeld;
        public static bool GetMouseButtonDown(int button) => button == 0 && MouseDown;
        public static void Reset() { Held.Clear(); Down.Clear(); MouseHeld = MouseDown = false; mousePosition = Vector2.zero; }
    }
    public static class Time { public static float unscaledDeltaTime = .02f; }
    public static class Debug
    {
        public static List<string> Logs { get; } = new();
        public static List<string> Errors { get; } = new();
        public static List<string> Warnings { get; } = new();
        public static void LogWarning(object value) => Warnings.Add(value.ToString());
        public static void Log(object value) => Logs.Add(value.ToString());
        public static void LogError(object value) => Errors.Add(value.ToString());
    }
}
namespace UnityEngine.SceneManagement
{
    public readonly record struct Scene(int Handle);
    public static class SceneManager
    {
        public static Scene ActiveScene = new(1);
        public static Scene GetActiveScene() => ActiveScene;
    }
}
namespace UnityEngine.Events { public delegate void UnityAction(); }
namespace UnityEngine.EventSystems
{
    public sealed class EventSystem : UnityEngine.MonoBehaviour
    {
        public static EventSystem current;
        public bool sendNavigationEvents = true;
        public UnityEngine.GameObject currentSelectedGameObject { get; private set; }
        public void SetSelectedGameObject(UnityEngine.GameObject value) => currentSelectedGameObject = value;
    }
}
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.MonoBehaviour
    {
        public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform;
        public bool raycastTarget;
        public UnityEngine.Color32 color;
        public int DirtyCount { get; private set; }
        public void SetVerticesDirty() => DirtyCount++;
    }
    public class MaskableGraphic : Graphic
    {
        protected virtual void OnPopulateMesh(VertexHelper helper) { }
        protected virtual void OnDestroy() { }
    }
    public sealed class Image : Graphic { public UnityEngine.Sprite sprite; public bool preserveAspect; }
    public sealed class CanvasScaler : UnityEngine.Component
    {
        public enum ScaleMode { ScaleWithScreenSize }
        public ScaleMode uiScaleMode;
        public UnityEngine.Vector2 referenceResolution;
        public float matchWidthOrHeight;
    }
    public sealed class GraphicRaycaster : UnityEngine.Component { }
    public struct Navigation { public enum Mode { None }; public Mode mode; }
    public sealed class Button : UnityEngine.MonoBehaviour
    {
        public Graphic targetGraphic;
        public Navigation navigation;
        public readonly ClickEvent onClick = new();
        public sealed class ClickEvent
        {
            private readonly List<UnityEngine.Events.UnityAction> actions = new();
            public void AddListener(UnityEngine.Events.UnityAction action) => actions.Add(action);
            public void Invoke() { foreach (var action in actions.ToArray()) action(); }
        }
        public void Press() { if (!gameObject.activeInHierarchy || !enabled) throw new InvalidOperationException("Cannot press inactive stub button."); onClick.Invoke(); }
    }
    public sealed class VertexHelper
    {
        public readonly record struct Vertex(UnityEngine.Vector2 Position, UnityEngine.Color32 Color);
        public List<Vertex> Vertices { get; } = new();
        public List<(int A, int B, int C)> Triangles { get; } = new();
        public int currentVertCount => Vertices.Count;
        public void Clear() { Vertices.Clear(); Triangles.Clear(); }
        public void AddVert(UnityEngine.Vector2 position, UnityEngine.Color32 color, UnityEngine.Vector2 uv) => Vertices.Add(new(position, color));
        public void AddTriangle(int a, int b, int c) => Triangles.Add((a, b, c));
    }
}
namespace TMPro
{
    public sealed class TMP_FontAsset : UnityEngine.Object { }
    public static class TMP_Settings { public static TMP_FontAsset defaultFontAsset = new(); }
    public enum TextAlignmentOptions { Center }
    public sealed class TextMeshProUGUI : UnityEngine.UI.Graphic
    {
        public TMP_FontAsset font;
        public string text;
        public float fontSize;
        public bool richText, enableWordWrapping;
        public TextAlignmentOptions alignment;
    }
}
// Only the production entry contract and read-only main menu/locale surfaces are exposed.
public interface IMod { void Init(ModManifest manifest); void OnEnable(); void OnDisable(); }
public sealed class ModManifest { public string ID; }
// Provider lookup surface only: does not simulate loading DLLs, PNGs or actual mod lifecycle.
public class LoadedMod
{
    public bool IsEnabled = true;
    public ModManifest Manifest;
    public Dictionary<string, UnityEngine.Sprite> Sprites = new();
    public Dictionary<string, Dictionary<string, string>> Localization = new();
}
public sealed class StubMod : LoadedMod { }
public static class ModLoader { public static List<LoadedMod> Mods { get; } = new(); }
public static class ModHook
{
    public static event Action<LoadedMod> OnModAssetsLoaded;
    public static int SubscriberCount => OnModAssetsLoaded?.GetInvocationList().Length ?? 0;
    public static void RaiseAssetsLoaded(LoadedMod mod) => OnModAssetsLoaded?.Invoke(mod);
}
public sealed class MainMenuUIController : UnityEngine.MonoBehaviour
{
    public static MainMenuUIController Instance;
    public TMPro.TextMeshProUGUI title, body, versionTMP;
}
// Data-only surface for the game's asynchronously loaded TMP assets; no Localization package.
public sealed class FontLoader
{
    public static FontLoader Instance;
    public TMPro.TMP_FontAsset simplifiedChineseFont, japaneseFont, koreanFont, notoSans;
}
public static class LocHelper
{
    public static string Locale = "en";
    public static string GetCurrentLocaleCode() => Locale;
}
