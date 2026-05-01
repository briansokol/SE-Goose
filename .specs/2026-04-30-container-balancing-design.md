# Container Balancing — Design

> Brainstorming spec for the container-balancing feature. Companion to the implementation plan in `.specs/container-balancing.md`.

**Date:** 2026-04-30 (initial design); 2026-05-01 (iteration 2)
**Status:** approved, implemented; superseded in part by iter-2 changes (see top of `.specs/container-balancing.md`)

> **Iteration 2 changes** (2026-05-01) — recorded here so this snapshot remains an accurate record of the original design plus its first revision:
>
> - PB-level keys are **all percent-of-volume**, not count. `reactorUraniumPerBlock` → `reactorUraniumFillPercent`, `gasIcePerBlock` → `gasIceFillPercent` (both clamped 0–100). The semantic across all three classes is unified to "fill X% of the block's inventory volume with the relevant item."
> - **Per-block override via `[Balance=N]` name tag** added. Forces a single block to be balanced to N units (count) instead of the class percent. Works on all three classes; tagged blocks bypass the class enable check.
> - **Live-merge of balancer keys into PB CustomData** added. On parse, missing keys are appended with default 0 and a one-line `MyIni` comment hint.
> - **Algorithm** generalised: per-block dispatch on `BalanceTagCount` (≥0 → count-based, otherwise → percent-volume fill). The percent-volume path uses one-unit-at-a-time pulls/pushes so it works for any unit volume.

## Goal

Add a "balancer" subsystem that keeps fuel/ammo levels at a configurable target on three classes of consumer blocks:

- **Reactors** — `Ingot/Uranium`, count target.
- **O2/H2 Generators + Irrigation systems** — `Ore/Ice`, count target.
- **Weapons** (turrets, fixed guns) — any accepted `AmmoMagazine/*` subtype, percent-of-volume target.

Configured PB-wide in CustomData. Per-block opt-out via a `[NoBalance]` name tag (balancer-only opt-out; use existing `[Ignore]` to exclude from the whole script).

## User-confirmed design decisions

1. **Configuration shape:** three per-block-class scalar keys in the existing `[Goose]` section of the PB CustomData — not per-item-type entries.
2. **No per-block target overrides** — PB is the single source of truth for "how much."
3. **Detection by item-acceptance probing**, not vanilla-interface whitelist. Auto-handles modded irrigation/reactors/weapons without maintaining a type list.
4. **Pull from any container** by priority. Stock containers are fair game.
5. **Drain excess back to category containers** when a block has more than its target. Strict target enforcement.
6. **`[NoBalance]` name tag** opts a block out of the balancer only. Sorter still iterates it.
7. **Discrete two-step pipeline addition**: `StepCategorizeConsumers` → `StepBalanceConsumers`.

## Pipeline placement

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

Why this order: probing depends on entries built by `StepCategorizeContainers`. Balancing runs *after* `StepFulfillStockQuotas` and `StepSortGenericCargo` so it sees the freshest inventory state and can pull from a stock container that was just topped up. Excess pushed back to category containers won't be re-redistributed by sort within the same cycle — they'll settle on the next pass, which keeps each cycle's transfer count bounded.

## Configuration schema

Three keys appended to the existing `[Goose]` section. **No new section.**

```ini
[Goose]
; ... existing keys ...
reactorUraniumPerBlock=100      ; integer ingot count per reactor; 0 disables
gasIcePerBlock=500              ; integer ore count per gas generator / irrigation system; 0 disables
weaponAmmoFillPercent=80        ; 0–100, percent of inventory volume per weapon; 0 disables
```

Stored on `GooseConfig` as `int ReactorUraniumPerBlock = 0`, `int GasIcePerBlock = 0`, `int WeaponAmmoFillPercent = 0`. Defaults `0` — opt-in.

Out-of-range `WeaponAmmoFillPercent` clamped 0–100 with one-shot warning `balancer:bad-percent`. Negative count values clamped to 0 with `balancer:bad-count`.

The hardcoded fuel item types — `Ingot/Uranium` and `Ore/Ice` — and the control item `Component/SteelPlate` live as private static readonly fields in `Program.Balancer.cs`. Modded fuels are out of scope for v1.

## Consumer detection

A new partial `Goose/Program.Balancer.cs` owns `StepCategorizeConsumers`. After step 2, walk each entry and probe its cached `Inventory`:

```csharp
enum ConsumerKind { None, Reactor, Gas, Weapon }
```

Detection rules (each is `inv.CanItemsBeAdded(MyFixedPoint.SmallestPossibleValue, t)`):

