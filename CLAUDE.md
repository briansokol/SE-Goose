# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **Space Engineers Programmable Block Script** for **inventory management**. The script runs on an in-game programmable block and is intended to monitor, organize, and report on item inventories across connected cargo containers, refineries, assemblers, connectors, and other inventory-bearing blocks on a grid.

The project uses the Malware Development Kit (MDK2) framework to develop, build, and deploy the script into Space Engineers.

## Technology Stack

- **Framework**: .NET Framework 4.8 (C# 6.0)
- **Target Platform**: Space Engineers Programmable Blocks
- **Build System**: MDK2 (Malware Development Kit 2)
- **Package Manager**: NuGet
- **Root Namespace**: `IngameScript`
- **Entry Point**: `Program.cs` implementing `MyGridProgram`

## CRITICAL: C# 6.0 Language Constraints

**ALL CODE MUST BE COMPATIBLE WITH C# 6.0**. This project is constrained to C# 6.0 syntax and features due to Space Engineers API limitations (see `<LangVersion>6</LangVersion>` in `Goose.csproj`). DO NOT use features from C# 7.0 or later, including:

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

## Custom Data System

Space Engineers blocks expose a per-block custom data string. Use it for configuration:

- Store configuration in INI format via `MyIni` (from `VRage.Game.ModAPI.Ingame.Utilities`).
- Use a consistent section header for this project (suggested: `[Goose]`).
- Parse and validate custom data on each update cycle, or only when the block is (re)discovered — weigh cost against flexibility.

## Block Group System

Operate on blocks within a named group configured via the Programmable Block's custom data:

- Groups the relevant inventory blocks for coordinated management.
- Automatically discover and categorize blocks within the group.
- Support hot-swapping of blocks without script restart by re-scanning when groups change.

## Update Frequency

- Typical cadence: every 100 game ticks (~1.67 seconds at normal sim speed) for monitoring scripts.
- Set via `Runtime.UpdateFrequency = UpdateFrequency.Update100;` in the constructor.
- Heavy inventory scans may justify spreading work across `Update10` ticks instead of doing everything at once.

## Build Commands

```bash
# Build the project (Debug configuration)
dotnet build "Goose.csproj" -c Debug

# Build for Release
dotnet build "Goose.csproj" -c Release

# Build solution file
dotnet build "SE-Goose.sln"
```

**Always build after code changes** to verify the script compiles cleanly. Build errors here mean the script won't load in-game.

## MDK2 Configuration

Build behavior is controlled by:

- `mdk.ini`: Project-specific MDK settings (in source control).
- `mdk.local.ini`: Local development overrides (not in source control).

### Script Minification

Configured in `mdk.ini`:

- `minify=none`: No optimization (default for development)
- Other options: `trim`, `stripcomments`, `lite`, `full`

### File Exclusions

Files matching these patterns are excluded from the packaged script:

- `obj/**/*`
- `MDK/**/*`
- `**/*.debug.cs`

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
