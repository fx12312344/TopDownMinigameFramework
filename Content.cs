using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPrototype.Content
{
    // Data only: content packs cannot install callbacks, scripts, or arbitrary gameplay behaviours.
    [Serializable]
    public sealed class ContentPack
    {
        public int ApiVersion = 1;
        public string Id, NameZh, NameEn;
        public string[] Requires = Array.Empty<string>();
        public ItemDefinition[] Items = Array.Empty<ItemDefinition>();
        public WeaponDefinition[] Weapons = Array.Empty<WeaponDefinition>();
        public MapDefinition[] Maps = Array.Empty<MapDefinition>();
        public SkinDefinition[] Skins = Array.Empty<SkinDefinition>();
    }
    [Serializable]
    public sealed class ItemDefinition
    {
        public string Id, NameZh, NameEn, SpriteKey;
        public int Width = 1, Height = 1, MaxStack = 1;
    }
    [Serializable]
    public sealed class WeaponDefinition
    {
        public string Id, NameZh, NameEn, SpriteKey;
        public float Damage, Cooldown, Range;
        public bool Automatic;
    }
    [Serializable]
    public sealed class MapDefinition
    {
        public string Id, NameZh, NameEn;
        public float Width, Height;
        public Vec2 PlayerSpawn;
        public EnemySpawn[] Enemies = Array.Empty<EnemySpawn>();
        public BoxSpawn[] Boxes = Array.Empty<BoxSpawn>();
        public Rect[] Obstacles = Array.Empty<Rect>();
        public bool HasExtraction = true;
        public Vec2 ExtractionPoint;
        public float ExtractionRadius = 1.2f, ExtractionSeconds = 3;
        public string SkinId;
        public string[] Loadout = Array.Empty<string>();
    }
    [Serializable]
    public sealed class EnemySpawn
    {
        public Vec2 Position;
        public float Health = 60, Speed = 1.5f, Damage = 10, AttackSeconds = 1;
        public string SpriteKey;
    }
    [Serializable]
    public sealed class BoxSpawn
    {
        public Vec2 Position;
        public LootEntry[] Loot = Array.Empty<LootEntry>();
        public string SpriteKey;
    }
    [Serializable]
    public sealed class LootEntry
    {
        public string ItemId;
        public int Count;
    }
    [Serializable]
    public struct RGBA
    {
        public float R, G, B, A;
        public RGBA(float r, float g, float b, float a = 1) { R = r; G = g; B = b; A = a; }
    }
    [Serializable]
    public sealed class SkinDefinition
    {
        public string Id, NameZh, NameEn;
        public RGBA Floor = new RGBA(.10f, .14f, .17f), Grid = new RGBA(.18f, .23f, .27f),
            Wall = new RGBA(.32f, .37f, .42f), WallEdge = new RGBA(.56f, .63f, .68f),
            Player = new RGBA(.28f, .80f, .92f), Enemy = new RGBA(.94f, .32f, .30f),
            Crate = new RGBA(.85f, .64f, .28f), OpenCrate = new RGBA(.43f, .39f, .30f),
            Exit = new RGBA(.23f, .72f, .43f, .35f), ExitEdge = new RGBA(.38f, .92f, .57f),
            Bullet = new RGBA(1, .90f, .48f), UIBackground = new RGBA(.07f, .10f, .13f, .95f),
            UIText = new RGBA(.93f, .96f, .98f), UIButton = new RGBA(.22f, .34f, .43f);
        public string BackgroundKey, PlayerKey, EnemyKey, CrateKey, WallKey, ItemKey;
    }

    /// <summary>Thread-safe, atomic, bounded content registry. All outward DTOs are detached deep copies.</summary>
    public static class ContentRegistry
    {
        public const int ApiVersion = 1;
        public const int MaxPacks = 128, MaxDefinitions = 16384, MaxDefinitionsPerPack = 4096;
        public const int MaxEnemiesPerMap = 64, MaxBoxesPerMap = 128, MaxObstaclesPerMap = 256;
        public const int MaxSimulationCostPerMap = 100000, MaxEstimatedVerticesPerMap = 60000;
        public const int MaxLootEntriesPerBox = 64, MaxLootCountPerMap = 1000000, MaxInventoryStacks = 4096;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ContentPack> Registered = new Dictionary<string, ContentPack>(StringComparer.Ordinal);
        public static IReadOnlyList<ContentPack> Packs { get { lock (Gate) return Array.AsReadOnly(Registered.Values.Select(Copy).ToArray()); } }
        public static IReadOnlyList<ItemDefinition> Items { get { lock (Gate) return Array.AsReadOnly(Registered.Values.SelectMany(p => p.Items).Select(Copy).ToArray()); } }
        public static IReadOnlyList<WeaponDefinition> Weapons { get { lock (Gate) return Array.AsReadOnly(Registered.Values.SelectMany(p => p.Weapons).Select(Copy).ToArray()); } }
        public static IReadOnlyList<MapDefinition> Maps { get { lock (Gate) return Array.AsReadOnly(Registered.Values.SelectMany(p => p.Maps).Select(Copy).ToArray()); } }
        public static IReadOnlyList<SkinDefinition> Skins { get { lock (Gate) return Array.AsReadOnly(Registered.Values.SelectMany(p => p.Skins).Select(Copy).ToArray()); } }
        public static bool IsRegistered(string packId) { lock (Gate) return packId != null && Registered.ContainsKey(packId); }
        public static void Register(ContentPack pack)
        {
            lock (Gate)
            {
                Check(pack != null, "Pack is null.");
                Check(Registered.Count < MaxPacks, "Pack limit exceeded.");
                Check(pack.ApiVersion == ApiVersion, "Unsupported API version.");
                Token(pack.Id, "pack id"); Names(pack.NameZh, pack.NameEn);
                Check(!Registered.ContainsKey(pack.Id), "Duplicate pack id: " + pack.Id);
                Shape(pack); // Bound the work before deep copying nested arrays.
                var candidate = Copy(pack);
                int count = candidate.Items.Length + candidate.Weapons.Length + candidate.Maps.Length + candidate.Skins.Length;
                Check(Registered.Values.Sum(p => p.Items.Length + p.Weapons.Length + p.Maps.Length + p.Skins.Length) + count <= MaxDefinitions, "Registry definition limit exceeded.");
                var dependencies = new HashSet<string>(StringComparer.Ordinal);
                foreach (string dependency in candidate.Requires)
                {
                    Token(dependency, "dependency");
                    Check(dependency != candidate.Id && dependencies.Add(dependency) && Registered.ContainsKey(dependency), "Missing, duplicate or self dependency: " + dependency);
                }
                var allowed = new HashSet<string>(dependencies, StringComparer.Ordinal) { candidate.Id };
                foreach (string dependency in dependencies) AddDependencies(dependency, allowed);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in Registered.Values) foreach (string id in Ids(p)) ids.Add(id);
                foreach (string id in Ids(candidate))
                {
                    Token(id, "definition id");
                    Check(id.StartsWith(candidate.Id + ".", StringComparison.Ordinal) && id.Length > candidate.Id.Length + 1, "Definition id must use pack.Id + '.': " + id);
                    Check(ids.Add(id), "Duplicate definition id: " + id);
                }
                var all = Registered.Values.Concat(new[] { candidate }).ToArray();
                var items = all.SelectMany(p => p.Items).ToDictionary(x => x.Id, StringComparer.Ordinal);
                var weapons = all.SelectMany(p => p.Weapons).ToDictionary(x => x.Id, StringComparer.Ordinal);
                var skins = all.SelectMany(p => p.Skins).ToDictionary(x => x.Id, StringComparer.Ordinal);
                var owners = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in all) foreach (string id in Ids(p)) owners.Add(id, p.Id);
                foreach (var item in candidate.Items)
                {
                    Names(item.NameZh, item.NameEn); Sprite(item.SpriteKey);
                    Check(item.Width >= 1 && item.Width <= 64 && item.Height >= 1 && item.Height <= 64 && item.MaxStack >= 1 && item.MaxStack <= MaxLootCountPerMap, "Invalid item dimensions/stack.");
                }
                foreach (var weapon in candidate.Weapons)
                {
                    Names(weapon.NameZh, weapon.NameEn); Sprite(weapon.SpriteKey);
                    Number(weapon.Damage, .001f, 100000, "weapon damage"); Number(weapon.Cooldown, .001f, 3600, "weapon cooldown"); Number(weapon.Range, .001f, 100000, "weapon range");
                }
                foreach (var skin in candidate.Skins) Validate(skin);
                foreach (var map in candidate.Maps) Validate(map, items, weapons, skins, owners, allowed);
                Registered.Add(candidate.Id, candidate); // The only mutation, after every validation succeeds.
            }
        }
        /// <summary>Unknown ids are a no-op; a pack with registered dependants cannot be removed.</summary>
        public static void Unregister(string packId)
        {
            if (packId == null) throw new ArgumentNullException(nameof(packId));
            lock (Gate)
            {
                if (!Registered.ContainsKey(packId)) return;
                if (Registered.Values.Any(p => Array.IndexOf(p.Requires, packId) >= 0)) throw new InvalidOperationException("Pack is required by another registered pack: " + packId);
                Registered.Remove(packId);
            }
        }
        public static ItemDefinition GetItem(string id) { lock (Gate) return Copy(Find(Registered.Values.SelectMany(p => p.Items), x => x.Id, id)); }
        public static WeaponDefinition GetWeapon(string id) { lock (Gate) return Copy(Find(Registered.Values.SelectMany(p => p.Weapons), x => x.Id, id)); }
        public static MapDefinition GetMap(string id) { lock (Gate) return Copy(Find(Registered.Values.SelectMany(p => p.Maps), x => x.Id, id)); }
        public static SkinDefinition GetSkin(string id) { lock (Gate) return Copy(Find(Registered.Values.SelectMany(p => p.Skins), x => x.Id, id)); }
        internal static ContentSession CreateSession(string mapId)
        {
            lock (Gate)
            {
                var map = GetMap(mapId);
                var items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
                foreach (var box in map.Boxes) foreach (var loot in box.Loot) if (!items.ContainsKey(loot.ItemId)) items.Add(loot.ItemId, GetItem(loot.ItemId));
                return new ContentSession(map, GetSkin(map.SkinId), map.Loadout.Select(GetWeapon).ToArray(), items);
            }
        }
        private static T Find<T>(IEnumerable<T> entries, Func<T, string> key, string id) where T : class
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            return entries.FirstOrDefault(x => key(x) == id) ?? throw new KeyNotFoundException("Unknown content id: " + id);
        }
        private static void AddDependencies(string id, HashSet<string> result)
        { foreach (string next in Registered[id].Requires) if (result.Add(next)) AddDependencies(next, result); }
        private static IEnumerable<string> Ids(ContentPack p)
        { return p.Items.Select(x => x.Id).Concat(p.Weapons.Select(x => x.Id)).Concat(p.Maps.Select(x => x.Id)).Concat(p.Skins.Select(x => x.Id)); }
        private static void Shape(ContentPack p)
        {
            ArraySize(p.Requires, MaxPacks, "dependencies"); ArraySize(p.Items, MaxDefinitionsPerPack, "items");
            ArraySize(p.Weapons, MaxDefinitionsPerPack, "weapons"); ArraySize(p.Maps, 128, "maps"); ArraySize(p.Skins, MaxDefinitionsPerPack, "skins");
            int definitionCount=p.Items.Length+p.Weapons.Length+p.Maps.Length+p.Skins.Length;
            Check(definitionCount>0,"Content pack contains no definitions.");
            Check(definitionCount <= MaxDefinitionsPerPack, "Pack definition limit exceeded.");
            // Also bound nested aggregate work (not only individual arrays).
            long nodes = 0;
            foreach (var m in p.Maps)
            {
                ArraySize(m.Enemies, MaxEnemiesPerMap, "enemies"); ArraySize(m.Boxes, MaxBoxesPerMap, "boxes");
                ArraySize(m.Obstacles, MaxObstaclesPerMap, "obstacles"); ArraySize(m.Loadout, 64, "loadout");
                nodes += m.Enemies.Length + m.Boxes.Length + m.Obstacles.Length + m.Loadout.Length;
                foreach (var b in m.Boxes) { ArraySize(b.Loot, MaxLootEntriesPerBox, "loot"); nodes += b.Loot.Length; }
                Check(nodes <= 65536, "Pack nested content limit exceeded.");
            }
        }
        private static void ArraySize<T>(T[] array, int max, string name)
        {
            Check(array != null && array.Length <= max, "Invalid or oversized " + name + " array.");
            foreach (T value in array) Check(value != null, "Null " + name + " entry.");
        }
        private static void Validate(MapDefinition map, Dictionary<string, ItemDefinition> items, Dictionary<string, WeaponDefinition> weapons,
            Dictionary<string, SkinDefinition> skins, Dictionary<string, string> owners, HashSet<string> allowed)
        {
            Names(map.NameZh, map.NameEn); Number(map.Width, 2, 10000, "map width"); Number(map.Height, 2, 10000, "map height");
            foreach (var r in map.Obstacles)
            {
                Number(r.MinX, 0, map.Width, "wall min x"); Number(r.MaxX, 0, map.Width, "wall max x");
                Number(r.MinY, 0, map.Height, "wall min y"); Number(r.MaxY, 0, map.Height, "wall max y");
                Check(r.MinX < r.MaxX && r.MinY < r.MaxY, "Degenerate obstacle.");
            }
            Position(map, map.PlayerSpawn, RaidModel.PlayerRadius, "player spawn");
            foreach (var enemy in map.Enemies)
            {
                Position(map, enemy.Position, RaidModel.EnemyRadius, "enemy spawn"); Sprite(enemy.SpriteKey);
                Number(enemy.Health, .001f, 100000, "enemy health"); Number(enemy.Speed, 0, 100, "enemy speed");
                Number(enemy.Damage, 0, 100000, "enemy damage"); Number(enemy.AttackSeconds, .001f, 3600, "enemy attack seconds");
            }
            // Conservative work units per nominal 50 ms simulation step: entities × walls × speed substeps.
            // Validate speeds first so NaN/Infinity can never bypass or overflow this budget.
            float maxSpeed = map.Enemies.Length == 0 ? 0 : map.Enemies.Max(e => e.Speed);
            long speedSteps = Math.Max(1L, (long)Math.Ceiling(maxSpeed * .05d / RaidModel.EnemyRadius));
            long simulationCost = (map.Enemies.Length + 1L) * Math.Max(1, map.Obstacles.Length) * speedSteps;
            Check(simulationCost <= MaxSimulationCostPerMap, "Map simulation cost budget exceeded.");
            // Includes a fixed allowance for the player, shot, extraction, and a renderer-sampled grid.
            long estimatedVertices = map.Enemies.Length * 40L + map.Boxes.Length * 8L + map.Obstacles.Length * 8L + 2500;
            Check(estimatedVertices < MaxEstimatedVerticesPerMap, "Map estimated geometry vertex budget exceeded.");
            Number(map.ExtractionRadius, .001f, 10000, "extraction radius"); Number(map.ExtractionSeconds, .001f, 3600, "extraction seconds");
            // Even inactive coordinates must be finite. Inactive exits need not fit the map.
            Number(map.ExtractionPoint.X, -10000, 10000, "extraction x"); Number(map.ExtractionPoint.Y, -10000, 10000, "extraction y");
            if (map.HasExtraction) Position(map, map.ExtractionPoint, map.ExtractionRadius, "extraction");
            Reference(map.SkinId, skins, owners, allowed);
            Check(map.Loadout.Length > 0, "Map has no usable weapons.");
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (string weapon in map.Loadout) { Reference(weapon, weapons, owners, allowed); Check(distinct.Add(weapon), "Duplicate loadout weapon."); }
            long total = 0;
            var quantities = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var box in map.Boxes)
            {
                Position(map, box.Position, RaidModel.PlayerRadius, "box spawn"); Sprite(box.SpriteKey);
                foreach (var loot in box.Loot)
                {
                    Reference(loot.ItemId, items, owners, allowed);
                    Check(loot.Count > 0 && loot.Count <= items[loot.ItemId].MaxStack, "Loot count must be positive and fit its item's MaxStack.");
                    total += loot.Count; Check(total <= MaxLootCountPerMap, "Map loot total exceeded.");
                    quantities.TryGetValue(loot.ItemId, out long previous); quantities[loot.ItemId] = previous + loot.Count;
                }
            }
            long stacks = quantities.Sum(q => (q.Value + items[q.Key].MaxStack - 1) / items[q.Key].MaxStack);
            Check(stacks <= MaxInventoryStacks, "Map inventory stack limit exceeded.");
        }
        private static void Reference<T>(string id, Dictionary<string, T> entries, Dictionary<string, string> owners, HashSet<string> allowed)
        { Check(id != null && entries.ContainsKey(id) && allowed.Contains(owners[id]), "Missing reference or undeclared dependency: " + id); }
        private static void Position(MapDefinition map, Vec2 p, float radius, string name)
        {
            Number(p.X, radius, map.Width - radius, name + " x"); Number(p.Y, radius, map.Height - radius, name + " y");
            foreach (var r in map.Obstacles)
            {
                float x = Math.Max(r.MinX, Math.Min(p.X, r.MaxX)), y = Math.Max(r.MinY, Math.Min(p.Y, r.MaxY));
                Check(!r.Contains(p) && Vec2.DistanceSquared(p, new Vec2(x, y)) >= radius * radius, name + " intersects a wall.");
            }
        }
        private static void Validate(SkinDefinition skin)
        {
            Names(skin.NameZh, skin.NameEn);
            foreach (var c in new[] { skin.Floor, skin.Grid, skin.Wall, skin.WallEdge, skin.Player, skin.Enemy, skin.Crate, skin.OpenCrate, skin.Exit, skin.ExitEdge, skin.Bullet, skin.UIBackground, skin.UIText, skin.UIButton })
            { Number(c.R, 0, 1, "red"); Number(c.G, 0, 1, "green"); Number(c.B, 0, 1, "blue"); Number(c.A, 0, 1, "alpha"); }
            foreach (string key in new[] { skin.BackgroundKey, skin.PlayerKey, skin.EnemyKey, skin.CrateKey, skin.WallKey, skin.ItemKey }) Sprite(key);
        }
        private static void Token(string text, string name)
        {
            Check(!string.IsNullOrEmpty(text) && text.Length <= 128 && text[0] != '.' && text[text.Length - 1] != '.', "Invalid " + name + ".");
            foreach (char c in text) Check(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-', "Invalid " + name + ".");
            Check(!text.Contains(".."), "Invalid " + name + ".");
        }
        private static void Names(string zh, string en)
        { Check(!string.IsNullOrWhiteSpace(zh) && zh.Length <= 256 && !string.IsNullOrWhiteSpace(en) && en.Length <= 256, "Both localized names are required (max 256 chars)."); }
        private static void Sprite(string key)
        {
            if (key == null) return;
            Check(key.Length <= 257, "Sprite key is too long.");
            int split = key.IndexOf(':'); Check(split > 0 && split == key.LastIndexOf(':') && split < key.Length - 1, "SpriteKey must be modId:spriteName.");
            Token(key.Substring(0, split), "sprite mod id"); Token(key.Substring(split + 1), "sprite name");
        }
        private static void Number(float n, float min, float max, string name)
        { Check(!float.IsNaN(n) && !float.IsInfinity(n) && n >= min && n <= max, "Invalid " + name + "."); }
        private static void Check(bool condition, string message) { if (!condition) throw new ArgumentException(message); }

        internal static ContentPack Copy(ContentPack p) => new ContentPack { ApiVersion = p.ApiVersion, Id = p.Id, NameZh = p.NameZh, NameEn = p.NameEn, Requires = (string[])p.Requires.Clone(), Items = p.Items.Select(Copy).ToArray(), Weapons = p.Weapons.Select(Copy).ToArray(), Maps = p.Maps.Select(Copy).ToArray(), Skins = p.Skins.Select(Copy).ToArray() };
        internal static ItemDefinition Copy(ItemDefinition x) => new ItemDefinition { Id = x.Id, NameZh = x.NameZh, NameEn = x.NameEn, Width = x.Width, Height = x.Height, MaxStack = x.MaxStack, SpriteKey = x.SpriteKey };
        internal static WeaponDefinition Copy(WeaponDefinition x) => new WeaponDefinition { Id = x.Id, NameZh = x.NameZh, NameEn = x.NameEn, Damage = x.Damage, Cooldown = x.Cooldown, Range = x.Range, Automatic = x.Automatic, SpriteKey = x.SpriteKey };
        internal static MapDefinition Copy(MapDefinition x) => new MapDefinition { Id = x.Id, NameZh = x.NameZh, NameEn = x.NameEn, Width = x.Width, Height = x.Height, PlayerSpawn = x.PlayerSpawn, Enemies = x.Enemies.Select(Copy).ToArray(), Boxes = x.Boxes.Select(Copy).ToArray(), Obstacles = (Rect[])x.Obstacles.Clone(), HasExtraction = x.HasExtraction, ExtractionPoint = x.ExtractionPoint, ExtractionRadius = x.ExtractionRadius, ExtractionSeconds = x.ExtractionSeconds, SkinId = x.SkinId, Loadout = (string[])x.Loadout.Clone() };
        internal static EnemySpawn Copy(EnemySpawn x) => new EnemySpawn { Position = x.Position, Health = x.Health, Speed = x.Speed, Damage = x.Damage, AttackSeconds = x.AttackSeconds, SpriteKey = x.SpriteKey };
        internal static BoxSpawn Copy(BoxSpawn x) => new BoxSpawn { Position = x.Position, Loot = x.Loot.Select(Copy).ToArray(), SpriteKey = x.SpriteKey };
        internal static LootEntry Copy(LootEntry x) => new LootEntry { ItemId = x.ItemId, Count = x.Count };
        internal static SkinDefinition Copy(SkinDefinition x) => new SkinDefinition { Id = x.Id, NameZh = x.NameZh, NameEn = x.NameEn, Floor = x.Floor, Grid = x.Grid, Wall = x.Wall, WallEdge = x.WallEdge, Player = x.Player, Enemy = x.Enemy, Crate = x.Crate, OpenCrate = x.OpenCrate, Exit = x.Exit, ExitEdge = x.ExitEdge, Bullet = x.Bullet, UIBackground = x.UIBackground, UIText = x.UIText, UIButton = x.UIButton, BackgroundKey = x.BackgroundKey, PlayerKey = x.PlayerKey, EnemyKey = x.EnemyKey, CrateKey = x.CrateKey, WallKey = x.WallKey, ItemKey = x.ItemKey };
    }
    internal sealed class ContentSession
    {
        internal readonly MapDefinition Map;
        internal readonly SkinDefinition Skin;
        internal readonly WeaponDefinition[] Loadout;
        internal readonly Dictionary<string, ItemDefinition> Items;
        internal ContentSession(MapDefinition map, SkinDefinition skin, WeaponDefinition[] loadout, Dictionary<string, ItemDefinition> items)
        { Map = map; Skin = skin; Loadout = loadout; Items = items; }
    }
}
