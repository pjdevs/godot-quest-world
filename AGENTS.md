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
- CSharpier is the source of truth for C# formatting. For conventions it does not enforce, follow the surrounding code instead of maintaining a separate prose style guide.
- Before each task, check whether existing feature docs or `docs/memory/` entries are relevant.
- After each workflow pitfall or hard user correction on the implementation (not feature tweaking or ordinary corrections), document it in `docs/memory/<desc_of_things>.md`.

## Documentation Structure

Repository documentation has four distinct roles. Keep them separate instead of duplicating the same information in several places.

### Feature architecture reference

- `docs/feature/<feature>/<feature>.md` is the durable technical reference for that feature.
- It describes the architecture that exists now: responsibilities, boundaries, invariants, important flows and integration points.
- Durable architectural decisions belong in an `Architecture decisions` section in this document.
- Do not keep migration history or already-completed implementation plans here unless the historical fact is required to understand a current invariant.
- After a feature change, update this document when the current architecture, public boundary or durable decision changed.

### Planned work

- `docs/feature/<feature>/planned/` contains only genuinely future work: proposals, designs and implementation plans that are not fully implemented yet.
- Treat `planned/` as a roadmap, not an archive.
- Once a plan is implemented, move any durable decisions or invariants that are still useful into the feature architecture reference, then delete the implemented plan.
- If a plan is partially implemented or stale, reduce it to the remaining future scope rather than preserving obsolete steps.

### Plugin README

- `addons/<plugin>/README.md` is concise and user-facing.
- Focus on what the plugin does, how to use/configure it, the minimum concepts/API a consumer needs, and a small example when useful.
- Do not duplicate deep internal architecture, historical decisions or implementation plans there; link to the corresponding feature doc when more detail is needed.

### Shared root documentation

- `docs/*.md` is for repository-wide or cross-feature material that does not naturally belong to one feature, such as framework design principles, global project/game design, or shared conventions.
- `docs/memory/` is for reusable operational knowledge and workflow pitfalls. It is not a substitute for feature architecture decisions or planned design work.

## Game Design Environment

Anything that refer to game/level/narrative design of the demo should be confronted to [the one pager](./docs/one-pager.md).
