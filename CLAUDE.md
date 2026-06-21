# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This repository contains **two companion Space Engineers Programmable Block Scripts** and a shared code library:

- **Goose**: inventory management. Monitors, organizes, and reports on item inventories across connected cargo containers, refineries, assemblers, connectors, and other inventory-bearing blocks. Also manages refinery/reactor stocking. INI section: `[Goose]`.
- **Crane**: production management (autocrafting). Manages assembler queues to maintain user-defined item quotas, with multi-assembler dispatch and capability-aware blueprint resolution. INI section: `[Crane]`.

The two scripts run as separate PBs on the same grid, each with their own CustomData configuration. They do not call each other directly; coordination is implicit (Crane queues items, Goose moves them).

Both scripts use the Malware Development Kit (MDK2) framework for development, build, and deployment into Space Engineers.

### Repository Layout

```
Goose/          Goose script project (Goose.csproj + Program.*.cs partials)
Crane/          Crane script project (Crane.csproj + Program.*.cs partials)
Shared/         Mixin source library, compiled into BOTH scripts via
                <Compile Include="..\Shared\..." LinkBase="..." /> in each
                script's .csproj. Holds reusable components: Scope, Ini,
                Blocks (tag helpers), Items, Inventory, Logging, Dispatcher,
                Autocraft. Not a referenced assembly; source-level include.
Goose.Tests/    Goose-only test project.
Crane.Tests/    Crane-only test project.
Shared.Tests/   Tests for the shared library (the bulk of the test suite).
SE-Goose.sln    Solution file containing all of the above.
```

**Shared discovery model:** Both scripts use the same scope-based block discovery (see "Block Discovery & Scope" below). A change to that model should usually be made in `Shared/Scope/` so both scripts benefit.

## Design Documentation

Authoritative design documentation for this project lives in the GitHub wiki. **Consult these documents before making architectural or feature-level decisions.** They represent the intended design of the script and its features.

Current design documents:

- **Technical Architecture** — overall technical architecture of the Goose script — https://github.com/briansokol/SE-Goose/wiki/Technical-Architecture-Design
- **Inventory Sorter (Functional Design)** — functional design of Goose's inventory sorting feature — https://github.com/briansokol/SE-Goose/wiki/Inventory-Sorter-%E2%80%90-Functional-Design
- **Production Management (Functional Design)** — functional design of the Crane companion script (autocrafting and refinery management) — https://github.com/briansokol/SE-Goose/wiki/Production-Management-%E2%80%90-Functional-Design

This list will grow as additional features are planned; more documents will be added here over time.

**Local wiki clone:** The wiki repo (`git@github.com:briansokol/SE-Goose.wiki.git`) is cloned next to this repo at `../SE-Goose.wiki` (i.e., a sibling of the `SE-Goose` directory). Always read and edit wiki content through that clone. Do not create alternate clones in `tmp/`, `/tmp`, scratch directories, or anywhere else. If the sibling clone is missing, clone it there first with `git clone git@github.com:briansokol/SE-Goose.wiki.git ../SE-Goose.wiki` and then use it; do not work around its absence with a different location. Always pull the latest changes before editing.

**Keeping the docs in sync:** If a decision made during implementation contradicts any of these documents, update the corresponding wiki document so it reflects the new decision. If the contradiction is significant or the right path forward is unclear, surface it to the user before proceeding rather than silently diverging from the documented design.

## Plans

**All implementation plans must be written into the `.specs` folder** at the repository root. When you produce a plan for a multi-step task, save it as a file under `.specs/` rather than leaving it only in the conversation. Create the folder if it does not yet exist.

## Technology Stack

