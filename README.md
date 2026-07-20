# Creature Arena

Headless C# prototype of an explore → capture → shop → auto-battle loop.
Built to port into Unity later (3D free-roam). Game rules live in `GameCore` with **no Unity dependency**.

## Run

```powershell
dotnet run --project src/GameConsole
```

## Edit creatures / stats

Add or tweak entries in `src/GameCore/Content/` (`species.json`, `moves.json`, `items.json`, etc.).

Every species uses the same fields. Use `null` for unused scalars and `[]` for empty lists (e.g. final evolutions have `"evolutions": []`). Branching evolution is an `evolutions` array (Big Slime can become Slime King **or** Ooze Titan).

## Commands

- **Start:** `starters` | `pick <1-3>`
- **Explore:** `look` | `go <room>` | `fight` | `party` | `end`
- **After wild win:** `accept` | `decline`
- **Shop:** `shop` | `buy item|creature|move <id>` | `sell <creature>` | `give <creature> <item>` | `teach <creature> <move>` | `board <1-6> <creature|clear>` | `evolve <creature> [intoId]` | `ready`
- **Anytime:** `status` | `help` | `seed <n>` | `quit`

Wild encounters show an estimated win %. Types: Fire / Water / Grass / Normal with STAB (1.5x) and a simple triangle matchup. Species use `learnset` (level-ups) and `shopMoveIds` (teach from shop).
