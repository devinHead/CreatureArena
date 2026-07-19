using GameCore;

Console.WriteLine("=== Creature Arena (headless prototype) ===");
Console.WriteLine("Explore rooms, fight wilds, shop, then auto-battle. Type 'help'.");
Console.WriteLine();

var session = new GameSession();
Print(session.Handle("look"));
Print(session.Handle("status"));

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;
    if (line.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    Print(session.Handle(line));
    if (session.Phase == GameCore.Models.GamePhase.GameOver)
        break;
}

Console.WriteLine("Bye.");

static void Print(CommandResult result)
{
    Console.WriteLine(result.Ok ? result.Message : $"! {result.Message}");
    foreach (var line in result.Lines)
        Console.WriteLine(line);
    Console.WriteLine();
}
