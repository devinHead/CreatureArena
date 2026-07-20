namespace GameCore;

public sealed class CommandResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    public static CommandResult Success(string message, IEnumerable<string>? lines = null) => new()
    {
        Ok = true,
        Message = message,
        // Snapshot so later mutation / lazy iterators can't scramble console output.
        Lines = lines?.ToArray() ?? Array.Empty<string>()
    };

    public static CommandResult Fail(string message) => new()
    {
        Ok = false,
        Message = message
    };
}