| Kind     | Required-accept              | Required-reject               |
|----------|------------------------------|-------------------------------|
| Reactor  | `Ingot/Uranium`              | `Component/SteelPlate`        |
| Gas      | `Ore/Ice`                    | `Component/SteelPlate`        |
| Weapon   | *any* `AmmoMagazine/*`       | `Component/SteelPlate`        |

Precedence: Reactor → Gas → Weapon → None. First match wins.

For weapons, iterate known `AmmoMagazine/*` types from `_knownItems` plus a small vanilla seed list. Cache *accepted* magazine types per weapon on `ContainerEntry.AcceptedAmmo` so the balance step doesn't re-probe each tick.

**Exclusion gate (`[NoBalance]`):** before any probing, if `NameHasTag(block.CustomName, "[NoBalance]")` returns true, set `ConsumerKind = None` and skip the probe entirely.

**`[NoBalance]` vs `[Ignore]`:**

- `[NoBalance]` — excludes the balancer only. Sorter treats it as a generic container.
- `[Ignore]` — `IsManaged` returns false; the block is invisible to the entire script.

Yields: `BudgetExceeded()` per block; `ChunkBoundary` every 25.

## Balance algorithm

`StepBalanceConsumers` iterates `_entryByBlock.Values` once per cycle. Order: reactors first, then gas, then weapons.

**Reactor / Gas (count target):**

```
target  = (kind == Reactor) ? config.ReactorUraniumPerBlock : config.GasIcePerBlock
item    = (kind == Reactor) ? IngotUranium : OreIce
current = GetCurrentAmount(entry.Inventory, item)

if current < target:
    needed = target - current
    walk entries (priority order, skip self) whose inventory has `item`;
    TryMove(src, entry.Inventory, item, needed) until needed == 0 or sources exhausted

if current > target:
    excess = current - target
    routes = _containersByCategory[Classify(item)]   // Ingots / Ores
    walk routes by priority; TryMove(entry.Inventory, route.Inventory, item, excess)
        until excess == 0 or all routes full
    if no routes: LogWarningOnce("balancer:no-route:" + cat, ...)
```

**Weapon (volume-percent target):**

```
percent = config.WeaponAmmoFillPercent
if percent == 0: skip block (feature disabled)

targetVolume = inv.MaxVolume * (percent / 100f)

if inv.CurrentVolume < targetVolume:
    for each ammoType in entry.AcceptedAmmo:
        if inv.CurrentVolume >= targetVolume: break
        walk source containers in priority order:
            TryMove(src, inv, ammoType, amount=1)  // one mag at a time
            recheck inv.CurrentVolume; if >= targetVolume: break

if inv.CurrentVolume > targetVolume:
    walk items in inv (oldest stack first):
        if inv.CurrentVolume <= targetVolume: break
        TryMove(inv, route.Inventory, item.Type, amount=1) for each route
            in _containersByCategory[Ammo] (priority order)
        recheck inv.CurrentVolume after each successful move
    if no Ammo route: LogWarningOnce("balancer:no-route:Ammo", ...)
```

**Cross-consumer transfers:** filling Reactor A is allowed to drain Reactor B's excess (where `B.current > B.target`). Handles hand-loaded reactors naturally.

**Distribution under scarcity:** *fill in priority order until source exhausted.* 250 Uranium across three reactors at target 100 → 100/100/50. Round-robin rejected.

**Yields:** `BudgetExceeded()` per block; `ChunkBoundary` every 5.

## Sort interaction guard

`Program.Sorting.cs:233` — inside `StepSortGenericCargo`, immediately after `if (IsStockTagged(block)) continue;`:

```csharp
ContainerEntry srcEntry;
_entryByBlock.TryGetValue(block, out srcEntry);
if (srcEntry != null && srcEntry.ConsumerKind != ConsumerKind.None) continue;
```

Reuse the lookup that exists below this point in the original code.

## `ContainerEntry` extension

Two new fields in `Program.Blocks.cs`:

```csharp
public ConsumerKind ConsumerKind = ConsumerKind.None;
public List<MyItemType> AcceptedAmmo;   // null for non-weapons
```

## Logging

Reuse `LogAction` (debug-only transfer log) and `LogWarningOnce`. New keys: `balancer:bad-percent`, `balancer:bad-count`, `balancer:no-route:<Category>`.

## Out of scope (v1)

- Per-block target overrides.
- Modded fuel/control items beyond `Ingot/Uranium` / `Ore/Ice`.
- Per-item fine-grained config (`Ingot/Uranium=100` style).
- Round-robin scarcity distribution.
- Refinery/Assembler input feeding (separate v2+ work).
- Cross-grid (connector) balancing.
