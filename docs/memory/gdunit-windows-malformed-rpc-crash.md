# Windows GdUnit bridge crash in runtime test assemblies

On Windows with Godot `4.7.2.stable.mono.official.ed1daf0bf`, .NET SDK `10.0.401`, `gdUnit4.api` `5.1.0-rc5`, and `gdUnit4.test.adapter` `3.1.1`, GdUnit can terminate the Godot bridge with exit code `-1073741819` (`0xC0000005`) before VSTest receives any result. The native failure happens during bridge startup/discovery; a targeted filter then misleadingly reports that no test matches.

The regression is bisected from the last known green CI commit `f95995a2e57db44876861c5d604814e1400281b1`:

- `f3e317f` is the first bad commit when the full test assembly is loaded. It introduced target transport and malformed manual RPC coverage in `GameplayActionRunnerNetworkTest`.
- With that suite excluded, `4e6d43d` is the first bad commit. It introduced `InteractionOfferTest` and its related interaction-offer surface.
- A clean `f95995a` worktree passes `CarryComponentTest` 2/2 on the same Windows machine and toolchain.

Symptoms include:

- `[GdUnit4] Rebuilding Godot Project ends with exit code: -1073741819`;
- `MSB6006: dotnet.exe` exits with code `1`;
- a targeted filter reports that no test matches because the bridge died before reporting results.

Temporary policy: guard the complete `GameplayActionRunnerNetworkTest` and `InteractionOfferTest` suites with `#if !GODOT_WINDOWS`. Keep malformed-RPC coverage in a dedicated non-Windows file. A runtime early return, or an MSBuild-only exclusion, is too late for the affected assemblies because the bridge can crash while loading/discovering them. Do not treat changing the SDK, deleting `.godot`, or broadening the test filter as a fix; those only remove unrelated variables.

