using GameCore.Content;
using GameCore.Models;

namespace GameCore.Combat;

/// <summary>
/// Speed-ordered auto-battler. After each death the turn order is rebuilt.
/// Loser HP damage = sum of winner survivors' EvolutionStage.
/// </summary>
public sealed class AutoBattleResolver : IBattleResolver
{
    private readonly ContentCatalog _content;
    private readonly Random _rng;
    private const int MaxRounds = 80;

    public AutoBattleResolver(ContentCatalog content, Random rng)
    {
        _content = content;
        _rng = rng;
    }

    public BattleSideResult Resolve(IReadOnlyList<Combatant> playerSide, IReadOnlyList<Combatant> enemySide) =>
        ResolveInternal(playerSide, enemySide, writeLog: true, syncHp: true);

    public double EstimateWinRate(
        IReadOnlyList<Combatant> playerSide,
        IReadOnlyList<Combatant> enemySide,
        int simulations = 24)
    {
        if (playerSide.Count == 0) return 0;
        if (enemySide.Count == 0) return 1;

        var wins = 0;
        for (var i = 0; i < simulations; i++)
        {
            var p = playerSide.Select(Clone).ToList();
            var e = enemySide.Select(Clone).ToList();
            if (ResolveInternal(p, e, writeLog: false, syncHp: false).Won)
                wins++;
        }

        return wins / (double)simulations;
    }

    private BattleSideResult ResolveInternal(
        IReadOnlyList<Combatant> playerSide,
        IReadOnlyList<Combatant> enemySide,
        bool writeLog,
        bool syncHp)
    {
        var player = playerSide.Select(Clone).ToList();
        var enemy = enemySide.Select(Clone).ToList();
        var log = writeLog ? new List<BattleEvent>() : null;
        var participants = player.Select(c => c.InstanceId).Concat(enemy.Select(c => c.InstanceId)).Distinct().ToList();

        if (writeLog)
        {
            log!.Add(Msg("=== Auto-battle start ==="));
            log.Add(Msg($"You: {Summarize(player)}"));
            log.Add(Msg($"Foe: {Summarize(enemy)}"));
        }

        var safety = 0;
        while (player.Any(c => c.IsAlive) && enemy.Any(c => c.IsAlive) && safety++ < MaxRounds)
        {
            var order = player.Where(c => c.IsAlive)
                .Concat(enemy.Where(c => c.IsAlive))
                .OrderByDescending(c => c.Stats.Spd)
                .ThenBy(_ => _rng.Next())
                .ToList();

            foreach (var actor in order)
            {
                if (!actor.IsAlive) continue;
                var foes = actor.IsPlayer ? enemy : player;
                if (!foes.Any(c => c.IsAlive)) break;

                Act(actor, foes, log);
                if (!player.Any(c => c.IsAlive) || !enemy.Any(c => c.IsAlive))
                    break;
            }
        }

        var playerWon = player.Any(c => c.IsAlive) && !enemy.Any(c => c.IsAlive);
        var survivors = playerWon ? player.Where(c => c.IsAlive) : enemy.Where(c => c.IsAlive);
        var damage = survivors.Sum(c => c.EvolutionStage);

        if (writeLog)
        {
            log!.Add(Msg(playerWon
                ? $"You win! Survivor evolution-stage total: {damage}."
                : $"You lose! Foe survivor evolution-stage total: {damage}."));
        }

        if (syncHp)
        {
            SyncHp(playerSide, player);
            SyncHp(enemySide, enemy);
        }

        return new BattleSideResult
        {
            Won = playerWon,
            DamageDealtToLoserHp = damage,
            ParticipantIds = participants,
            Log = log ?? new List<BattleEvent>()
        };
    }

