# Task CLI unavailable in the managed Windows shell

The repository workflow is exposed through `Taskfile.yml`, but the `task` executable may be unavailable in a managed Windows shell. When that happens, use the direct command represented by the relevant task: run CSharpier from the restored local tool, invoke `dotnet build`, and invoke `dotnet test` with `gdunit4.runsettings` and an explicit `GODOT_BIN` path. Do not change the platform-specific `GODOT_BIN` value in `Taskfile.yml` as a workaround.
