# Godot rewrites the script left open in the editor

## Symptom

A source file the change never touched appears modified after validation, with every line
re-indented from four spaces to tabs:

```text
 M addons/interaction_plugin/examples/interactive/InteractiveActor.cs
 1 file changed, 108 insertions(+), 108 deletions(-)
```

The diff is pure indentation, so it is easy to stage by accident and it makes `git stash pop`
abort in the middle of a restore.

## Cause

`.godot/editor/editor_layout.cfg` remembers the scripts open in the editor:

```text
open_scripts=["res://addons/interaction_plugin/examples/interactive/InteractiveActor.cs"]
```

Any Godot run that boots the editor infrastructure re-saves those scripts with Godot's own tab
indentation, overriding `.editorconfig` and CSharpier. Two commands in the standard validation do it:

- `dotnet test`, because GdUnit4 launches Godot through `gdunit4_testadapter_v5/`;
- `Godot --headless --path . --editor --quit`, used to generate `.cs.uid` for new scripts.

`Godot --headless --path . --scene ... --quit-after N` does **not** rewrite anything.

## What to do

- Check `git status` after `dotnet test` and restore the file when the only diff is indentation:

  ```bash
  git checkout HEAD -- addons/interaction_plugin/examples/interactive/InteractiveActor.cs
  ```

- Run the formatter and this check **after** the tests, not before, so the tree stays clean.
- Never `git stash` while such a rewrite is pending: the pop restores the untracked files, then
  aborts on the modified file and leaves the tracked changes in the stash. Recover with
  `git checkout stash@{0} -- .` followed by `git checkout HEAD -- <rewritten file>`.