    private void Act(Combatant actor, List<Combatant> foes, List<BattleEvent>? log)
    {
        var moveId = PickMove(actor);
        if (!_content.Moves.TryGetValue(moveId, out var move))
            move = new MoveDef { Id = "tackle", Name = "Tackle", Power = 5, Target = MoveTarget.EnemyFront };

        if (move.Target == MoveTarget.Self)
        {
            if (actor.Stats.Def >= actor.Stats.Atk + 6)
            {
                moveId = actor.MoveIds.FirstOrDefault(id =>
                    _content.Moves.TryGetValue(id, out var m) && m.Target != MoveTarget.Self) ?? "tackle";
                if (!_content.Moves.TryGetValue(moveId, out move))
                    move = new MoveDef { Id = "tackle", Name = "Tackle", Power = 5, Target = MoveTarget.EnemyFront };
            }
            else
            {
                actor.Stats.Def += 1;
                log?.Add(Msg($"{actor.Name} uses {move.Name}! Defense rises."));
                return;
            }
        }

        var livingFoes = foes.Where(c => c.IsAlive).ToList();
        if (livingFoes.Count == 0) return;

        var target = move.Target == MoveTarget.EnemyRandom
            ? livingFoes[_rng.Next(livingFoes.Count)]
            : livingFoes[0];

        var stab = TypeChart.Stab(actor.Type, move.Type);
        var effectiveness = TypeChart.Effectiveness(move.Type, target.Type);
        var raw = actor.Stats.Atk + move.Power - target.Stats.Def;
        var damage = Math.Max(1, (int)Math.Round(Math.Max(1, raw) * stab * effectiveness));
        target.CurrentHp = Math.Max(0, target.CurrentHp - damage);

        var tags = new List<string>();
        if (stab > 1.01) tags.Add("STAB");
        var effLabel = TypeChart.EffectivenessLabel(effectiveness);
        if (effLabel.Length > 0) tags.Add(effLabel);
        var tagText = tags.Count > 0 ? $" [{string.Join("; ", tags)}]" : "";

        log?.Add(Msg(
            $"{actor.Name} uses {move.Name} ({move.Type}) on {target.Name} for {damage} dmg.{tagText} " +
            $"({target.CurrentHp}/{target.Stats.Hp} HP)"));
        if (!target.IsAlive)
            log?.Add(Msg($"{target.Name} is defeated!"));
    }

    private string PickMove(Combatant actor)
    {
        if (actor.MoveIds.Count == 0) return "tackle";

        var attacks = actor.MoveIds
            .Where(id => _content.Moves.TryGetValue(id, out var m) && m.Target != MoveTarget.Self)
            .ToList();
        var buffs = actor.MoveIds
            .Where(id => _content.Moves.TryGetValue(id, out var m) && m.Target == MoveTarget.Self)
            .ToList();

        if (attacks.Count > 0 && (buffs.Count == 0 || _rng.NextDouble() < 0.75))
            return attacks[_rng.Next(attacks.Count)];
        if (buffs.Count > 0)
            return buffs[_rng.Next(buffs.Count)];
        return actor.MoveIds[_rng.Next(actor.MoveIds.Count)];
    }

    private static void SyncHp(IReadOnlyList<Combatant> original, List<Combatant> working)
    {
        foreach (var o in original)
        {
            var w = working.FirstOrDefault(x => x.InstanceId == o.InstanceId);
            if (w is not null) o.CurrentHp = w.CurrentHp;
        }
    }

    private static Combatant Clone(Combatant c) => new()
    {
        InstanceId = c.InstanceId,
        Name = c.Name,
        SpeciesId = c.SpeciesId,
        Type = c.Type,
        EvolutionStage = c.EvolutionStage,
        Level = c.Level,
        Stats = c.Stats.Clone(),
        CurrentHp = c.CurrentHp,
        MoveIds = c.MoveIds.ToList(),
        IsPlayer = c.IsPlayer
    };

    private static string Summarize(IEnumerable<Combatant> side) =>
        string.Join(", ", side.Select(c => $"{c.Name}({c.Stats.Spd}spd)"));

    private static BattleEvent Msg(string m) => new() { Message = m };
}
