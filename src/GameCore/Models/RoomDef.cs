namespace GameCore.Models;

public sealed class RoomDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Exits { get; set; } = new();
    public List<string> EncounterSpeciesIds { get; set; } = new();
    public double EncounterChance { get; set; } = 0.7;
    public int GoldLootMin { get; set; }
    public int GoldLootMax { get; set; }
    public List<string> ItemLootIds { get; set; } = new();
    public string? VisitFlag { get; set; }
}
