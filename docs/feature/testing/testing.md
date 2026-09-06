# Test strategy

## Principle

The default workflow is to run the smallest scope that can prove the change. The full suite is a deliberate validation step, not the first command to reach for.

Before running every test, challenge the reason:

- Does the change affect shared fixtures, the GdUnit adapter, test infrastructure, or several features?
- Does it affect network authority, peer lifecycle, replication, or another cross-cutting runtime path?
- Is this the validation required before merging or by CI?

If the answer is no, run the impacted suite or feature task instead.

## Classifications

| Classification | Meaning | Current examples |
| --- | --- | --- |
| `Runtime` | Needs Godot objects, a scene runner, frames, physics, input, or another engine lifecycle. | Most current suites, including the Interaction behavior suites. |
| `Network` | Exercises peer setup, authority, replication, late join, acknowledgement, or peer lifecycle. | `InteractionNetworkTest`, `InteractionNetworkStateTest`, `InteractionNetworkLateJoinTest`, `InteractionNetworkBehaviorTest`, `InteractionAckTest`, and the GameplayAction `*NetworkTest` suites. |
| `Fast` | Reserved for tests that do not need the Godot runtime and can run without `[RequireGodotRuntime]`. There are no suites in this class yet. | Add a dedicated suite when pure logic is extracted. |

Categories are declared on the test suite with `[TestCategory("Runtime")]` or `[TestCategory("Network")]`. A suite belongs to one primary category; a network suite is understood to be runtime-backed as well.

The current 321 cases are classified as 253 `Runtime` cases and 68 `Network` cases. No `Fast` suite exists yet because the current test assembly still requires the Godot runtime for every suite.

`InteractionBehaviorTest` was split into focused suites without deleting cases:

- `InteractionExecutionBehaviorTest`
- `InteractionTimedExecutionTest`
- `InteractionFocusAndAvailabilityTest`
- `InteractionInputTest`
- `InteractionStatefulBehaviorTest`
- `InteractionConcurrencyTest`
- `InteractionNetworkBehaviorTest`

Their shared world builders and test doubles live in `InteractionTestBase`. Each test still builds and owns its own world; the base class is a code-sharing boundary, not a shared mutable fixture.

The large network suite follows the same rule and is split into `InteractionNetworkTest`, `InteractionNetworkStateTest`, and `InteractionNetworkLateJoinTest`, while sharing `InteractionNetworkTestBase`.

## Commands

Task is the platform-agnostic entry point. It configures the correct headless Godot executable on macOS and expects `godot` on `PATH` on Windows.

```text
task --list
task format
task format:check
task build
task test:suite SUITE=CharacterBehaviorTest
task test:suite SUITE=InteractionNetworkTest
task test:interaction
task test:gameplay
task test:character
task test:stateful
task test:network
task test:runtime
task ci
```

The full suite requires an explicit confirmation. This is intentional: the caller must have challenged whether the extra feedback is worth the cost.

```text
task test:full CONFIRM_FULL=yes
```

When Task is unavailable, use the VSTest filter directly:

```text
GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot dotnet test --filter "FullyQualifiedName~InteractionNetworkTest"
```

On Windows, set `GODOT_BIN` to the Godot executable path if `godot` is not on `PATH`. See `AGENTS.md` for the required build and validation policy.

## Which scope to run

| Change | First command | Add this when relevant |
| --- | --- | --- |
| One test or one suite | `task test:suite SUITE=<SuiteName>` | The feature task if helpers or neighboring suites changed. |
| Interaction behavior | `task test:interaction` | `task test:network` for authority/replication/lifecycle changes. |
| GameplayAction behavior | `task test:gameplay` | `task test:network` for network suites or authority changes. |
| Shared Godot/GdUnit fixture or adapter | `task test:runtime` | `task test:network` if peer behavior can be affected. |
| Cross-feature refactor or pre-merge validation | — | `task test:full CONFIRM_FULL=yes`, after the criteria above are satisfied. |

Splitting a suite is useful when its name, setup, or execution mode no longer tells the reader what it proves. It is not a reason to remove coverage or to create one file per test. A focused suite should keep a coherent behavior boundary and remain independently runnable.
