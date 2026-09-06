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

### Documentation structure

- `docs/feature/<feature>/<feature>.md` is the durable technical reference for a feature. It documents the current architecture, invariants, boundaries and behavior, and contains an `Architecture decisions` section for durable ADRs. It should describe the system as it exists now, not preserve migration history.
- `docs/feature/<feature>/planned/` contains only genuinely future work: proposals, designs and implementation plans that are still relevant. Once a plan is implemented, move any durable architectural decisions into the feature's main document and delete the implemented plan instead of keeping it as an archive.
- `addons/<plugin>/README.md` is concise and user-facing. It should explain what the plugin does, how to install/configure it, the minimum concepts/API needed to use it, and a small usage example when useful. Detailed internal architecture belongs in the feature documentation, not in the README.
- Root `docs/*.md` files are for shared or cross-feature documentation that does not naturally belong to one feature, such as framework-wide principles and game-wide design documents.
- `docs/memory/` stores reusable workflow pitfalls and operational knowledge. It is not a substitute for feature architecture documentation or ADRs.

### Mandatory Rules

- After each code (not docs tasks) task run `csharpier format .`, `dotnet build` and `$env:GODOT_BIN = (Get-Command godot).Source; dotnet test` on Windows
  and `GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot dotnet test` on macOS (`godot` is on path but as a symlink so it breaks in headless).
- CSharpier is the source of truth for C# formatting. For conventions it does not enforce, follow the surrounding code and existing project patterns.
- Keep the relevant feature documentation current when architecture, invariants, boundaries or public usage materially change.
- After each workflow pitfall or hard user correction on the implementation (not tweaking a feature or corrections),
  document it in `docs/memory/<desc_of_things>.md`
- Before each task, check if docs or memory can be useful

## Game Design Environment

Anything that refer to game/level/narrative design of the demo should be confronted to [the one pager](./docs/one-pager.md).
