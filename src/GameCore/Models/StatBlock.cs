namespace GameCore.Models;

public sealed class StatBlock
{
    public int Hp { get; set; }
    public int Atk { get; set; }
    public int Def { get; set; }
    public int Spd { get; set; }

    public StatBlock Clone() => new()
    {
        Hp = Hp,
        Atk = Atk,
        Def = Def,
        Spd = Spd
    };
}
