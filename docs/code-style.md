# Code style

## Purpose

The project uses CSharpier for useful C# syntax and formatting checks while keeping the conventions already used by the Godot codebase.

## Conventions

- Namespaced files use file-scoped namespaces, for example `namespace QuestWorld.Character;`.
- `using` directives stay at the top of the file, before the namespace.
- Files that integrate with Godot through the global namespace are not wrapped in an artificial namespace.
- Member access does not require `this.`. Private field names remain free to follow the surrounding code instead of being forced into aCSharpier naming policy.
- XML documentation is optional for gameplay and integration methods;   CSharpier documentation and file-header warnings are disabled.
- Unused `using` directives produce build warnings and are removed by the Roslyn formatter.
- Target-typed object creation keeps parentheses attached to `new`, as in `new()` and `new(argument)`.
- Long argument and parameter lists are chopped when they exceed the configured 120-character line length.
- CSharpier spacing, punctuation, trailing-comma, brace, member-ordering, and blank-line rules remain active; `dotnet format` is allowed to apply those conventions.
- Private fields are prefixed `_`
- Multiline call should be like this with last `)` on its own line:

  ```cs
  obj.Call(
    arg1,
    ....
  );
  ```

## Validation

Run the project checks from the repository root:

```powershell
dotnet format quest-world.csproj
dotnet build
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test
```
