namespace GameCore.Models;

public sealed class AbilityDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class AbilityOption
{
    public string AbilityId { get; set; } = "";
    /// <summary>Relative weight when rolling an ability on spawn.</summary>
    public double Weight { get; set; } = 1.0;
}
