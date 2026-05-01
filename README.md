# Goose

An automatic inventory sorter for Space Engineers — a single Programmable Block script that watches your grid's containers and quietly keeps everything in the right place.

## What it does

You tell Goose where each kind of item belongs by adding short tags to your container names. Goose handles the rest:

- Items end up in the containers you marked for that category.
- "Stock" containers can hold a fixed amount of a specific item — Goose tops them up and pushes any excess somewhere else.
- When more than one container is tagged for the same category, you can set a priority order so the first one fills up before the next.
- Any block you don't want Goose to touch can be excluded with a single tag.

It runs continuously in the background. Drop ore into any cargo container on the grid and within a couple of seconds it's sitting with the rest of your ore. Build a turret that needs ammo and the right rounds will arrive. Empty a "Stock" container of steel plates and Goose refills it from your bulk storage.

## Setting it up

1. Place a Programmable Block on your grid.
2. Load the Goose script into it.
3. Tag your cargo containers (see below).
4. Run the Programmable Block — Goose starts sorting on the next tick.

That's the whole setup. There is no separate control panel or mod to install.

## Tagging containers

Tags are written in square brackets anywhere in the container's name. You can combine them.

### Category tags

Add one (or more) of these to a container to tell Goose what belongs in it:

| Tag | Holds |
|---|---|
| `[Ingots]` | Refined metals |
| `[Ores]` | Raw ore |
| `[Components]` | Construction components |
| `[Prototech]` | Prototech components |
| `[Tools]` | Hand tools, oxygen and hydrogen bottles |
| `[Weapons]` | Hand weapons |
| `[Ammo]` | Ammunition |
| `[Consumables]` | Food and drink |
| `[Ingredients]` | Cooking ingredients |
| `[Meals]` | Prepared meals |
| `[Misc]` | Anything not covered above |

Examples:
```
Cargo - Ingots [Ingots]
Cargo - Ammo & Weapons [Ammo][Weapons]
Cargo - Catch-all [Misc]
```

A container can carry multiple category tags and will accept items from any of them.

### Priority tag

If you have several containers for the same category, add `[P:NN]` to control fill order. `[P:01]` is the highest priority and `[P:99]` is the lowest. Containers without a priority tag fill last.

```
Cargo - Ores Primary [Ores][P:01]
Cargo - Ores Overflow [Ores][P:50]
```

### Stock tag

Add `[Stock]` to make a container hold a managed quota of specific items. Then put the quota rules in that container's **Custom Data**:

```
[Goose]
SteelPlate=100        ; Keep exactly 100 — top up from elsewhere, push excess away
Girder=50M            ; Keep at least 50 — never pushes excess out
InteriorPlate=200L    ; Cap at 200 — never pulls more in, pushes excess away
Construction=All      ; Fill this slot to capacity with this item
```

The suffix letters are case-insensitive. Use the in-game item subtype name (the same name you'd see when crafting).

### Exclusion tags

Goose will leave a block alone if its name contains either of:

- `[Ignore]` — Goose pretends it doesn't exist.
- `[Locked]` — same effect, useful for containers you've manually locked for a specific purpose.

## Configuring the Programmable Block

The Programmable Block's own Custom Data accepts a few optional settings:

```
[Goose]
rescanIntervalTicks=600       ; How often to rediscover blocks (in ticks)
budgetFraction=0.5            ; How much per-tick processing time to use (0–1)
debugLogging=false            ; Log every transfer to the action log
maxActionLogEntries=48        ; How many recent actions to remember
maxWarningEntries=32          ; How many distinct warnings to remember
```

You can also override the category Goose picks for a specific item. This is useful when an item lands in `Misc` but you'd rather see it sorted somewhere else:

```
Override.MyObjectBuilder_Component/SteelPlate=Misc
```

## Commands

You can send these as the argument to a "Run" terminal action (or from a button panel, sensor, timer block, etc.):

| Command | Effect |
|---|---|
| `rescan` | Force Goose to rediscover all blocks on the grid right now |
| `pause` | Stop sorting (Goose stays loaded but does nothing) |
| `resume` | Start sorting again |
| `debug on` / `debug off` | Toggle verbose action logging |

## Status display

The Programmable Block's detail panel shows a live status readout — recent actions, any warnings, and a summary of what Goose is currently doing. Open the PB in the terminal to check on it.

## What Goose doesn't do (yet)

This is the v1 feature set. A few things from the broader design are not in this release:

- It will not move items between grids connected by a ship connector.
- It does not yet manage refineries, assemblers, reactors, or gas generators.
- It does not yet drive dedicated LCD panels — the status is in the PB's own detail panel.

These are tracked in the project's design docs and will arrive in later versions.
