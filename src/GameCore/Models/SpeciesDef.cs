namespace GameCore.Models;

public sealed class SpeciesDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public StatBlock BaseStats { get; set; } = new();
    /// <summary>Evolution tier used for PvP loser damage (1/2/3).</summary>
    public int EvolutionStage { get; set; } = 1;
    public List<string> MoveIds { get; set; } = new();
    public string? AbilityId { get; set; }
    public string? EvolvesIntoId { get; set; }
    public int? EvolveAtLevel { get; set; }
    public string? EvolveRequiresFlag { get; set; }
    /// <summary>Chance (0-1) to drop an item when player declines capture.</summary>
    public double DeclineItemChance { get; set; } = 0.45;
    /// <summary>Chance (0-1) to also get an item when accepting into the party.</summary>
    public double AcceptAlsoItemChance { get; set; } = 0.12;
    public List<string> DropItemIds { get; set; } = new();
    public int DeclineGoldBonus { get; set; } = 5;
}
