# macOS Godot headless CLI can hang before project logs

With Godot 4.7.2 Mono on this checkout, `godot --headless --path . --quit-after 3` can remain
running after printing only the engine banner. The same behavior was reproduced with a minimal
scene and with `--editor`; stopping the process produced exit code 130 and no project log beyond
the banner.

This is independent of the tested scene: GdUnit runs successfully when `GODOT_BIN` points to the
full Mono binary at `/Applications/Godot_mono.app/Contents/MacOS/Godot`. Until the startup issue
is resolved, treat the standalone headless CLI check as an environment blocker rather than evidence
of a project scene failure.

The same checkout can also print `handle_crash: Program crashed with signal 11` while returning zero
to the shell. Verbose output shows leaked editor input/shortcut objects and `Editor*Plugin` wrappers,
or leaked C# scripts/resources when a gameplay scene is started. This shutdown failure is distinct
from a project teardown error reported before it: an invalid project signal disconnect should still
be fixed in the project, while this resource cleanup crash belongs to the Godot Mono/editor runtime.
