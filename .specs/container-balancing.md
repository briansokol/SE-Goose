# Container Balancing — Reactors, Gas Generators / Irrigation, Weapons

> **For agentic workers:** Use `superpowers:subagent-driven-development` if executing task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new "balancer" subsystem that keeps fuel/ammo levels at a configurable target on three classes of consumer blocks — reactors (Ingot/Uranium), O2/H2 generators + irrigation systems (Ore/Ice), and weapons (any AmmoMagazine subtype). Targets are configured PB-wide in CustomData; per-block opt-out via a `[NoBalance]` name tag.

**Tech stack:** C# 6.0, .NET Framework 4.8, MDK2, Space Engineers PB API. xUnit + FluentAssertions for tests.

---

## Context

The Inventory-Sorter functional design wiki already names this feature: *"O2/H2 Gens, Reactors, Refineries, Irrigation, Weapons (static and turrets) all have the highest priority. Essentially `[P:00]`. Optional Functionality (ON by default): automatic Uranium balancing to reactors, Ice balancing to O2/H2 and irrigation, and ammo loading to weapons. Items move INTO these blocks."* The Technical Architecture page reserves Step 6 ("Balance same-role containers") for this work as a v2+ slot.

Today these consumer blocks are discovered (they pass `IsManaged`) but uncategorized. They never get pushed-to (they have no category tag), but `StepSortGenericCargo` *does* iterate them as sources, which means a hand-loaded reactor's Uranium leaks back to Ingot containers on the next sort pass. The balancer must therefore fix two things at once: actively top-up the consumers, and prevent the existing sort from draining them.

**User-confirmed design decisions** (initial brainstorm 2026-04-30; iteration 2 2026-05-01):

