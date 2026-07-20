namespace GameCore.Models;

public sealed class PlayerState
{
    public const int MaxBoardSize = 6;
    public const int StartingHp = 15;

    public int Hp { get; set; } = StartingHp;
    public int Gold { get; set; } = 10;
    public List<Creature> Party { get; set; } = new();
    public List<string> InventoryItemIds { get; set; } = new();
    /// <summary>Learned-move tomes sitting in inventory (teach to creatures).</summary>
    public List<string> InventoryMoveIds { get; set; } = new();
    /// <summary>Board slots reference creature Ids from Party. Null = empty slot.</summary>
    public Guid?[] Board { get; set; } = new Guid?[MaxBoardSize];
    public HashSet<string> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Creature? FindCreature(Guid id) => Party.FirstOrDefault(c => c.Id == id);

    public Creature? FindCreatureByName(string name) =>
        Party.FirstOrDefault(c => c.Nickname.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<Creature> GetBoardCreatures()
    {
        foreach (var id in Board)
        {
            if (id is null) continue;
            var c = FindCreature(id.Value);
            if (c is not null) yield return c;
        }
    }
}
