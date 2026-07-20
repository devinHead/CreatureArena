namespace GameCore.Models;

/// <summary>One possible evolution branch. Different species should not share the same IntoId.</summary>
public sealed class EvolutionOption
{
    public string IntoId { get; set; } = "";
    public int? AtLevel { get; set; }
    public List<string>? RequiresPlayerFlags { get; set; }
    public List<string>? RequiresCreatureFlags { get; set; }
}

public sealed class SpeciesDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ElementType Type { get; set; } = ElementType.Normal;
    public StatBlock BaseStats { get; set; } = new();
    public int EvolutionStage { get; set; } = 1;
    /// <summary>Moves learned automatically by leveling (level = first level they can know it).</summary>
    public List<LearnsetEntry> Learnset { get; set; } = new();
    /// <summary>Moves this species is allowed to learn from shop tomes.</summary>
    public List<string> ShopMoveIds { get; set; } = new();
    public bool IsStarter { get; set; }
    public List<AbilityOption> AbilityOptions { get; set; } = new();
    public List<EvolutionOption> Evolutions { get; set; } = new();
    public double? DeclineItemChance { get; set; }
    public double? AcceptItemChance { get; set; }
    public List<string> DropItemIds { get; set; } = new();
    public int? DeclineGoldBonus { get; set; }
    public int? BuyGoldMin { get; set; }
    public int? BuyGoldMax { get; set; }
    public int? SellGoldMin { get; set; }
    public int? SellGoldMax { get; set; }
}
