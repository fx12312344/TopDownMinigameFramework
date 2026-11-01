using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RaidPrototype.Content
{
    /// <summary>In-memory JSON content loading. No files, resources, network or game save APIs are accessed.</summary>
    public static class ContentLoader
    {
        // Limits are character counts / structural nodes, not UTF-8 bytes. All checks
        // precede definition-array allocation; lexical limits also precede Newtonsoft parsing.
        public const int MaxJsonCharacters = 1024 * 1024;
        public const int MaxJsonDepth = 32;
        public const int MaxNestedNodes = 65536;

        /// <summary>
        /// Registers a camelCase JSON pack and returns its Id. apiVersion must explicitly be 1;
        /// missing or other versions are rejected. Unknown fields are ignored; known keys are case sensitive.
        /// Invalid content throws ArgumentException (or the registry's validation exception).
        /// Registration is performed only after all DTOs have been converted.
        /// </summary>
        public static string RegisterJson(string json) => RegisterJsonCore(json, null);
        public static string RegisterJsonForProvider(string json, string providerId)
        {
            if(string.IsNullOrWhiteSpace(providerId))throw new ArgumentException("Provider id is required.",nameof(providerId));
            return RegisterJsonCore(json,providerId);
        }
        private static string RegisterJsonCore(string json,string providerId)
        {
            ValidateJsonEnvelope(json);
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Content JSON must not be empty.", nameof(json));
            var text = json.Trim();
            if (text.Length < 2 || text[0] != '{' || text[text.Length - 1] != '}')
                throw new ArgumentException("Content JSON must be a JSON object.", nameof(json));
            JObject root;
            PackDto dto;
            try
            {
                // StringReader is in-memory only; disable date coercion so ISO-looking
                // JSON strings stay strings. No serializer/type resolution is involved.
                using (var input = new System.IO.StringReader(text))
                using (var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = MaxJsonDepth })
                {
                    root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new ArgumentException("Content JSON must contain exactly one object.", nameof(json));
                }
                dto = ReadPack(root);
            }
            catch (JsonException ex) { throw new ArgumentException("Invalid content JSON: " + ex.Message, nameof(json), ex); }
            catch (OverflowException ex) { throw new ArgumentException("Content JSON number is out of range.", nameof(json), ex); }
            if (dto.apiVersion != 1) throw new ArgumentException("Unsupported apiVersion: " + dto.apiVersion + "; expected 1.", nameof(json));
            if(providerId!=null && !string.Equals(dto.id,providerId,StringComparison.Ordinal))
                throw new ArgumentException("Content pack id must equal its provider manifest ID: "+providerId,nameof(json));
            ValidateDtoBudget(dto);

            var pack = new ContentPack
            {
                ApiVersion = dto.apiVersion, Id = dto.id, NameZh = dto.nameZh, NameEn = dto.nameEn,
                Requires = dto.requires ?? Array.Empty<string>(),
                Items = Convert(dto.items, Item), Weapons = Convert(dto.weapons, Weapon),
                Maps = Convert(dto.maps, Map), Skins = Convert(dto.skins, Skin)
            };
            var declaredMaps = Field(root, "maps") as JArray;
            if (declaredMaps != null && declaredMaps.Count > 0 && pack.Maps.Length != declaredMaps.Count)
                throw new ArgumentException("Parsed map count does not match JSON maps.", nameof(json));
            ContentRegistry.Register(pack);
            return pack.Id;
        }

        private static void ValidateJsonEnvelope(string json)
        {
            if (json == null) throw new ArgumentException("Content JSON must not be empty.", nameof(json));
            if (json.Length > MaxJsonCharacters) throw new ArgumentException("Content JSON exceeds MaxJsonCharacters (1048576).", nameof(json));
            // Strings may contain braces, escaped quotes and backslashes. Only count
            // containers outside strings; never recurse or run a regex on untrusted JSON.
            var stack = new char[MaxJsonDepth];
            int depth = 0;
            bool inString = false, escaped = false;
            foreach (char c in json)
            {
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{' || c == '[')
                {
                    if (depth == MaxJsonDepth) throw new ArgumentException("Content JSON exceeds MaxJsonDepth (32).", nameof(json));
                    stack[depth++] = c;
                }
                else if (c == '}' || c == ']')
                {
                    if (depth == 0 || stack[depth - 1] != (c == '}' ? '{' : '['))
                        throw new ArgumentException("Content JSON has unbalanced containers.", nameof(json));
                    depth--;
                }
            }
            if (inString || depth != 0) throw new ArgumentException("Content JSON has an unterminated string or container.", nameof(json));
        }
        private static int ArrayBudget<T>(T[] values, int maximum, string field, ref int nodes)
        {
            if (values == null) return 0;
            if (values.Length > maximum) throw new ArgumentException("Content array limit exceeded: " + field + " (max " + maximum + ").");
            if (values.Length > MaxNestedNodes - nodes) throw new ArgumentException("Content exceeds MaxNestedNodes (65536).");
            nodes += values.Length;
            foreach (T value in values)
                if (value == null) throw new ArgumentException("Content arrays must not contain null entries: " + field + ".");
            return values.Length;
        }
        private static void ValidateDtoBudget(PackDto dto)
        {
            // Per-call state only. No static counters shared by concurrent registrations.
            // Walk the entire parsed DTO tree before Convert allocates any definition arrays.
            int nodes = 0, definitions = 0;
            ArrayBudget(dto.requires, ContentRegistry.MaxPacks, "requires", ref nodes);
            definitions += ArrayBudget(dto.items, ContentRegistry.MaxDefinitionsPerPack, "items", ref nodes);
            definitions += ArrayBudget(dto.weapons, ContentRegistry.MaxDefinitionsPerPack, "weapons", ref nodes);
            definitions += ArrayBudget(dto.skins, ContentRegistry.MaxDefinitionsPerPack, "skins", ref nodes);
            definitions += ArrayBudget(dto.maps, 128, "maps", ref nodes);
            if (definitions == 0) throw new ArgumentException("Content pack must contain at least one definition.");
            if (definitions > ContentRegistry.MaxDefinitionsPerPack)
                throw new ArgumentException("Content definition limit exceeded (max " + ContentRegistry.MaxDefinitionsPerPack + ").");
            if (dto.maps == null) return;
            foreach (var map in dto.maps)
            {
                ArrayBudget(map.enemies, ContentRegistry.MaxEnemiesPerMap, "map.enemies", ref nodes);
                ArrayBudget(map.boxes, ContentRegistry.MaxBoxesPerMap, "map.boxes", ref nodes);
                ArrayBudget(map.obstacles, ContentRegistry.MaxObstaclesPerMap, "map.obstacles", ref nodes);
                ArrayBudget(map.loadout, 64, "map.loadout", ref nodes);
                if (map.boxes != null) foreach (var box in map.boxes)
                    ArrayBudget(box.loot, ContentRegistry.MaxLootEntriesPerBox, "box.loot", ref nodes);
            }
        }

        // Do not use ToObject/JsonConvert or a contract resolver here: reflection-based
        // binding accepts wrong-case names and can expose runtime reflection capabilities.
        // JObject's ordinal lookup plus explicit scalar types is the whole DTO contract.
        private static JToken Field(JObject value, string name) => value.GetValue(name, StringComparison.Ordinal);
        private static JObject Object(JToken value)
        {
            if (value is JObject obj) return obj;
            throw new ArgumentException("Content JSON expected an object: " + value.Path + ".");
        }
        private static bool Missing(JToken value) => value == null || value.Type == JTokenType.Null;
        private static ArgumentException TypeError(JToken value, string expected) =>
            new ArgumentException("Content JSON expected " + expected + ": " + value.Path + ".");
        private static string Text(JToken value)
        {
            if (Missing(value)) return null;
            if (value.Type != JTokenType.String) throw TypeError(value, "a string");
            return (string)value;
        }
        private static int Integer(JToken value)
        {
            if (value == null) return 0;
            if (value.Type != JTokenType.Integer) throw TypeError(value, "an integer");
            return (int)value;
        }
        private static float Number(JToken value)
        {
            if (value == null) return 0;
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float) throw TypeError(value, "a number");
            // Finite/range checks belong to ContentRegistry, after conversion and before mutation.
            return (float)value;
        }
        private static bool Boolean(JToken value)
        {
            if (value == null) return false;
            if (value.Type != JTokenType.Boolean) throw TypeError(value, "a boolean");
            return (bool)value;
        }
        private static T[] ReadArray<T>(JToken value, Func<JToken, T> read) where T : class
        {
            if (Missing(value)) return null;
            if (!(value is JArray array)) throw TypeError(value, "an array");
            var result = new T[array.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Missing(array[i]) ? null : read(array[i]);
            return result;
        }
        private static PointDto ReadPoint(JToken value)
        {
            if (Missing(value)) return null;
            var o = Object(value);
            return new PointDto { x = Number(Field(o, "x")), y = Number(Field(o, "y")) };
        }
        private static ColorDto ReadColor(JToken value)
        {
            if (Missing(value)) return null;
            var o = Object(value);
            return new ColorDto { r = Number(Field(o, "r")), g = Number(Field(o, "g")), b = Number(Field(o, "b")), a = Number(Field(o, "a")) };
        }
        private static PackDto ReadPack(JObject o) => new PackDto
        {
            apiVersion = Integer(Field(o, "apiVersion")), id = Text(Field(o, "id")),
            nameZh = Text(Field(o, "nameZh")), nameEn = Text(Field(o, "nameEn")),
            requires = ReadArray(Field(o, "requires"), Text), items = ReadArray(Field(o, "items"), ReadItem),
            weapons = ReadArray(Field(o, "weapons"), ReadWeapon), maps = ReadArray(Field(o, "maps"), ReadMap),
            skins = ReadArray(Field(o, "skins"), ReadSkin)
        };
        private static ItemDto ReadItem(JToken value)
        {
            var o = Object(value);
            return new ItemDto
            {
                id = Text(Field(o, "id")), nameZh = Text(Field(o, "nameZh")), nameEn = Text(Field(o, "nameEn")),
                spriteKey = Text(Field(o, "spriteKey")), width = Integer(Field(o, "width")),
                height = Integer(Field(o, "height")), maxStack = Integer(Field(o, "maxStack"))
            };
        }
        private static WeaponDto ReadWeapon(JToken value)
        {
            var o = Object(value);
            return new WeaponDto
            {
                id = Text(Field(o, "id")), nameZh = Text(Field(o, "nameZh")), nameEn = Text(Field(o, "nameEn")),
                spriteKey = Text(Field(o, "spriteKey")), damage = Number(Field(o, "damage")),
                cooldown = Number(Field(o, "cooldown")), range = Number(Field(o, "range")), automatic = Boolean(Field(o, "automatic"))
            };
        }
        private static RectDto ReadRect(JToken value)
        {
            var o = Object(value);
            return new RectDto { minX = Number(Field(o, "minX")), minY = Number(Field(o, "minY")), maxX = Number(Field(o, "maxX")), maxY = Number(Field(o, "maxY")) };
        }
        private static EnemyDto ReadEnemy(JToken value)
        {
            var o = Object(value);
            return new EnemyDto
            {
                position = ReadPoint(Field(o, "position")), health = Number(Field(o, "health")), speed = Number(Field(o, "speed")),
                damage = Number(Field(o, "damage")), attackSeconds = Number(Field(o, "attackSeconds")), spriteKey = Text(Field(o, "spriteKey"))
            };
        }
        private static LootDto ReadLoot(JToken value)
        {
            var o = Object(value);
            return new LootDto { itemId = Text(Field(o, "itemId")), count = Integer(Field(o, "count")) };
        }
        private static BoxDto ReadBox(JToken value)
        {
            var o = Object(value);
            return new BoxDto { position = ReadPoint(Field(o, "position")), spriteKey = Text(Field(o, "spriteKey")), loot = ReadArray(Field(o, "loot"), ReadLoot) };
        }
        private static MapDto ReadMap(JToken value)
        {
            var o = Object(value);
            return new MapDto
            {
                id = Text(Field(o, "id")), nameZh = Text(Field(o, "nameZh")), nameEn = Text(Field(o, "nameEn")),
                width = Number(Field(o, "width")), height = Number(Field(o, "height")), playerSpawn = ReadPoint(Field(o, "playerSpawn")),
                enemies = ReadArray(Field(o, "enemies"), ReadEnemy), boxes = ReadArray(Field(o, "boxes"), ReadBox),
                obstacles = ReadArray(Field(o, "obstacles"), ReadRect), hasExtraction = Boolean(Field(o, "hasExtraction")),
                extractionPoint = ReadPoint(Field(o, "extractionPoint")), extractionRadius = Number(Field(o, "extractionRadius")),
                extractionSeconds = Number(Field(o, "extractionSeconds")), skinId = Text(Field(o, "skinId")), loadout = ReadArray(Field(o, "loadout"), Text)
            };
        }
        private static SkinDto ReadSkin(JToken value)
        {
            var o = Object(value);
            return new SkinDto
            {
                id = Text(Field(o, "id")), nameZh = Text(Field(o, "nameZh")), nameEn = Text(Field(o, "nameEn")),
                floor = ReadColor(Field(o, "floor")), grid = ReadColor(Field(o, "grid")), wall = ReadColor(Field(o, "wall")),
                wallEdge = ReadColor(Field(o, "wallEdge")), player = ReadColor(Field(o, "player")), enemy = ReadColor(Field(o, "enemy")),
                crate = ReadColor(Field(o, "crate")), openCrate = ReadColor(Field(o, "openCrate")), exit = ReadColor(Field(o, "exit")),
                exitEdge = ReadColor(Field(o, "exitEdge")), bullet = ReadColor(Field(o, "bullet")), uiBackground = ReadColor(Field(o, "uiBackground")),
                uiText = ReadColor(Field(o, "uiText")), uiButton = ReadColor(Field(o, "uiButton")), backgroundKey = Text(Field(o, "backgroundKey")),
                playerKey = Text(Field(o, "playerKey")), enemyKey = Text(Field(o, "enemyKey")), crateKey = Text(Field(o, "crateKey")),
                wallKey = Text(Field(o, "wallKey")), itemKey = Text(Field(o, "itemKey"))
            };
        }

        private static TResult[] Convert<TSource, TResult>(TSource[] source, Func<TSource, TResult> convert) where TSource : class
        {
            if (source == null) return Array.Empty<TResult>();
            var result = new TResult[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null) throw new ArgumentException("Content arrays must not contain null entries (index " + i + ").");
                result[i] = convert(source[i]);
            }
            return result;
        }
        private static Vec2 Point(PointDto p, string field)
        {
            if (p == null) throw new ArgumentException("Missing required point: " + field + ".");
            return new Vec2(p.x, p.y);
        }
        private static ItemDefinition Item(ItemDto d) => new ItemDefinition
        {
            Id = d.id, NameZh = d.nameZh, NameEn = d.nameEn,
            Width = d.width, Height = d.height, MaxStack = d.maxStack, SpriteKey = d.spriteKey
        };
        private static WeaponDefinition Weapon(WeaponDto d) => new WeaponDefinition
        {
            Id = d.id, NameZh = d.nameZh, NameEn = d.nameEn, Damage = d.damage,
            Cooldown = d.cooldown, Range = d.range, Automatic = d.automatic, SpriteKey = d.spriteKey
        };
        private static MapDefinition Map(MapDto d) => new MapDefinition
        {
            Id = d.id, NameZh = d.nameZh, NameEn = d.nameEn, Width = d.width, Height = d.height,
            PlayerSpawn = Point(d.playerSpawn, "playerSpawn"),
            Enemies = Convert(d.enemies, e => new EnemySpawn
            {
                Position = Point(e.position, "enemy.position"), Health = e.health, Speed = e.speed,
                Damage = e.damage, AttackSeconds = e.attackSeconds, SpriteKey = e.spriteKey
            }),
            Boxes = Convert(d.boxes, b => new BoxSpawn
            {
                Position = Point(b.position, "box.position"), SpriteKey = b.spriteKey,
                Loot = Convert(b.loot, l => new LootEntry { ItemId = l.itemId, Count = l.count })
            }),
            Obstacles = Convert(d.obstacles, r => new Rect(r.minX, r.minY, r.maxX, r.maxY)),
            HasExtraction = d.hasExtraction,
            ExtractionPoint = d.extractionPoint == null && !d.hasExtraction ? Vec2.Zero : Point(d.extractionPoint, "extractionPoint"),
            ExtractionRadius = d.extractionRadius, ExtractionSeconds = d.extractionSeconds,
            SkinId = d.skinId, Loadout = d.loadout ?? Array.Empty<string>()
        };
        private static RGBA Color(ColorDto d, float r, float g, float b) => d == null
            ? new RGBA { R = r, G = g, B = b, A = 1f }
            : new RGBA { R = d.r, G = d.g, B = d.b, A = d.a };
        private static SkinDefinition Skin(SkinDto d) => new SkinDefinition
        {
            Id = d.id, NameZh = d.nameZh, NameEn = d.nameEn,
            Floor = Color(d.floor, .08f, .12f, .17f), Grid = Color(d.grid, .14f, .20f, .26f),
            Wall = Color(d.wall, .25f, .30f, .36f), WallEdge = Color(d.wallEdge, .42f, .50f, .58f),
            Player = Color(d.player, .30f, .80f, 1f), Enemy = Color(d.enemy, 1f, .35f, .30f),
            Crate = Color(d.crate, .85f, .65f, .25f), OpenCrate = Color(d.openCrate, .40f, .35f, .20f),
            Exit = Color(d.exit, .20f, .65f, .40f), ExitEdge = Color(d.exitEdge, .40f, 1f, .65f),
            Bullet = Color(d.bullet, 1f, .90f, .45f), UIBackground = Color(d.uiBackground, .06f, .08f, .12f),
            UIText = Color(d.uiText, .92f, .95f, 1f), UIButton = Color(d.uiButton, .18f, .30f, .42f),
            BackgroundKey = d.backgroundKey, PlayerKey = d.playerKey, EnemyKey = d.enemyKey,
            CrateKey = d.crateKey, WallKey = d.wallKey, ItemKey = d.itemKey
        };

#pragma warning disable 0649 // Data-only DTOs; every known field is assigned by the explicit JToken readers.
        [Serializable] private sealed class PackDto
        {
            public int apiVersion;
            public string id, nameZh, nameEn;
            public string[] requires;
            public ItemDto[] items;
            public WeaponDto[] weapons;
            public MapDto[] maps;
            public SkinDto[] skins;
        }
        [Serializable] private sealed class ItemDto
        {
            public string id, nameZh, nameEn, spriteKey;
            public int width, height, maxStack;
        }
        [Serializable] private sealed class WeaponDto
        {
            public string id, nameZh, nameEn, spriteKey;
            public float damage, cooldown, range;
            public bool automatic;
        }
        [Serializable] private sealed class PointDto { public float x, y; }
        [Serializable] private sealed class RectDto { public float minX, minY, maxX, maxY; }
        [Serializable] private sealed class ColorDto { public float r, g, b, a; }
        [Serializable] private sealed class EnemyDto
        {
            public PointDto position;
            public float health, speed, damage, attackSeconds;
            public string spriteKey;
        }
        [Serializable] private sealed class LootDto { public string itemId; public int count; }
        [Serializable] private sealed class BoxDto
        {
            public PointDto position;
            public LootDto[] loot;
            public string spriteKey;
        }
        [Serializable] private sealed class MapDto
        {
            public string id, nameZh, nameEn, skinId;
            public float width, height;
            public PointDto playerSpawn, extractionPoint;
            public EnemyDto[] enemies;
            public BoxDto[] boxes;
            public RectDto[] obstacles;
            public bool hasExtraction;
            public float extractionRadius, extractionSeconds;
            public string[] loadout;
        }
        [Serializable] private sealed class SkinDto
        {
            public string id, nameZh, nameEn;
            public ColorDto floor, grid, wall, wallEdge, player, enemy, crate, openCrate,
                exit, exitEdge, bullet, uiBackground, uiText, uiButton;
            public string backgroundKey, playerKey, enemyKey, crateKey, wallKey, itemKey;
        }
#pragma warning restore 0649
    }
}
