using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RaidPrototype.Content;

namespace RaidPrototype
{
    public enum RaidState { Playing, Extracted, Dead }
    // Legacy compatibility only. Content-driven UIs should use Loadout and CurrentWeapon.
    public enum Weapon { Pistol, Rifle }
    public readonly struct Rect
    {
        public readonly float MinX, MinY, MaxX, MaxY;
        public Rect(float minX, float minY, float maxX, float maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    }
    public sealed class Enemy
    {
        public Vec2 Position { get; internal set; }
        public float Health { get; internal set; }
        public float MaxHealth { get; }
        public float Speed { get; }
        public float Damage { get; }
        public float AttackSeconds { get; }
        public string SpriteKey { get; }
        public bool Alive => Health > 0;
        internal float AttackCooldown;
        internal Enemy(Vec2 position) : this(new EnemySpawn { Position = position }) { }
        internal Enemy(EnemySpawn spawn)
        {
            Position = spawn.Position; Health = MaxHealth = spawn.Health; Speed = spawn.Speed;
            Damage = spawn.Damage; AttackSeconds = spawn.AttackSeconds; SpriteKey = spawn.SpriteKey;
        }
    }
    public sealed class LootBox
    {
        private readonly global::RaidPrototype.Content.LootEntry[] loot;
        public Vec2 Position { get; }
        public bool Opened { get; internal set; }
        public string SpriteKey { get; }
        public IReadOnlyList<global::RaidPrototype.Content.LootEntry> Loot => Array.AsReadOnly(loot.Select(ContentRegistry.Copy).ToArray());
        internal global::RaidPrototype.Content.LootEntry[] Contents => loot;
        internal LootBox(Vec2 position) : this(new BoxSpawn { Position = position }) { }
        internal LootBox(BoxSpawn spawn)
        { Position = spawn.Position; SpriteKey = spawn.SpriteKey; loot = spawn.Loot.Select(ContentRegistry.Copy).ToArray(); }
    }
    /// <summary>Deterministic in-memory simulation. Its private content snapshot survives registry mutations/unloads.</summary>
    public sealed class RaidModel
    {
        public const float PlayerRadius = 0.3f, EnemyRadius = 0.3f, PlayerSpeed = 4f, PlayerMaxHealth = 100f;
        // Legacy demo constants. For the active map use MapBounds and Map.Extraction*.
        public const float ExtractionDuration = 3f, ExtractionRadius = 1.2f, InteractionRadius = 1.4f;
        public const string DefaultMapId = DefaultContent.DefaultMapId;
        public static readonly Rect Bounds = new Rect(0, 0, 20, 14);
        private readonly ContentSession session;
        private readonly List<Enemy> enemies = new List<Enemy>();
        private readonly List<LootBox> boxes = new List<LootBox>();
        private readonly List<global::RaidPrototype.Content.LootEntry> inventory = new List<global::RaidPrototype.Content.LootEntry>();
        private int selectedIndex;
        private float fireCooldown;
        public MapDefinition Map => ContentRegistry.Copy(session.Map);
        public SkinDefinition Skin => ContentRegistry.Copy(session.Skin);
        public Rect MapBounds { get; }
        public WeaponDefinition CurrentWeapon => ContentRegistry.Copy(session.Loadout[selectedIndex]);
        public IReadOnlyList<WeaponDefinition> Loadout => Array.AsReadOnly(session.Loadout.Select(ContentRegistry.Copy).ToArray());
        public IReadOnlyList<global::RaidPrototype.Content.LootEntry> Inventory => Array.AsReadOnly(inventory.Select(ContentRegistry.Copy).ToArray());
        public ItemDefinition GetItem(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (!session.Items.TryGetValue(id, out var item)) throw new KeyNotFoundException("Unknown session item: " + id);
            return ContentRegistry.Copy(item);
        }
        public IReadOnlyList<Rect> Obstacles { get; }
        public IReadOnlyList<Enemy> Enemies { get; }
        public IReadOnlyList<LootBox> Boxes { get; }
        public Vec2 PlayerPosition { get; private set; }
        public float PlayerHealth { get; private set; }
        public RaidState State { get; private set; }
        public Weapon SelectedWeapon => session.Loadout[selectedIndex].Id == DefaultContent.RifleId ? Weapon.Rifle : Weapon.Pistol;
        public int LootCount { get; private set; }
        public float ExtractionProgress { get; private set; }
        public Vec2 ExtractionPoint => session.Map.ExtractionPoint;
        public Vec2 LastShotStart { get; private set; }
        public Vec2 LastShotEnd { get; private set; }
        public long ShotSerial { get; private set; }

        public RaidModel() : this(DefaultMapId) { }
        public RaidModel(string mapId)
        {
            DefaultContent.EnsureRegistered();
            session = ContentRegistry.CreateSession(mapId);
            MapBounds = new Rect(0, 0, session.Map.Width, session.Map.Height);
            Obstacles = Array.AsReadOnly(session.Map.Obstacles);
            Enemies = new ReadOnlyCollection<Enemy>(enemies);
            Boxes = new ReadOnlyCollection<LootBox>(boxes);
            Reset();
        }
        public void Reset()
        {
            PlayerPosition = session.Map.PlayerSpawn; PlayerHealth = PlayerMaxHealth; State = RaidState.Playing;
            selectedIndex = 0; inventory.Clear(); LootCount = 0; ExtractionProgress = 0; fireCooldown = 0;
            ShotSerial = 0; LastShotStart = LastShotEnd = PlayerPosition;
            enemies.Clear(); foreach (var spawn in session.Map.Enemies) enemies.Add(new Enemy(spawn));
            boxes.Clear(); foreach (var spawn in session.Map.Boxes) boxes.Add(new LootBox(spawn));
        }
        public void SelectWeapon(int index)
        {
            if (index < 0 || index >= session.Loadout.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (State == RaidState.Playing) selectedIndex = index;
        }
        public void SelectWeapon(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            int index = Array.FindIndex(session.Loadout, w => w.Id == id);
            if (index < 0) throw new ArgumentException("Weapon is not in the session loadout: " + id, nameof(id));
            SelectWeapon(index);
        }
        public void SelectWeapon(Weapon weapon)
        {
            if (!Enum.IsDefined(typeof(Weapon), weapon)) throw new ArgumentOutOfRangeException(nameof(weapon));
            SelectWeapon(weapon == Weapon.Pistol ? DefaultContent.PistolId : DefaultContent.RifleId);
        }
        /// <summary>Open the nearest unopened unobstructed box in range; merge loot into MaxStack-bounded stacks.</summary>
        public bool Interact()
        {
            if (State != RaidState.Playing) return false;
            LootBox closest = null; float best = InteractionRadius * InteractionRadius;
            foreach (var box in boxes)
            {
                float d = Vec2.DistanceSquared(PlayerPosition, box.Position);
                if (!box.Opened && d <= best && ClearLine(PlayerPosition, box.Position)) { closest = box; best = d; }
            }
            if (closest == null) return false;
            foreach (var loot in closest.Contents)
            {
                int left = loot.Count, maxStack = session.Items[loot.ItemId].MaxStack;
                foreach (var stack in inventory)
                {
                    if (stack.ItemId != loot.ItemId || stack.Count == maxStack) continue;
                    int add = Math.Min(left, maxStack - stack.Count); stack.Count += add; left -= add;
                    if (left == 0) break;
                }
                while (left > 0)
                {
                    int add = Math.Min(left, maxStack); inventory.Add(new global::RaidPrototype.Content.LootEntry { ItemId = loot.ItemId, Count = add }); left -= add;
                }
                LootCount += loot.Count;
            }
            closest.Opened = true; return true;
        }
        /// <summary>Fire one hitscan toward a world-space target. Automatic is input metadata, not an implicit firing loop.</summary>
        public bool Fire(Vec2 target)
        {
            ValidateVector(target, nameof(target));
            if (State != RaidState.Playing || fireCooldown > 0) return false;
            double dx = (double)target.X - PlayerPosition.X, dy = (double)target.Y - PlayerPosition.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.0001) return false;
            Vec2 direction = new Vec2((float)(dx / length), (float)(dy / length));
            var weapon = session.Loadout[selectedIndex];
            fireCooldown = weapon.Cooldown;
            float damage = weapon.Damage;
            Enemy hit = null; float nearest = weapon.Range;
            if (direction.X > 0) nearest = Math.Min(nearest, (MapBounds.MaxX - PlayerPosition.X) / direction.X);
            else if (direction.X < 0) nearest = Math.Min(nearest, (MapBounds.MinX - PlayerPosition.X) / direction.X);
            if (direction.Y > 0) nearest = Math.Min(nearest, (MapBounds.MaxY - PlayerPosition.Y) / direction.Y);
            else if (direction.Y < 0) nearest = Math.Min(nearest, (MapBounds.MinY - PlayerPosition.Y) / direction.Y);
            foreach (var obstacle in session.Map.Obstacles)
            {
                float lo = 0, hi = nearest;
                if (Clip(PlayerPosition.X, direction.X, obstacle.MinX, obstacle.MaxX, ref lo, ref hi) &&
                    Clip(PlayerPosition.Y, direction.Y, obstacle.MinY, obstacle.MaxY, ref lo, ref hi)) nearest = Math.Min(nearest, lo);
            }
            foreach (var enemy in enemies)
            {
                if (!enemy.Alive) continue;
                Vec2 toEnemy = enemy.Position - PlayerPosition;
                float t = Vec2.Dot(toEnemy, direction);
                if (t + EnemyRadius < 0 || t - EnemyRadius > nearest) continue;
                float perpendicular = toEnemy.LengthSquared() - t * t;
                if (perpendicular > EnemyRadius * EnemyRadius) continue;
                float entry = Math.Max(0, t - (float)Math.Sqrt(Math.Max(0, EnemyRadius * EnemyRadius - perpendicular)));
                if (entry < nearest) { nearest = entry; hit = enemy; }
            }
            if (hit != null) hit.Health = Math.Max(0, hit.Health - damage);
            LastShotStart = PlayerPosition; LastShotEnd = PlayerPosition + direction * nearest; ShotSerial++;
            return true;
        }
        /// <summary>Advance simulation in seconds, with bounded fixed-size substeps and terminal states.</summary>
        public void Update(float deltaTime, Vec2 movement)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0 || deltaTime > 60) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            ValidateVector(movement, nameof(movement));
            if (State != RaidState.Playing || deltaTime == 0) return;
            double movementLength = Math.Sqrt((double)movement.X * movement.X + (double)movement.Y * movement.Y);
            if (movementLength > 1) movement = new Vec2((float)(movement.X / movementLength), (float)(movement.Y / movementLength));
            float remaining = deltaTime;
            while (remaining > 0 && State == RaidState.Playing)
            {
                // Subdivide by the fastest entity too, so content-defined speeds cannot tunnel through thin walls.
                float fastest = PlayerSpeed;
                foreach (var enemy in enemies) if (enemy.Alive) fastest = Math.Max(fastest, enemy.Speed);
                float dt = Math.Min(remaining, Math.Min(.05f, PlayerRadius / fastest)); remaining -= dt;
                fireCooldown = Math.Max(0, fireCooldown - dt);
                PlayerPosition = Move(PlayerPosition, movement * (PlayerSpeed * dt), PlayerRadius);
                foreach (var enemy in enemies)
                {
                    if (!enemy.Alive) continue;
                    enemy.AttackCooldown = Math.Max(0, enemy.AttackCooldown - dt);
                    Vec2 diff = PlayerPosition - enemy.Position; float distance = diff.Length();
                    if (distance > 0.901f)
                        enemy.Position = Move(enemy.Position, diff / distance * (Math.Min(enemy.Speed * dt, distance - 0.9f)), EnemyRadius);
                    else if (enemy.AttackCooldown <= 0 && ClearLine(enemy.Position, PlayerPosition))
                    {
                        PlayerHealth = Math.Max(0, PlayerHealth - enemy.Damage); enemy.AttackCooldown = enemy.AttackSeconds;
                        if (PlayerHealth <= 0) { State = RaidState.Dead; break; }
                    }
                }
                if (State != RaidState.Playing) break;
                if (session.Map.HasExtraction && Vec2.DistanceSquared(PlayerPosition, ExtractionPoint) <= session.Map.ExtractionRadius * session.Map.ExtractionRadius)
                {
                    ExtractionProgress = Math.Min(session.Map.ExtractionSeconds, ExtractionProgress + dt);
                    if (ExtractionProgress >= session.Map.ExtractionSeconds - .00001f) { ExtractionProgress = session.Map.ExtractionSeconds; State = RaidState.Extracted; }
                }
                else ExtractionProgress = 0;
            }
        }
        private Vec2 Move(Vec2 position, Vec2 delta, float radius)
        {
            var x = new Vec2(position.X + delta.X, position.Y);
            if (Walkable(x, radius)) position = x;
            var y = new Vec2(position.X, position.Y + delta.Y);
            if (Walkable(y, radius)) position = y;
            return position;
        }
        private bool Walkable(Vec2 p, float radius)
        {
            if (p.X < MapBounds.MinX + radius || p.X > MapBounds.MaxX - radius || p.Y < MapBounds.MinY + radius || p.Y > MapBounds.MaxY - radius) return false;
            foreach (var r in session.Map.Obstacles)
            {
                float x = Math.Max(r.MinX, Math.Min(p.X, r.MaxX));
                float y = Math.Max(r.MinY, Math.Min(p.Y, r.MaxY));
                if (Vec2.DistanceSquared(p, new Vec2(x, y)) < radius * radius) return false;
            }
            return true;
        }
        private bool ClearLine(Vec2 a, Vec2 b)
        {
            foreach (var r in session.Map.Obstacles)
            {
                float lo = 0, hi = 1;
                if (Clip(a.X, b.X - a.X, r.MinX, r.MaxX, ref lo, ref hi) && Clip(a.Y, b.Y - a.Y, r.MinY, r.MaxY, ref lo, ref hi)) return false;
            }
            return true;
        }
        private static bool Clip(float origin, float delta, float min, float max, ref float lo, ref float hi)
        {
            if (Math.Abs(delta) < 1e-8f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            if (a > b) { float t = a; a = b; b = t; }
            lo = Math.Max(lo, a); hi = Math.Min(hi, b); return lo <= hi;
        }
        private static void ValidateVector(Vec2 value, string name)
        {
            if (float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsInfinity(value.X) || float.IsInfinity(value.Y)) throw new ArgumentOutOfRangeException(name);
        }
    }
}
