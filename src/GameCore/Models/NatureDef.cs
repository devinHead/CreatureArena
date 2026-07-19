namespace GameCore.Models;

public sealed class NatureDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Multiplies XP gained (1.0 = normal).</summary>
    public double XpGrowth { get; set; } = 1.0;
    /// <summary>Optional combat-stat bias keys: Hp, Atk, Def, Spd. Values like 1.1 / 0.9.</summary>
    public Dictionary<string, double> StatBias { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
