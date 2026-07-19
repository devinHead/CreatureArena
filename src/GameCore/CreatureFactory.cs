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
            StartingStats = RollStarting(),
            Potential = RollPotential(),
            NatureId = nature.Id,
            MoveIds = species.MoveIds.ToList()
        };

        Refresh(creature);
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

    private NatureDef PickRandomNature()
    {
        var list = _content.Natures.Values.ToList();
        return list[_rng.Next(list.Count)];
    }

    private StatBlock RollStarting() => new()
    {
        Hp = _rng.Next(0, 6),
        Atk = _rng.Next(0, 6),
        Def = _rng.Next(0, 6),
        Spd = _rng.Next(0, 6)
    };

    private StatBlock RollPotential() => new()
    {
        Hp = _rng.Next(0, 16),
        Atk = _rng.Next(0, 16),
        Def = _rng.Next(0, 16),
        Spd = _rng.Next(0, 16)
    };
}
