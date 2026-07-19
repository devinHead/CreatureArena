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
    public GamePhase Phase { get; private set; } = GamePhase.Explore;
    public int Round { get; private set; } = 1;
    public int Seed { get; private set; }
    public string CurrentRoomId { get; private set; } = "camp";
    public string? ActiveEncounterSpeciesId { get; private set; }
    public bool RoomLootTaken { get; private set; }
    public Creature? PendingWildCapture { get; private set; }

    public GameSession(ContentCatalog? content = null, int? seed = null)
    {
        _content = content ?? ContentCatalog.Load();
        Seed = seed ?? Environment.TickCount;
        _rng = new Random(Seed);
        _factory = new CreatureFactory(_content, _rng);
        _battler = new AutoBattleResolver(_content, _rng);

        var starter = _factory.Create("slime", level: 2, nickname: "Buddy");
        Player.Party.Add(starter);
        Player.Board[0] = starter.Id;
        EnsureRoomFlags();
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
            GamePhase.Explore => HandleExplore(cmd, args),
            GamePhase.WildReward => HandleWildReward(cmd, args),
            GamePhase.Shop => HandleShop(cmd, args),
            GamePhase.Battle => CommandResult.Fail("Battle resolves automatically. Use 'ready' from shop."),
            _ => CommandResult.Fail("Unknown phase.")
        };
    }

    private CommandResult HandleExplore(string cmd, string[] args)
    {
        return cmd switch
        {
            "look" => Look(),
            "go" when args.Length >= 1 => Go(args[0]),
            "fight" => FightWild(),
            "take" => TakeLoot(),
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
            "buy" when args.Length >= 1 => Buy(args[0]),
            "give" when args.Length >= 2 => Give(args[0], args[1]),
            "board" when args.Length >= 2 => Board(args[0], args[1]),
            "evolve" when args.Length >= 1 => Evolve(args[0]),
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
            $"Exits: {string.Join(", ", room.Exits)}",
            ActiveEncounterSpeciesId is null
                ? "No wild creature in sight."
                : $"Wild encounter: {_content.GetSpecies(ActiveEncounterSpeciesId).Name} ({ActiveEncounterSpeciesId}). Type 'fight'.",
            RoomLootTaken ? "Loot already taken." : $"Possible loot: gold {room.GoldLootMin}-{room.GoldLootMax}" +
                (room.ItemLootIds.Count > 0 ? $", items ({string.Join("/", room.ItemLootIds)})" : "")
        };
        return CommandResult.Success($"Round {Round} | Explore", lines);
    }

    private CommandResult Go(string roomId)
    {
        var room = _content.GetRoom(CurrentRoomId);
        if (!room.Exits.Any(e => e.Equals(roomId, StringComparison.OrdinalIgnoreCase)))
            return CommandResult.Fail($"Can't go to '{roomId}' from here. Exits: {string.Join(", ", room.Exits)}");

        CurrentRoomId = room.Exits.First(e => e.Equals(roomId, StringComparison.OrdinalIgnoreCase));
        RoomLootTaken = false;
        ActiveEncounterSpeciesId = null;
        EnsureRoomFlags();
        MaybeSpawnEncounter();
        return Look();
    }

    private void EnsureRoomFlags()
    {
        var room = _content.GetRoom(CurrentRoomId);
        if (!string.IsNullOrEmpty(room.VisitFlag))
            Player.Flags.Add(room.VisitFlag);
    }

    private void MaybeSpawnEncounter()
    {
        var room = _content.GetRoom(CurrentRoomId);
        if (room.EncounterSpeciesIds.Count == 0) return;
        if (_rng.NextDouble() > room.EncounterChance) return;
        ActiveEncounterSpeciesId = room.EncounterSpeciesIds[_rng.Next(room.EncounterSpeciesIds.Count)];
    }

    private CommandResult TakeLoot()
    {
        if (RoomLootTaken)
            return CommandResult.Fail("Nothing left to take here.");

        var room = _content.GetRoom(CurrentRoomId);
        RoomLootTaken = true;
        var gold = room.GoldLootMax <= room.GoldLootMin
            ? room.GoldLootMin
            : _rng.Next(room.GoldLootMin, room.GoldLootMax + 1);
        Player.Gold += gold;
        var lines = new List<string> { $"Found {gold} gold. (Total {Player.Gold})" };
        if (room.ItemLootIds.Count > 0 && _rng.NextDouble() < 0.5)
        {
            var itemId = room.ItemLootIds[_rng.Next(room.ItemLootIds.Count)];
            Player.InventoryItemIds.Add(itemId);
            lines.Add($"Found item: {itemId}");
        }

        return CommandResult.Success("Loot taken.", lines);
    }

    private CommandResult FightWild()
    {
        if (ActiveEncounterSpeciesId is null)
            return CommandResult.Fail("No wild encounter here. Try 'go' to another room.");

        var fighters = GetPlayerFighters();
        if (fighters.Count == 0)
            return CommandResult.Fail("You need at least one creature in your party.");

        foreach (var c in fighters) _factory.HealFull(c);

        var wildLevel = Math.Max(1, Round + _rng.Next(0, 2));
        var wild = _factory.Create(ActiveEncounterSpeciesId, wildLevel);
        var wildSpecies = _content.GetSpecies(wild.SpeciesId);

        var playerSide = fighters.Select(c => Combatant.FromCreature(c, _content.GetSpecies(c.SpeciesId), true)).ToList();
        var enemySide = new List<Combatant> { Combatant.FromCreature(wild, wildSpecies, false) };

        var result = _battler.Resolve(playerSide, enemySide);
        ApplyHpFromCombatants(fighters, playerSide);
        AwardXp(fighters, wild.Level, result.Won);

        var lines = result.Log.Select(e => e.Message).ToList();

        if (!result.Won)
        {
            ActiveEncounterSpeciesId = null;
            lines.Add("The wild creature got away. You earned some XP.");
            return CommandResult.Success("Wild fight lost.", lines);
        }

        PendingWildCapture = wild;
        Phase = GamePhase.WildReward;
        ActiveEncounterSpeciesId = null;
        lines.Add($"Victory! Accept {wild.Nickname} into your party, or decline for a bonus.");
        lines.Add($"Wild rolled nature={wild.NatureId}, potential Atk={wild.Potential.Atk}/Def={wild.Potential.Def}/Spd={wild.Potential.Spd}/Hp={wild.Potential.Hp}");
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
        Player.Party.Add(wild);
        var lines = new List<string> { $"{wild.Nickname} joined your party! ({Player.Party.Count} creatures)" };

        if (species.DropItemIds.Count > 0 && _rng.NextDouble() < species.AcceptAlsoItemChance)
        {
            var itemId = species.DropItemIds[_rng.Next(species.DropItemIds.Count)];
            Player.InventoryItemIds.Add(itemId);
            lines.Add($"Lucky! Also received item: {itemId}");
        }

        PendingWildCapture = null;
        Phase = GamePhase.Explore;
        lines.Add("Back to explore. Type 'look' or 'end' when ready for shop.");
        return CommandResult.Success("Creature accepted.", lines);
    }

    private CommandResult DeclineWild()
    {
        if (PendingWildCapture is null)
            return CommandResult.Fail("No pending capture.");

        var wild = PendingWildCapture;
        var species = _content.GetSpecies(wild.SpeciesId);
        Player.Gold += species.DeclineGoldBonus;
        var lines = new List<string>
        {
            $"Declined. +{species.DeclineGoldBonus} gold (total {Player.Gold})."
        };

        if (species.DropItemIds.Count > 0 && _rng.NextDouble() < species.DeclineItemChance)
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
        lines.Add("Back to explore. Type 'look' or 'end' when ready for shop.");
        return CommandResult.Success("Declined capture.", lines);
    }

    private CommandResult EndExploreToShop()
    {
        if (Phase == GamePhase.WildReward)
            return CommandResult.Fail("Finish accept/decline first.");

        Phase = GamePhase.Shop;
        HealParty();
        var shop = ShopList();
        var lines = new List<string> { shop.Message };
        lines.AddRange(shop.Lines);
        return CommandResult.Success(
            $"Explore ended. Shop phase — Round {Round}. Assign your board (max {PlayerState.MaxBoardSize}), then 'ready'.",
            lines);
    }

    private CommandResult ShopList()
    {
        var lines = _content.Items.Values
            .Select(i => $"  {i.Id} — {i.Name} ({i.ShopCost}g): {i.Description}")
            .ToList();
        lines.Insert(0, $"Gold: {Player.Gold}");
        lines.Add("Inventory: " + (Player.InventoryItemIds.Count == 0 ? "(empty)" : string.Join(", ", Player.InventoryItemIds)));
        lines.Add(DescribeBoard());
        return CommandResult.Success("Shop", lines);
    }

    private CommandResult Buy(string itemId)
    {
        if (!_content.Items.TryGetValue(itemId, out var item))
            return CommandResult.Fail($"Unknown item '{itemId}'.");
        if (Player.Gold < item.ShopCost)
            return CommandResult.Fail($"Need {item.ShopCost} gold (have {Player.Gold}).");

        Player.Gold -= item.ShopCost;
        Player.InventoryItemIds.Add(item.Id);
        return CommandResult.Success($"Bought {item.Name}. Gold left: {Player.Gold}");
    }

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
        return CommandResult.Success($"{creature.Nickname} now holds {itemId}. Stats refreshed: {creature}");
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

    private CommandResult Evolve(string creatureName)
    {
        var creature = Player.FindCreatureByName(creatureName);
        if (creature is null)
            return CommandResult.Fail($"No creature named '{creatureName}'.");

        var species = _content.GetSpecies(creature.SpeciesId);
        if (string.IsNullOrEmpty(species.EvolvesIntoId))
            return CommandResult.Fail($"{creature.Nickname} cannot evolve further.");

        if (species.EvolveAtLevel is int needLevel && creature.Level < needLevel)
            return CommandResult.Fail($"Needs level {needLevel} (currently {creature.Level}).");

        if (!string.IsNullOrEmpty(species.EvolveRequiresFlag) && !Player.Flags.Contains(species.EvolveRequiresFlag))
            return CommandResult.Fail($"Missing flag: {species.EvolveRequiresFlag}");

        var next = _content.GetSpecies(species.EvolvesIntoId);
        creature.SpeciesId = next.Id;
        creature.Nickname = next.Name;
        creature.MoveIds = next.MoveIds.ToList();
        _factory.Refresh(creature);
        _factory.HealFull(creature);
        return CommandResult.Success($"{creatureName} evolved into {next.Name} (stage {next.EvolutionStage})! {creature}");
    }

    private CommandResult ReadyBattle()
    {
        var fighters = GetPlayerFighters();
        if (fighters.Count == 0)
            return CommandResult.Fail("Put at least one creature on the board: board <1-6> <name>");

        Phase = GamePhase.Battle;
        HealParty();
        fighters = GetPlayerFighters().ToList();

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
            // AI "loses" HP conceptually; player gains a little gold.
            Player.Gold += 3 + Round;
            lines.Add($"You win the round! +{3 + Round} gold. Player HP still {Player.Hp}/{PlayerState.StartingHp}.");
            Player.Flags.Add("WonBattle");
        }
        else
        {
            Player.Hp -= result.DamageDealtToLoserHp;
            lines.Add($"You took {result.DamageDealtToLoserHp} damage. Player HP: {Player.Hp}/{PlayerState.StartingHp}");
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
        RoomLootTaken = false;
        ActiveEncounterSpeciesId = null;
        MaybeSpawnEncounter();
        HealParty();
        lines.Add($"--- Round {Round} Explore begins at Camp Clearing ---");
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
            var c = _factory.Create(id, level);
            list.Add(c);
        }

        return list;
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
            parts.Add($"{i + 1}:{(c is null ? "?" : c.Nickname)}");
        }

        return "Board [" + string.Join(" | ", parts) + "]";
    }

    private CommandResult Party()
    {
        if (Player.Party.Count == 0)
            return CommandResult.Success("Party empty.");

        var lines = Player.Party.Select(c =>
        {
            var sp = _content.GetSpecies(c.SpeciesId);
            return $"  {c} | nature={c.NatureId} potential=A{c.Potential.Atk}/D{c.Potential.Def}/S{c.Potential.Spd}/H{c.Potential.Hp} " +
                   $"start=A{c.StartingStats.Atk}/D{c.StartingStats.Def}/S{c.StartingStats.Spd}/H{c.StartingStats.Hp} " +
                   $"XP {c.Xp}/{c.XpToNextLevel()} stage={sp.EvolutionStage} item={c.HeldItemId ?? "-"}";
        }).ToList();
        return CommandResult.Success("Party:", lines);
    }

    private CommandResult Status()
    {
        var lines = new List<string>
        {
            $"Phase={Phase} Round={Round} Seed={Seed}",
            $"Player HP={Player.Hp}/{PlayerState.StartingHp} Gold={Player.Gold}",
            $"Room={CurrentRoomId} Encounter={ActiveEncounterSpeciesId ?? "none"}",
            DescribeBoard(),
            $"Flags: {(Player.Flags.Count == 0 ? "(none)" : string.Join(", ", Player.Flags.OrderBy(f => f)))}"
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
        "Explore: look | go <room> | fight | take | party | end",
        "After wild win: accept | decline",
        "Shop: shop | buy <item> | give <creature> <item> | board <1-6> <creature|clear> | evolve <creature> | ready",
        "Anytime: status | help | seed <n>",
        "Tip: edit src/GameCore/Content/*.json to add species/stats."
    });
}