1. **Configuration shape:** three per-block-class scalar keys in the existing `[Goose]` section of the PB CustomData — not per-item-type entries. Smallest config surface, easiest to reason about.
2. **PB-level keys are percent-of-volume across all three classes** *(iter 2)*. `reactorUraniumFillPercent`, `gasIceFillPercent`, `weaponAmmoFillPercent` — each meaning "fill this fraction of the block's inventory volume with the relevant item." Unifies the semantic across classes.
3. **Per-block unit-count override via `[Balance=N]` name tag** *(iter 2)*. Tagging a block forces a unit count target instead of the class percent. A tagged block runs even when its class percent is 0 — explicit per-block intent always wins over class disable. Works on all three classes: a tagged reactor → N Uranium ingots, tagged gas/irrigation → N Ice, tagged weapon → N total magazines.
4. **Detection by item-acceptance probing**, not vanilla-interface whitelist. Auto-handles modded irrigation systems, modded reactors, and modded weapons without maintaining a type list.
5. **Pull from any container**, scanning by priority. Treats consumer fill as the highest-priority demand on the grid (matches the wiki's `[P:00]`-equivalent framing). Stock containers are fair game.
6. **Drain excess back to category containers** when a block has more than its target. Strict target enforcement, not "fill-only."
7. **`[NoBalance]` name tag** opts a block out of the balancer only. Excluded blocks behave like normal generic containers — sorter still iterates them.
8. **Live-merge balancer keys into PB CustomData** *(iter 2)*. On parse, if any of the three keys is missing under `[Goose]`, it's appended with default 0 and a one-line hint comment. User-set values and existing comments are preserved. Mirrors the stock-template live-merge pattern.
9. **Discrete two-step pipeline addition**: `StepCategorizeConsumers` (probes once per rescan, caches result + `[Balance=N]` count on `ContainerEntry`) followed by `StepBalanceConsumers` (does the work). Mirrors the existing categorize→sort separation.

**Why these choices:** The user's framing was "limits configurable in CustomData of the PB" — that's class-level scalars, not per-item entries. Item-acceptance probing was chosen explicitly to cover irrigation systems (mod-only blocks). "Drain excess" was chosen over "fill-only" because the user wants strict target enforcement; the safety valve for hand-loading is the `[NoBalance]` tag, which is more discoverable than tuning targets per block. Discrete steps were preferred over folding probing into `StepCategorizeContainers` because that step already does five things (name tags, stock detection, quota parsing, template sync, category routing).

**Wiki implications:** Per `CLAUDE.md`, the Technical Architecture page (which lists Step 6 as a v2+ placeholder) and the Inventory-Sorter functional-design page (which mentions reactor/weapon/gas balancing as future-work) both need to reflect this v1 implementation. New documentation: a "Container Balancing" section covering grammar, semantics, detection, and excess behaviour, plus updates to the dispatcher step list.

---

## Design summary

### Pipeline placement

```
0  StepRescanIfDue              (existing)
1  StepParseConfigIfDirty       (extend: parse 3 new keys)
2  StepCategorizeContainers     (existing, untouched)
3  StepCategorizeConsumers      (NEW)
4  StepScanInventories          (existing)
5  StepFulfillStockQuotas       (existing)
6  StepSortGenericCargo         (one-line guard added)
7  StepBalanceConsumers         (NEW)
```

The hardcoded `for (int i = 0; i < 6; i++)` in `Program.Dispatcher.cs:63` becomes `i < 8`. `StepLabels` and `StepFor` get two new entries.

Why this order: probing depends on the entries built by `StepCategorizeContainers`, so it goes after step 2. Balancing must run *after* `StepFulfillStockQuotas` (step 5) and `StepSortGenericCargo` (step 6) so it sees the freshest inventory state and can pull from a stock container that was just topped up. Putting balance last also means excess pushed back to category containers won't be re-redistributed by sort within the same cycle — they'll settle on the next pass, which keeps each cycle's transfer count bounded.

### Configuration schema

Three keys live-merged into the existing `[Goose]` section. **No new section** — keeps the user's CustomData surface single-section and matches the existing style. All three are percent-of-volume (0–100, clamped on parse).

```ini
[Goose]
; ... existing keys ...
; Percent (0-100) of each reactor's inventory volume to fill with Uranium. 0 disables.
; Per-block override: name-tag [Balance=N] for unit count; [NoBalance] to opt out.
reactorUraniumFillPercent=80
; Percent (0-100) of each gas generator / irrigation block's inventory volume to fill with Ice. 0 disables.
gasIceFillPercent=80
; Percent (0-100) of each weapon's inventory volume to fill with ammo. 0 disables.
weaponAmmoFillPercent=80
```

Stored on `GooseConfig` (Program.Config.cs:44) as:
- `public int ReactorUraniumFillPercent = 0;`
- `public int GasIceFillPercent = 0;`
- `public int WeaponAmmoFillPercent = 0;`

Defaults are `0` — feature is opt-in, so an existing user's grid behaves identically until they add at least one key. Out-of-range values are clamped to 0–100 with a one-shot warning (`balancer:bad-percent:<key>`).

`StepParseConfigIfDirty` calls `EnsureBalancerKeysPopulated` after parsing. That helper checks each of the three balancer keys against the parsed `MyIni`; for any missing key it calls `_ini.Set` + `_ini.SetComment` to inject the default value plus the hint shown above, then writes the resulting INI string back to `Me.CustomData` and updates `_lastSeenCustomData` so the next cycle does not re-parse. The merge is idempotent — keys the user has already set are preserved untouched.

The hardcoded fuel item types — `Ingot/Uranium` and `Ore/Ice` — live as lazy-init properties in the new `Program.Balancer.cs` partial. Modded fuels (e.g., `Ingot/Thorium`) are explicitly out of scope for v1. A control item type — `Component/SteelPlate` — is also a lazy-init property here for the probing test.

### Per-block override: `[Balance=N]` *(iter 2)*

Any consumer block can override its class percent with a unit count by tagging its CustomName with `[Balance=N]`:

```
"Big Reactor [Balance=200]"          -> fill exactly 200 Uranium ingots
"Cargo O2 Generator [Balance=300]"   -> fill exactly 300 Ice
"Forward Turret [Balance=50]"        -> fill exactly 50 magazines (any accepted ammo type)
```

Parsed by `ParseBalanceTagCount(string name)` (Program.Blocks.cs, alongside `ParsePriorityFromName`); returns `-1` when the tag is absent or malformed. Cached on `ContainerEntry.BalanceTagCount` during `StepCategorizeConsumers`, so the parse runs once per rescan rather than every balance cycle.

A tagged block bypasses the class enable check — even with `reactorUraniumFillPercent=0`, a reactor named `[Balance=100]` is still balanced. This matches user intent (explicit per-block direction overrides class default).

### Consumer detection

A new partial `Goose/Program.Balancer.cs` owns `StepCategorizeConsumers`. After step 2 (`StepCategorizeContainers`) populates `_entryByBlock`, the new step walks each entry and probes its already-cached `Inventory`:

```csharp
enum ConsumerKind { None, Reactor, Gas, Weapon }
```

Detection rules (each is `inv.CanItemsBeAdded(MyFixedPoint.SmallestPossibleValue, t)` against the cached `IMyInventory` — the existing `ContainerEntry.Inventory`):

| Kind     | Required-accept                    | Required-reject               |
|----------|------------------------------------|-------------------------------|
| Reactor  | `Ingot/Uranium`                    | `Component/SteelPlate`        |
| Gas      | `Ore/Ice`                          | `Component/SteelPlate`        |
| Weapon   | *any* known `AmmoMagazine/*`       | `Component/SteelPlate`        |

Detection precedence inside the step: Reactor → Gas → Weapon → None (a block satisfying multiple is unlikely in vanilla; first match wins).

For weapons, the step iterates known `AmmoMagazine/*` types from `_knownItems` plus a small built-in seed list of vanilla magazine subtypes (so probing works on a fresh world before any ammo has been seen). The list of *accepted* magazine types per weapon is cached on `ContainerEntry.AcceptedAmmo` so the balance step doesn't re-probe each tick.

**Exclusion gate (`[NoBalance]`):** before any probing, if `NameHasTag(block.CustomName, "[NoBalance]")` returns true, set `entry.ConsumerKind = None` and skip the probe entirely. Effect: excluded blocks behave like normal generic containers everywhere downstream — sorter still iterates them, balancer ignores them.

**`[NoBalance]` vs `[Ignore]`** — important UX distinction to document in the wiki:

- `[NoBalance]` excludes the block from the balancer *only*. The sorter treats it as a generic container, which means it *will* route items out of the block by category. This is what you want when probing mis-detects a normal cargo container as a consumer.
- `[Ignore]` (existing) makes `IsManaged` return false, so the block is invisible to the entire script. This is what you want when you've hand-loaded a reactor with extra Uranium and don't want anything (sort or balance) to touch it.

Yields: `BudgetExceeded()` after each probed block; `ChunkBoundary` every 25 blocks (matches `StepCategorizeContainers` cadence).

### Balance algorithm

`StepBalanceConsumers` runs three sub-passes per cycle: reactors first, then gas, then weapons (so critical-power demand wins under scarcity). Inside each class, blocks are processed in `_entryByBlock` iteration order — fill-in-order under scarcity is the documented v1 behaviour.

For each consumer entry, `BalanceConsumersOfKind` dispatches per block based on `ContainerEntry.BalanceTagCount`:

```
if entry.BalanceTagCount >= 0:
    BalanceConsumerByCount(entry, kind, dst, entry.BalanceTagCount)   // tagged: count-based
else if classPercent > 0:
    BalanceConsumerByPercent(entry, kind, dst, classPercent)          // untagged + class enabled
else:
    skip                                                                // untagged + class disabled
```

A tagged block bypasses the class enable check, so a reactor named `[Balance=100]` is balanced even when `reactorUraniumFillPercent=0`.

**Count-based (tagged) — Reactor / Gas:**

```
item    = (kind == Reactor) ? IngotUranium : OreIce
current = GetCurrentAmount(dst, item)  // reuses Sorting.cs:69

if current < target:
    needed = target - current
    walk all entries (skip self) whose inventory has `item`;
    TryMove(src, dst, item, needed) until needed reaches 0 or sources exhausted

if current > target:
    excess = current - target
    routes = _containersByCategory[Classify(item)]   // Ingots / Ores
    walk routes; TryMove(dst, route, item, excess) until excess is 0
    if no routes: LogWarningOnce("balancer:no-route:" + cat, ...)
```

**Count-based (tagged) — Weapon:**

```
currentMags = sum of GetCurrentAmount(dst, ammo) for ammo in entry.AcceptedAmmo

if currentMags < target:
    needed = target - currentMags
    for each ammo in entry.AcceptedAmmo:
        walk all entries (skip self); TryMove(src, dst, ammo, needed)
            until needed reaches 0 or sources exhausted

if currentMags > target:
    excess = currentMags - target
    walk items in dst (oldest stack first); for each stack:
        take = min(stackAmount, excess)
        TryMove(dst, route, stack.Type, take) into Ammo-tagged routes
        excess -= moved
    if no Ammo route exists: LogWarningOnce("balancer:no-route:Ammo", ...)
```

**Percent-based (untagged) — unified for all three classes:**

```
targetVolume = ComputeFillTargetVolume(dst.MaxVolume, classPercent)
                = dst.MaxVolume * (classPercent / 100f)

if dst.CurrentVolume < targetVolume:
    PULL: walk all entries (skip self), pull one unit / one magazine at a time
          and re-check dst.CurrentVolume after each transfer until target hit
          or sources exhausted
        For Reactor: item = IngotUranium
        For Gas:     item = OreIce
        For Weapon:  iterate entry.AcceptedAmmo, one ammo type at a time

if dst.CurrentVolume > targetVolume:
    PUSH: one unit / magazine at a time to category-tagged routes
        (Ingots / Ores / Ammo via Classify(item) or hardcoded for weapons)
    if no route exists: LogWarningOnce("balancer:no-route:" + cat, ...)
```

The one-unit-at-a-time pattern is naturally correct for any unit volume (vanilla or modded) — no per-item-volume math needed.

**Cross-consumer transfers:** when filling Reactor A, the balancer is *allowed* to pull Uranium from another reactor that has excess (because the source iteration walks every entry, not just non-consumers). This handles hand-loaded reactors and the `[Balance=N]` re-distribution case naturally without special logic.

**Distribution under scarcity:** *fill in iteration order until source exhausted.* A grid with 250 Uranium and three untagged reactors at 80% fill ends up filling reactors 1 and 2 fully then reactor 3 partially — fewer reactors fully fueled beats all reactors partly fueled (survival-game-friendly). Round-robin is explicitly rejected for v1.

**Yields:** `BudgetExceeded()` after each block; `ChunkBoundary` every 5 blocks (consumer counts are typically smaller than container counts).

### Sort interaction guard

`Program.Sorting.cs:233` — inside `StepSortGenericCargo`, immediately after the existing `if (IsStockTagged(block)) continue;` line:

```csharp
ContainerEntry srcEntry;
_entryByBlock.TryGetValue(block, out srcEntry);
if (srcEntry != null && srcEntry.ConsumerKind != ConsumerKind.None) continue;
```

Note: the existing code already does the `_entryByBlock.TryGetValue` lookup just below this point, so the new check can either reuse that lookup (preferred — moves the lookup up one block) or duplicate it. **Reuse**.

This single guard prevents the sorter from draining Uranium out of reactors, Ice out of gas generators, and ammo out of turrets. `[NoBalance]`-tagged blocks fall through (their `ConsumerKind` is `None`) and continue to participate in normal sort.

### `ContainerEntry` extension

`Goose/Program.Blocks.cs:35-53`:

```csharp
public class ContainerEntry {
    // ... existing fields ...

    /// <summary>Detected consumer class for the balancer; None for non-consumers and [NoBalance]-tagged blocks.</summary>
    public ConsumerKind ConsumerKind = ConsumerKind.None;

    /// <summary>Cached per-weapon list of accepted AmmoMagazine types; null for non-weapons.</summary>
    public List<MyItemType> AcceptedAmmo;
}
```

The `ConsumerKind` enum lives in `Program.Balancer.cs` (the new partial).

### Logging

Reuse existing `LogAction` (Program.Logging.cs) for transfer log lines (debug-only) and `LogWarningOnce` for one-shot diagnostics. New warning keys: `balancer:bad-percent`, `balancer:bad-count`, `balancer:no-route:<Category>`. No new log infrastructure needed.

---

## File responsibility map

| File | Change |
|---|---|
| `Goose/Program.Balancer.cs` *(new)* | `enum ConsumerKind`, static-readonly item type fields, `StepCategorizeConsumers`, `StepBalanceConsumers`, plus pure helpers `ComputeWeaponTargetVolume(maxVol, percent)` and `IsConsumerKindFromProbes(canIngotU, canOreIce, canAmmo, canSteelPlate)` for unit testing. |
| `Goose/Program.Blocks.cs` | Add two fields to `ContainerEntry`: `ConsumerKind`, `AcceptedAmmo`. No method changes. |
| `Goose/Program.Config.cs` | Extend `GooseConfig` with three new fields; extend `StepParseConfigIfDirty` to parse + clamp them. |
| `Goose/Program.Dispatcher.cs` | Bump loop cap 6→8 in `StepRoot`; add two entries to `StepLabels`; add two cases to `StepFor`. |
| `Goose/Program.Sorting.cs` | One-line guard in `StepSortGenericCargo` (skip blocks with `ConsumerKind != None`). |
| `Goose/Goose.csproj` | Add `Program.Balancer.cs` to `<Compile>` items if not picked up by the wildcard. |
| `Goose.Tests/BalancerTests.cs` *(new)* | Theory tests for `ComputeWeaponTargetVolume` (math + clamp) and `IsConsumerKindFromProbes` (truth-table). |
| `Goose.Tests/ConfigTests.cs` | Extend with theories for the three new keys (parse, clamp, defaults). |
| Wiki: `Technical-Architecture-Design` | Update step list to show steps 3 + 7; new "Container Balancing" subsection covering config keys, detection, excess behaviour, `[NoBalance]` tag. |
| Wiki: `Inventory-Sorter — Functional Design` | Update the "Optional Functionality (ON by default)" section to reflect this v1 implementation (link to architecture page for detail). |

No changes needed in: `Program.Discovery.cs` (existing `IsManaged` already includes reactors/weapons/gas), `Program.Catalog.cs`, `Program.Logging.cs`.

---

## Tasks

Each task is independently buildable and committable. Conventional Commits per `CLAUDE.md`.

### Task 1 — Config keys

- [ ] Add `ReactorUraniumPerBlock`, `GasIcePerBlock`, `WeaponAmmoFillPercent` (all `int = 0`) to `GooseConfig` in `Program.Config.cs:44`.
- [ ] In `StepParseConfigIfDirty` (Program.Config.cs:74), parse each via `_ini.Get(section, key).ToInt32(default)`. Clamp `WeaponAmmoFillPercent` to 0–100; clamp the two count keys to ≥ 0. Each clamp emits the corresponding `LogWarningOnce` key.
- [ ] Extend `Goose.Tests/ConfigTests.cs` with three new theory cases (default, valid, out-of-range).
- [ ] Build clean (`dotnet build "Goose.csproj" -c Debug`), tests green (`dotnet test Goose.Tests/Goose.Tests.csproj`).
- [ ] Commit: `feat(balancer): add PB CustomData keys for consumer targets`.

### Task 2 — `ContainerEntry` extension + `ConsumerKind` enum

- [ ] Create `Goose/Program.Balancer.cs` partial with `enum ConsumerKind { None, Reactor, Gas, Weapon }` and the static-readonly item type fields (`IngotUranium`, `OreIce`, `ComponentSteelPlate`).
- [ ] Add `ConsumerKind ConsumerKind = ConsumerKind.None;` and `List<MyItemType> AcceptedAmmo;` fields to `ContainerEntry` in `Program.Blocks.cs:35`.
- [ ] Add the two pure helpers (`ComputeWeaponTargetVolume`, `IsConsumerKindFromProbes`) to `Program.Balancer.cs` as `internal static` so tests can reach them.
- [ ] Build clean.
- [ ] Commit: `refactor(balancer): introduce ConsumerKind and ContainerEntry fields`.

### Task 3 — `StepCategorizeConsumers`

- [ ] Implement in `Program.Balancer.cs`. Walk `_allInventoryBlocks`; for each block, look up the existing `ContainerEntry` from `_entryByBlock`; if `[NoBalance]` tag present, leave `ConsumerKind = None`; else probe in order Reactor → Gas → Weapon. For weapons, populate `AcceptedAmmo` from `_knownItems` filtered to `MyObjectBuilder_AmmoMagazine` plus a small vanilla seed list.
- [ ] Yield: `BudgetExceeded` per block, `ChunkBoundary` every 25.
- [ ] Build clean.
- [ ] Commit: `feat(balancer): probe blocks for consumer kind once per rescan`.

### Task 4 — Sort interaction guard

- [ ] In `StepSortGenericCargo` (Program.Sorting.cs:233), move the existing `_entryByBlock.TryGetValue(block, out srcEntry)` lookup above the route-search and add `if (srcEntry != null && srcEntry.ConsumerKind != ConsumerKind.None) continue;` immediately after `if (IsStockTagged(block)) continue;`.
- [ ] Verify `[NoBalance]`-tagged blocks still participate in sort (their `ConsumerKind` is `None`).
- [ ] Build clean; existing sort tests still green.
- [ ] Commit: `feat(balancer): skip consumer blocks as sort sources`.

### Task 5 — `StepBalanceConsumers`

- [ ] Implement the algorithm above. Reactors first, then Gas, then Weapons. Reuse `TryMove` (Sorting.cs:85), `GetCurrentAmount` (Sorting.cs:69), `_containersByCategory`. Cross-consumer pulls (drain another consumer's excess) are allowed.
- [ ] Excess push-back routes via `Classify(item)` to `_containersByCategory[Ingots|Ores|Ammo]`. Missing route → `LogWarningOnce("balancer:no-route:" + cat, ...)`.
- [ ] For weapons, no per-magazine volume calculation is needed — pull/push one magazine at a time and re-check `inv.CurrentVolume` after each successful transfer (see pseudo-algorithm). This is naturally correct for any magazine size including modded ones, at the cost of a few extra API calls per weapon-cycle. Acceptable for v1; the per-tick budget yield catches it.
- [ ] Yield: `BudgetExceeded` per block, `ChunkBoundary` every 5.
- [ ] Build clean.
- [ ] Commit: `feat(balancer): fill reactor/gas/weapon consumers to PB-configured targets`.

### Task 6 — Wire steps into dispatcher

- [ ] In `Program.Dispatcher.cs:60` (`StepRoot`): change `for (int i = 0; i < 6; i++)` to `i < 8`.
- [ ] Extend `StepLabels` (Dispatcher.cs:45) so the array is exactly 8 entries in this order: `RescanIfDue, ParseConfigIfDirty, CategorizeContainers, CategorizeConsumers, ScanInventories, FulfillStockQuotas, SortGenericCargo, BalanceConsumers`. (The existing entries at indices 3–5 shift right by one.)
- [ ] Rewrite `StepFor` (Dispatcher.cs:80) so all 8 indices are explicit cases, not a `default:` fall-through. Add explicit `case 6: return StepSortGenericCargo();` and `case 7: return StepBalanceConsumers();`; change the `default:` to throw or return a no-op enumerator (defensive against future index drift).
- [ ] Build clean.
- [ ] Commit: `feat(balancer): wire CategorizeConsumers and BalanceConsumers into dispatcher`.

### Task 7 — Tests

- [ ] Create `Goose.Tests/BalancerTests.cs` with two theory classes:
  - `ComputeWeaponTargetVolume_Tests`: `(maxVol=10, pct=80) -> 8.0`, `(0, 50) -> 0`, `(10, 0) -> 0`, `(10, 100) -> 10`, etc.
  - `IsConsumerKindFromProbes_Tests`: truth-table over (canIngotU, canOreIce, canAmmo, canSteelPlate) → expected `ConsumerKind`.
- [ ] All tests green.
- [ ] Commit: `test(balancer): cover weapon target-volume math and consumer-kind detection`.

### Task 8 — Wiki + spec doc updates

- [ ] Update https://github.com/briansokol/SE-Goose/wiki/Technical-Architecture-Design — extend the dispatcher step table to 8 steps; new "Container Balancing" subsection.
- [ ] Update https://github.com/briansokol/SE-Goose/wiki/Inventory-Sorter-‐-Functional-Design — under "Optional Functionality (ON by default)", point the reactor/gas/weapon-balancing entries at the new architecture section and confirm v1 status.
- [ ] Confirm wiki edits with the user before applying (per `CLAUDE.md`).
- [ ] Commit: `docs(balancer): document container balancing in wiki`.

### Task 9 — Build + verification

- [ ] `dotnet build "Goose.csproj" -c Debug` → 0/0.
- [ ] `dotnet build "Goose.csproj" -c Release` → 0/0.
- [ ] `dotnet test Goose.Tests/Goose.Tests.csproj` → all green.
- [ ] C# 7+ syntax audit on new code: 0 hits.
- [ ] Invoke `superpowers:verification-before-completion` and paste actual build + test summaries.
- [ ] Invoke `superpowers:requesting-code-review`.

### Task 10 — In-game smoke test (requires user)

Cannot be agent-executed. Hand off to user.

- [ ] Confirm Release build deployed to `%APPDATA%\SpaceEngineers\IngameScripts\local\Goose\Script.cs`.
- [ ] On a test grid: set `reactorUraniumPerBlock=100`, `gasIcePerBlock=500`, `weaponAmmoFillPercent=80`. Place 250 Uranium ingots in a generic container. Three reactors empty. Expect after a few cycles: reactor1=100, reactor2=100, reactor3=50 (fill-in-order).
- [ ] Hand-load reactor1 with 500 Uranium (target=100). Expect: 400 drain back to an `[Ingots]` category container.
- [ ] Rename reactor1 to add `[NoBalance]`. Confirm balancer leaves it alone (no drain, no fill from balancer). **Note:** because `[NoBalance]` only excludes the balancer, the sorter will treat reactor1 as a generic source — if any `[Ingots]`-tagged container exists, sort will move Uranium out of reactor1. Use `[Ignore]` instead to fully exclude. Verify both behaviours.
- [ ] Set up an O2 generator and an irrigation system; both should be detected as Gas consumers and filled with Ice.
- [ ] Set up two turrets accepting different ammo (e.g., gatling + assault cannon); both should fill to ~80% volume with their respective ammo.
- [ ] Negative: set `weaponAmmoFillPercent=200`. Expect single `balancer:bad-percent` warning, percent clamped to 100.
- [ ] Negative: remove every `[Ingots]` container, set non-zero reactor target. Hand-load a reactor with excess. Expect `balancer:no-route:Ingots` warning, no exception.

---

## Verification (end-to-end)

1. **Unit:** `dotnet test Goose.Tests/Goose.Tests.csproj` — `BalancerTests` and extended `ConfigTests` all green.
2. **Build:** Debug + Release both 0/0.
3. **C# 6 audit:** no forbidden constructs in new code.
4. **In-game (Task 10):**
   - Reactors fill to target from generic and stock containers.
   - Gas generators + irrigation systems both detected and filled with Ice.
   - Weapons fill to ≈ percent target by volume.
   - Excess drains back to category containers.
   - `[NoBalance]` excludes balancer only; `[Ignore]` excludes the whole script.
   - Sort no longer drains consumer blocks.
   - Bad config values produce one-shot warnings without exceptions.
5. **Docs:** Wiki + `.specs/` reflect the feature.

---

## Critical files

Modified:
- `Goose/Program.Config.cs` — three new `GooseConfig` fields + parsing.
- `Goose/Program.Blocks.cs` — two new `ContainerEntry` fields.
- `Goose/Program.Dispatcher.cs` — bump step cap, add labels, add cases.
- `Goose/Program.Sorting.cs` — one-line guard.
- `Goose.Tests/ConfigTests.cs` — extend with three new key theories.

New:
- `Goose/Program.Balancer.cs` — enum, helpers, two new steps.
- `Goose.Tests/BalancerTests.cs` — two theory classes.

Read-only reference:
- `.specs/v1-foundation-and-sorting.md` — original v1 spec; defines `ContainerEntry`, `StockQuota`, `QuotaMode`.
- `.specs/stock-container-name-tag-overrides.md` — same-shape recent feature; good template for tests and commit cadence.
- `.specs/2026-04-30-container-balancing-design.md` — extracted design summary (sibling spec).
- `Goose/Program.Sorting.cs:45,69,85` — `MoveAllOfType`, `GetCurrentAmount`, `TryMove` (reused; do not duplicate).

---

## Out of scope (explicit non-goals)

- **Per-item fine-grained class config** (e.g., `Ingot/Uranium=100` line under `[Goose]`). Considered and rejected during brainstorming in favour of the three class-scalar percent keys. *Per-block* overrides via `[Balance=N]` are now supported (iter 2) but they're a per-block tag, not a per-item-type config.
- **Modded fuel/control items** beyond `Ingot/Uranium`, `Ore/Ice`. Modded thorium reactors and the like will appear as Reactor consumers (probe accepts Uranium, rejects SteelPlate) but will only be balanced for Uranium; their other fuel slots are ignored. v1 limitation; revisit in v2.
- **Round-robin distribution under scarcity.** Considered and rejected; fill-in-order is the v1 behaviour.
- **Refinery/Assembler input feeding.** Wiki lists this as separate v2+ work. Not part of this balancer.
- **Cross-grid (connector) balancing.** Wiki notes connectors and `[ALLOW]` tags as v2+. Balancer respects the existing same-construct rule from `IsManaged`.
