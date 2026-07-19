# Creature Arena

Headless C# prototype of an explore → capture → shop → auto-battle loop.
Built to port into Unity later (3D free-roam). Game rules live in `GameCore` with **no Unity dependency**.

## Run

```powershell
dotnet run --project src/GameConsole
```

## Edit creatures / stats

Add or tweak entries in:

- `src/GameCore/Content/species.json`
- `src/GameCore/Content/natures.json`
- `src/GameCore/Content/moves.json`
- `src/GameCore/Content/items.json`
- `src/GameCore/Content/rooms.json`

## Commands

- **Explore:** `look` | `go <room>` | `fight` | `take` | `party` | `end`
- **After wild win:** `accept` | `decline`
- **Shop:** `shop` | `buy <item>` | `give <creature> <item>` | `board <1-6> <creature|clear>` | `evolve <creature>` | `ready`
- **Anytime:** `status` | `help` | `seed <n>` | `quit`