- **Framework**: .NET Framework 4.8 (C# 6.0)
- **Target Platform**: Space Engineers Programmable Blocks
- **Build System**: MDK2 (Malware Development Kit 2)
- **Package Manager**: NuGet
- **Root Namespace**: `IngameScript`
- **Entry Point**: `Program.cs` implementing `MyGridProgram`

## CRITICAL: C# 6.0 Language Constraints

**ALL CODE MUST BE COMPATIBLE WITH C# 6.0**. This project is constrained to C# 6.0 syntax and features due to Space Engineers API limitations (see `<LangVersion>6</LangVersion>` in `Goose.csproj`, `Crane.csproj`, and `Shared.csproj`). The constraint applies to `Shared/` code too, since those sources are compiled directly into both scripts. DO NOT use features from C# 7.0 or later, including:

- **Readonly structs** (C# 7.2) - Use regular structs or const fields instead
- **Tuple syntax** `(int, string)` (C# 7.0) - Use named classes or structs
- **Pattern matching** with `is` expressions (C# 7.0)
- **Out variables** `int.TryParse(s, out var result)` (C# 7.0) - declare the variable first
- **Expression-bodied constructors/destructors** (C# 7.0)
- **Local functions** (C# 7.0)
- **Ref returns and ref locals** (C# 7.0)

**Available C# 6.0 features:**

- Expression-bodied members for properties and methods
- Auto-property initializers
- String interpolation `$"text {variable}"`
- Null-conditional operators `?.` and `?[]`
- `nameof` expressions
- Exception filters in catch blocks

## General Guidance

**When there isn't a clear ideal solution to a problem, ASK QUESTIONS.** Don't make assumptions about implementation details, design preferences, or trade-offs without consulting the user first. Present options when multiple valid approaches exist.

## Code Quality Principles

1. **Avoid Code Duplication**: Always prefer refactoring code into reusable methods rather than duplicating logic. If you find yourself copying similar code blocks, extract them into a shared method.

2. **Clarify Before Acting**: If a user's request is ambiguous or lacks necessary details, ask clarifying questions before proceeding with implementation. It's better to understand the requirements fully than to make incorrect assumptions.

3. **Present Options**: When multiple valid approaches exist (different algorithms, architectures, or trade-offs), present the options to the user with pros and cons, and let them choose the direction. Don't arbitrarily pick one approach when the ideal solution isn't clear.

## Code Documentation

**Always add C#/.NET-style XML documentation comments (docblocks) above every function, method, class, struct, enum, property, and class field.** Use the standard `/// <summary>`, `/// <param>`, and `/// <returns>` tags. Keep summaries concise — one short sentence is usually enough. Use `<param>` and `<returns>` only when they add information that isn't obvious from the parameter or return type.

Example:

```csharp
/// <summary>Classifies an item into one of the configured categories.</summary>
/// <param name="type">Item type to classify.</param>
/// <returns>The matched category, or <c>Misc</c> if unrecognized.</returns>
ItemCategory Classify(MyItemType type) { ... }
```

**Keep inline comments minimal.** Only add an inline comment when the _why_ is non-obvious — a hidden constraint, a subtle invariant, a workaround for a specific bug, or behavior that would surprise a reader. Avoid comments that:

- Describe _what_ the code does when the code is already self-explanatory.
- Reference the version, change, or task that introduced a method (e.g., `// v1: ...`, `// NEW: shared helper introduced by the plan refactor`). That history belongs in the commit log, not the code.
- Restate the docblock.

If a comment would be useful as documentation rather than as a margin note, fold it into the XML docblock instead.

## Custom Data System

Space Engineers blocks expose a per-block custom data string. Use it for configuration:

- Store configuration in INI format via `MyIni` (from `VRage.Game.ModAPI.Ingame.Utilities`).
- Section headers identify the owning script:
    - **Goose PB**: `[Goose]` section.
    - **Crane PB**: `[Crane]` section.
    - Per-block configs (e.g., the `[CCraft]` LCD's quota lines) use the same section as their owning script.
- Each script lazily auto-populates its recognised keys into the PB CustomData on first parse (`EnsureConfigKeysPopulated`), preserving existing user keys and comments. Add new config keys via that path so users get usable defaults without manual editing.
- Parse and validate custom data on each update cycle, or only when the block is (re)discovered — weigh cost against flexibility.

## Block Discovery & Scope

Both scripts discover the blocks they manage by building a **scope**: the set of grid EntityIds that count as "in scope" for the PB. Scope construction is shared via `Shared/Scope/ScopeBuilder.cs` and works the same way for Goose and Crane:

- **Seed grid**: `Me.CubeGrid` (the PB's own grid).
- **Mechanical edges**: traverses rotor/hinge/piston connections into subgrids by default. A mechanical block whose CustomName carries the `[NoSubgrid]` tag blocks the edge.
- **Connector edges**: traverses docked connectors **only** when the PB-side connector carries the `[Federate]` tag and the script's `enableConnectorFederation` config flag is true (default true in both scripts).
- **Drift detection**: a rolling hash of the mechanical + connector edge state is compared each cycle; a change triggers a rescan.

Once scope is built, each script enumerates blocks via `GridTerminalSystem.GetBlocksOfType<T>(list, predicate)`, filtering with `_scopeGrids.Contains(block.CubeGrid.EntityId)` plus any role-specific filters (interface type, name tags such as `[Ignore]` / `[CCraft]` / `[CError]`). Neither script uses a manually configured block group.

Hot-swapping (adding/removing blocks, docking, undocking) is handled automatically by the next rescan, either on the `rescanIntervalTicks` cadence, when scope drift is detected, or when the `rescan` command is issued via the PB.

## Update Frequency

- Typical cadence: every 100 game ticks (~1.67 seconds at normal sim speed) for monitoring scripts.
- Set via `Runtime.UpdateFrequency = UpdateFrequency.Update100;` in the constructor.
- Heavy inventory scans may justify spreading work across `Update10` ticks instead of doing everything at once.

## Build Commands

```bash
# Build the whole solution (preferred; covers Goose, Crane, Shared, and all test projects)
dotnet build SE-Goose.sln -c Debug

# Build a single script
dotnet build Goose/Goose.csproj -c Debug
dotnet build Crane/Crane.csproj -c Debug

# Release build
dotnet build SE-Goose.sln -c Release

# Run all tests
dotnet test SE-Goose.sln
```

**Always run `dotnet format SE-Goose.sln` before every build** to apply the project's C# style rules from `.editorconfig`.

**Always build after code changes** to verify the scripts compile cleanly. A change to `Shared/` affects both Goose and Crane, so always build the full solution after touching Shared. Build errors mean the script won't load in-game.

### Script Size Budget

Space Engineers rejects Programmable Block scripts over **100,000 characters**. Both scripts pack with `minify=full`, and packed size is the binding constraint on new features.

**After completing each new feature, measure the minified output and report the remaining headroom for both scripts.** Building the solution packs each script to `<output>/<ScriptName>/script.cs`, where `<output>` comes from the `output` key in each project's `mdk.local.ini`:

```bash
dotnet build SE-Goose.sln -c Release
wc -c <output>/Goose/script.cs <output>/Crane/script.cs
```

Report the character count and headroom (100,000 minus count) per script, e.g. "Goose: 65,389 (34,611 headroom); Crane: 55,187 (44,813 headroom)". If a feature pushes either script near the limit, see `.specs/2026-06-11-script-size-reduction.md` for which size optimizations actually pay off under full minification (string literals and un-renamable API spellings matter; statement-count dedup and `var` do not).

## MDK2 Configuration

Build behavior is controlled by:

- `mdk.ini`: Project-specific MDK settings (in source control).
- `mdk.local.ini`: Local development overrides (not in source control).

### Script Minification

Configured in `mdk.ini`:

- `minify=none`: No optimization (default for development)
- Other options: `trim`, `stripcomments`, `lite`, `full`

#### Minification Safety (full minify)

Both scripts pack with `minify=full`, which **renames identifiers** (types, members, locals, enum members) and **strips all `using` directives**. Code that compiles fine under `dotnet build` can still fail or misbehave in-game because of this. Two rules:

- **Never derive a user-visible string or a tag-match key from an identifier name.** `enumValue.ToString()`, `nameof(...)`, and string interpolation of an enum (`$"{category}"`) all resolve to the renamed single-character name in the packed script. Back every label, log message, and tag comparison with an explicit string literal instead (e.g. route categories through `CategoryName(ItemCategory)` / the `CategoryTags` array). Enum values are still fine as dictionary keys or dedup keys, since those are never displayed.
- **Fully qualify any type from a namespace the in-game compiler does not auto-import.** Space Engineers injects a fixed set of `using`s (it does **not** include `System.Globalization`), and full minify removes the directives the source relied on. So write `System.Globalization.CultureInfo` / `System.Globalization.NumberStyles` (and similar) inline rather than depending on a `using`.

### File Exclusions

Files matching these patterns are excluded from the packaged scripts (each script has its own `mdk.ini` that lists exclusions):

- `{Goose,Crane}/obj/**/*`
- `{Goose,Crane}/MDK/**/*`
- `**/*.debug.cs`

## Branching

**Never commit directly to `main`.** All work — features, refactors, docs, even one-line fixes — lands on `main` through pull requests from a topic branch.

**Before every commit, check the current branch.** Run `git rev-parse --abbrev-ref HEAD` (or read `git status`'s first line) and confirm it's not `main`. Don't make a commit on `main` even if the change is small or tests pass; the rule is no exceptions.

If you find yourself on `main` and about to commit, **stop and ask the user** whether to create a new branch (and what to call it). Don't auto-name a branch and proceed silently. Suggested naming follows the existing convention in this repo:

- `feat/<short-name>` — new features
- `fix/<short-name>` — bug fixes
- `refactor/<short-name>` — code restructuring
- `chore/<short-name>` — tooling, deps, process notes
- `docs/<short-name>` — documentation-only changes
- `test/<short-name>` — test additions

If you've already accidentally committed to `main`, recover by creating a branch from the current `main` HEAD, then resetting `main` back to its upstream (`git reset --hard origin/main`). Verify the working tree's uncommitted changes survive (stash first if needed). Always confirm with the user before running `git reset --hard`.

## Git Commits

**All commits must use the [Conventional Commits](https://www.conventionalcommits.org/) format:**

```
<type>[optional scope]: <short description>

[optional body]

[optional footer(s)]
```

**Types:**

| Type       | When to use                                  |
| ---------- | -------------------------------------------- |
| `feat`     | A new feature                                |
| `fix`      | A bug fix                                    |
| `docs`     | Documentation changes only                   |
| `style`    | Formatting, whitespace — no logic change     |
| `refactor` | Code restructuring without behavior change   |
| `test`     | Adding or updating tests                     |
| `chore`    | Build process, tooling, config, dependencies |
| `perf`     | Performance improvements                     |

**Examples:**

```
feat(sorting): add category-based item priority sorting

fix(discovery): handle null inventory on newly placed blocks

docs: update wiki links in CLAUDE.md

refactor(dispatcher): extract shared block-scan helper method

chore: update MDK2 to latest version
```

**Rules:**

- Use lowercase for type and description.
- Keep the subject line under 72 characters.
- Use an imperative, present-tense verb ("add", "fix", "update" — not "added", "fixes").
- Add a body when the "why" needs explanation; leave it out when the subject line is sufficient.

## Versioning

Goose is versioned (Crane is not yet). The single source of truth is the
`Version X.Y.Z` line in `Goose/Instructions.readme`. It follows semantic
versioning, and while Goose is pre-1.0 (`0.x.y`) the bump rule is:

| Change to Goose | Version bump | Example |
| --------------- | ------------ | ------- |
| Breaking change | minor (`Y`)  | `0.30.0` -> `0.31.0` |
| `feat`          | minor (`Y`)  | `0.30.0` -> `0.31.0` |
| `fix` / `perf`  | patch (`Z`)  | `0.30.0` -> `0.30.1` |
| `docs` / `chore` / `style` / `test` / `refactor` | no bump | `0.30.0` (unchanged) |

**When a change affects Goose, bump the `Version` line in the same commit**
that makes the change, according to the table above. Bump only once per
commit even if the diff touches several files. Changes that do not affect
Goose (Crane-only work, shared-library work that Goose does not consume,
pure tooling) do not bump the Goose version.

## Communication

- Be clear about what you're doing and why.
- Explain trade-offs when they exist.
- Ask questions when requirements are unclear.
- Confirm understanding of complex or unusual requests before implementing.

## Key Space Engineers API Documentation

- **PB API Reference**: https://malforge.github.io/spaceengineers/pbapi/
    - Look up any class by its full namespace + `.html` (e.g., `IMyCargoContainer` → https://malforge.github.io/spaceengineers/pbapi/SpaceEngineers.Game.ModAPI.Ingame.IMyCargoContainer.html).
- Inventory-relevant interfaces to know: `IMyInventory`, `IMyInventoryItem`, `MyItemType`, `IMyCargoContainer`, `IMyRefinery`, `IMyAssembler`, `IMyShipConnector`, `IMyShipController`, `IMyTerminalBlock.HasInventory`.

When implementing against an unfamiliar API, fetch the current docs first rather than guessing — the PB API is a restricted subset of the full Space Engineers API and method availability shifts between game versions.
