using RaidPrototype;
using RaidPrototype.Content;

internal static class ContentTests
{
    private static ContentPack Sample(string id) => new ContentPack
    {
        Id = id, NameZh = "扩展测试", NameEn = "Content test",
        Requires = new[] { DefaultContent.PackId },
        Items = new[] { new ItemDefinition { Id = id + ".gem", NameZh = "宝石", NameEn = "Gem", Width = 2, Height = 1, MaxStack = 4, SpriteKey = id + ":gem" } },
        Weapons = new[] { new WeaponDefinition { Id = id + ".laser", NameZh = "第三武器", NameEn = "Laser", Damage = 77, Cooldown = .25f, Range = 40, Automatic = true, SpriteKey = id + ":laser" } },
        Skins = new[] { new SkinDefinition { Id = id + ".skin", NameZh = "新皮肤", NameEn = "New skin", Floor = new RGBA(.3f, .4f, .5f), PlayerKey = id + ":player" } },
        Maps = new[] { new MapDefinition
        {
            Id = id + ".map", NameZh = "大地图", NameEn = "Large map", Width = 32, Height = 24, PlayerSpawn = new Vec2(2, 2),
            SkinId = id + ".skin", Loadout = new[] { DefaultContent.PistolId, DefaultContent.RifleId, id + ".laser" },
            HasExtraction = false, ExtractionPoint = new Vec2(2, 2),
            Enemies = new[] { new EnemySpawn { Position = new Vec2(8, 2), Health = 90, Speed = 0, Damage = 17, AttackSeconds = .3f, SpriteKey = id + ":enemy" } },
            Boxes = new[]
            {
                new BoxSpawn { Position = new Vec2(2, 2), SpriteKey = id + ":box", Loot = new[] { new LootEntry { ItemId = id + ".gem", Count = 3 } } },
                new BoxSpawn { Position = new Vec2(2.5f, 2), Loot = new[] { new LootEntry { ItemId = id + ".gem", Count = 3 } } }
            }
        } }
    };
    private static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (KeyNotFoundException) { return true; }
    }
    public static void Run(Action<bool, string> check)
    {
        DefaultContent.EnsureRegistered();
        int original = ContentRegistry.Packs.Count;
        check(ContentRegistry.ApiVersion == 1 && ContentRegistry.GetMap(DefaultContent.DefaultMapId).Width == 20, "default pack registered through registry");
        check(ContentRegistry.GetWeapon(DefaultContent.PistolId).NameEn == "GLOCK 17" && ContentRegistry.GetWeapon(DefaultContent.RifleId).NameEn == "AK-47", "default localized weapon names");
        var pack = Sample("test.content");
        ContentRegistry.Register(pack);
        var game = new RaidModel("test.content.map");
        check(game.MapBounds.MaxX == 32 && game.MapBounds.MaxY == 24 && game.Map.Id == "test.content.map" && game.Loadout.Count == 3, "different map dimensions and third weapon need no core changes");
        check(game.Skin.Floor.R == .3f && game.Skin.PlayerKey == "test.content:player" && ContentRegistry.GetItem("test.content.gem").Width == 2, "custom skin and new item");
        check(game.Enemies[0].MaxHealth == 90 && game.Enemies[0].Speed == 0 && game.Enemies[0].Damage == 17 && game.Enemies[0].AttackSeconds == .3f && game.Enemies[0].SpriteKey == "test.content:enemy", "content enemy attributes and sprite");
        game.SelectWeapon(2);
        check(game.CurrentWeapon.Id == "test.content.laser" && game.Fire(new Vec2(8, 2)) && game.Enemies[0].Health == 13, "third weapon damage driven by definition");
        check(!game.Fire(new Vec2(8, 2)), "third weapon cooldown enforced");
        game.Update(.251f, Vec2.Zero);
        check(game.Fire(new Vec2(8, 2)) && !game.Enemies[0].Alive, "third weapon cooldown and kill");
        game.SelectWeapon(DefaultContent.RifleId);
        check(game.SelectedWeapon == Weapon.Rifle && game.CurrentWeapon.Automatic, "string selection and legacy enum wrapper");
        game.SelectWeapon(Weapon.Pistol);
        check(game.CurrentWeapon.Id == DefaultContent.PistolId && Reject(() => game.SelectWeapon(99)) && Reject(() => game.SelectWeapon("missing")), "weapon selection validation");
        check(game.Interact() && game.Interact() && !game.Interact() && game.LootCount == 6 && game.Inventory.Count == 2 && game.Inventory[0].Count == 4 && game.Inventory[1].Count == 2, "loot quantities merge into bounded stacks");
        check(game.Boxes[0].SpriteKey == "test.content:box" && game.Boxes[0].Loot[0].ItemId == "test.content.gem", "loot boxes expose detached content");
        game.Inventory[0].Count = 999; game.Boxes[0].Loot[0].Count = 999;
        check(game.Inventory[0].Count == 4 && game.Boxes[0].Loot[0].Count == 3, "inventory and box DTO mutation isolation");
        game.Update(10, Vec2.Zero);
        check(game.State == RaidState.Playing && game.ExtractionProgress == 0, "disabled extraction never automatically extracts");
        game.Update(10, Vec2.UnitX);
        check(game.PlayerPosition.X > 31 && game.PlayerPosition.X <= 32 - RaidModel.PlayerRadius, "movement uses custom map boundary");
        game.Reset(); game.Enemies[0].Health = 0; game.SelectWeapon(2);
        check(game.Fire(new Vec2(100, 2)) && Math.Abs(game.LastShotEnd.X - 32) < .001f, "custom map bullet boundary");
        // Every collection API and getter must copy all nested DTO arrays, not just its top level.
        pack.Maps[0].Width = 3; pack.Maps[0].Loadout[0] = "missing"; pack.Maps[0].Enemies[0].Health = 1;
        pack.Maps[0].Boxes[0].Loot[0].Count = 4; pack.Weapons[0].Damage = 1; pack.Skins[0].PlayerKey = "broken"; pack.Items[0].MaxStack = 1;
        var map = ContentRegistry.GetMap("test.content.map"); map.Width = 9; map.Loadout[0] = "bad"; map.Boxes[0].Loot[0].Count = 1; map.Enemies[0].Health = 1;
        ContentRegistry.GetWeapon("test.content.laser").Damage = 2; ContentRegistry.GetItem("test.content.gem").MaxStack = 1;
        ContentRegistry.GetSkin("test.content.skin").Floor = new RGBA(0, 0, 0);
        var outward = ContentRegistry.Packs.First(p => p.Id == "test.content"); outward.Maps[0].Width = 8; outward.Requires[0] = "bad";
        ContentRegistry.Maps.First(m => m.Id == "test.content.map").Width = 7;
        ContentRegistry.Weapons.First(w => w.Id == "test.content.laser").Damage = 3;
        ContentRegistry.Items.First(i => i.Id == "test.content.gem").MaxStack = 2;
        ContentRegistry.Skins.First(s => s.Id == "test.content.skin").PlayerKey = "bad";
        check(ContentRegistry.GetMap("test.content.map").Width == 32 && ContentRegistry.GetMap("test.content.map").Boxes[0].Loot[0].Count == 3 && ContentRegistry.GetMap("test.content.map").Enemies[0].Health == 90 && ContentRegistry.GetWeapon("test.content.laser").Damage == 77 && ContentRegistry.GetItem("test.content.gem").MaxStack == 4 && ContentRegistry.GetSkin("test.content.skin").Floor.R == .3f, "registration input, getters and all registry enumerations deep copy");
        game.Map.Width = 5; game.Map.Enemies[0].Speed = 90; game.Map.Boxes[0].Loot[0].Count = 999;
        game.Skin.PlayerKey = "bad"; game.CurrentWeapon.Damage = 1; game.Loadout[2].Damage = 2;
        check(game.Map.Width == 32 && game.Map.Enemies[0].Speed == 0 && game.Map.Boxes[0].Loot[0].Count == 3 && game.Skin.PlayerKey == "test.content:player" && game.CurrentWeapon.Damage == 77, "all model content properties deep copy");
        check(Reject(() => ContentRegistry.Unregister(DefaultContent.PackId)) && ContentRegistry.IsRegistered(DefaultContent.PackId), "required pack cannot be unregistered");
        var snapshot = ContentRegistry.Maps;
        ContentRegistry.Unregister("test.content");
        check(snapshot.Any(m => m.Id == "test.content.map") && !ContentRegistry.Maps.Any(m => m.Id == "test.content.map"), "registry enumeration is a stable snapshot");
        game.Reset(); game.SelectWeapon("test.content.laser");
        check(game.Fire(new Vec2(8, 2)) && game.Enemies[0].Health == 13 && game.Interact() && game.LootCount == 3 && game.Skin.Floor.R == .3f, "session survives unregister including reset weapons enemies loot and skin");
        game.GetItem("test.content.gem").NameEn = "mutated";
        check(game.GetItem("test.content.gem").NameEn == "Gem" && game.GetItem("test.content.gem").MaxStack == 4 && Reject(() => game.GetItem("missing")), "session item getter survives unload and returns isolated DTO");
        var replacement = Sample("test.content"); replacement.Maps[0].Width = 40; replacement.Weapons[0].Damage = 3;
        ContentRegistry.Register(replacement);
        var later = new RaidModel("test.content.map"); later.SelectWeapon(2);
        check(later.MapBounds.MaxX == 40 && later.CurrentWeapon.Damage == 3 && game.MapBounds.MaxX == 32 && game.CurrentWeapon.Damage == 77, "reregister changes new sessions only");
        ContentRegistry.Unregister("test.content");

        var invalids = new (string Name, Action<ContentPack> Mutate)[]
        {
            ("version", p => p.ApiVersion = 2),
            ("pack namespace", p => p.Id = "bad:id"),
            ("definition namespace", p => p.Items[0].Id = "outside.gem"),
            ("duplicate id", p => p.Weapons[0].Id = p.Items[0].Id),
            ("missing dependency", p => p.Requires = new[] { "not.registered" }),
            ("duplicate dependency", p => p.Requires = new[] { DefaultContent.PackId, DefaultContent.PackId }),
            ("self dependency", p => p.Requires = new[] { p.Id }),
            ("undeclared reference dependency", p => p.Requires = Array.Empty<string>()),
            ("missing skin", p => p.Maps[0].SkinId = "missing.skin"),
            ("missing weapon", p => p.Maps[0].Loadout[0] = "missing.weapon"),
            ("missing item", p => p.Maps[0].Boxes[0].Loot[0].ItemId = "missing.item"),
            ("empty loadout", p => p.Maps[0].Loadout = Array.Empty<string>()),
            ("duplicate loadout", p => p.Maps[0].Loadout = new[] { DefaultContent.PistolId, DefaultContent.PistolId }),
            ("NaN damage", p => p.Weapons[0].Damage = float.NaN),
            ("infinite cooldown", p => p.Weapons[0].Cooldown = float.PositiveInfinity),
            ("zero cooldown", p => p.Weapons[0].Cooldown = 0),
            ("negative range", p => p.Weapons[0].Range = -1),
            ("zero damage", p => p.Weapons[0].Damage = 0),
            ("map NaN", p => p.Maps[0].Height = float.NaN),
            ("small map", p => p.Maps[0].Width = 1),
            ("huge map", p => p.Maps[0].Width = 10001),
            ("player boundary", p => p.Maps[0].PlayerSpawn = new Vec2(0, 2)),
            ("player wall", p => p.Maps[0].Obstacles = new[] { new Rect(1, 1, 3, 3) }),
            ("player radius wall", p => p.Maps[0].Obstacles = new[] { new Rect(2.1f, 1, 3, 3) }),
            ("enemy boundary", p => p.Maps[0].Enemies[0].Position = new Vec2(32, 2)),
            ("enemy wall", p => p.Maps[0].Obstacles = new[] { new Rect(7, 1, 9, 3) }),
            ("enemy health", p => p.Maps[0].Enemies[0].Health = 0),
            ("enemy speed", p => p.Maps[0].Enemies[0].Speed = -1),
            ("enemy damage", p => p.Maps[0].Enemies[0].Damage = float.NegativeInfinity),
            ("enemy attack", p => p.Maps[0].Enemies[0].AttackSeconds = 0),
            ("box boundary", p => p.Maps[0].Boxes[0].Position = new Vec2(1, 25)),
            ("box wall", p => { p.Maps[0].Boxes[0].Position = new Vec2(12, 12); p.Maps[0].Obstacles = new[] { new Rect(11, 11, 13, 13) }; }),
            ("exit boundary", p => { p.Maps[0].HasExtraction = true; p.Maps[0].ExtractionPoint = new Vec2(32, 12); }),
            ("exit radius boundary", p => { p.Maps[0].HasExtraction = true; p.Maps[0].ExtractionPoint = new Vec2(.5f, 12); }),
            ("exit wall", p => { p.Maps[0].HasExtraction = true; p.Maps[0].ExtractionPoint = new Vec2(12, 12); p.Maps[0].Obstacles = new[] { new Rect(11, 11, 13, 13) }; }),
            ("exit seconds", p => p.Maps[0].ExtractionSeconds = float.NaN),
            ("exit radius", p => p.Maps[0].ExtractionRadius = 0),
            ("wall outside", p => p.Maps[0].Obstacles = new[] { new Rect(30, 20, 33, 21) }),
            ("wall reversed", p => p.Maps[0].Obstacles = new[] { new Rect(12, 12, 10, 14) }),
            ("wall NaN", p => p.Maps[0].Obstacles = new[] { new Rect(float.NaN, 12, 14, 14) }),
            ("zero loot", p => p.Maps[0].Boxes[0].Loot[0].Count = 0),
            ("negative loot", p => p.Maps[0].Boxes[0].Loot[0].Count = -1),
            ("stack overflow", p => p.Maps[0].Boxes[0].Loot[0].Count = 5),
            ("item width", p => p.Items[0].Width = 0),
            ("item stack", p => p.Items[0].MaxStack = 0),
            ("color NaN", p => p.Skins[0].Floor = new RGBA(float.NaN, 0, 0)),
            ("color range", p => p.Skins[0].UIText = new RGBA(1, 1, 1, 2)),
            ("sprite format", p => p.Items[0].SpriteKey = "no-namespace"),
            ("sprite traversal", p => p.Skins[0].WallKey = "mod:../file"),
            ("name missing", p => p.Weapons[0].NameZh = null),
            ("null entries", p => p.Maps[0].Enemies = new EnemySpawn[] { null }),
            ("null arrays", p => p.Items = null),
            ("oversized array", p => p.Maps[0].Boxes = new BoxSpawn[ContentRegistry.MaxBoxesPerMap + 1])
        };
        foreach (var invalid in invalids)
        {
            var candidate = Sample("test.invalid"); invalid.Mutate(candidate);
            check(Reject(() => ContentRegistry.Register(candidate)) && !ContentRegistry.IsRegistered(candidate.Id) && ContentRegistry.Packs.Count == original && !ContentRegistry.Items.Any(i => i.Id == "test.invalid.gem"), "atomic reject: " + invalid.Name);
        }
        var badLast = Sample("test.rollback"); badLast.Maps[0].Boxes[1].Loot[0].ItemId = "missing.last";
        check(Reject(() => ContentRegistry.Register(badLast)) && Reject(() => ContentRegistry.GetWeapon("test.rollback.laser")), "late validation rollback exposes no half pack");
        badLast.Maps[0].Boxes[1].Loot[0].ItemId = "test.rollback.gem"; ContentRegistry.Register(badLast);
        check(ContentRegistry.IsRegistered("test.rollback") && Reject(() => ContentRegistry.Register(badLast)), "corrected rollback pack registers and duplicate pack rejected");
        ContentRegistry.Unregister("test.rollback");
        check(Reject(() => ContentRegistry.Register(new ContentPack {Id="test.empty",NameZh="空包",NameEn="Empty"})), "empty content rejected instead of false success");
        var parent = new ContentPack { Id = "test.parent", NameZh = "依赖", NameEn = "Dependency", Items=new[]{new ItemDefinition{Id="test.parent.item",NameZh="物品",NameEn="Item"}} };
        var child = new ContentPack { Id = "test.child", NameZh = "子包", NameEn = "Child", Items=new[]{new ItemDefinition{Id="test.child.item",NameZh="物品",NameEn="Item"}}, Requires = new[] { parent.Id } };
        var grandchild = new ContentPack { Id = "test.grandchild", NameZh = "孙包", NameEn = "Grandchild", Items=new[]{new ItemDefinition{Id="test.grandchild.item",NameZh="物品",NameEn="Item"}}, Requires = new[] { child.Id } };
        ContentRegistry.Register(parent); ContentRegistry.Register(child); ContentRegistry.Register(grandchild);
        check(Reject(() => ContentRegistry.Unregister(parent.Id)) && Reject(() => ContentRegistry.Unregister(child.Id)), "direct and transitive dependency chain protected");
        ContentRegistry.Unregister(grandchild.Id); ContentRegistry.Unregister(child.Id); ContentRegistry.Unregister(parent.Id);
        // Per-map aggregate count and stack limits, not just single loot-entry limits.
        var tooMuch = Sample("test.total"); tooMuch.Items[0].MaxStack = ContentRegistry.MaxLootCountPerMap;
        foreach (var b in tooMuch.Maps[0].Boxes) b.Loot[0].Count = ContentRegistry.MaxLootCountPerMap;
        check(Reject(() => ContentRegistry.Register(tooMuch)), "aggregate map loot total bounded");
        var tooManyStacks = Sample("test.stacks"); tooManyStacks.Items[0].MaxStack = 1;
        tooManyStacks.Maps[0].Boxes = Enumerable.Range(0, 65).Select(_ => new BoxSpawn { Position = new Vec2(2, 2), Loot = Enumerable.Range(0, 64).Select(_ => new LootEntry { ItemId = "test.stacks.gem", Count = 1 }).ToArray() }).ToArray();
        check(Reject(() => ContentRegistry.Register(tooManyStacks)), "aggregate inventory stack count bounded");
        check(ContentRegistry.MaxEnemiesPerMap == 64 && ContentRegistry.MaxBoxesPerMap == 128 && ContentRegistry.MaxObstaclesPerMap == 256, "lightweight per-map entity limits fixed");
        var overEnemies = Sample("test.enemycap");
        overEnemies.Maps[0].Enemies = Enumerable.Range(0, 65).Select(_ => new EnemySpawn { Position = new Vec2(8, 2) }).ToArray();
        check(Reject(() => ContentRegistry.Register(overEnemies)) && !ContentRegistry.IsRegistered(overEnemies.Id), "enemy count over lightweight cap rejected atomically");
        var overWalls = Sample("test.wallcap");
        overWalls.Maps[0].Obstacles = Enumerable.Repeat(new Rect(20, 20, 21, 21), 257).ToArray();
        check(Reject(() => ContentRegistry.Register(overWalls)) && !ContentRegistry.IsRegistered(overWalls.Id), "wall count over lightweight cap rejected atomically");
        var budget = Sample("test.budget");
        budget.Maps[0].Enemies = Enumerable.Range(0, 64).Select(_ => new EnemySpawn { Position = new Vec2(8, 2), Speed = 100 }).ToArray();
        budget.Maps[0].Obstacles = Enumerable.Repeat(new Rect(20, 20, 21, 21), 256).ToArray();
        check(Reject(() => ContentRegistry.Register(budget)) && !ContentRegistry.IsRegistered(budget.Id) && !ContentRegistry.Weapons.Any(w => w.Id == "test.budget.laser"), "individually valid counts rejected by aggregate simulation budget without half pack");
        // At maximum counts, speed 30 requires five substeps: 65 * 256 * 5 = 83,200, within budget.
        // Its geometry estimate is 64*40 + 128*8 + 256*8 + 2500 = 8,132, below the UI vertex cap.
        foreach (var e in budget.Maps[0].Enemies) e.Speed = 30;
        budget.Maps[0].Width = budget.Maps[0].Height = 10000;
        budget.Maps[0].Boxes = Enumerable.Range(0, 128).Select(_ => new BoxSpawn { Position = new Vec2(2, 2) }).ToArray();
        ContentRegistry.Register(budget);
        check(ContentRegistry.GetMap("test.budget.map").Enemies.Length == 64 && ContentRegistry.GetMap("test.budget.map").Boxes.Length == 128 && ContentRegistry.GetMap("test.budget.map").Obstacles.Length == 256 && ContentRegistry.GetMap("test.budget.map").Width == 10000, "maximum lightweight counts within cost and geometry budgets accepted on large map");
        ContentRegistry.Unregister(budget.Id);
        // Six substeps cost 99,840 (allowed); seven cost 116,480 (rejected).
        foreach (var e in budget.Maps[0].Enemies) e.Speed = 36;
        ContentRegistry.Register(budget); ContentRegistry.Unregister(budget.Id);
        foreach (var e in budget.Maps[0].Enemies) e.Speed = 37;
        check(Reject(() => ContentRegistry.Register(budget)), "aggregate simulation budget rejects first seven-substep case");
        check(ContentRegistry.MaxEnemiesPerMap * 40L + ContentRegistry.MaxBoxesPerMap * 8L + ContentRegistry.MaxObstaclesPerMap * 8L + 2500 < ContentRegistry.MaxEstimatedVerticesPerMap, "hard lightweight counts guarantee renderer geometry stays below 60000 vertices");
        var filled = new List<string>();
        try
        {
            while (ContentRegistry.Packs.Count < ContentRegistry.MaxPacks)
            {
                string id = "limit.p" + filled.Count;
                ContentRegistry.Register(new ContentPack { Id = id, NameZh = "限制", NameEn = "Limit", Items=new[]{new ItemDefinition{Id=id+".item",NameZh="物品",NameEn="Item"}} }); filled.Add(id);
            }
            check(Reject(() => ContentRegistry.Register(new ContentPack { Id = "limit.overflow", NameZh = "溢出", NameEn = "Overflow" })), "global registered pack count bounded");
        }
        finally { foreach (string id in filled) ContentRegistry.Unregister(id); }
        check(ContentRegistry.Packs.Count == original, "content tests clean up all registered test packs");
        var extract = Sample("test.extract"); extract.Maps[0].Enemies = Array.Empty<EnemySpawn>(); extract.Maps[0].HasExtraction = true; extract.Maps[0].ExtractionSeconds = .2f; extract.Maps[0].ExtractionRadius = .5f;
        ContentRegistry.Register(extract);
        var extractionGame = new RaidModel("test.extract.map"); extractionGame.Update(.21f, Vec2.Zero);
        check(extractionGame.State == RaidState.Extracted && extractionGame.ExtractionProgress == .2f, "custom extraction radius and duration");
        ContentRegistry.Unregister(extract.Id);
        var attack = Sample("test.attack"); attack.Maps[0].Enemies[0].Position = new Vec2(2.8f, 2);
        ContentRegistry.Register(attack);
        var attackGame = new RaidModel("test.attack.map"); attackGame.Update(.01f, Vec2.Zero);
        check(attackGame.PlayerHealth == 83, "custom enemy damage"); attackGame.Update(.31f, Vec2.Zero);
        check(attackGame.PlayerHealth == 66, "custom enemy attack interval");
        ContentRegistry.Unregister(attack.Id);
        ContentRegistry.Unregister(DefaultContent.PackId);
        var restored = new RaidModel();
        check(ContentRegistry.IsRegistered(DefaultContent.PackId) && restored.Map.Id == DefaultContent.DefaultMapId, "default constructor restores ordinary default pack after unload");
    }
}
