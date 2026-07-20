namespace GameCore.Models;

public sealed class MoveDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ElementType Type { get; set; } = ElementType.Normal;
    public int Power { get; set; }
    public MoveTarget Target { get; set; } = MoveTarget.EnemyFront;
    public int ShopCost { get; set; }
    public string Description { get; set; } = "";
}
