---
name: csharp-style
description: Use when writing or editing C# code in the SE-Goose repository - encodes the project's subjective style conventions (braces, naming, var usage, null checks, member order, etc.) so generated code matches existing files without rework.
---

# C# Style — SE-Goose

Apply these conventions to every C# file you write or edit in this repo. They are *project preferences*, not language rules — pick them over whatever the language allows.

For XML doc requirements and the C# 6.0 language constraint (Goose and Crane projects only — Shared and tests use `LangVersion=latest`), see CLAUDE.md. This skill covers everything else.

## Quick reference

| Topic | Rule |
|---|---|
| Braces | Allman — opening brace on its own line |
| Single-line `if`/`for`/`while`/`foreach` | **Always** wrap the body in braces, even one statement |
| `var` | Only when the type is obvious from the right-hand side |
| Private fields | `_camelCase` (leading underscore) |
| `readonly` | Mark every private field that is only assigned in the constructor |
| `private` modifier | Always write it explicitly; don't rely on the default |
| `this.` prefix | Only when needed to disambiguate a parameter from a field |
| Null checks | `== null` / `!= null` |
| Strings | `$"..."` interpolation; reserve `+` for trivial 1–2-term joins, `StringBuilder` for loops |
| `using` directives | Outside the namespace. `System.*` group first, then others, each group alphabetical |
| Expression-bodied (`=>`) | Trivial one-liners only (return a value, call one method). Block body for anything multi-step |
| Loops | `foreach` unless you genuinely need the index |
| Control flow | Guard clauses / early returns over deep nesting |
| Member order | `const` → fields → constructors → properties → methods. Public before private within each group |
| Line length | Soft 120 chars. Don't break lines if breaking hurts readability |
| `#region` | Don't use. The codebase has none — keep it that way. Use `partial` classes if a file gets too big (see `Program.Dispatcher.cs`, `Program.Autocraft.cs`) |

## Examples

### Braces — always

```csharp
// Yes
if (item == null)
{
    return;
}

// No — even one statement gets braces
if (item == null) return;
if (item == null)
    return;
```

### `var` — only when obvious

```csharp
var customers = new List<Customer>();    // obvious from `new`
var count = (int)reader.Field;            // obvious from cast
int total = CalculateTotal();             // explicit — return type isn't visible
Item item = repo.Find(id);                // explicit — return type isn't visible
```

### Fields, readonly, naming

```csharp
class Dispatcher
{
    const int MaxRetries = 3;

    private readonly ILogger _logger;
    private int _tickCount;

    public Dispatcher(ILogger logger)
    {
        _logger = logger;        // no `this.` needed — `_logger` is unambiguous
    }
}
```

### Strings — interpolation

```csharp
// Yes
Echo($"Found {count} items in {bin.Name}");
var msg = $"Error: {ex.Message}";

// No
Echo("Found " + count + " items in " + bin.Name);
var msg = string.Format("Error: {0}", ex.Message);
```

### Expression-bodied — trivial only

```csharp
// Yes
public int Count => _items.Count;
public string FullName => $"{First} {Last}";
public Item Get(int id) => _store[id];

// No — multi-step work belongs in a block
public void Process()
{
    Validate();
    _store.Save();
}
```

### Member order

```csharp
class Foo
{
    // 1. const
    const int Max = 10;

    // 2. fields
    private readonly Store _store;
    private int _count;

    // 3. constructors
    public Foo(Store store) { _store = store; }

    // 4. properties
    public int Count => _count;

    // 5. methods — public before private
    public void DoThing() { /* ... */ }

    private void Helper() { /* ... */ }
}
```

### Guard clauses

```csharp
// Yes
public void Process(Item item)
{
    if (item == null)
    {
        return;
    }
    if (!item.IsValid)
    {
        return;
    }

    _store.Save(item);
}

// No — deep nesting
public void Process(Item item)
{
    if (item != null)
    {
        if (item.IsValid)
        {
            _store.Save(item);
        }
    }
}
```

## When existing code disagrees

Match the surrounding file's style when it's clearly intentional and consistent within that file. If the file is mixed or arbitrary, follow this skill. Don't reformat unrelated code just to bring it in line — surgical edits only.
