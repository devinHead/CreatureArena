using GameCore.Combat;
using GameCore.Content;
using GameCore.Models;

namespace GameCore;

public sealed class GameSession
{
    private readonly ContentCatalog _content;
    private readonly CreatureFactory _factory;
    private readonly IBattleResolver _battler;
    private Random _rng;

    public PlayerState Player { get; } = new();
    public GamePhase Phase { get; private set; } = GamePhase.StarterSelect;
    public int Round { get; private set; } = 1;
    public int Seed { get; private set; }
    public string CurrentRoomId { get; private set; } = "camp";
    public Creature? ActiveWild { get; private set; }
    public Creature? PendingWildCapture { get; private set; }
    public IReadOnlyList<Creature> StarterOptions => _starterOptions;
    private readonly List<Creature> _starterOptions = new();
    private readonly HashSet<string> _lootedRoomsThisVisit = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _shopItemIds = new();
    private List<string> _shopCreatureSpeciesIds = new();
    private List<string> _shopMoveIds = new();

    public GameSession(ContentCatalog? content = null, int? seed = null)
    {
        _content = content ?? ContentCatalog.Load();
        Seed = seed ?? Environment.TickCount;
        _rng = new Random(Seed);
        _factory = new CreatureFactory(_content, _rng);
        _battler = new AutoBattleResolver(_content, _rng);
        RollStarterOptions();
    }

    public void Reseed(int seed)
    {
        Seed = seed;
        _rng = new Random(seed);
    }

    public CommandResult Handle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CommandResult.Fail("Type a command. Try 'help'.");

        var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        if (cmd is "help") return Help();
        if (cmd is "status") return Status();
        if (cmd is "party") return Party();
        if (cmd is "seed" && args.Length == 1 && int.TryParse(args[0], out var s))
        {
            Reseed(s);
            return CommandResult.Success($"RNG seed set to {s} (affects future rolls).");
        }

        if (Phase == GamePhase.GameOver)
            return CommandResult.Fail("Game over. Restart the app to play again.");

