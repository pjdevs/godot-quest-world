# Game Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with verification checkpoints.

**Goal:** Implement the generic `game_session` addon described by `docs/feature/game_session/planned/game-session-design.md`, including persistent participants, replicated world travel, global readiness barriers, late join handling, and QuestWorld integration.

**Architecture:** `GameSession` composes `NetworkSession` and owns only persistent `PlayerState` nodes plus the current world beneath persistent spawner roots. Both participant and world lifecycles use custom `MultiplayerSpawner` spawn data; the server owns admission, travel IDs, world replacement, and readiness state, while clients only instantiate, acknowledge, and report local failures.

**Tech Stack:** Godot 4.7.2 C#, .NET 10 preview, ENet high-level multiplayer, GdUnit4Net.

**Spec:** `docs/feature/game_session/planned/game-session-design.md`

## Global Constraints

- `game_session` may depend on `network_session` but not on QuestWorld, Character, Interaction, GameplayAction, Inventory, or project spawner abstractions.
- `GameSession` must be explicitly initialized and safe when `NetworkSession` is already active.
- `PlayerState` identity is assigned before tree entry and remains server-authoritative.
- Exactly zero or one managed world may exist under `WorldContainer`; `SceneTree.CurrentScene` remains the persistent game root.
- Global travel waits for local world readiness and every remote participant present at travel start; late join readiness must not put existing peers into `Traveling`.
- Expected stale/refused requests are ignored or rejected without entering `Failed`; unrecoverable local replacement failures enter `Failed`.
- Every code task runs `csharpier format .`, `dotnet build`, and Godot-backed `dotnet test` on Windows.
- Each feature task updates `docs/feature/<feature>.md`; workflow pitfalls are recorded under `docs/memory/`.

---

### Task 1: Add the generic participant model and configuration validation

**Files:**
- Create: `addons/game_session/scripts/GameSessionState.cs`
- Create: `addons/game_session/scripts/PlayerState.cs`
- Create: `addons/game_session/scripts/GameSession.cs`
- Create: `addons/game_session/README.md`
- Create: `addons/game_session/tests/GameSessionConfigurationTest.cs`
- Create: `addons/game_session/tests/GameSessionParticipantTest.cs`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- `PlayerState.ParticipantId`, `PlayerState.PeerId`, and `PlayerState.InitializeIdentity(long participantId, long peerId)`.
- `GameSession.Initialize()`, `GameSession.Reset()`, `GameSession.PlayerStateScene`, `GameSession.Players`, `GameSession.PlayerStateSpawner`, `GameSession.WorldContainer`, and `GameSession.WorldSpawner`.
- `GameSession.State`, `IsAcceptingPlayers`, `CurrentTravelId`, `CurrentWorld`, `CurrentWorldPath`, `PlayerStates`, `TryGetPlayerStateByPeerId(long, out PlayerState)`, and `TryGetPlayerStateByParticipantId(long, out PlayerState)`.

- [x] **Step 1: Write tests for identity-before-ready, offline/host/dedicated/client admission, indexes, and invalid configuration.** Use real Godot nodes and a derived `PlayerState` scene; assert that missing dependencies leave the session uninitialized and that offline initialization creates exactly one state.
- [x] **Step 2: Run `dotnet test --filter FullyQualifiedName~GameSessionConfigurationTest` and `...~GameSessionParticipantTest` to observe the expected missing-type failures.**
- [x] **Step 3: Implement the enum, base state, exported dependencies, validation, custom player-state spawn factory, participant indexes, admission reconciliation, and lifecycle signals.**
- [x] **Step 4: Run the focused suites and confirm they pass.**
- [x] **Step 5: Run formatting/build/tests required by `AGENTS.md` and update the feature doc with the public contract.**

### Task 2: Implement native PlayerState replication and peer lifecycle

**Files:**
- Modify: `addons/game_session/scripts/GameSession.cs`
- Create: `addons/game_session/tests/GameSessionNetworkTest.cs`
- Create: `addons/game_session/tests/GameSessionTestFixtures.cs`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- `PlayerJoined(PlayerState)`, `PlayerLeft(long participantId, long peerId)`, and `GameSession.CanJoin(long peerId, out string reason)`.
- Server-side `PlayerStateSpawner.Spawn(Variant)` with `{ participant_id, peer_id }` data and client-side reconstruction through the same factory.

- [x] **Step 1: Add network tests for server admission, exact replication, no client-authored state, late join state reconstruction, disconnect cleanup, custom `CanJoin`, refusal, and `IsAcceptingPlayers`.**
- [x] **Step 2: Run the focused network suite and verify it fails for the absent addon behavior.**
- [x] **Step 3: Wire peer signals, `RefuseNewConnections`, server-only admission/despawn, custom spawn/despawn lifecycle, and derived `PlayerStateScene` validation.**
- [x] **Step 4: Run the network suite, then the participant/runtime suites.**
- [x] **Step 5: Run required format/build/test commands and document any replication ordering findings.**

### Task 3: Implement replicated current-world spawning and offline travel

