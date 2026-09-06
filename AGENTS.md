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
- Task available in PATH: `task` (`Taskfile.yml` is the platform-agnostic entry point)

### Documentation structure

- `docs/feature/<feature>/<feature>.md` is the durable technical reference for a feature. It documents the current architecture, invariants, boundaries and behavior, and contains an `Architecture decisions` section for durable ADRs. It should describe the system as it exists now, not preserve migration history.
- `docs/feature/<feature>/planned/` contains only genuinely future work: proposals, designs and implementation plans that are still relevant. Once a plan is implemented, move any durable architectural decisions into the feature's main document and delete the implemented plan instead of keeping it as an archive.
- `addons/<plugin>/README.md` is concise and user-facing. It should explain what the plugin does, how to install/configure it, the minimum concepts/API needed to use it, and a small usage example when useful. Detailed internal architecture belongs in the feature documentation, not in the README.
- Root `docs/*.md` files are for shared or cross-feature documentation that does not naturally belong to one feature, such as framework-wide principles and game-wide design documents.
- `docs/memory/` stores reusable workflow pitfalls and operational knowledge. It is not a substitute for feature architecture documentation or ADRs.

### Mandatory Rules

- After each code (not docs tasks) task run `task format` and `task build`.
- `task ci` is the complete local/CI gate: it runs `format:check`, `build`, then the explicitly confirmed full suite.
- Run the smallest impacted test scope first with Task: `task test:suite SUITE=<SuiteName>` or one of the feature tasks documented in [the test strategy](./docs/feature/testing/testing.md).
- For changes touching network authority, peer lifecycle, replication, shared test fixtures, the GdUnit adapter, or cross-feature runtime behavior, also run `task test:network` or `task test:runtime` as appropriate.
- The full suite is deliberately not the default and is usually a bad first reflex. Before running it, challenge whether the change is cross-cutting, changes shared infrastructure/fixtures, or is being validated before merge/CI. Only then run `task test:full CONFIRM_FULL=yes`.
- On Windows, Task sets `GODOT_BIN` to `godot`, which must be available on `PATH`; when it is not, use the direct fallback command with the executable path.
- On macOS, Task uses `/Applications/Godot_mono.app/Contents/MacOS/Godot` because the `godot` PATH entry is a symlink that breaks in headless mode.
- Direct fallback commands remain `dotnet test --filter "FullyQualifiedName~<SuiteName>"` with the platform-specific `GODOT_BIN` above.
- After each task maintain a doc per feature in `docs/feature/<thedoc>.md`
- CSharpier is the source of truth for C# formatting. For conventions it does not enforce, follow the surrounding code and existing project patterns.
- Keep the relevant feature documentation current when architecture, invariants, boundaries or public usage materially change.
- After each workflow pitfall or hard user correction on the implementation (not tweaking a feature or corrections),
  document it in `docs/memory/<desc_of_things>.md`
- Before each task, check if docs or memory can be useful

## Game Design Environment

Anything that refer to game/level/narrative design of the demo should be confronted to [the one pager](./docs/one-pager.md).