        return Phase switch
        {
            GamePhase.StarterSelect => HandleStarter(cmd, args),
            GamePhase.Explore => HandleExplore(cmd, args),
            GamePhase.WildReward => HandleWildReward(cmd, args),
            GamePhase.Shop => HandleShop(cmd, args),
            GamePhase.Battle => CommandResult.Fail("Battle resolves automatically. Use 'ready' from shop."),
            _ => CommandResult.Fail("Unknown phase.")
        };
    }

    private void RollStarterOptions()
    {
        _starterOptions.Clear();
        var starters = _content.Species.Values.Where(s => s.IsStarter).Select(s => s.Id).ToList();
        if (starters.Count == 0)
            starters = new List<string> { "slime", "rockmite", "sparkbat" };

        // Three options: prefer distinct species, reshuffle IVs/ability/nature per option.
        var shuffled = starters.OrderBy(_ => _rng.Next()).ToList();
        while (shuffled.Count < 3)
            shuffled.Add(starters[_rng.Next(starters.Count)]);

        for (var i = 0; i < 3; i++)
            _starterOptions.Add(_factory.Create(shuffled[i], level: 2));
    }

    private CommandResult HandleStarter(string cmd, string[] args)
    {
        if (cmd is "starters" or "look")
            return ShowStarters();

        if (cmd is "pick" && args.Length >= 1 && int.TryParse(args[0], out var n))
            return PickStarter(n);

        return CommandResult.Fail("Pick a starter: 'pick 1', 'pick 2', or 'pick 3'. Type 'starters' to review.");
    }

    public CommandResult ShowStarters()
    {
        var lines = new List<string>
        {
            "Choose your starter with:  pick 1  |  pick 2  |  pick 3",
            ""
        };
        for (var i = 0; i < _starterOptions.Count; i++)
            lines.AddRange(FormatCreatureCard(i + 1, _starterOptions[i]));
        return CommandResult.Success("Starter select", lines);
    }

    private CommandResult PickStarter(int index)
    {
        if (index < 1 || index > _starterOptions.Count)
            return CommandResult.Fail($"Pick a number 1-{_starterOptions.Count}.");

        var starter = _starterOptions[index - 1];
        Player.Party.Add(starter);
        Player.Board[0] = starter.Id;
        Phase = GamePhase.Explore;
        EnsureRoomFlags();
        var entryLoot = ApplyRoomEntryLoot(force: true);
        MaybeSpawnEncounter();

        var lines = new List<string> { $"You chose {starter.Nickname}!", "" };
        lines.AddRange(entryLoot);
        lines.AddRange(Look().Lines);
        return CommandResult.Success("Run started.", lines);
    }

    private List<string> FormatCreatureCard(int number, Creature c)
    {
        var sp = _content.GetSpecies(c.SpeciesId);
        var ability = c.AbilityId is null ? null : _content.TryGetAbility(c.AbilityId);
        var nature = _content.GetNature(c.NatureId);
        var abilityText = ability is null ? "None" : $"{ability.Name} ({ability.Description})";
        var moveBits = c.MoveIds.Select(id =>
            _content.Moves.TryGetValue(id, out var m) ? $"{m.Name}/{m.Type}" : id);
        var learnBits = sp.Learnset
            .OrderBy(e => e.Level)
            .Select(e => $"{e.MoveId}@lv{e.Level}");
        return new List<string>
        {
            $"[{number}] {c.Nickname} — Type:{sp.Type}  Lv{c.Level}",
            $"    HP {c.CombatStats.Hp}  Atk {c.CombatStats.Atk}  Def {c.CombatStats.Def}  Spd {c.CombatStats.Spd}",
            $"    IVs HP {c.Potential.Hp}/15  Atk {c.Potential.Atk}/15  Def {c.Potential.Def}/15  Spd {c.Potential.Spd}/15  (max ~+10%)",
            $"    Nature: {nature.Name} (XP x{nature.XpGrowth:0.##})",
            $"    Ability: {abilityText}",
            $"    Known: {string.Join(", ", moveBits)}",
            $"    Learnset: {string.Join(", ", learnBits)}",
            $"    Shop-learnable: {string.Join(", ", sp.ShopMoveIds)}",
            ""
        };
    }

    private CommandResult HandleExplore(string cmd, string[] args)
    {
        return cmd switch
        {
            "look" => Look(),
            "go" when args.Length >= 1 => Go(args[0]),
            "fight" => FightWild(),
            "end" => EndExploreToShop(),
            _ => CommandResult.Fail($"Unknown explore command '{cmd}'. Try help.")
        };
    }

    private CommandResult HandleWildReward(string cmd, string[] args)
    {
        return cmd switch
        {
            "accept" => AcceptWild(),
            "decline" => DeclineWild(),
            "status" => Status(),
            "party" => Party(),
            _ => CommandResult.Fail("Choose 'accept' (add to party) or 'decline' (bonus / item chance).")
        };
    }

    private CommandResult HandleShop(string cmd, string[] args)
    {
        return cmd switch
        {
            "shop" => ShopList(),
            "buy" when args.Length >= 1 => Buy(args),
            "sell" when args.Length >= 1 => SellCreature(args[0]),
            "give" when args.Length >= 2 => Give(args[0], args[1]),
            "teach" when args.Length >= 2 => Teach(args[0], args[1]),
            "board" when args.Length >= 2 => Board(args[0], args[1]),
            "evolve" when args.Length >= 1 => Evolve(args),
            "party" => Party(),
            "ready" => ReadyBattle(),
            "status" => Status(),
            _ => CommandResult.Fail($"Unknown shop command '{cmd}'. Try help.")
        };
    }

    private CommandResult Look()
    {
        var room = _content.GetRoom(CurrentRoomId);
        var lines = new List<string>
        {
            $"{room.Name} — {room.Description}",
            $"Exits: {string.Join(", ", room.Exits)}"
        };

        if (ActiveWild is null)
        {
            lines.Add("No wild creature in sight.");
        }
        else
        {
            var sp = _content.GetSpecies(ActiveWild.SpeciesId);
            var winPct = EstimateWildWinPercent();
            var riskLabel = winPct switch
            {
                >= 70 => "FAVORABLE",
                >= 45 => "EVEN",
                >= 25 => "RISKY",
                _ => "DANGER"
            };
            lines.Add($"Wild: {ActiveWild.Nickname} [{sp.Type}] Lv{ActiveWild.Level} " +
                      $"HP {ActiveWild.CombatStats.Hp} Atk {ActiveWild.CombatStats.Atk} Def {ActiveWild.CombatStats.Def} Spd {ActiveWild.CombatStats.Spd}");
            lines.Add($"Risk: ~{winPct:0}% estimated win chance [{riskLabel}] — type 'fight' to engage.");
            var living = GetLivingExploreFighters().Count;
            if (living == 0)
                lines.Add("You have no living creatures — you cannot fight until shop/PvP heals your party.");
        }

        return CommandResult.Success($"Round {Round} | Explore", lines);
    }

    private int EstimateWildWinPercent()
    {
        if (ActiveWild is null) return 0;
        var fighters = GetLivingExploreFighters();
        if (fighters.Count == 0) return 0;

        var playerSide = fighters.Select(c => Combatant.FromCreature(c, _content.GetSpecies(c.SpeciesId), true)).ToList();
        var enemySide = new List<Combatant>
        {
            Combatant.FromCreature(ActiveWild, _content.GetSpecies(ActiveWild.SpeciesId), false)
        };
        return (int)Math.Round(_battler.EstimateWinRate(playerSide, enemySide) * 100);
    }

    private CommandResult Go(string roomId)
    {
        var room = _content.GetRoom(CurrentRoomId);
        if (!room.Exits.Any(e => e.Equals(roomId, StringComparison.OrdinalIgnoreCase)))
            return CommandResult.Fail($"Can't go to '{roomId}' from here. Exits: {string.Join(", ", room.Exits)}");

        CurrentRoomId = room.Exits.First(e => e.Equals(roomId, StringComparison.OrdinalIgnoreCase));
        ActiveWild = null;
        EnsureRoomFlags();
        var lootLines = ApplyRoomEntryLoot().ToList();
        MaybeSpawnEncounter();
        var look = Look();
        var lines = lootLines.Concat(look.Lines).ToList();
        return CommandResult.Success(look.Message, lines);
    }

    private void EnsureRoomFlags()
    {
        var room = _content.GetRoom(CurrentRoomId);
        if (string.IsNullOrEmpty(room.VisitFlag))
            return;

        // Player-level: "has this run ever reached Crystal Cave?"
        Player.Flags.Add(room.VisitFlag);

        // Creature-level: only party members present with you get the visit credit.
        foreach (var creature in Player.Party)
            creature.Flags.Add(room.VisitFlag);
    }

    private IEnumerable<string> ApplyRoomEntryLoot(bool force = false)
    {
        if (!force && _lootedRoomsThisVisit.Contains(CurrentRoomId))
            yield break;

        _lootedRoomsThisVisit.Add(CurrentRoomId);
        var room = _content.GetRoom(CurrentRoomId);
        var gold = room.GoldLootMax <= room.GoldLootMin
            ? room.GoldLootMin
            : _rng.Next(room.GoldLootMin, room.GoldLootMax + 1);
        if (gold > 0)
        {
            Player.Gold += gold;
            yield return $"Room loot: +{gold} gold (total {Player.Gold}).";
        }

        if (room.ItemLootIds.Count > 0 && _rng.NextDouble() < 0.45)
        {
            var itemId = room.ItemLootIds[_rng.Next(room.ItemLootIds.Count)];
            Player.InventoryItemIds.Add(itemId);
            yield return $"Room loot: found {itemId}.";
        }
    }

    private void MaybeSpawnEncounter()
    {
        var room = _content.GetRoom(CurrentRoomId);
        if (room.EncounterSpeciesIds.Count == 0) return;
        if (_rng.NextDouble() > room.EncounterChance) return;

        var speciesId = room.EncounterSpeciesIds[_rng.Next(room.EncounterSpeciesIds.Count)];
        var wildLevel = Math.Max(1, Round + _rng.Next(0, 2));
        ActiveWild = _factory.Create(speciesId, wildLevel);
    }

    private CommandResult FightWild()
    {
        if (ActiveWild is null)
            return CommandResult.Fail("No wild encounter here. Try 'go' to another room.");

        var fighters = GetLivingExploreFighters();
        if (fighters.Count == 0)
            return CommandResult.Fail("No living creatures can fight. Survive to the shop to heal, or end explore.");

        var winPct = EstimateWildWinPercent();
        var wild = ActiveWild;
        var wildSpecies = _content.GetSpecies(wild.SpeciesId);

        // Explore HP persists — do not heal before wild fights.
        var playerSide = fighters.Select(c => Combatant.FromCreature(c, _content.GetSpecies(c.SpeciesId), true)).ToList();
        var enemySide = new List<Combatant> { Combatant.FromCreature(wild, wildSpecies, false) };

        var result = _battler.Resolve(playerSide, enemySide);
        ApplyHpFromCombatants(fighters, playerSide);
        AwardXp(fighters, wild.Level, result.Won);

        var lines = new List<string> { $"Engaging (pre-fight win estimate was ~{winPct}%)." };
        lines.AddRange(result.Log.Select(e => e.Message));
        // Player HP is NEVER lost in wild fights.
        lines.Add($"Party HP after fight: {string.Join(", ", fighters.Select(c => $"{c.Nickname} {c.CurrentHp}/{c.CombatStats.Hp}"))}");

        if (!result.Won)
        {
            ActiveWild = null;
            lines.Add("The wild creature got away. Your injured party keeps its HP for the rest of explore.");
            return CommandResult.Success("Wild fight lost.", lines);
        }

        // Credit only the creatures that fought this win — not the whole party.
        var battleFlag = $"WonBattle:{CurrentRoomId}";
        foreach (var fighter in fighters)
            fighter.Flags.Add(battleFlag);
        lines.Add($"Creature flags granted: {battleFlag} → {string.Join(", ", fighters.Select(f => f.Nickname))}");

        PendingWildCapture = wild;
        Phase = GamePhase.WildReward;
        ActiveWild = null;
        lines.Add($"Victory! Accept {wild.Nickname} into your party, or decline for a bonus.");
        lines.Add($"Wild: nature={wild.NatureId} ability={wild.AbilityId ?? "-"} IVs A{wild.Potential.Atk}/D{wild.Potential.Def}/S{wild.Potential.Spd}/H{wild.Potential.Hp}");
        lines.Add("Type 'accept' or 'decline'.");
        return CommandResult.Success("Wild fight won — choose reward.", lines);
    }

    private CommandResult AcceptWild()
    {
        if (PendingWildCapture is null)
            return CommandResult.Fail("No pending capture.");

        var wild = PendingWildCapture;
        var species = _content.GetSpecies(wild.SpeciesId);
        wild.Nickname = UniqueNickname(wild.Nickname);
        _factory.HealFull(wild);
        Player.Party.Add(wild);
        var lines = new List<string> { $"{wild.Nickname} joined your party! ({Player.Party.Count} creatures)" };

        if (species.DropItemIds.Count > 0 &&
            _rng.NextDouble() < (species.AcceptItemChance ?? 0))
        {
            var itemId = species.DropItemIds[_rng.Next(species.DropItemIds.Count)];
            Player.InventoryItemIds.Add(itemId);
            lines.Add($"Lucky! Also received item: {itemId}");
        }

        PendingWildCapture = null;
        Phase = GamePhase.Explore;
        lines.Add("Back to explore. Injured party members stay injured until shop.");
        return CommandResult.Success("Creature accepted.", lines);
    }

    private CommandResult DeclineWild()
    {
        if (PendingWildCapture is null)
            return CommandResult.Fail("No pending capture.");

        var wild = PendingWildCapture;
        var species = _content.GetSpecies(wild.SpeciesId);
        Player.Gold += species.DeclineGoldBonus ?? 0;
        var lines = new List<string>
        {
            $"Declined. +{species.DeclineGoldBonus ?? 0} gold (total {Player.Gold})."
        };

        if (species.DropItemIds.Count > 0 &&
            _rng.NextDouble() < (species.DeclineItemChance ?? 0))
        {
            var itemId = species.DropItemIds[_rng.Next(species.DropItemIds.Count)];
            Player.InventoryItemIds.Add(itemId);
            lines.Add($"Item drop: {itemId}");
        }
        else
        {
            lines.Add("No item dropped this time.");
        }

        PendingWildCapture = null;
        Phase = GamePhase.Explore;
        return CommandResult.Success("Declined capture.", lines);
    }

    private CommandResult EndExploreToShop()
    {
        if (Phase == GamePhase.WildReward)
            return CommandResult.Fail("Finish accept/decline first.");

        Phase = GamePhase.Shop;
        HealParty();
        RestockShop();
        var shop = ShopList();
        var lines = new List<string> { "Party fully healed for shop / PvP." };
        lines.Add(shop.Message);
        lines.AddRange(shop.Lines);
        return CommandResult.Success(
            $"Explore ended. Shop phase — Round {Round}. Assign your board (max {PlayerState.MaxBoardSize}), then 'ready'.",
            lines);
    }

    private void RestockShop()
    {
        _shopItemIds = _content.Items.Keys.OrderBy(_ => _rng.Next()).Take(3).ToList();
        _shopMoveIds = _content.Moves.Keys.OrderBy(_ => _rng.Next()).Take(3).ToList();
        _shopCreatureSpeciesIds = _content.Species.Values
            .Where(s => s.BuyGoldMin is not null && s.BuyGoldMax is not null)
            .Select(s => s.Id)
            .OrderBy(_ => _rng.Next())
            .Take(3)
            .ToList();
    }

    private CommandResult ShopList()
    {
        var lines = new List<string> { $"Gold: {Player.Gold}", "", "-- Items --" };
        foreach (var id in _shopItemIds)
        {
            var i = _content.Items[id];
            lines.Add($"  item {i.Id} — {i.Name} ({i.ShopCost}g): {i.Description}");
        }

        lines.Add("");
        lines.Add("-- Creatures --");
        foreach (var id in _shopCreatureSpeciesIds)
        {
            var s = _content.GetSpecies(id);
            lines.Add($"  creature {s.Id} — {s.Name} [{s.Type}] buy {s.BuyGoldMin}-{s.BuyGoldMax}g / sell {s.SellGoldMin}-{s.SellGoldMax}g");
        }

        lines.Add("");
        lines.Add("-- Moves --");
        foreach (var id in _shopMoveIds)
        {
            var m = _content.Moves[id];
            lines.Add($"  move {m.Id} — {m.Name} [{m.Type}] ({m.ShopCost}g) power {m.Power}: {m.Description}");
        }

        lines.Add("");
        lines.Add("Items inv: " + (Player.InventoryItemIds.Count == 0 ? "(empty)" : string.Join(", ", Player.InventoryItemIds)));
        lines.Add("Moves inv: " + (Player.InventoryMoveIds.Count == 0 ? "(empty)" : string.Join(", ", Player.InventoryMoveIds)));
        lines.Add(DescribeBoard());
        lines.Add("Buy: buy item <id> | buy creature <id> | buy move <id>");
        lines.Add("Also: give <creature> <item> | teach <creature> <move> | sell <creature> | evolve <creature> [intoId]");
        return CommandResult.Success("Shop", lines);
    }

    private CommandResult Buy(string[] args)
    {
        // buy item X | buy creature X | buy move X | buy X (auto)
        string kind;
        string id;
        if (args.Length >= 2 && args[0] is "item" or "creature" or "move")
        {
            kind = args[0].ToLowerInvariant();
            id = args[1];
        }
        else
        {
            id = args[0];
            kind = DetectShopKind(id) ?? "";
            if (kind.Length == 0)
                return CommandResult.Fail("Usage: buy item|creature|move <id> (must be in current shop stock).");
        }

        return kind switch
        {
            "item" => BuyItem(id),
            "creature" => BuyCreature(id),
            "move" => BuyMove(id),
            _ => CommandResult.Fail("Usage: buy item|creature|move <id>")
        };
    }

    private string? DetectShopKind(string id)
    {
        if (_shopItemIds.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase))) return "item";
        if (_shopCreatureSpeciesIds.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase))) return "creature";
        if (_shopMoveIds.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase))) return "move";
        return null;
    }

    private CommandResult BuyItem(string itemId)
    {
        if (!_shopItemIds.Any(x => x.Equals(itemId, StringComparison.OrdinalIgnoreCase)))
            return CommandResult.Fail($"Item '{itemId}' is not in today's shop. Type 'shop'.");
        if (!_content.Items.TryGetValue(itemId, out var item))
            return CommandResult.Fail($"Unknown item '{itemId}'.");
        if (Player.Gold < item.ShopCost)
            return CommandResult.Fail($"Need {item.ShopCost} gold (have {Player.Gold}).");

        Player.Gold -= item.ShopCost;
        Player.InventoryItemIds.Add(item.Id);
        _shopItemIds.RemoveAll(x => x.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
        return CommandResult.Success($"Bought {item.Name}. Gold left: {Player.Gold}");
    }

    private CommandResult BuyCreature(string speciesId)
    {
        if (!_shopCreatureSpeciesIds.Any(x => x.Equals(speciesId, StringComparison.OrdinalIgnoreCase)))
            return CommandResult.Fail($"Creature '{speciesId}' is not in today's shop. Type 'shop'.");

        var species = _content.GetSpecies(speciesId);
        if (species.BuyGoldMin is null || species.BuyGoldMax is null)
            return CommandResult.Fail($"{species.Name} cannot be bought.");

        var cost = RollRange(species.BuyGoldMin.Value, species.BuyGoldMax.Value);
        if (Player.Gold < cost)
            return CommandResult.Fail($"Need {cost} gold (have {Player.Gold}).");

        Player.Gold -= cost;
        var bought = _factory.Create(species.Id, level: Math.Max(1, Round));
        bought.Nickname = UniqueNickname(bought.Nickname);
        Player.Party.Add(bought);
        _shopCreatureSpeciesIds.RemoveAll(x => x.Equals(species.Id, StringComparison.OrdinalIgnoreCase));
        return CommandResult.Success(
            $"Bought {bought.Nickname} for {cost}g. Gold left: {Player.Gold}",
            new[] { bought.ToString() });
    }

    private CommandResult BuyMove(string moveId)
    {
        if (!_shopMoveIds.Any(x => x.Equals(moveId, StringComparison.OrdinalIgnoreCase)))
            return CommandResult.Fail($"Move '{moveId}' is not in today's shop. Type 'shop'.");
        if (!_content.Moves.TryGetValue(moveId, out var move))
            return CommandResult.Fail($"Unknown move '{moveId}'.");
        if (Player.Gold < move.ShopCost)
            return CommandResult.Fail($"Need {move.ShopCost} gold (have {Player.Gold}).");

        Player.Gold -= move.ShopCost;
        Player.InventoryMoveIds.Add(move.Id);
        _shopMoveIds.RemoveAll(x => x.Equals(move.Id, StringComparison.OrdinalIgnoreCase));
        return CommandResult.Success($"Bought move tome: {move.Name}. Use 'teach <creature> {move.Id}'. Gold left: {Player.Gold}");
    }

    private CommandResult SellCreature(string creatureName)
    {
        var creature = Player.FindCreatureByName(creatureName);
        if (creature is null)
            return CommandResult.Fail($"No creature named '{creatureName}'.");

        var species = _content.GetSpecies(creature.SpeciesId);
        if (species.SellGoldMin is null || species.SellGoldMax is null)
            return CommandResult.Fail($"{creature.Nickname} cannot be sold.");

        var payout = RollRange(species.SellGoldMin.Value, species.SellGoldMax.Value);
        for (var i = 0; i < Player.Board.Length; i++)
        {
            if (Player.Board[i] == creature.Id)
                Player.Board[i] = null;
        }

        if (creature.HeldItemId is not null)
            Player.InventoryItemIds.Add(creature.HeldItemId);

        Player.Party.Remove(creature);
        Player.Gold += payout;
        return CommandResult.Success($"Sold {creature.Nickname} for {payout}g. Gold: {Player.Gold}");
    }

    private int RollRange(int min, int max) =>
        max <= min ? min : _rng.Next(min, max + 1);

    private CommandResult Give(string creatureName, string itemId)
    {
        var creature = Player.FindCreatureByName(creatureName);
        if (creature is null)
            return CommandResult.Fail($"No creature named '{creatureName}'.");

        var invIndex = Player.InventoryItemIds.FindIndex(i => i.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (invIndex < 0)
            return CommandResult.Fail($"Item '{itemId}' not in inventory.");

        if (creature.HeldItemId is not null)
            Player.InventoryItemIds.Add(creature.HeldItemId);

        Player.InventoryItemIds.RemoveAt(invIndex);
        creature.HeldItemId = itemId;
        _factory.Refresh(creature);
        // Shop party is healed anyway, but keep current==max if they were full.
        if (Phase == GamePhase.Shop)
            _factory.HealFull(creature);

        return CommandResult.Success($"{creature.Nickname} now holds {itemId}. {creature}");
    }

    private CommandResult Teach(string creatureName, string moveId)
    {
        var creature = Player.FindCreatureByName(creatureName);
        if (creature is null)
            return CommandResult.Fail($"No creature named '{creatureName}'.");

        var invIndex = Player.InventoryMoveIds.FindIndex(m => m.Equals(moveId, StringComparison.OrdinalIgnoreCase));
        if (invIndex < 0)
            return CommandResult.Fail($"Move '{moveId}' not in move inventory. Buy one from the shop first.");

        if (!_content.Moves.TryGetValue(moveId, out var move))
            return CommandResult.Fail($"Unknown move '{moveId}'.");

        if (!_factory.CanLearnFromShop(creature, move.Id))
        {
            var species = _content.GetSpecies(creature.SpeciesId);
            return CommandResult.Fail(
                $"{creature.Nickname} cannot learn {move.Name} from the shop. Allowed: {string.Join(", ", species.ShopMoveIds)}");
        }

        if (creature.MoveIds.Any(m => m.Equals(move.Id, StringComparison.OrdinalIgnoreCase)))
            return CommandResult.Fail($"{creature.Nickname} already knows {move.Name}.");

        if (creature.MoveIds.Count >= Creature.MaxMoves)
            return CommandResult.Fail($"{creature.Nickname} already knows {Creature.MaxMoves} moves. (Forget-move not implemented yet.)");

        Player.InventoryMoveIds.RemoveAt(invIndex);
        creature.MoveIds.Add(move.Id);
        return CommandResult.Success(
            $"{creature.Nickname} learned {move.Name} ({move.Type})! Moves: {string.Join(", ", creature.MoveIds)}");
    }

    private CommandResult Board(string slotText, string creatureName)
    {
        if (!int.TryParse(slotText, out var slot) || slot < 1 || slot > PlayerState.MaxBoardSize)
            return CommandResult.Fail($"Slot must be 1-{PlayerState.MaxBoardSize}.");

        if (creatureName.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            Player.Board[slot - 1] = null;
            return CommandResult.Success($"Cleared board slot {slot}.");
        }

        var creature = Player.FindCreatureByName(creatureName);
        if (creature is null)
            return CommandResult.Fail($"No creature named '{creatureName}'. Use party nicknames.");

        for (var i = 0; i < Player.Board.Length; i++)
        {
            if (Player.Board[i] == creature.Id)
                Player.Board[i] = null;
        }

        Player.Board[slot - 1] = creature.Id;
        return CommandResult.Success($"Slot {slot} = {creature.Nickname}. {DescribeBoard()}");
    }

    private CommandResult Evolve(string[] args)
    {
        var creatureName = args[0];
        var intoHint = args.Length >= 2 ? args[1] : null;
        var creature = Player.FindCreatureByName(creatureName);
        if (creature is null)
            return CommandResult.Fail($"No creature named '{creatureName}'.");

        var species = _content.GetSpecies(creature.SpeciesId);
        var options = species.Evolutions ?? new List<EvolutionOption>();
        if (options.Count == 0)
            return CommandResult.Fail($"{creature.Nickname} cannot evolve further.");

        var eligible = options.Where(o => EvolutionEligible(creature, o)).ToList();

        if (intoHint is null)
        {
            if (eligible.Count == 1)
                return ApplyEvolution(creature, eligible[0]);

            var lines = new List<string> { $"Evolution options for {creature.Nickname}:" };
            foreach (var o in options)
            {
                var next = _content.GetSpecies(o.IntoId);
                var ok = EvolutionEligible(creature, o);
                var reqParts = new List<string> { $"lv>={(o.AtLevel?.ToString() ?? "?")}" };
                if (o.RequiresPlayerFlags is { Count: > 0 })
                    reqParts.Add("player:" + string.Join("+", o.RequiresPlayerFlags));
                if (o.RequiresCreatureFlags is { Count: > 0 })
                    reqParts.Add("creature:" + string.Join("+", o.RequiresCreatureFlags));
                lines.Add($"  {(ok ? "[ready]" : "[locked]")} {o.IntoId} ({next.Name}) — {string.Join(", ", reqParts)}");
            }

            if (eligible.Count == 0)
                lines.Add("None ready. Meet a path's level/flag, then: evolve <name> <intoId>");
            else
                lines.Add("Choose with: evolve <name> <intoId>");

            return CommandResult.Success("Choose evolution path.", lines);
        }

        var chosen = options.FirstOrDefault(o => o.IntoId.Equals(intoHint, StringComparison.OrdinalIgnoreCase));
        if (chosen is null)
            return CommandResult.Fail($"'{intoHint}' is not an evolution of {creature.Nickname}. Type 'evolve {creatureName}' to list paths.");

        if (!EvolutionEligible(creature, chosen))
            return CommandResult.Fail(DescribeEvolutionBlock(creature, chosen));

        return ApplyEvolution(creature, chosen);
    }

    private bool EvolutionEligible(Creature creature, EvolutionOption option)
    {
        if (option.AtLevel is int need && creature.Level < need)
            return false;
        if (option.RequiresPlayerFlags is { Count: > 0 } &&
            option.RequiresPlayerFlags.Any(f => !Player.Flags.Contains(f)))
            return false;
        if (option.RequiresCreatureFlags is { Count: > 0 } &&
            option.RequiresCreatureFlags.Any(f => !creature.Flags.Contains(f)))
            return false;
        return true;
    }

    private string DescribeEvolutionBlock(Creature creature, EvolutionOption option)
    {
        if (option.AtLevel is int need && creature.Level < need)
            return $"Needs level {need} (currently {creature.Level}).";
        if (option.RequiresPlayerFlags is { Count: > 0 })
        {
            var missing = option.RequiresPlayerFlags.Where(f => !Player.Flags.Contains(f)).ToList();
            if (missing.Count > 0)
                return $"Missing player flag(s): {string.Join(", ", missing)}";
        }

        if (option.RequiresCreatureFlags is { Count: > 0 })
        {
            var missing = option.RequiresCreatureFlags.Where(f => !creature.Flags.Contains(f)).ToList();
            if (missing.Count > 0)
                return $"Missing creature flag(s) on {creature.Nickname}: {string.Join(", ", missing)}";
        }

        return "Evolution requirements not met.";
    }

    private CommandResult ApplyEvolution(Creature creature, EvolutionOption option)
    {
        var next = _content.GetSpecies(option.IntoId);
        var keepHpRatio = creature.CombatStats.Hp <= 0
            ? 1.0
            : creature.CurrentHp / (double)creature.CombatStats.Hp;
        creature.SpeciesId = next.Id;
        creature.Nickname = UniqueNickname(next.Name);
        var learnedOnEvolve = _factory.TryLearnLevelMoves(creature);

        if (next.AbilityOptions.Count > 0 &&
            (creature.AbilityId is null ||
             next.AbilityOptions.All(o => !o.AbilityId.Equals(creature.AbilityId, StringComparison.OrdinalIgnoreCase))))
        {
            creature.AbilityId = next.AbilityOptions[_rng.Next(next.AbilityOptions.Count)].AbilityId;
        }

        _factory.Refresh(creature);
        creature.CurrentHp = Math.Max(0, (int)Math.Round(creature.CombatStats.Hp * keepHpRatio));
        if (Phase == GamePhase.Shop)
            _factory.HealFull(creature);

        var msg = $"Evolved into {next.Name} (stage {next.EvolutionStage})! {creature}";
        if (learnedOnEvolve.Count > 0)
            msg += $" Learned: {string.Join(", ", learnedOnEvolve)}.";
        return CommandResult.Success(msg);
    }

    private CommandResult ReadyBattle()
    {
        var fighters = GetPlayerFighters().Where(c => c.IsAlive).ToList();
        if (fighters.Count == 0)
            return CommandResult.Fail("Put at least one living creature on the board: board <1-6> <name>");

        Phase = GamePhase.Battle;
        HealParty();
        fighters = GetPlayerFighters().Where(c => c.IsAlive).ToList();

        var enemy = BuildAiBoard();
        var playerSide = fighters.Select(c => Combatant.FromCreature(c, _content.GetSpecies(c.SpeciesId), true)).ToList();
        var enemySide = enemy.Select(c => Combatant.FromCreature(c, _content.GetSpecies(c.SpeciesId), false)).ToList();

        var result = _battler.Resolve(playerSide, enemySide);
        ApplyHpFromCombatants(fighters, playerSide);
        AwardXp(fighters, enemy.Max(e => e.Level), result.Won);

        var lines = result.Log.Select(e => e.Message).ToList();
        lines.Add($"Enemy board was: {string.Join(", ", enemy.Select(e => $"{e.Nickname} Lv{e.Level}"))}");

        if (result.Won)
        {
            Player.Gold += 3 + Round;
            lines.Add($"You win the round! +{3 + Round} gold. Player HP still {Player.Hp}/{PlayerState.StartingHp}.");
            Player.Flags.Add("WonBattle");
        }
        else
        {
            Player.Hp -= result.DamageDealtToLoserHp;
            lines.Add($"You took {result.DamageDealtToLoserHp} player HP damage. Player HP: {Player.Hp}/{PlayerState.StartingHp}");
            if (Player.Hp <= 0)
            {
                Phase = GamePhase.GameOver;
                lines.Add("You were eliminated. Game over.");
                return CommandResult.Success("Game over.", lines);
            }
        }

        Round++;
        Phase = GamePhase.Explore;
        CurrentRoomId = "camp";
        ActiveWild = null;
        _lootedRoomsThisVisit.Clear();
        EnsureRoomFlags();
        HealParty();
        var loot = ApplyRoomEntryLoot(force: true).ToList();
        MaybeSpawnEncounter();
        lines.Add($"--- Round {Round} Explore begins (party healed for new explore cycle) ---");
        lines.AddRange(loot);
        lines.AddRange(Look().Lines);
        return CommandResult.Success("Battle complete.", lines);
    }

    private List<Creature> BuildAiBoard()
    {
        var pool = _content.Species.Values
            .Where(s => s.EvolutionStage <= Math.Min(3, 1 + Round / 2))
            .Select(s => s.Id)
            .ToList();
        if (pool.Count == 0)
            pool = _content.Species.Keys.ToList();

        var count = Math.Clamp(1 + Round / 2, 1, PlayerState.MaxBoardSize);
        var list = new List<Creature>();
        for (var i = 0; i < count; i++)
        {
            var id = pool[_rng.Next(pool.Count)];
            var level = Math.Max(1, Round + _rng.Next(0, 3));
            list.Add(_factory.Create(id, level));
        }

        return list;
    }

    /// <summary>Living fighters for explore wilds: living board first, else living party.</summary>
    private List<Creature> GetLivingExploreFighters()
    {
        var boardLiving = Player.GetBoardCreatures().Where(c => c.IsAlive).ToList();
        if (boardLiving.Count > 0) return boardLiving;
        return Player.Party.Where(c => c.IsAlive).Take(PlayerState.MaxBoardSize).ToList();
    }

    private List<Creature> GetPlayerFighters()
    {
        var board = Player.GetBoardCreatures().ToList();
        if (board.Count > 0) return board;
        return Player.Party.Take(PlayerState.MaxBoardSize).ToList();
    }

    private void ApplyHpFromCombatants(IEnumerable<Creature> creatures, List<Combatant> side)
    {
        foreach (var c in creatures)
        {
            var match = side.FirstOrDefault(s => s.InstanceId == c.Id);
            if (match is not null)
                c.CurrentHp = match.CurrentHp;
        }
    }

    private void AwardXp(IEnumerable<Creature> creatures, int foeLevel, bool won)
    {
        foreach (var c in creatures)
        {
            var nature = _content.GetNature(c.NatureId);
            var baseXp = Math.Max(1, foeLevel * 3 + (won ? 4 : 1));
            var gained = Math.Max(1, (int)Math.Round(baseXp * nature.XpGrowth));
            c.Xp += gained;
            while (c.Xp >= c.XpToNextLevel())
            {
                c.Xp -= c.XpToNextLevel();
                c.Level++;
                _factory.Refresh(c);
                _factory.TryLearnLevelMoves(c);
            }

            _factory.Refresh(c);
        }
    }

    private void HealParty()
    {
        foreach (var c in Player.Party)
        {
            _factory.Refresh(c);
            _factory.HealFull(c);
        }
    }

    private string DescribeBoard()
    {
        var parts = new List<string>();
        for (var i = 0; i < Player.Board.Length; i++)
        {
            var id = Player.Board[i];
            if (id is null)
            {
                parts.Add($"{i + 1}:empty");
                continue;
            }

            var c = Player.FindCreature(id.Value);
            parts.Add($"{i + 1}:{(c is null ? "?" : $"{c.Nickname}({c.CurrentHp}/{c.CombatStats.Hp})")}");
        }

        return "Board [" + string.Join(" | ", parts) + "]";
    }

    private CommandResult Party()
    {
        if (Player.Party.Count == 0)
            return CommandResult.Success("Party empty.");

        var lines = Player.Party.SelectMany(c =>
        {
            var sp = _content.GetSpecies(c.SpeciesId);
            var ability = c.AbilityId is null ? null : _content.TryGetAbility(c.AbilityId);
            return new[]
            {
                $"  {c} | {sp.Type} | nature={c.NatureId} ability={ability?.Name ?? "-"}",
                $"      IVs A{c.Potential.Atk}/D{c.Potential.Def}/S{c.Potential.Spd}/H{c.Potential.Hp} " +
                $"XP {c.Xp}/{c.XpToNextLevel()} stage={sp.EvolutionStage} item={c.HeldItemId ?? "-"}" +
                (c.IsAlive ? "" : " [FAINTED]"),
                $"      Moves: {string.Join(", ", c.MoveIds.Select(FormatKnownMove))}",
                $"      Flags: {(c.Flags.Count == 0 ? "(none)" : string.Join(", ", c.Flags.OrderBy(f => f)))}"
            };
        }).ToList();
        return CommandResult.Success("Party:", lines);
    }

    private string FormatKnownMove(string moveId) =>
        _content.Moves.TryGetValue(moveId, out var m) ? $"{m.Name}[{m.Type}]" : moveId;

    private CommandResult Status()
    {
        var lines = new List<string>
        {
            $"Phase={Phase} Round={Round} Seed={Seed}",
            $"Player HP={Player.Hp}/{PlayerState.StartingHp} Gold={Player.Gold}",
            $"Room={CurrentRoomId} Encounter={ActiveWild?.Nickname ?? "none"}",
            DescribeBoard(),
            $"Player flags: {(Player.Flags.Count == 0 ? "(none)" : string.Join(", ", Player.Flags.OrderBy(f => f)))}"
        };
        return CommandResult.Success("Status", lines);
    }

    private string UniqueNickname(string desired)
    {
        if (Player.FindCreatureByName(desired) is null)
            return desired;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{desired}{i}";
            if (Player.FindCreatureByName(candidate) is null)
                return candidate;
        }

        return $"{desired}-{Guid.NewGuid().ToString()[..4]}";
    }

    private static CommandResult Help() => CommandResult.Success("Commands", new[]
    {
        "Start: starters | pick <1-3>",
        "Explore: look | go <room> | fight | party | end",
        "After wild win: accept | decline",
        "Shop: shop | buy item|creature|move <id> | sell <creature> | give <creature> <item>",
        "      teach <creature> <move> | board <1-6> <creature|clear> | evolve <creature> [intoId] | ready",
        "Anytime: status | help | seed <n>",
        "Edit content in src/GameCore/Content/*.json (species use evolutions[] + buy/sell gold ranges)."
    });
}
