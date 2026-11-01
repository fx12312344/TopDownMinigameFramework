using RaidPrototype;

int count = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
var game = new RaidModel();
Check(game.State == RaidState.Playing && game.PlayerHealth == 100 && game.Enemies.Count == 2, "initial state");
Check(!game.Interact(), "out of interaction range");
game.Update(0.2f, Vec2.One);
Check(game.Interact() && game.LootCount == 1 && !game.Interact(), "box opens once");
game.Reset();
Check(game.LootCount == 0 && !game.Boxes[0].Opened && game.PlayerPosition == new Vec2(2, 2), "reset restores loot and position");
Check(game.Fire(game.Enemies[0].Position), "pistol fires");
Check(game.Enemies[0].Health == 25 && !game.Fire(game.Enemies[0].Position), "pistol damage and cooldown");
game.Update(.5f, Vec2.Zero);
Check(game.Fire(game.Enemies[0].Position) && !game.Enemies[0].Alive, "enemy killed");
game.SelectWeapon(Weapon.Rifle); game.Update(.5f, Vec2.Zero);
Check(game.Fire(new Vec2(19, 2)), "rifle fires without ammunition inventory");
game.Reset(); game.Update(5, new Vec2(-1, 0));
Check(game.PlayerPosition.X >= RaidModel.PlayerRadius && game.PlayerPosition.X < .5f, "world boundary collision");
game.Reset(); game.Update(1, Vec2.UnitY); game.Update(3, Vec2.UnitX);
Check(game.PlayerPosition.X <= 6 - RaidModel.PlayerRadius + .001f && game.PlayerPosition.Y > 3, "rectangle collision prevents tunneling");
game.Reset(); game.Update(20, Vec2.Zero);
Check(game.State == RaidState.Dead && game.PlayerHealth == 0, "enemy pursuit and death");
var deadPos = game.PlayerPosition; game.Update(1, Vec2.One);
Check(game.PlayerPosition == deadPos && !game.Fire(Vec2.One) && !game.Interact(), "dead state is terminal");
game.Reset();
// Take an unobstructed route around the lower edges, killing attackers along the way.
bool reachedWaypoint = false;
for (int i = 0; i < 180 && game.State == RaidState.Playing; i++) {
    foreach (var e in game.Enemies) if (e.Alive) { game.Fire(e.Position); break; }
    if (game.PlayerPosition.Y >= 11.8f) reachedWaypoint = true;
    Vec2 target = reachedWaypoint ? game.ExtractionPoint : new Vec2(2, 12);
    Vec2 d = target - game.PlayerPosition;
    game.Update(.1f, d.Length() < .2f ? Vec2.Zero : Vec2.Normalize(d));
    if (Vec2.Distance(game.PlayerPosition, game.ExtractionPoint) <= RaidModel.ExtractionRadius) break;
}
Check(Vec2.Distance(game.PlayerPosition, game.ExtractionPoint) <= RaidModel.ExtractionRadius, "reach extraction zone");
game.Update(1, Vec2.Zero); Check(game.State == RaidState.Playing && game.ExtractionProgress > 0, "extraction countdown");
game.Update(1, -Vec2.UnitX); Check(game.ExtractionProgress == 0, "leaving extraction resets countdown");
game.Update(1, Vec2.UnitX); game.Update(3.1f, Vec2.Zero);
Check(game.State == RaidState.Extracted && game.ExtractionProgress == RaidModel.ExtractionDuration, "extraction succeeds");
var extractedPos = game.PlayerPosition; var extractedLoot = game.LootCount; var extractedSerial = game.ShotSerial; var extractedWeapon = game.SelectedWeapon;
game.Update(2, Vec2.UnitX); game.SelectWeapon(Weapon.Rifle);
Check(!game.Interact() && !game.Fire(Vec2.Zero) && game.PlayerPosition == extractedPos && game.LootCount == extractedLoot && game.ShotSerial == extractedSerial && game.SelectedWeapon == extractedWeapon && game.ExtractionProgress == RaidModel.ExtractionDuration, "extraction terminal and box unchanged");
game.Reset(); Check(game.State == RaidState.Playing && game.PlayerHealth == 100 && game.ExtractionProgress == 0 && game.ShotSerial == 0, "reset after extraction");
bool rejected = false; try { game.Update(-1, Vec2.Zero); } catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected, "invalid time rejected");
// The test project compiles the model in the same assembly, allowing controlled enemy setups.
game.Reset(); game.Enemies[0].Position = new Vec2(10, 4); game.Enemies[1].Position = new Vec2(18, 10);
game.Update(.5f, Vec2.UnitY); var blockedHealth = game.Enemies[0].Health;
Check(game.Fire(new Vec2(10, 4)) && game.Enemies[0].Health == blockedHealth && Math.Abs(game.LastShotEnd.X - 6) < .001f, "wall blocks damage and clips shot");
Check(game.LastShotStart == game.PlayerPosition && game.ShotSerial == 1, "shot origin and serial");
var previousEnd = game.LastShotEnd; Check(!game.Fire(new Vec2(10, 4)) && game.ShotSerial == 1 && game.LastShotEnd == previousEnd, "rejected shot leaves record unchanged");
game.Reset(); game.Enemies[0].Position = new Vec2(8, 2); game.Enemies[1].Position = new Vec2(5, 2);
Check(game.Fire(new Vec2(19, 2)) && game.Enemies[1].Health == 25 && game.Enemies[0].Health == 60 && Math.Abs(game.LastShotEnd.X - 4.7f) < .001f, "nearest enemy hit irrespective of list order");
game.Reset(); Check(game.Fire(new Vec2(-100, 2)) && Math.Abs(game.LastShotEnd.X) < .001f, "shot clips map boundary");
game.Reset(); game.SelectWeapon(Weapon.Rifle); Check(game.Fire(new Vec2(2, 13)), "rifle first shot");
game.Update(.1f, Vec2.Zero); Check(!game.Fire(new Vec2(2, 13)), "rifle cooldown not elapsed");
game.Update(.021f, Vec2.Zero); Check(game.Fire(new Vec2(2, 13)) && game.ShotSerial == 2, "rifle cooldown elapsed");
game.Reset(); game.Update(.5f, new Vec2(100, 100));
Check(Math.Abs(Vec2.Distance(game.PlayerPosition, new Vec2(2, 2)) - 2) < .001f, "diagonal movement normalized");
game.Reset(); game.Update(.5f, new Vec2(float.MaxValue, float.MaxValue));
Check(Math.Abs(Vec2.Distance(game.PlayerPosition, new Vec2(2, 2)) - 2) < .001f, "large finite movement normalized safely");
bool Throws(Action action) { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }
Check(Throws(() => game.Update(float.MaxValue, Vec2.Zero)) && Throws(() => game.Update(float.NaN, Vec2.Zero)) && Throws(() => game.Update(float.PositiveInfinity, Vec2.Zero)), "invalid and enormous time rejected");
Check(Throws(() => game.Update(1, new Vec2(float.NaN, 0))) && Throws(() => game.Update(1, new Vec2(0, float.NegativeInfinity))) && Throws(() => game.Fire(new Vec2(float.PositiveInfinity, 0))) && Throws(() => game.Fire(new Vec2(float.NaN, 0))), "nonfinite vectors rejected");
Console.WriteLine($"All {count} original model checks passed.");
ContentTests.Run(Check);
Console.WriteLine($"All {count} checks passed.");
