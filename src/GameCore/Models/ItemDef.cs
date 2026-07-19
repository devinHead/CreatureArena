namespace GameCore.Models;

public sealed class ItemDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int ShopCost { get; set; }
    public int AtkBonus { get; set; }
    public int DefBonus { get; set; }
    public int SpdBonus { get; set; }
    public int HpBonus { get; set; }
}
