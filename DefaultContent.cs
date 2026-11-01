using System;

namespace RaidPrototype.Content
{
    /// <summary>The built-in demo is an ordinary validated pack, not a special simulation path.</summary>
    public static class DefaultContent
    {
        public const string PackId = "raid.builtin";
        public const string DefaultMapId = PackId + ".map";
        public const string PistolId = PackId + ".pistol", RifleId = PackId + ".rifle";
        public const string DefaultSkinId = PackId + ".skin", DefaultItemId = PackId + ".loot";
        private static readonly object Gate = new object();
        public static void EnsureRegistered()
        {
            lock (Gate)
            {
                if (!ContentRegistry.IsRegistered(PackId)) ContentRegistry.Register(CreatePack());
            }
        }
        public static ContentPack CreatePack() => new ContentPack
        {
            Id = PackId, NameZh = "内置演示", NameEn = "Built-in demo",
            Items = new[] { new ItemDefinition { Id = DefaultItemId, NameZh = "战利品", NameEn = "Loot", Width = 1, Height = 1, MaxStack = 99 } },
            Weapons = new[]
            {
                new WeaponDefinition { Id = PistolId, NameZh = "示例手枪", NameEn = "GLOCK 17", Damage = 35, Cooldown = .4f, Range = 12, Automatic = false },
                new WeaponDefinition { Id = RifleId, NameZh = "示例步枪", NameEn = "AK-47", Damage = 20, Cooldown = .12f, Range = 16, Automatic = true }
            },
            Skins = new[] { new SkinDefinition { Id = DefaultSkinId, NameZh = "深色战术", NameEn = "Dark tactical" } },
            Maps = new[] { new MapDefinition
            {
                Id = DefaultMapId, NameZh = "演示区域", NameEn = "Demo raid", Width = 20, Height = 14, PlayerSpawn = new Vec2(2, 2),
                Enemies = new[] { new EnemySpawn { Position = new Vec2(10, 2) }, new EnemySpawn { Position = new Vec2(17, 10) } },
                Boxes = new[]
                {
                    new BoxSpawn { Position = new Vec2(3, 3), Loot = new[] { new LootEntry { ItemId = DefaultItemId, Count = 1 } } },
                    new BoxSpawn { Position = new Vec2(10, 11), Loot = new[] { new LootEntry { ItemId = DefaultItemId, Count = 1 } } }
                },
                Obstacles = new[] { new Rect(6, 3, 7, 10), new Rect(12, 5, 15, 6) },
                ExtractionPoint = new Vec2(18, 12), SkinId = DefaultSkinId, Loadout = new[] { PistolId, RifleId }
            } }
        };
    }
}
