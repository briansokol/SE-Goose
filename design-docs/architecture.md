# Goose — Technical Architecture

## Purpose

Goose is a Space Engineers Programmable Block script that manages inventory across connected cargo containers and production blocks on a grid. It sorts items into role-tagged containers, fills reactors and gas generators, feeds refineries, queues assembler crafts, and reports status on LCD surfaces. This document defines the **architecture** that supports those features. Per-subsystem feature specifications will be produced separately.

---

## 1. Architectural Principles

- **Bounded per-tick work.** Any step must be able to pause and resume. No step is allowed to assume it runs to completion in a single tick.
- **Rebuild the transient, persist the learned.** Block lists and inventory snapshots are cheap enough to reconstruct on a timer. Blueprint mappings and ore yields are not — those go to `Storage`.
- **Wrap state, iterate primitives.** Blocks that carry per-block behavior or persistent state get wrapper classes; blocks that are operated on uniformly stay as raw SE interfaces in typed lists.
- **Subsystems are partial-class files.** Each subsystem lives in its own `Program.Xxx.cs` partial so the entry-point file stays small and subsystem boundaries are visible in the file tree.
- **MyIni everywhere for configuration.** PB and per-block config under `[Goose]`. Persistent learned data under `[Goose.Learned]` in `Storage`. No bespoke text formats.
- **Degrade, don't halt.** An exception in one step logs and advances; the script keeps running the other subsystems.

---

## 2. Cycle Management

### 2.1 Update cadence

The PB runs on `Runtime.UpdateFrequency = UpdateFrequency.Update10` (one tick per ~0.167 s of sim time). A single cadence keeps the dispatcher simple. If per-tick latency becomes a problem after testing, the dispatcher is built so we can split work and display onto `Update1 + Update10` without rewriting step bodies.

### 2.2 Step-coroutine runner

Work is organized as an **ordered list of named steps**. Each step is implemented as a method returning `IEnumerator<YieldReason>` so it can pause mid-execution by yielding:

```csharp
enum YieldReason
{
    BudgetHit,       // instruction budget threshold crossed
    ChunkBoundary,   // natural seam inside a long loop
    ExternalWait,    // waiting on a game-side action (rare)
}

struct Step
{
    public string Name;
    public Func<IEnumerator<YieldReason>> Factory;
}
```

The dispatcher (`StepRunner`) holds:

- the ordered `Step[]`,
- the current step index,
- the current step's live `IEnumerator<YieldReason>` (null if the next tick starts a new step).

Each tick, `Main` calls `StepRunner.Tick()`:

1. If no live enumerator, create one from the current step's factory.
2. While within the tick's instruction budget, call `enumerator.MoveNext()`.
3. If `MoveNext` returns `false`, the step is finished — dispose, clear the enumerator, advance the step index (wrap to 0 at end of list), and optionally start the next step in the same tick if budget allows.
4. If `MoveNext` returns `true`, the step yielded — keep the enumerator, end the tick.
5. Any exception inside `MoveNext` is caught, logged with `step.Name`, the enumerator is discarded, and the step index advances. The failing step does **not** retry in a tight loop.

### 2.3 Instruction budget

A single helper governs yielding from inside step bodies:

```csharp
bool BudgetExceeded()
    => Runtime.CurrentInstructionCount > Config.InstructionBudgetFraction * Runtime.MaxInstructionCount;
```

`InstructionBudgetFraction` defaults to ~0.5 — half of what the script is allowed to consume in a tick, leaving headroom for other scripts on the same construct and for Space Engineers' own work. Step bodies call `BudgetExceeded()` at loop seams and `yield return YieldReason.BudgetHit` when it fires.

Steps may also yield at explicit chunk boundaries (e.g. every N containers processed) with `YieldReason.ChunkBoundary` even when the budget isn't stressed, so a single tick never monopolizes the schedule.

### 2.4 Step list (v1 skeleton)

Order is significant — later steps depend on state produced by earlier ones. Exact grouping is a subsystem concern; the current skeleton:

