using System.Text;
using GameCore;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=== Creature Arena (headless prototype) ===");
Console.WriteLine("Pick a starter, explore, fight wilds, shop, then auto-battle. Type 'help'.");
Console.WriteLine();

var session = new GameSession();
Print(session.ShowStarters());

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
    var sb = new StringBuilder();
    sb.AppendLine(result.Ok ? result.Message : $"! {result.Message}");
    foreach (var line in result.Lines)
        sb.AppendLine(line);
    Console.Write(sb.ToString());
    Console.WriteLine();
}
