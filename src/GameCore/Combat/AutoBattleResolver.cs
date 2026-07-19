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
    private const int MaxRounds = 200;

    public AutoBattleResolver(ContentCatalog content, Random rng)
    {
        _content = content;
        _rng = rng;
    }

    public BattleSideResult Resolve(IReadOnlyList<Combatant> playerSide, IReadOnlyList<Combatant> enemySide)
    {
        var player = playerSide.Select(Clone).ToList();
        var enemy = enemySide.Select(Clone).ToList();
        var log = new List<BattleEvent>();
        var participants = player.Select(c => c.InstanceId).Concat(enemy.Select(c => c.InstanceId)).Distinct().ToList();

        log.Add(Msg("=== Auto-battle start ==="));
        log.Add(Msg($"You: {Summarize(player)}"));
        log.Add(Msg($"Foe: {Summarize(enemy)}"));

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
                var allies = actor.IsPlayer ? player : enemy;
                var foes = actor.IsPlayer ? enemy : player;
                if (!foes.Any(c => c.IsAlive)) break;

                Act(actor, allies, foes, log);
                if (!player.Any(c => c.IsAlive) || !enemy.Any(c => c.IsAlive))
                    break;
            }
        }

        var playerWon = player.Any(c => c.IsAlive) && !enemy.Any(c => c.IsAlive);
        var survivors = playerWon ? player.Where(c => c.IsAlive) : enemy.Where(c => c.IsAlive);
        var damage = survivors.Sum(c => c.EvolutionStage);

        log.Add(Msg(playerWon
            ? $"You win! Opponent takes {damage} HP damage (sum of your survivors' evolution stages)."
            : $"You lose! You take {damage} HP damage (sum of foe survivors' evolution stages)."));

        // Write remaining HP back is handled by caller via participant lists if needed;
        // sync HP onto originals by InstanceId for player's creatures.
        SyncHp(playerSide, player);
        SyncHp(enemySide, enemy);

        return new BattleSideResult
        {
            Won = playerWon,
            DamageDealtToLoserHp = damage,
            ParticipantIds = participants,
            Log = log
        };
    }

    private void Act(Combatant actor, List<Combatant> allies, List<Combatant> foes, List<BattleEvent> log)
    {
        var moveId = actor.MoveIds.Count == 0 ? "tackle" : actor.MoveIds[_rng.Next(actor.MoveIds.Count)];
        if (!_content.Moves.TryGetValue(moveId, out var move))
            move = new MoveDef { Id = "tackle", Name = "Tackle", Power = 5, Target = MoveTarget.EnemyFront };

        if (move.Target == MoveTarget.Self)
        {
            actor.Stats.Def += 1;
            log.Add(Msg($"{actor.Name} uses {move.Name}! Defense rises."));
            return;
        }

        var livingFoes = foes.Where(c => c.IsAlive).ToList();
        if (livingFoes.Count == 0) return;

        Combatant target = move.Target == MoveTarget.EnemyRandom
            ? livingFoes[_rng.Next(livingFoes.Count)]
            : livingFoes.OrderByDescending(c => c.Stats.Spd).First(); // "front" ≈ highest threat / first in speed for now

        // Prefer first living as front line: use list order as board order.
        if (move.Target == MoveTarget.EnemyFront)
            target = livingFoes[0];

        var damage = Math.Max(1, actor.Stats.Atk + move.Power - target.Stats.Def / 2);
        target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
        log.Add(Msg($"{actor.Name} uses {move.Name} on {target.Name} for {damage} dmg. ({target.CurrentHp}/{target.Stats.Hp} HP)"));
        if (!target.IsAlive)
            log.Add(Msg($"{target.Name} is defeated!"));
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
