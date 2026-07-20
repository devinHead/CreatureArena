namespace GameCore.Models;

public enum ElementType
{
    Normal,
    Fire,
    Water,
    Grass
}

public enum GamePhase
{
    StarterSelect,
    Explore,
    WildReward,
    Shop,
    Battle,
    GameOver
}

public enum MoveTarget
{
    EnemyFront,
    EnemyRandom,
    Self
}
