namespace GameCore.Models;

public sealed class Creature
{
    public const int MaxMoves = 4;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nickname { get; set; } = "";
    public string SpeciesId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    /// <summary>IVs 0-15. Each adds up to ~10% of base+growth toward the combat stat.</summary>
    public StatBlock Potential { get; set; } = new();
    public string NatureId { get; set; } = "";
    public string? AbilityId { get; set; }
    public List<string> MoveIds { get; set; } = new();
    public string? HeldItemId { get; set; }
    public int CurrentHp { get; set; }
    /// <summary>Per-creature history (visited rooms with them, won battles, etc.).</summary>
    public HashSet<string> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public StatBlock CombatStats { get; set; } = new();

    public bool IsAlive => CurrentHp > 0;

    /// <summary>
    /// Recalculates combat stats. Max-HP increases also raise current HP by the same amount
    /// (so a full-health creature stays full when equipping Life Seed). Fainted stay faintd.
    /// </summary>
    public void RecalculateStats(SpeciesDef species, NatureDef nature, ItemDef? heldItem)
    {
        var oldMax = CombatStats.Hp;
        var oldCurrent = CurrentHp;
        CombatStats = DeriveStats(species, nature, heldItem);
        var newMax = CombatStats.Hp;

        if (oldCurrent <= 0)
        {
            // Stay fainted until explicitly healed.
            CurrentHp = 0;
            return;
        }

        var delta = newMax - oldMax;
        if (delta > 0)
            CurrentHp = Math.Min(newMax, oldCurrent + delta);
        else
            CurrentHp = Math.Min(oldCurrent, newMax);
    }

    public StatBlock DeriveStats(SpeciesDef species, NatureDef nature, ItemDef? heldItem)
    {
        const double IvCeiling = 0.10;
        const int GrowthPerLevel = 1;

        int Scale(int baseStat, int iv, string key)
        {
            var bias = nature.StatBias.TryGetValue(key, out var b) ? b : 1.0;
            var grown = baseStat + (Level - 1) * GrowthPerLevel;
            var ivFactor = 1.0 + (Math.Clamp(iv, 0, 15) / 15.0) * IvCeiling;
            var raw = grown * ivFactor * bias;
            return Math.Max(1, (int)Math.Round(raw) + (heldItem is null ? 0 : BonusFor(heldItem, key)));
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
            Hp = Scale(species.BaseStats.Hp, Potential.Hp, "Hp"),
            Atk = Scale(species.BaseStats.Atk, Potential.Atk, "Atk"),
            Def = Scale(species.BaseStats.Def, Potential.Def, "Def"),
            Spd = Scale(species.BaseStats.Spd, Potential.Spd, "Spd")
        };
    }

    public int XpToNextLevel() => Level * 10;

    public override string ToString() =>
        $"{Nickname} Lv{Level} ({SpeciesId}) HP {CurrentHp}/{CombatStats.Hp} " +
        $"Atk {CombatStats.Atk} Def {CombatStats.Def} Spd {CombatStats.Spd}";
}