| # | Step | Subsystem |
|---|---|---|
| 0 | Rescan blocks if due | Discovery |
| 1 | Parse PB config if dirty | Config |
| 2 | Categorize containers by tag | Discovery |
| 3 | Scan inventories into item totals | Sorting |
| 4 | Fulfill stock container quotas | Sorting |
| 5 | Sort generic cargo by type | Sorting |
| 6 | Balance same-role containers | Sorting |
| 7 | Feed refineries | Production |
| 8 | Update assembler queues | Production |
| 9 | Fill reactors | Production |
| 10 | Fill gas generators & bottles | Production |
| 11 | Render displays | Display |
| 12 | Persist learned state if dirty | State |

The step list lives in one place (the dispatcher's init) so its order and membership are visible at a glance.

### 2.5 Error boundary

The dispatcher's try/catch around `MoveNext` is the **only** place step-level exceptions are caught. Subsystems do not need internal try/catch except when they want to suppress a specific expected failure. The logger receives step name, exception type, and message — not a stack trace (too expensive and the PB environment scrubs most frames anyway).

No "N consecutive errors → halt" behavior. Logging is enough; the user sees failures on the warnings surface and decides what to do.

---

## 3. State Management

### 3.1 Three kinds of state

| Kind | Example | Where it lives | Rebuild cadence |
|---|---|---|---|
| Transient | Block lists, inventory snapshots, per-cycle totals | Fields on `Program` (cleared & refilled) | Every cycle or every N cycles (see §3.2) |
| Config | User toggles, quotas, tags | PB CustomData `[Goose]` section, per-block CustomData `[Goose]` section | On rescan or on explicit argument trigger |
| Learned | Blueprint mappings, ore yield ratios | `Storage` under `[Goose.Learned]` | Written only when it changes |

### 3.2 Rescan policy

Block lists are cached between rescans. A rescan is triggered by:

- timer (configurable, default ~30 s of sim time),
- user argument `rescan`,
- detection of a `null` or non-valid block during a step (opportunistic).

Every access to a cached block validates `block != null && !block.Closed && block.IsSameConstructAs(Me)` before use; stale entries are dropped in place. This tolerates destroyed or detached blocks without waiting for the next scheduled rescan.

### 3.3 Config loading

`[Goose]` section on PB CustomData is parsed via `MyIni` at startup and on each rescan. Per-block config (stock container quotas, display surface roles, exclusions) is parsed when that block is first seen after a rescan. Parsing results are stored on the block's wrapper (if any) or in a side dictionary keyed by the block.

Parse failures do not abort the rescan — they log a warning for that specific block and the block is skipped.

### 3.4 Persistence

`Save()` writes `Storage` once. The script also writes opportunistically when learned data changes (gated by a dirty flag so we don't serialize on every cycle). Format:

```
[Goose.Learned]
blueprint.<ItemTypeId> = <BlueprintDefinitionId>
oreyield.<OreTypeId>.<IngotTypeId> = <ratio>
```

Learned data is rebuilt over time from observation. Loss of `Storage` (server restart corruption, etc.) is not fatal — the script re-learns.

---

## 4. Block Handling

### 4.1 Targeted wrapper classes

Wrapper classes exist **only** for blocks that carry non-trivial per-block state. The rest are iterated as raw SE interfaces in typed lists.

| Wrapper | Wraps | Reason |
|---|---|---|
| `StockContainer` | `IMyTerminalBlock` with cargo inventory | Owns per-container quota list, priority, quota-template dirty state |
| `ManagedAssembler` | `IMyAssembler` | Owns learning-mode state, observed-output buffer, blueprint candidate set |
| `DisplaySurface` | `IMyTextSurface` | Owns role (status / log / autocraft / level / inventory), scroll cursor, last-draw hash |
| `ManagedRefinery` (v2, optional) | `IMyRefinery` | Only if/when ore-yield learning is implemented; until then, refineries are raw |

Wrappers hold a reference to their underlying block and a small amount of cached-across-cycles state. They do **not** own the subsystem logic — a `StockContainer` doesn't know how to run a sort cycle; the sorting step reads from and writes to `StockContainer` instances.

### 4.2 Raw typed lists

```csharp
List<IMyCargoContainer>     cargoContainers;
List<IMyReactor>            reactors;
List<IMyGasGenerator>       gasGenerators;
List<IMyGasTank>            gasTanks;
List<IMyShipConnector>      connectors;
List<IMyMechanicalConnectionBlock> mechanicals;
List<IMyRefinery>           refineries;          // until a wrapper is needed
List<IMyAssembler>          rawAssemblers;       // input to wrapper rebuild
List<IMyTextSurfaceProvider> surfaceProviders;   // input to DisplaySurface rebuild
```

Wrapper collections live alongside:

```csharp
List<StockContainer>    stockContainers;
List<ManagedAssembler>  assemblers;
List<DisplaySurface>    displaySurfaces;
```

The rescan step rebuilds raw lists via `GridTerminalSystem.GetBlocksOfType<T>` and then rebuilds wrappers from those, preserving wrapper state where the underlying block still exists (match by `EntityId`).

---

## 5. File Organization

One partial `class Program : MyGridProgram` spread across per-subsystem files.

```
Program.cs                  // constructor, Main, Save, root fields
Program.Dispatcher.cs       // StepRunner, Step, YieldReason, BudgetExceeded, step registration
Program.Discovery.cs        // block scanning, categorization, rescan
Program.Config.cs           // MyIni parsing, config object, argument dispatch
Program.State.cs            // Storage persistence for learned data
Program.Logging.cs          // action log ring buffer, warnings list
Program.Blocks.cs           // wrapper classes (StockContainer, ManagedAssembler, DisplaySurface, ...)
Program.Sorting.cs          // type-based sorting, stock-container quotas, balancing steps
Program.Production.cs       // refinery / assembler / reactor / gas generator steps
Program.Display.cs          // display rendering steps, sprite helpers
```

Each subsystem file exposes **steps** (methods matching `IEnumerator<YieldReason>` signature) that `Program.Dispatcher.cs` wires into the step list. Shared helpers (e.g. `IsExcluded(IMyTerminalBlock)`) live in whichever subsystem owns the concept, not in a generic `Utils` bucket.

MDK2's minifier will collapse this into a single file for deployment; the split is for authoring ergonomics only.

---

## 6. Tag & Configuration Conventions

Mixed: **role via tags in block names, quantitative config via `[Goose]` MyIni in CustomData.**

### 6.1 Tags on block names

- Role tags: `[Ore]`, `[Ingot]`, `[Component]`, `[Tool]`, `[Ammo]`, `[Bottle]`, `[Consumable]`, `[Seed]`, `[Misc]`
- Special containers: `[Stock]` (per-container quota list in CustomData)
- Display surfaces: `[GooseStatus]`, `[GooseLog]`, `[GooseAutocraft]`, `[GooseLevel]`, `[GooseInventory]`
- Exclusion: `[NoGoose]` (hard exclude), `[NoSort]` (sorting only)

Tags are bracket-wrapped tokens anywhere in the name. Parsing is straightforward regex / `IndexOf('[')` scan.

### 6.2 `[Goose]` section in CustomData

- **PB CustomData** — master configuration (cadence overrides, budget fraction, feature master switches, rescan interval, display preferences).
- **Stock container CustomData** — quota list:
  ```
  [Goose]
  priority = 3
  SteelPlate = 100
  Girder = 50 min
  InteriorPlate = 200 limit
  ```
- **Display surface CustomData** (multi-surface blocks only) — which surface index maps to which role:
  ```
  [Goose]
  surface.0 = Status
  surface.1 = Log
  ```

### 6.3 Learned data in Storage

See §3.4. Separate `[Goose.Learned]` section keeps user-facing config (CustomData) cleanly separated from machine-maintained state (`Storage`).

---

## 7. Logging & Display Pipeline

### 7.1 Log channels

- **Action log** — ring buffer of the last N human-readable events (default 48). Rendered on `[GooseLog]` surfaces.
- **Warnings** — distinct list, deduplicated by message, surfaced on `[GooseStatus]` and in a section of `[GooseLog]` if present.
- **Step-failure log** — subset of warnings tagged with the failing step name (for diagnostic display).

All three are fields on `Program`, reused across cycles. The logger does not call `Echo` during normal operation; `Echo` is reserved for responses to user arguments.

### 7.2 Display rendering

`DisplaySurface` instances encapsulate one rendered role each. The display step iterates them and, for each, calls the role-specific renderer (e.g. `RenderStatus(surface)`). Renderers use a single `MySpriteDrawFrame` per surface and yield at surface boundaries so a long list of LCDs doesn't blow the budget.

Renderers are expected to be idempotent and cheap — they read from already-computed state (totals, warnings, stock container summaries) rather than recomputing.

---

## 8. Argument Dispatch

User arguments are parsed via `MyCommandLine` in `Main`. A `Dictionary<string, Action<MyCommandLine>>` maps command names to handlers. Arguments that change persistent behavior (toggles, learning triggers) update the config object and mark it dirty; the next rescan picks up the change.

Initial command surface (names tentative — subsystem specs will finalize):

- `rescan` — force an immediate rescan on the next tick
- `reset-learned` — clear `[Goose.Learned]` from Storage
- `pause` / `resume` — suspend / restart the step runner
- `debug <on|off>` — verbose logging toggle

---

## 9. Feature Coverage Summary

Architecture choices above are sized to support the following features across subsystems. Each will be specified in its own subsystem document.

**Sorting**
- Type-based routing into role-tagged containers
- Stock containers with per-item quotas (exact / minimum / limiter / fill)
- Balancing across same-role containers
- Exclusion of locked / hidden / marked containers

**Production**
- Refinery auto-feed (ore → refinery inventories)
- Assembler queue maintenance from autocraft quotas (assemble, disassemble, priority, hidden)
- Reactor uranium fill with per-grid-size defaults
- Gas generator ice fill with bottle-refill headroom
- Bottle refill (H₂ / O₂) via generators or tanks

**Display**
- Status dashboard (inventory totals, warnings, spinner)
- Action log with timestamps
- Autocraft quota panel (interactive via CustomData)
- Per-container fill-level panel
- Per-container inventory listing panel

**Learning**
- Blueprint discovery (pattern-guess + observational learning on tagged assemblers)
- Ore yield learning (optional; drives refinery prioritization)

**Multi-grid**
- Subgrid inclusion by default; per-connector or per-grid exclusion tags
- Multi-PB coexistence via a priority declared in PB CustomData (v2; noted but not v1)

Features not in v1 scope but not precluded by the architecture: cross-grid IGC messaging, mining-drone integration, historical metrics persistence.

---

## 10. Verification

Because SE programmable-block scripts cannot be unit-tested outside the game, verification is in-game:

1. `dotnet build Goose.csproj -c Debug` — must compile with zero warnings under `<LangVersion>6</LangVersion>`.
2. Load the built script into a PB on a test grid containing at least one of each managed block type.
3. Confirm the step runner advances through the step list (visible on `[GooseLog]`).
4. Introduce a deliberate exception in a step and confirm it logs and the dispatcher advances rather than halting.
5. Force budget exhaustion (large container count) and confirm `BudgetHit` yields appear in the log without missed work.
6. Reload the world and confirm `[Goose.Learned]` survives and is re-applied.

---

## 11. Out of Scope for This Document

- Subsystem-internal algorithms (sorting order, refinery priority, assembler queue logic)
- Display visual design (layout, color palette, font metrics)
- Exact command names and argument grammars
- Tag and role taxonomy beyond the examples given

These belong in the per-subsystem specs that follow.
