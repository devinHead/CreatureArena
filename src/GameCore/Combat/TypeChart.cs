using GameCore.Models;

namespace GameCore.Combat;

/// <summary>
/// Classic triangle + Normal (neutral). Tunable constants live here.
/// STAB = 1.5 when move type matches user type.
/// Super-effective = 2.0, resisted = 0.5.
/// </summary>
public static class TypeChart
{
    public const double StabMultiplier = 1.5;
    public const double SuperEffective = 2.0;
    public const double NotVeryEffective = 0.5;
    public const double Neutral = 1.0;

    public static double Effectiveness(ElementType moveType, ElementType defenderType)
    {
        if (moveType == ElementType.Normal || defenderType == ElementType.Normal)
            return Neutral;

        return (moveType, defenderType) switch
        {
            (ElementType.Fire, ElementType.Grass) => SuperEffective,
            (ElementType.Fire, ElementType.Water) => NotVeryEffective,
            (ElementType.Water, ElementType.Fire) => SuperEffective,
            (ElementType.Water, ElementType.Grass) => NotVeryEffective,
            (ElementType.Grass, ElementType.Water) => SuperEffective,
            (ElementType.Grass, ElementType.Fire) => NotVeryEffective,
            _ => Neutral
        };
    }

    public static double Stab(ElementType userType, ElementType moveType) =>
        userType == moveType ? StabMultiplier : Neutral;

    public static string EffectivenessLabel(double multiplier) => multiplier switch
    {
        > 1.01 => "It's super effective!",
        < 0.99 => "It's not very effective...",
        _ => ""
    };
}
