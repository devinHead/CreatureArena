# Agent handoff — Creature Arena

**Repo:** https://github.com/devinHead/CreatureArena  
**Local path:** `c:\Users\devin\Projects\Game`  
**Stack:** .NET 8 C# — `GameCore` (pure rules, no Unity) + `GameConsole` (REPL)  
**Long-term plan:** port `GameCore` into Unity for 3D free-roam explore later.

## Run

```powershell
# If `dotnet` is missing in a new Cursor terminal, refresh PATH or restart Cursor:
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

cd c:\Users\devin\Projects\Game
dotnet run --project src/GameConsole
```

Quit any running `GameConsole` before rebuilding (DLL file lock otherwise).

## Game loop

`StarterSelect` → `Explore` (rooms, wild fights) → `Shop` → PvP auto-battle (`ready`) → repeat.  
Player HP (start **15**) only drops on **PvP**. Creature HP **persists during explore**; shop / new round heals party.

## Design goals (player intent)

- Not a Hearthstone BG clone — hybrid: explore/collect → shop → auto-battler.
- Eventually 3D free-roam; console is for nailing rules first.
- Placeholder art/content OK; focus on tunable data in JSON.
- Battle mode for MVP: **auto-battler** (turn-based/RTS deferred; `IBattleResolver` exists for swaps).

## Where to edit content

All under `src/GameCore/Content/`:

| File | Purpose |
|------|---------|
| `species.json` | Creatures: type, stats, learnset, shopMoveIds, evolutions[], buy/sell gold, drops |
| `moves.json` | Moves: type, power, target, shopCost |
| `items.json` | Held items |
| `abilities.json` | Ability defs (mostly stubs) |
| `natures.json` | XP growth + small stat bias |
| `rooms.json` | Room graph, encounters, loot, `visitFlag` |

## Important systems (current)

### Types
- Elements: **Fire / Water / Grass / Normal** (`ElementType` in `Models/Enums.cs`).
- Triangle: Fire→Grass→Water→Fire; Normal always neutral.
- **STAB 1.5×**, SE **2×**, resisted **0.5×** — `Combat/TypeChart.cs`.
- Damage in `AutoBattleResolver`: `(Atk + Power - Def)` then × STAB × effectiveness (min 1).

### Moves / learning
- Species **`learnset`**: `{ moveId, level }` — known at create / level-up / evolve if move slots free (max **4**).
- Species **`shopMoveIds`**: only these can be taught via shop `teach`.
- Creature known moves live on instance: `Creature.MoveIds`.

### Flags (two layers)
- **Player flags:** run-wide (`Visited:CrystalCave`, `WonBattle` from PvP).
- **Creature flags:** per party member — enter room → party gets `visitFlag`; win wild fight → fighters get `WonBattle:{roomId}`.
- Evolutions use `requiresPlayerFlags` / `requiresCreatureFlags` arrays (nullable).

### Shop
- Restocks each shop phase: items, creatures, moves.
- Commands: `buy item|creature|move <id>`, `sell <creature>`, `give`, `teach`, `evolve <name> [intoId]`, `board`, `ready`.

### Stats / IVs
- IVs 0–15 add up to **~10%** of (base + level growth) — must not dominate runs.
- Equip item that raises max HP also raises current HP by the delta (shop `give` also full-heals in shop).

### Key commands
- Start: `pick 1|2|3`
- Explore: `look` | `go <room>` | `fight` | `accept`/`decline` | `end`
- Wilds show **estimated win %** (Monte Carlo, quiet sims).

## Architecture map

```
src/GameCore/GameSession.cs     — phases + commands
src/GameCore/CreatureFactory.cs — spawn, IVs, learnset, shop-learn check
src/GameCore/Combat/            — AutoBattleResolver, TypeChart, Combatant
src/GameCore/Models/            — Creature, SpeciesDef, MoveDef, etc.
src/GameConsole/Program.cs      — REPL
```

## Explicitly deferred / known gaps

- Unity / 3D / networking
- Ability combat effects (stubs only)
- Forget-move when at 4 moves
- Deeper combat formula redesign (player said they’ll revisit)
- Procedural dungeons

## Recent work (this session arc)

1. Headless full loop prototype  
2. Starter pick, risk %, explore HP persistence, abilities, auto room loot  
3. Shop: creatures + moves; multi-path evolutions; buy/sell gold ranges  
4. Player vs creature flags for evolve conditions  
5. Types + STAB + chart; learnset + shopMoveIds  

## GitHub

- Remote: `origin` → `https://github.com/devinHead/CreatureArena.git`
- Branch: `main`
- `gh` logged in as **devinHead**

When starting a new agent: read this file + `README.md`, then skim `species.json` / `GameSession.cs` before changing rules.
