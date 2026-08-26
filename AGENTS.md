# Quest World

Prototyping project for gameplay system.

## Project Context

### Goal

Port personal custom gameplay system from Unreal (Interaction, Quest, Dialog and Inventory coming from [this repository](https://github.com/pjdevs/QuestWorld))
and build a demo game that use this systems in reel conditions.

## Technical Environment

### Architecture

Vertical, one folder per feature with subfolder `scripts` etc.

### Tools

- Godot CLI available in PATH : `godot`
- `dotnet`

### Mandatory Rules

- After each code (not docs tasks) task run `csharpier format .`, `dotnet build` and `$env:GODOT_BIN = (Get-Command godot).Source; dotnet test` on Windows
  and `GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot dotnet test` on macOS (`godot` is on path but as a symlink so it breaks in headless).
- After each task maintain a doc per feature in `docs/feature/<thedoc>.md`
- After each workflow pitfall or hard user correction on the implementation (not tweaking a feature or corrections),
  document it in `docs/memory/<desc_of_things>.md`
- Before each task, check if docs or memory can be useful
- Read [this](./docs/code-style.md) for coding style

## Game Design Environment

Anything that refer to game/level/narrative design of the demo should be confronted to [the one pager](./docs/one-pager.md).
