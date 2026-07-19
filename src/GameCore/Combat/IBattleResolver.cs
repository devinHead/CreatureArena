namespace GameCore.Combat;

public sealed class BattleEvent
{
    public string Message { get; init; } = "";
}

public sealed class BattleSideResult
{
    public bool Won { get; init; }
    public int DamageDealtToLoserHp { get; init; }
    public List<Guid> ParticipantIds { get; init; } = new();
    public List<BattleEvent> Log { get; init; } = new();
}

public interface IBattleResolver
{
    BattleSideResult Resolve(IReadOnlyList<Combatant> playerSide, IReadOnlyList<Combatant> enemySide);
}
