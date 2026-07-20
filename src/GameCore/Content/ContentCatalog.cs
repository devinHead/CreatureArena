using System.Text.Json;
using System.Text.Json.Serialization;
using GameCore.Models;

namespace GameCore.Content;

public sealed class ContentCatalog
{
    public IReadOnlyDictionary<string, SpeciesDef> Species { get; }
    public IReadOnlyDictionary<string, NatureDef> Natures { get; }
    public IReadOnlyDictionary<string, MoveDef> Moves { get; }
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyDictionary<string, RoomDef> Rooms { get; }
    public IReadOnlyDictionary<string, AbilityDef> Abilities { get; }

    private ContentCatalog(
        Dictionary<string, SpeciesDef> species,
        Dictionary<string, NatureDef> natures,
        Dictionary<string, MoveDef> moves,
        Dictionary<string, ItemDef> items,
        Dictionary<string, RoomDef> rooms,
        Dictionary<string, AbilityDef> abilities)
    {
        Species = species;
        Natures = natures;
        Moves = moves;
        Items = items;
        Rooms = rooms;
        Abilities = abilities;
    }

    public static ContentCatalog Load(string? contentDirectory = null)
    {
        var dir = contentDirectory ?? Path.Combine(AppContext.BaseDirectory, "Content");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        var species = LoadList<SpeciesDef>(Path.Combine(dir, "species.json"), options)
            .ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var natures = LoadList<NatureDef>(Path.Combine(dir, "natures.json"), options)
            .ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var moves = LoadList<MoveDef>(Path.Combine(dir, "moves.json"), options)
            .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var items = LoadList<ItemDef>(Path.Combine(dir, "items.json"), options)
            .ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var rooms = LoadList<RoomDef>(Path.Combine(dir, "rooms.json"), options)
            .ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var abilities = LoadList<AbilityDef>(Path.Combine(dir, "abilities.json"), options)
            .ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        return new ContentCatalog(species, natures, moves, items, rooms, abilities);
    }

    private static List<T> LoadList<T>(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing content file: {path}");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, options)
               ?? throw new InvalidOperationException($"Failed to parse {path}");
    }

    public SpeciesDef GetSpecies(string id) =>
        Species.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException($"Unknown species '{id}'");

    public NatureDef GetNature(string id) =>
        Natures.TryGetValue(id, out var n) ? n : throw new KeyNotFoundException($"Unknown nature '{id}'");

    public AbilityDef? TryGetAbility(string id) =>
        Abilities.TryGetValue(id, out var a) ? a : null;

    public ItemDef? TryGetItem(string id) =>
        Items.TryGetValue(id, out var i) ? i : null;

    public RoomDef GetRoom(string id) =>
        Rooms.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"Unknown room '{id}'");
}
