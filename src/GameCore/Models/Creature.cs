namespace GameCore.Models;

public sealed class Creature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nickname { get; set; } = "";
    public string SpeciesId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public StatBlock StartingStats { get; set; } = new();
    public StatBlock Potential { get; set; } = new();
    public string NatureId { get; set; } = "";
    public List<string> MoveIds { get; set; } = new();
    public string? HeldItemId { get; set; }
    public int CurrentHp { get; set; }

    public StatBlock CombatStats { get; set; } = new();

    public bool IsAlive => CurrentHp > 0;

    public void RecalculateStats(SpeciesDef species, NatureDef nature, ItemDef? heldItem)
    {
        CombatStats = DeriveStats(species, nature, heldItem);
        if (CurrentHp <= 0 || CurrentHp > CombatStats.Hp)
            CurrentHp = CombatStats.Hp;
    }

    public StatBlock DeriveStats(SpeciesDef species, NatureDef nature, ItemDef? heldItem)
    {
        int Scale(int baseStat, int start, int pot, string key)
        {
            var bias = nature.StatBias.TryGetValue(key, out var b) ? b : 1.0;
            var raw = baseStat + start + pot + (Level - 1) * 2;
            return Math.Max(1, (int)Math.Round(raw * bias) + (heldItem is null ? 0 : BonusFor(heldItem, key)));
        }

        static int BonusFor(ItemDef item, string key) => key switch
        {
            "Hp" => item.HpBonus,
            "Atk" => item.AtkBonus,
            "Def" => item.DefBonus,
            "Spd" => item.SpdBonus,
            _ => 0
        };

        return new StatBlock
        {
            Hp = Scale(species.BaseStats.Hp, StartingStats.Hp, Potential.Hp, "Hp"),
            Atk = Scale(species.BaseStats.Atk, StartingStats.Atk, Potential.Atk, "Atk"),
            Def = Scale(species.BaseStats.Def, StartingStats.Def, Potential.Def, "Def"),
            Spd = Scale(species.BaseStats.Spd, StartingStats.Spd, Potential.Spd, "Spd")
        };
    }

    public int XpToNextLevel() => Level * 10;

    public override string ToString() =>
        $"{Nickname} Lv{Level} ({SpeciesId}) HP {CurrentHp}/{CombatStats.Hp} " +
        $"Atk {CombatStats.Atk} Def {CombatStats.Def} Spd {CombatStats.Spd}";
}
