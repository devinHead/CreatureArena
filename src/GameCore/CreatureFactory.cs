using GameCore.Content;
using GameCore.Models;

namespace GameCore;

public sealed class CreatureFactory
{
    private readonly ContentCatalog _content;
    private readonly Random _rng;

    public CreatureFactory(ContentCatalog content, Random rng)
    {
        _content = content;
        _rng = rng;
    }

    public Creature Create(string speciesId, int level = 1, string? nickname = null)
    {
        var species = _content.GetSpecies(speciesId);
        var nature = PickRandomNature();
        var creature = new Creature
        {
            SpeciesId = species.Id,
            Nickname = nickname ?? species.Name,
            Level = Math.Max(1, level),
            Xp = 0,
            Potential = RollPotential(),
            NatureId = nature.Id,
            AbilityId = RollAbility(species),
            MoveIds = MovesKnownAtLevel(species, Math.Max(1, level))
        };

        Refresh(creature);
        HealFull(creature);
        return creature;
    }

    public void Refresh(Creature creature)
    {
        var species = _content.GetSpecies(creature.SpeciesId);
        var nature = _content.GetNature(creature.NatureId);
        var item = creature.HeldItemId is null ? null : _content.TryGetItem(creature.HeldItemId);
        creature.RecalculateStats(species, nature, item);
    }

    public void HealFull(Creature creature) => creature.CurrentHp = creature.CombatStats.Hp;

    /// <summary>
    /// Learns any learnset moves newly available at the creature's current level.
    /// Returns names of moves learned (empty if none / no room).
    /// </summary>
    public List<string> TryLearnLevelMoves(Creature creature)
    {
        var species = _content.GetSpecies(creature.SpeciesId);
        var learned = new List<string>();
        foreach (var entry in species.Learnset.OrderBy(e => e.Level))
        {
            if (entry.Level > creature.Level) continue;
            if (creature.MoveIds.Contains(entry.MoveId, StringComparer.OrdinalIgnoreCase)) continue;
            if (creature.MoveIds.Count >= Creature.MaxMoves) break;
            if (!_content.Moves.ContainsKey(entry.MoveId)) continue;

            creature.MoveIds.Add(entry.MoveId);
            var moveName = _content.Moves[entry.MoveId].Name;
            learned.Add(moveName);
        }

        return learned;
    }

    public static List<string> MovesKnownAtLevel(SpeciesDef species, int level) =>
        species.Learnset
            .Where(e => e.Level <= level)
            .OrderBy(e => e.Level)
            .Select(e => e.MoveId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Creature.MaxMoves)
            .ToList();

    public bool CanLearnFromShop(Creature creature, string moveId)
    {
        var species = _content.GetSpecies(creature.SpeciesId);
        return species.ShopMoveIds.Any(m => m.Equals(moveId, StringComparison.OrdinalIgnoreCase));
    }

    private string? RollAbility(SpeciesDef species)
    {
        if (species.AbilityOptions.Count == 0)
            return null;

        var total = species.AbilityOptions.Sum(o => Math.Max(0, o.Weight));
        if (total <= 0)
            return species.AbilityOptions[0].AbilityId;

        var roll = _rng.NextDouble() * total;
        foreach (var option in species.AbilityOptions)
        {
            roll -= Math.Max(0, option.Weight);
            if (roll <= 0)
                return option.AbilityId;
        }

        return species.AbilityOptions[^1].AbilityId;
    }

    private NatureDef PickRandomNature()
    {
        var list = _content.Natures.Values.ToList();
        return list[_rng.Next(list.Count)];
    }

    private StatBlock RollPotential() => new()
    {
        Hp = _rng.Next(0, 16),
        Atk = _rng.Next(0, 16),
        Def = _rng.Next(0, 16),
        Spd = _rng.Next(0, 16)
    };
}