**Files:**
- Modify: `addons/game_session/scripts/GameSession.cs`
- Create: `addons/game_session/tests/GameSessionTravelTest.cs`
- Create: `addons/game_session/tests/GameSessionTravelWorld.cs`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- `Travel(PackedScene worldScene)` returning `bool`.
- `TravelStarted(long travelId, string resourcePath)`, `WorldLoaded(long travelId, Node world)`, `TravelCompleted(long travelId, Node world)`, and `TravelFailed(long travelId, string reason)`.

- [x] **Step 1: Add offline tests for first travel, replacement travel, persistent session/player state, preflight failure, duplicate/in-progress/client rejection, one-world invariant, and ready lifecycle after `_Ready()`.**
- [x] **Step 2: Run the focused travel suite and observe the expected failures.**
- [x] **Step 3: Implement validated scene preflight, monotonic travel IDs, persistent `WorldSpawner` custom spawn data, old-world retirement, local readiness observation, offline barrier completion, and failure transitions.**
- [x] **Step 4: Run the travel suite and verify each semantic signal fires once.**
- [x] **Step 5: Run required format/build/test commands and update the feature doc.**

### Task 4: Add network travel readiness and late-join handling

**Files:**
- Modify: `addons/game_session/scripts/GameSession.cs`
- Modify: `addons/network_session/scripts/NetworkSession.cs` only if a narrow transport hook is required by tests
- Create: `addons/game_session/tests/GameSessionTravelNetworkTest.cs`
- Modify: `docs/feature/game_session/game-session.md`
- Create: `docs/memory/game-session-replication-findings.md` when a new Godot ordering/ownership pitfall is confirmed

**Interfaces:**
- `PlayerWorldReady(PlayerState)`.
- Reliable RPCs `TravelReady(long)`, `CompleteTravel(long)`, `CurrentWorldReady(long)`, and `WorldLoadFailed(long, string)` addressed by peer identity and validated against the current travel.

- [ ] **Step 1: Add real ENet in-process tests for host/client barrier waiting, local-before-global readiness, duplicate/stale ACKs, disconnect release, dedicated-server local readiness, exactly-once completion, client load failure removal, late-join reconstruction, and late-join-only `PlayerWorldReady`.**
- [ ] **Step 2: Run the focused network travel suite and verify it fails before implementation.**
- [ ] **Step 3: Implement server barrier snapshots, ACK validation, reliable completion notification, late-join pending state, failure reporting, and per-participant readiness signals without changing existing peers to `Traveling`.**
- [ ] **Step 4: Run the network travel suite, then all game-session suites.**
- [ ] **Step 5: Run required format/build/test commands and record any confirmed Godot multiplayer lifecycle pitfall.**

### Task 5: Migrate the QuestWorld root to persistent GameSession

**Files:**
- Create: `addons/game_session/GameSession.tscn` if authored composition improves runtime integration
- Create: `quest_world/network/QuestWorldPlayerState.cs`
- Modify: `quest_world/network/QuestWorldNetworkPlayers.cs`
- Modify: `quest_world/world/World.cs`
- Modify: `quest_world/levels/test_world.tscn`
- Modify: `quest_world/levels/facility.tscn`
- Create/modify: QuestWorld integration tests under `quest_world/tests/`
- Modify: `docs/feature/network_session/network_session.md`
- Modify: `docs/feature/character/replication.md`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- Persistent `Game.tscn`/runtime root owns `NetworkSession`, `GameSession`, `QuestWorldNetworkPlayers`, and the optional local controller.
- `QuestWorldNetworkPlayers` consumes `PlayerJoined`, `PlayerLeft`, `PlayerWorldReady`, and `TravelCompleted`; it owns only Character/Pawn spawning, authority, possession, and explicit current-world context.

- [ ] **Step 1: Add integration tests proving Character/Pawn nodes are recreated per world while derived PlayerState data and GameSession survive travel.**
- [ ] **Step 2: Run the integration suite and observe failures against the old world-owned lifecycle.**
- [ ] **Step 3: Move startup and player glue to the persistent root, remove World’s NetworkSession/NetworkPlayers ownership, replace `CurrentScene` world lookups with injected `GameSession.CurrentWorld`, and preserve offline/host demo behavior.**
- [ ] **Step 4: Run the integration tests and headless `test_world` smoke scene.**
- [ ] **Step 5: Run required format/build/test commands and update feature docs.**

### Task 6: Final verification and review checkpoint

**Files:**
- Modify: only files needed for fixes found by verification/review
- Modify: `docs/feature/game_session/game-session.md`

- [ ] **Step 1: Re-read the spec and check all 40 minimum behaviors against tests and public API.**
- [ ] **Step 2: Run `csharpier format .`, `dotnet build`, and the full Godot-backed test suite from the worktree.**
- [ ] **Step 3: Run headless runtime smoke checks for the persistent root and demo world, inspect logs, and review `git diff --check`.**
- [ ] **Step 4: Dispatch a code review against the worktree diff, fix Critical/Important findings with new failing tests first, and rerun verification.**
- [ ] **Step 5: Commit the implementation on `feat/game-session` with a focused message and report the worktree path, commit, tests, and any non-blocking environment warnings.**
