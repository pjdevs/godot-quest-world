# Godot .NET runtime target

- Godot 4.7.1 Mono in this workspace loads project assemblies with the installed .NET 10 runtime. Targeting `net11.0` compiles with the preview SDK but fails when Godot loads the assembly because `System.Runtime, Version=11.0.0.0` is unavailable.
- Keep `LangVersion=preview` for the C# union-type syntax while targeting `net10.0` for Godot compatibility. Revisit the target only when the Godot Mono distribution is upgraded to a .NET 11 runtime.
- CSharpier cannot parse the preview `union` declaration. `csharpier format .` reports
  `error CS1001: Identificateur attendu` for the file holding it and skips that file; the rest of the
  project is still formatted and `dotnet build` stays clean. This is expected, not a regression: keep
  union files hand-formatted in the project style.
