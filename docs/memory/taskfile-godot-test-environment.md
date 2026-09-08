# Taskfile and the Godot test environment

The GdUnit VSTest adapter needs `GODOT_BIN` when a suite has `[RequireGodotRuntime]`. Task v3 supports task-level environment variables, but setting `env` inside an individual `cmd` did not propagate `GODOT_BIN` to the child `dotnet test` process in this project.

`Taskfile.yml` therefore routes test execution through platform-specific helper tasks and sets `GODOT_BIN` at the task level:

- macOS: `/Applications/Godot_mono.app/Contents/MacOS/Godot`
- Windows: `godot`, resolved through `PATH` by the test environment
- Linux: `command -v godot`, resolved to an absolute executable path before invoking the adapter

`[RequireGodotRuntime]` only tells the adapter to start Godot; it does not imply headless execution.
Every test command therefore also loads `gdunit4.runsettings`, which passes `--headless` through the
adapter's supported `GdUnit4/Parameters` setting. Do not append arguments to `GODOT_BIN`: that variable
must remain an executable path.

The local GdUnit adapter exposes `[TestCategory]` through the VSTest property `TestCategory`, so filters use `TestCategory=Network` and `TestCategory=Runtime`. `Category=...` does not select these suites with the versions pinned by this project.

The full suite is guarded by `CONFIRM_FULL=yes` so running it is an explicit decision rather than the default feedback loop.
