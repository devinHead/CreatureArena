using GameCore.Models;

namespace GameCore.Combat;

public sealed class Combatant
{
    public Guid InstanceId { get; init; }
    public string Name { get; init; } = "";
    public string SpeciesId { get; init; } = "";
    public int EvolutionStage { get; init; } = 1;
    public int Level { get; init; } = 1;
    public StatBlock Stats { get; init; } = new();
    public int CurrentHp { get; set; }
    public List<string> MoveIds { get; init; } = new();
    public bool IsPlayer { get; init; }

    public bool IsAlive => CurrentHp > 0;

    public static Combatant FromCreature(Creature creature, SpeciesDef species, bool isPlayer) => new()
    {
        InstanceId = creature.Id,
        Name = creature.Nickname,
        SpeciesId = creature.SpeciesId,
        EvolutionStage = species.EvolutionStage,
        Level = creature.Level,
        Stats = creature.CombatStats.Clone(),
        CurrentHp = creature.CurrentHp,
        MoveIds = creature.MoveIds.ToList(),
        IsPlayer = isPlayer
    };
}
