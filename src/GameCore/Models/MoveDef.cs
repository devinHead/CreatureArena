namespace GameCore.Models;

public sealed class MoveDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Power { get; set; }
    public MoveTarget Target { get; set; } = MoveTarget.EnemyFront;
}
