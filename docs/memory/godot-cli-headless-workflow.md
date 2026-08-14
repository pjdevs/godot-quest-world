# Godot CLI headless workflow

## Commands

Use the Godot Mono CLI available in `PATH`:

```powershell
godot --version
dotnet --version
dotnet build quest-world.sln
godot --headless --path . --scene res://quest_world/character/Character.tscn --quit-after 2 --log-file .godot/character-runtime.log
godot --headless --path . --scene res://quest_world/levels/test_world.tscn --quit-after 2 --log-file .godot/test-world-runtime.log
godot --headless --path . --editor --quit --log-file .godot/headless.log
```

## Environment findings

- On the managed Windows workspace, `dotnet build` may need escalated execution because the default user `NuGet.Config` can be inaccessible to the sandbox even when `Godot.NET.Sdk` is already cached.
- Godot headless may fail while opening `user://logs`; always pass a writable `--log-file` for scripted validation.
- Headless runs can return code `0` while printing non-fatal environment errors such as the root certificate-store warning or editor-settings write errors. Inspect the output and log, not only the exit code.
- Do not commit temporary NuGet or dotnet-home workarounds used only to get around the managed environment.
