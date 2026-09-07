# Game Session V1 Hardening Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the landed `game_session` V1 into a deterministic lifecycle foundation by removing invalid state combinations, restoring the `NetworkSession` transport boundary, making local replica lifecycle signals symmetric, unifying world-ready acknowledgements, and proving travel/late-join behavior with nested world spawners.

**Architecture:** Keep the public Godot-facing `GameSession : Node` as the composition façade, but move participant indexing and active-travel bookkeeping into small internal state objects. `NetworkSession` remains the sole owner of transport operations. A single local world-ready transition drives both global-travel and late-join acknowledgement; the server decides how to interpret the sender from authoritative pending sets. Disconnect ends the current runtime game session deterministically without requiring the persistent `GameSession` node to be recreated.

**Tech Stack:** Godot 4.7.x C#, .NET, ENet high-level multiplayer, `MultiplayerSpawner`, `MultiplayerSynchronizer`, GdUnit4Net, Task/CSharpier.

**Spec:** `docs/feature/game_session/planned/game-session-design.md`

## Design amendments applied by this refactor

This plan intentionally tightens several details of the original design after review of the landed implementation. The executor must update the design document to match these amendments before changing runtime code.

1. **One supported bootstrap order:** authored integration subscribes first, network starts second:

   ```text
   GameSession.Initialize()
   QuestWorldNetworkPlayers.Initialize()
   NetworkSession.Start(options)
   ```

   `GameSession` no longer promises to reconstruct arbitrary peer history when initialized after an already-running network session. The persistent `Game` root exists specifically to guarantee ordering.

2. **Disconnect ends the current runtime session:** `NetworkSession.Disconnected` clears participants, current world, active travel, late-join pending state, and returns an initialized `GameSession` to `Idle`. A later `NetworkSession.Start()` may reuse the same node and begin a fresh runtime session.

3. **`WorldContainer` starts empty:** every managed world must originate from the persistent `WorldSpawner`. Pre-authored world children are configuration errors in V1.

4. **One readiness ACK:** replace separate `TravelReady(long)` and `CurrentWorldReady(long)` RPCs with one reliable `WorldReady(long travelId)`. The server interprets the sender as either a pending global-travel participant or a pending late join.

5. **Lifecycle signals are local replica lifecycle signals:** `PlayerJoined` and `PlayerLeft` fire on every peer when that peer locally registers/unregisters the replicated `PlayerState`. Server-only gameplay readiness remains `PlayerWorldReady`.

6. **Transport access stays inside `network_session`:** `game_session` never writes `RefuseNewConnections` and never calls `MultiplayerPeer.DisconnectPeer` directly.

## Global Constraints

- `game_session` may depend on `network_session` but must not depend on QuestWorld, Character, Interaction, GameplayAction, Inventory, or project-specific spawner abstractions.
- `NetworkSession` remains transport-only; the new control methods expose transport capabilities without introducing participant/game concepts.
- `PlayerState` remains server-authoritative and its identity is assigned before tree entry.
- `ParticipantId` remains distinct from `PeerId` and is never reused within one runtime game session.
- Exactly zero or one managed world may exist under `WorldContainer`; initialization requires zero.
- `WorldSpawner` remains the authoritative replicated lifecycle owner for the current world and late-join reconstruction.
- `SceneTree.CurrentScene` remains the persistent game root.
- Global travel blocks admission, snapshots remote participants, waits for server-local world readiness and all still-connected remote participants, then completes exactly once.
- Late join into an active world never places existing peers into `Traveling` and emits `PlayerWorldReady` only for the joining participant.
- Expected stale/duplicate/refused RPCs are ignored or rejected without entering `Failed`.
- A network disconnect clears the runtime session; only unrecoverable local configuration/replacement failures enter `Failed`.
- Do not add policy interfaces, generic GameMode/GameState, generic timeout abstractions, reconnect logic, or a reusable peer-barrier framework in this refactor.
- After every code task run `task format` and `task build` as required by `AGENTS.md`.
- Run the smallest affected suite first; use `task test:network` for peer lifecycle/replication tasks and `task test:runtime` for cross-feature runtime changes.
- Before final merge verification run `task ci` because this refactor changes shared network lifecycle and project integration.
- Keep `docs/feature/game_session/game-session.md`, `docs/feature/network_session/network_session.md`, and affected QuestWorld/character docs current with durable decisions.

---

# Target file structure

The refactor should end with these responsibilities.

```text
addons/network_session/scripts/
├── NetworkSession.cs
│   Transport lifecycle plus narrow server controls:
│   SetAcceptingConnections(bool), DisconnectPeer(long)
└── ...

addons/game_session/scripts/
├── GameSession.cs
│   Godot-facing façade, configuration, signals, spawner/RPC orchestration
├── GameSessionState.cs
├── GameSessionParticipantRegistry.cs
│   Pure participant indexes and deterministic add/remove/clear
├── GameSessionTravelState.cs
│   Pure active-global-travel ID/path/local-ready/pending-peer state
└── PlayerState.cs

addons/game_session/tests/
├── GameSessionConfigurationTest.cs
├── GameSessionParticipantTest.cs
├── GameSessionNetworkTest.cs
├── GameSessionTravelTest.cs
├── GameSessionTravelNetworkTest.cs
├── GameSessionTestFixtures.cs
└── fixtures/
    ├── NestedSpawnerWorld.tscn
    ├── NestedSpawnerWorld.cs
    ├── NestedSpawnedNode.tscn
    ├── ReplicatedTestPlayerState.cs
    └── ReplicatedTestPlayerState.tscn
```

Do **not** split `GameSession` into multiple partial classes. The two new helper classes should contain state/invariants while `GameSession.cs` remains the one runtime integration surface.

---

### Task 1: Amend the design contract and restore the NetworkSession transport boundary

**Files:**
- Modify: `docs/feature/game_session/planned/game-session-design.md`
- Modify: `addons/network_session/scripts/NetworkSession.cs`
- Create: `addons/network_session/tests/NetworkSessionControlTest.cs`
- Modify: `docs/feature/network_session/network_session.md`

**Interfaces:**
- Produces: `public bool SetAcceptingConnections(bool accepting)`.
- Produces: `public bool DisconnectPeer(long peerId)`.
- `GameSession` will consume these methods in Task 2+ and must stop accessing `NetworkSession.Multiplayer.MultiplayerPeer` directly.

- [ ] **Step 1: Update the planned design document with the six design amendments above.**

  Replace the old dual-ACK and late-initialization promises with an explicit target contract equivalent to:

  ```text
  bootstrap:
      GameSession.Initialize()
      project integration Initialize()
      NetworkSession.Start()

  readiness:
      client local world ready
          -> WorldReady(CurrentTravelId)
      server:
          if ActiveTravel expects sender -> global ACK
          else if PendingLateJoinPeers contains sender -> late-join ACK
          else -> ignore stale/duplicate

  disconnect:
      clear runtime GameSession state
      keep authored GameSession initialized/subscribed
      State = Idle
  ```

- [ ] **Step 2: Write failing `NetworkSessionControlTest` coverage for transport ownership.**

  Add tests that create a server session and assert:

  ```csharp
  [TestCase]
  public async Task ServerCanToggleConnectionAdmission()
  {
      // Start a real host/server NetworkSession.
      AssertThat(session.SetAcceptingConnections(false)).IsTrue();
      AssertThat(session.Multiplayer.MultiplayerPeer!.RefuseNewConnections).IsTrue();

      AssertThat(session.SetAcceptingConnections(true)).IsTrue();
      AssertThat(session.Multiplayer.MultiplayerPeer!.RefuseNewConnections).IsFalse();
  }

  [TestCase]
  public void ClientCannotControlServerAdmission()
  {
      AssertThat(clientSession.SetAcceptingConnections(false)).IsFalse();
  }

  [TestCase]
  public void InvalidDisconnectRequestIsRejected()
  {
      AssertThat(serverSession.DisconnectPeer(0)).IsFalse();
      AssertThat(clientSession.DisconnectPeer(1)).IsFalse();
  }
  ```

  Reuse the smallest existing ENet fixture pattern; do not introduce game-session concepts into these tests.

- [ ] **Step 3: Run the focused suite and confirm the methods are missing.**

  Run:

  ```bash
  task test:suite SUITE=NetworkSessionControlTest
  ```

  Expected: compile/test failure because `SetAcceptingConnections` and `DisconnectPeer` do not exist.

- [ ] **Step 4: Implement the narrow transport methods in `NetworkSession`.**

  Keep them server-only and defensive:

  ```csharp
  public bool SetAcceptingConnections(bool accepting)
  {
      if (!IsServer || Multiplayer.MultiplayerPeer is not MultiplayerPeer peer)
      {
          return false;
      }

      peer.RefuseNewConnections = !accepting;
      return true;
  }

  public bool DisconnectPeer(long peerId)
  {
      if (
          !IsServer
          || peerId <= 0
          || peerId == LocalPeerId
          || Multiplayer.MultiplayerPeer is not MultiplayerPeer peer
      )
      {
          return false;
      }

      peer.DisconnectPeer((int)peerId);
      return true;
  }
  ```

  Do not add admission semantics or refusal reasons to `NetworkSession`.

- [ ] **Step 5: Verify transport tests, format, build, and network suite.**

  Run:

  ```bash
  task test:suite SUITE=NetworkSessionControlTest
  task format
  task build
  task test:network
  ```

- [ ] **Step 6: Update durable network-session docs and commit.**

  Document that `NetworkSession` owns the `MultiplayerPeer` and exposes narrow transport controls without knowing participants.

  ```bash
  git add addons/network_session docs/feature/network_session docs/feature/game_session/planned/game-session-design.md
  git commit -m "refactor(network): encapsulate server transport controls"
  ```

---

### Task 2: Split GameSession bookkeeping into explicit participant and travel state

**Files:**
- Create: `addons/game_session/scripts/GameSessionParticipantRegistry.cs`
- Create: `addons/game_session/scripts/GameSessionTravelState.cs`
- Modify: `addons/game_session/scripts/GameSession.cs`
- Modify: `addons/game_session/tests/GameSessionConfigurationTest.cs`
- Modify: `addons/game_session/tests/GameSessionTravelTest.cs`
- Modify: `quest_world/game/Game.cs`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- Produces internal `GameSessionParticipantRegistry` with deterministic register/remove/clear and peer/participant lookup.
- Produces internal `GameSessionTravelState` representing the **only** active global travel.
- `GameSession.Initialize()` subscribes/configures while `State == Idle`; it no longer reconciles already-connected peers.
- `NetworkSession.Connected` becomes the transition that starts a fresh runtime game session and enters `Active`.

- [ ] **Step 1: Add failing configuration tests for strict authored topology and bootstrap semantics.**

  Add cases equivalent to:

  ```csharp
  [TestCase]
  public async Task InitializeRejectsMisconfiguredSpawnerPaths()
  {
      fixture.PlayerStateSpawner.SpawnPath = new NodePath("../WorldContainer");
      AssertThat(fixture.GameSession.Initialize()).IsFalse();
  }

  [TestCase]
  public async Task InitializeRejectsPreAuthoredWorldChild()
  {
      fixture.WorldContainer.AddChild(new Node { Name = "AuthoredWorld" });
      AssertThat(fixture.GameSession.Initialize()).IsFalse();
  }

  [TestCase]
  public async Task InitializeBeforeNetworkStartRemainsIdle()
  {
      AssertThat(fixture.GameSession.Initialize()).IsTrue();
      AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Idle);

      AssertThat(fixture.Network.Start(CreateOfflineOptions())).IsTrue();
      AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Active);
  }
  ```

- [ ] **Step 2: Run the focused configuration/travel suites and observe failures.**

  ```bash
  task test:suite SUITE=GameSessionConfigurationTest
  task test:suite SUITE=GameSessionTravelTest
  ```

- [ ] **Step 3: Create `GameSessionParticipantRegistry`.**

  Use one object as the source of participant lookup truth:

  ```csharp
  internal sealed class GameSessionParticipantRegistry
  {
      private readonly List<PlayerState> _players = [];
      private readonly Dictionary<long, PlayerState> _byParticipantId = [];
      private readonly Dictionary<long, PlayerState> _byPeerId = [];

      public IReadOnlyList<PlayerState> Players => _players;

      public bool TryAdd(PlayerState playerState)
      {
          if (
              playerState.ParticipantId <= 0
              || playerState.PeerId <= 0
              || _byParticipantId.ContainsKey(playerState.ParticipantId)
              || _byPeerId.ContainsKey(playerState.PeerId)
          )
          {
              return false;
          }

          _players.Add(playerState);
          _byParticipantId.Add(playerState.ParticipantId, playerState);
          _byPeerId.Add(playerState.PeerId, playerState);
          return true;
      }

      public bool Remove(PlayerState playerState)
      {
          if (!_byParticipantId.Remove(playerState.ParticipantId))
          {
              return false;
          }

          _byPeerId.Remove(playerState.PeerId);
          _players.Remove(playerState);
          return true;
      }

      public bool TryGetByPeerId(long peerId, out PlayerState? playerState) =>
          _byPeerId.TryGetValue(peerId, out playerState);

      public bool TryGetByParticipantId(long participantId, out PlayerState? playerState) =>
          _byParticipantId.TryGetValue(participantId, out playerState);

      public void Clear()
      {
          _players.Clear();
          _byParticipantId.Clear();
          _byPeerId.Clear();
      }
  }
  ```

- [ ] **Step 4: Create `GameSessionTravelState`.**

  Remove `_globalTravelActive`, `_expectedGlobalTravelId`, and `_pendingTravelPeers` from `GameSession` in favor of one object:

  ```csharp
  internal sealed class GameSessionTravelState
  {
      private readonly HashSet<long> _pendingPeers;

      public GameSessionTravelState(long id, string resourcePath, IEnumerable<long> pendingPeers)
      {
          Id = id;
          ResourcePath = resourcePath;
          _pendingPeers = [.. pendingPeers];
      }

      public long Id { get; }
      public string ResourcePath { get; }
      public bool IsLocalWorldReady { get; private set; }
      public bool IsComplete => IsLocalWorldReady && _pendingPeers.Count == 0;

      public void MarkLocalWorldReady() => IsLocalWorldReady = true;
      public bool MarkPeerReady(long peerId) => _pendingPeers.Remove(peerId);
      public bool RemovePeer(long peerId) => _pendingPeers.Remove(peerId);
      public bool ExpectsPeer(long peerId) => _pendingPeers.Contains(peerId);
  }
  ```

  Do not make this a Node/Resource or generic barrier abstraction.

- [ ] **Step 5: Refactor `GameSession.Initialize()` around explicit subscription-before-start ordering.**

  The initialization shape should become:

  ```csharp
  public bool Initialize()
  {
      if (_initialized)
      {
          return State != GameSessionState.Failed;
      }

      if (!ValidateConfiguration())
      {
          State = GameSessionState.Failed;
          return false;
      }

      ConfigureSpawnerCallbacks();
      SubscribeNetworkSignals();
      _initialized = true;
      State = GameSessionState.Idle;
      return true;
  }
  ```

  Delete `ReconcileCurrentNetworkSession()` completely. `OnConnected()` will create host/offline local participants after Task 3.

  Fix path validation to validate the authored `SpawnPath`, not a freshly computed path:

  ```csharp
  if (PlayerStateSpawner.GetNodeOrNull(PlayerStateSpawner.SpawnPath) != Players)
  {
      return FailConfiguration("PlayerStateSpawner.SpawnPath must point to Players.");
  }

  if (WorldSpawner.GetNodeOrNull(WorldSpawner.SpawnPath) != WorldContainer)
  {
      return FailConfiguration("WorldSpawner.SpawnPath must point to WorldContainer.");
  }

  if (WorldContainer.GetChildCount() != 0)
  {
      return FailConfiguration("WorldContainer must be empty before GameSession starts.");
  }
  ```

- [ ] **Step 6: Reorder QuestWorld bootstrap.**

  `Game.Initialize()` must subscribe everything before starting ENet:

  ```csharp
  if (!GameSession!.Initialize())
  {
      return false;
  }

  NetworkPlayers!.Initialize();

  if (!NetworkSession!.Start(launchOptions!))
  {
      GameSession.Reset();
      return false;
  }

  _initialized = true;

  if (InitialWorld is not null && NetworkSession.IsServer)
  {
      GameSession.Travel(InitialWorld);
  }
  ```

- [ ] **Step 7: Run focused suites, format/build, and commit.**

  ```bash
  task test:suite SUITE=GameSessionConfigurationTest
  task test:suite SUITE=GameSessionTravelTest
  task format
  task build
  task test:runtime
  git add addons/game_session quest_world/game docs/feature/game_session
  git commit -m "refactor(game-session): make runtime state explicit"
  ```

---

### Task 3: Make PlayerState replica lifecycle symmetric and removal deterministic

**Files:**
- Modify: `addons/game_session/scripts/GameSession.cs`
- Modify: `addons/game_session/scripts/PlayerState.cs`
- Modify: `addons/game_session/tests/GameSessionParticipantTest.cs`
- Modify: `addons/game_session/tests/GameSessionNetworkTest.cs`
- Modify: `addons/game_session/tests/GameSessionTestFixtures.cs`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- `PlayerJoined(PlayerState)` fires locally exactly once when a local replica enters the registry.
- `PlayerLeft(long participantId, long peerId)` fires locally exactly once when that local replica leaves the registry.
- Server disconnect removes registry membership synchronously before deferred/free lifecycle can release a travel barrier.
- `PlayerState` no longer contains test-only `IdentityWasInitializedBeforeReady` runtime API.

- [ ] **Step 1: Replace the runtime identity probe with a test-specific derived PlayerState.**

  Remove from production:

  ```csharp
  public bool IdentityWasInitializedBeforeReady { get; private set; }
  public override void _Ready() { ... }
  ```

  In the test fixture create/pack a derived probe class:

  ```csharp
  private sealed partial class ReadyProbePlayerState : PlayerState
  {
      public bool HadIdentityInReady { get; private set; }

      public override void _Ready()
      {
          HadIdentityInReady = ParticipantId > 0 && PeerId > 0;
      }
  }
  ```

  Assert on `HadIdentityInReady` instead of production instrumentation.

- [ ] **Step 2: Write failing network tests for symmetric signals and disconnect-before-barrier semantics.**

  Add cases equivalent to:

  ```csharp
  [TestCase]
  public async Task PlayerJoinedAndLeftAreLocalReplicaLifecycleSignals()
  {
      int serverJoined = 0;
      int clientJoined = 0;
      int clientLeft = 0;
      server.GameSession.PlayerJoined += _ => serverJoined++;
      client.GameSession.PlayerJoined += _ => clientJoined++;
      client.GameSession.PlayerLeft += (_, _) => clientLeft++;

      // Connect remote participant, then disconnect it.
      AssertThat(serverJoined).IsEqual(2); // host + remote local replicas
      AssertThat(clientJoined).IsEqual(2); // reconstructed host + own PlayerState
      AssertThat(clientLeft).IsEqual(1);
  }
  ```

  Add a travel-disconnect regression asserting the disconnected participant never receives server-side `PlayerWorldReady` after its `PeerDisconnected` was processed.

- [ ] **Step 3: Run participant/network tests and confirm current asymmetry fails.**

  ```bash
  task test:suite SUITE=GameSessionParticipantTest
  task test:suite SUITE=GameSessionNetworkTest
  task test:suite SUITE=GameSessionTravelNetworkTest
  ```

- [ ] **Step 4: Centralize registration in one idempotent local callback.**

  Use one method for server, client, late join, and offline:

  ```csharp
  private bool RegisterPlayerState(PlayerState playerState)
  {
      if (!_participants.TryAdd(playerState))
      {
          return false;
      }

      playerState.TreeExiting += () => UnregisterPlayerState(playerState);
      EmitSignal(SignalName.PlayerJoined, playerState);
      return true;
  }

  private bool UnregisterPlayerState(PlayerState playerState)
  {
      if (!_participants.Remove(playerState))
      {
          return false;
      }

      EmitSignal(SignalName.PlayerLeft, playerState.ParticipantId, playerState.PeerId);
      return true;
  }
  ```

  `OnPlayerStateSpawned` calls `RegisterPlayerState` for replicated remote-side spawns. The authoritative server path must also call `RegisterPlayerState(playerState)` on the node returned by `PlayerStateSpawner.Spawn(...)`; `MultiplayerSpawner.Spawned` must not be assumed to perform local-authority registration. Neither path emits `PlayerJoined` anywhere except inside `RegisterPlayerState`.

  For offline fallback, after `Players.AddChild(playerState, true)`, call the same `RegisterPlayerState(playerState)` path.

- [ ] **Step 5: Make server participant removal synchronous before free.**

  Replace `QueueFree()`-only removal with:

  ```csharp
  private void RemoveParticipant(long peerId)
  {
      if (!_participants.TryGetByPeerId(peerId, out PlayerState? playerState))
      {
          return;
      }

      UnregisterPlayerState(playerState);
      if (IsInstanceValid(playerState))
      {
          playerState.QueueFree();
      }
  }
  ```

  `TreeExiting` then becomes an idempotent no-op on the server and still owns replicated cleanup on clients.

- [ ] **Step 6: Verify signal counts, format/build, network suite, and commit.**

  ```bash
  task test:suite SUITE=GameSessionParticipantTest
  task test:suite SUITE=GameSessionNetworkTest
  task test:suite SUITE=GameSessionTravelNetworkTest
  task format
  task build
  task test:network
  git add addons/game_session docs/feature/game_session
  git commit -m "fix(game-session): make participant lifecycle deterministic"
  ```

---

### Task 4: Replace dual readiness RPCs with one WorldReady protocol and explicit world lifecycle

**Files:**
- Modify: `addons/game_session/scripts/GameSession.cs`
- Modify: `addons/game_session/scripts/GameSessionTravelState.cs`
- Modify: `addons/game_session/tests/GameSessionTravelTest.cs`
- Modify: `addons/game_session/tests/GameSessionTravelNetworkTest.cs`
- Modify: `addons/game_session/tests/GameSessionTestFixtures.cs`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- Removes: RPC `TravelReady(long)`.
- Removes: RPC `CurrentWorldReady(long)`.
- Produces: reliable AnyPeer RPC `WorldReady(long travelId)`.
- Keeps: authority RPC `BeginTravel(long travelId, string resourcePath)` for client technical lifecycle/loading UI semantics.
- Keeps: authority RPC `CompleteTravel(long travelId)`.
- Keeps: `PlayerWorldReady(PlayerState)` as a server semantic signal.

- [ ] **Step 1: Write failing tests for world despawn state and unified ACK behavior.**

  Add runtime coverage that directly exercises old-world retirement:

  ```csharp
  [TestCase]
  public async Task RetiringCurrentWorldClearsCurrentWorldBeforeReplacement()
  {
      await StartFirstWorld();
      Node first = fixture.GameSession.CurrentWorld!;

      AssertThat(fixture.GameSession.Travel(secondScene)).IsTrue();

      AssertThat(fixture.GameSession.CurrentWorld == first).IsFalse();
      await fixture.Runner.SimulateFrames(2);
      AssertThat(fixture.WorldContainer.GetChildCount()).IsEqual(1);
  }
  ```

  Add network tests that call only `WorldReady` for duplicate/stale/global/late-join cases. Delete test calls to `TravelReady`/`CurrentWorldReady`.

- [ ] **Step 2: Run the travel suites and confirm they fail before protocol refactor.**

  ```bash
  task test:suite SUITE=GameSessionTravelTest
  task test:suite SUITE=GameSessionTravelNetworkTest
  ```

- [ ] **Step 3: Make current-world tracking lifecycle-safe.**

  Subscribe to `WorldSpawner.Spawned` and `WorldSpawner.Despawned` during `Initialize()`.

  Centralize adoption/retirement:

  ```csharp
  private void AdoptCurrentWorld(Node world, long travelId, string resourcePath)
  {
      if (_currentWorld is not null && IsInstanceValid(_currentWorld) && _currentWorld != world)
      {
          FailRuntime("A second managed world was spawned before the previous world retired.");
          return;
      }

      _currentWorld = world;
      CurrentTravelId = travelId;
      CurrentWorldPath = resourcePath;
      _isCurrentWorldReady = false;
      world.TreeExiting += () => ClearCurrentWorld(world);
      ScheduleWorldReadinessCheck();
  }

  private void ClearCurrentWorld(Node world)
  {
      if (_currentWorld != world)
      {
          return;
      }

      _currentWorld = null;
      CurrentWorldPath = string.Empty;
      _isCurrentWorldReady = false;
  }
  ```

  `OnWorldSpawned(Node node)` calls `AdoptCurrentWorld(...)` for replicated remote-side world spawns. The authoritative `Travel()` path must also call `AdoptCurrentWorld(world, travelId, resourcePath)` on the node returned by `WorldSpawner.Spawn(...)`; do not assume `WorldSpawner.Spawned` performs authority-side adoption.

  `OnWorldDespawned(Node node)` calls `ClearCurrentWorld(node)` for remote replicas. The authoritative `RetireCurrentWorld()` clears the reference synchronously before freeing the old node.

- [ ] **Step 4: Collapse local readiness to one transition.**

  Remove `_worldLoadedEmitted`; `_isCurrentWorldReady` itself guarantees exactly-once local emission:

  ```csharp
  public void CompleteLocalWorldReadiness()
  {
      if (
          _isCurrentWorldReady
          || _currentWorld is null
          || !IsInstanceValid(_currentWorld)
          || _currentWorld.GetMeta(TravelIdMeta, 0L).AsInt64() != CurrentTravelId
      )
      {
          return;
      }

      _isCurrentWorldReady = true;
      EmitSignal(SignalName.WorldLoaded, CurrentTravelId, _currentWorld);

      if (NetworkSession?.IsServer == true)
      {
          _activeTravel?.MarkLocalWorldReady();
          TryCompleteTravel();
      }
      else if (!IsOfflineSession)
      {
          RpcId(1, nameof(WorldReady), CurrentTravelId);
      }
  }
  ```

  Offline uses the same `MarkLocalWorldReady` + `TryCompleteTravel` path with an empty pending-peer set.

- [ ] **Step 5: Implement one server `WorldReady` RPC.**

  The authoritative interpretation order is explicit:

  ```csharp
  [Rpc(
      MultiplayerApi.RpcMode.AnyPeer,
      CallLocal = false,
      TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
  )]
  public void WorldReady(long travelId)
  {
      if (NetworkSession?.IsServer != true || travelId != CurrentTravelId)
      {
          return;
      }

      long senderPeerId = Multiplayer.GetRemoteSenderId();
      if (!_participants.TryGetByPeerId(senderPeerId, out PlayerState? playerState))
      {
          return;
      }

      if (_activeTravel is not null && _activeTravel.Id == travelId)
      {
          if (_activeTravel.MarkPeerReady(senderPeerId))
          {
              TryCompleteTravel();
          }
          return;
      }

      if (State == GameSessionState.Active && _pendingLateJoinPeers.Remove(senderPeerId))
      {
          EmitSignal(SignalName.PlayerWorldReady, playerState);
      }
  }
  ```

  Duplicate/stale messages naturally become no-ops.

- [ ] **Step 6: Keep `BeginTravel` only for client technical state, not ACK classification.**

  It must tolerate the world spawn arriving first:

  ```csharp
  public void BeginTravel(long travelId, string resourcePath)
  {
      if (NetworkSession?.IsServer == true || travelId <= 0 || State != GameSessionState.Active)
      {
          return;
      }

      State = GameSessionState.Traveling;
      EmitSignal(SignalName.TravelStarted, travelId, resourcePath);

      // Do not reset an already-ready matching world if WorldSpawner replication won the race.
      if (CurrentTravelId != travelId)
      {
          CurrentTravelId = travelId;
          CurrentWorldPath = resourcePath;
          _isCurrentWorldReady = false;
      }
  }
  ```

  `CompleteTravel` still requires matching `CurrentTravelId`, valid `CurrentWorld`, and local readiness before returning the client to `Active`.

- [ ] **Step 7: Rework server travel completion around `GameSessionTravelState`.**

  At `Travel()`:

  ```csharp
  long travelId = ++_nextTravelId;
  long[] requiredPeers =
  [
      .. _participants.Players
          .Where(player => player.PeerId != NetworkSession.LocalPeerId)
          .Select(player => player.PeerId),
  ];

  _activeTravel = new GameSessionTravelState(travelId, resourcePath, requiredPeers);
  State = GameSessionState.Traveling;
  NetworkSession.SetAcceptingConnections(false);
  ```

  After the authoritative `WorldSpawner.Spawn(spawnData)` succeeds, call `AdoptCurrentWorld(world, travelId, resourcePath)` directly.

  At completion:

  ```csharp
  private void TryCompleteTravel()
  {
      if (
          NetworkSession?.IsServer != true
          || _activeTravel is null
          || !_activeTravel.IsComplete
          || _currentWorld is null
          || !_isCurrentWorldReady
      )
      {
          return;
      }

      long travelId = _activeTravel.Id;
      Node world = _currentWorld;
      _activeTravel = null;
      State = GameSessionState.Active;
      UpdateTransportAdmission();
      Rpc(nameof(CompleteTravel), travelId);
      EmitSignal(SignalName.TravelCompleted, travelId, world);

      foreach (PlayerState playerState in _participants.Players.ToArray())
      {
          EmitSignal(SignalName.PlayerWorldReady, playerState);
      }
  }
  ```

- [ ] **Step 8: Verify protocol and commit.**

  ```bash
  task test:suite SUITE=GameSessionTravelTest
  task test:suite SUITE=GameSessionTravelNetworkTest
  task format
  task build
  task test:network
  git add addons/game_session docs/feature/game_session
  git commit -m "refactor(game-session): unify world readiness protocol"
  ```

---

### Task 5: Define disconnect, failure, and restart as explicit runtime-session transitions

**Files:**
- Modify: `addons/game_session/scripts/GameSession.cs`
- Modify: `addons/game_session/tests/GameSessionNetworkTest.cs`
- Modify: `addons/game_session/tests/GameSessionTravelNetworkTest.cs`
- Modify: `addons/game_session/tests/GameSessionTestFixtures.cs`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- `Initialize()` configures/subscribes once.
- `Connected` begins a fresh runtime session and sets `Active`.
- `Disconnected` clears runtime state and sets `Idle` without unsubscribing the persistent node.
- `Failed` clears runtime state and sets `Failed`.
- `Reset()` clears runtime state **and** unsubscribes/configuration state for node teardown.
- A later `NetworkSession.Start()` after a normal stop may reuse the same initialized `GameSession` node.

- [ ] **Step 1: Add failing disconnect/restart tests.**

  Cover both idle and travel cases:

  ```csharp
  [TestCase]
  public async Task ClientDisconnectDuringTravelClearsRuntimeSession()
  {
      AssertThat(server.GameSession.Travel(world)).IsTrue();
      AssertThat(client.GameSession.State).IsEqual(GameSessionState.Traveling);

      client.Network.Stop();
      await fixture.Pump(4);

      AssertThat(client.GameSession.State).IsEqual(GameSessionState.Idle);
      AssertThat(client.GameSession.PlayerStates.Count).IsEqual(0);
      AssertThat(client.GameSession.CurrentWorld).IsNull();
  }
  ```

  Add a fixture helper that stops and restarts a client `NetworkSession` while keeping the same `GameSession` node, then assert a fresh connection reaches `Active` and reconstructs participants/current world once.

- [ ] **Step 2: Run focused network suites and observe stale runtime state failures.**

  ```bash
  task test:suite SUITE=GameSessionNetworkTest
  task test:suite SUITE=GameSessionTravelNetworkTest
  ```

- [ ] **Step 3: Implement a single runtime clear path.**

  Keep subscription teardown separate:

  ```csharp
  private void ClearRuntimeSession(GameSessionState nextState)
  {
      _activeTravel = null;
      _pendingLateJoinPeers.Clear();
      _nextParticipantId = 1;
      _nextTravelId = 0;

      foreach (PlayerState playerState in _participants.Players.ToArray())
      {
          UnregisterPlayerState(playerState);
          if (IsInstanceValid(playerState))
          {
              playerState.QueueFree();
          }
      }
      _participants.Clear();

      if (_currentWorld is not null && IsInstanceValid(_currentWorld))
      {
          Node world = _currentWorld;
          ClearCurrentWorld(world);
          world.QueueFree();
      }

      CurrentTravelId = 0;
      CurrentWorldPath = string.Empty;
      _isCurrentWorldReady = false;
      State = nextState;
  }
  ```

  `Reset()` calls this then unsubscribes and sets `_initialized = false`.

- [ ] **Step 4: Make connection transitions explicit.**

  ```csharp
  private void OnConnected()
  {
      if (!_initialized)
      {
          return;
      }

      ClearRuntimeSession(GameSessionState.Active);
      UpdateTransportAdmission();

      if (NetworkSession?.IsServer == true && !NetworkSession.IsDedicatedServer)
      {
          AdmitParticipant(NetworkSession.LocalPeerId);
      }
  }

  private void OnDisconnected()
  {
      if (_initialized)
      {
          ClearRuntimeSession(GameSessionState.Idle);
      }
  }

  private void OnNetworkFailed(string reason)
  {
      GD.PushError($"{GetPath()}: NetworkSession failed: {reason}");
      if (_initialized)
      {
          ClearRuntimeSession(GameSessionState.Failed);
      }
  }
  ```

  Do not special-case `Active` versus `Traveling` in disconnect handling.

- [ ] **Step 5: Route admission/refusal through NetworkSession APIs only.**

  Replace every direct peer access in `GameSession`:

  ```csharp
  private void UpdateTransportAdmission()
  {
      if (NetworkSession?.IsServer == true)
      {
          NetworkSession.SetAcceptingConnections(
              IsAcceptingPlayers && State == GameSessionState.Active
          );
      }
  }
  ```

  Rejections/failures call:

  ```csharp
  NetworkSession?.DisconnectPeer(peerId);
  ```

  A repository search after this task must show no `GameSession` access to `Multiplayer.MultiplayerPeer`.

- [ ] **Step 6: Verify restart/disconnect and commit.**

  ```bash
  task test:suite SUITE=GameSessionNetworkTest
  task test:suite SUITE=GameSessionTravelNetworkTest
  task format
  task build
  task test:network
  git grep "Multiplayer\.MultiplayerPeer" -- addons/game_session/scripts
  git add addons/game_session docs/feature/game_session
  git commit -m "fix(game-session): reset runtime state on disconnect"
  ```

  Expected grep result: no direct transport manipulation in `game_session`.

---

### Task 6: Make QuestWorld Character lifecycle server-authoritative and replica-safe

**Files:**
- Modify: `quest_world/network/QuestWorldNetworkPlayers.cs`
- Create: `quest_world/tests/QuestWorldNetworkPlayersTest.cs`
- Modify: `docs/feature/character/replication.md`
- Modify: `docs/feature/network_session/network_session.md`
- Modify: `docs/feature/game_session/game-session.md`

**Interfaces:**
- `QuestWorldNetworkPlayers` no longer subscribes to `GameSession.PlayerJoined` because it does not act on admission alone.
- `PlayerWorldReady(PlayerState)` remains the server-only trigger that materializes a participant as a world-local Character.
- Character replica indexes are cleared by actual Character tree lifecycle, not by client-side authoritative deletion.

- [ ] **Step 1: Write failing tests for server-only Character destruction and index cleanup.**

  Build a focused QuestWorld fixture with a fake/current `World` player spawner and assert:

  ```csharp
  [TestCase]
  public async Task ClientPlayerLeftDoesNotAuthoritativelyFreeReplicatedCharacter()
  {
      // Register a client-side Character replica in QuestWorldNetworkPlayers.
      gameSession.EmitSignal(GameSession.SignalName.PlayerLeft, participantId, peerId);
      await runner.SimulateFrames(1);

      AssertThat(GodotObject.IsInstanceValid(character)).IsTrue();
  }

  [TestCase]
  public async Task CharacterTreeExitRemovesReplicaIndex()
  {
      character.QueueFree();
      await runner.SimulateFrames(1);
      // Re-spawning a Character for the same peer must no longer be blocked by stale index state.
      AssertThat(integrationCanAcceptReplacement).IsTrue();
  }
  ```

  If direct private-index inspection would be required, assert behavior by spawning a replacement with the same peer ID instead of exposing test-only API.

- [ ] **Step 2: Run the focused QuestWorld suite and observe the client-side free/stale-index failures.**

  ```bash
  task test:suite SUITE=QuestWorldNetworkPlayersTest
  ```

- [ ] **Step 3: Remove empty PlayerJoined integration and make PlayerLeft server-only for Character destruction.**

  Initialization should subscribe only to signals it uses:

  ```csharp
  GameSession.PlayerLeft += OnPlayerLeft;
  GameSession.PlayerWorldReady += OnPlayerWorldReady;
  GameSession.WorldLoaded += OnWorldLoaded;
  GameSession.TravelCompleted += OnTravelCompleted;
  ```

  Character deletion:

  ```csharp
  private void OnPlayerLeft(long participantId, long peerId)
  {
      if (GameSession?.NetworkSession?.IsServer != true)
      {
          return;
      }

      if (_charactersByPeerId.TryGetValue(peerId, out ProjectCharacter? character)
          && IsInstanceValid(character))
      {
          character.QueueFree();
      }
  }
  ```

- [ ] **Step 4: Make `OnPlayerSpawned` own replica index lifecycle.**

  Attach one idempotent exit handler when a Character is observed:

  ```csharp
  private void OnPlayerSpawned(Node node)
  {
      if (node is not ProjectCharacter character
          || !QuestWorldNetworkIdentity.TryGetPeerId(character.Name, out int peerId))
      {
          return;
      }

      ConfigurePlayerAuthority(character, peerId);
      _charactersByPeerId[peerId] = character;
      character.TreeExiting += () =>
      {
          if (_charactersByPeerId.TryGetValue(peerId, out ProjectCharacter? indexed)
              && indexed == character)
          {
              _charactersByPeerId.Remove(peerId);
          }
      };
  }
  ```

  `RefreshCurrentWorld()` may still clear the whole index when the world-spawner instance changes.

- [ ] **Step 5: Verify QuestWorld/runtime/network tests and commit.**

  ```bash
  task test:suite SUITE=QuestWorldNetworkPlayersTest
  task format
  task build
  task test:game
  task test:runtime
  task test:network
  git add quest_world docs/feature/character docs/feature/network_session docs/feature/game_session
  git commit -m "fix(quest-world): align character lifecycle with server authority"
  ```

---

### Task 7: Prove nested-spawner travel, late join, and persistent derived PlayerState data

**Files:**
- Create: `addons/game_session/tests/fixtures/NestedSpawnerWorld.cs`
- Create: `addons/game_session/tests/fixtures/NestedSpawnerWorld.tscn`
- Create: `addons/game_session/tests/fixtures/NestedSpawnedNode.tscn`
- Create: `addons/game_session/tests/fixtures/ReplicatedTestPlayerState.cs`
- Create: `addons/game_session/tests/fixtures/ReplicatedTestPlayerState.tscn`
- Modify: `addons/game_session/tests/GameSessionTravelNetworkTest.cs`
- Modify: `addons/game_session/tests/GameSessionTestFixtures.cs`
- Modify: `docs/memory/game-session-world-replication-pitfalls.md` only if the test reveals a new engine ordering constraint.

**Interfaces:**
- Generic automated proof must exercise a persistent `WorldSpawner` spawning a world which itself contains a child `MultiplayerSpawner` with already-spawned replicated state before a late client joins.
- A **test-only** derived `PlayerState` with a `MultiplayerSynchronizer` proves project-extensible synchronized state without making `game_session` tests depend on QuestWorld.

- [ ] **Step 1: Create a minimal nested-spawner world fixture.**

  `NestedSpawnerWorld.cs` should expose a server helper and no game-session dependency:

  ```csharp
  using Godot;

  namespace QuestWorld.Tests.GameSessionTests.Fixtures;

  public partial class NestedSpawnerWorld : Node3D
  {
      public MultiplayerSpawner NestedSpawner => GetNode<MultiplayerSpawner>("NestedSpawner");
      public Node NestedRoot => GetNode("NestedRoot");

      public Node? SpawnReplicatedNode(string name)
      {
          if (!Multiplayer.IsServer())
          {
              return null;
          }

          PackedScene scene = GD.Load<PackedScene>(
              "res://addons/game_session/tests/fixtures/NestedSpawnedNode.tscn"
          );
          Node node = scene.Instantiate();
          node.Name = name;
          NestedRoot.AddChild(node, true);
          return node;
      }
  }
  ```

  Author `NestedSpawnerWorld.tscn` as:

  ```text
  NestedSpawnerWorld
  ├── NestedRoot
  └── NestedSpawner
      SpawnPath = ../NestedRoot
      spawnable scene = NestedSpawnedNode.tscn
  ```

  `NestedSpawnedNode.tscn` may be a plain named `Node3D`; the test is lifecycle/path reconstruction, not gameplay behavior.

- [ ] **Step 2: Add the critical nested-spawner late-join test.**

  Test sequence:

  ```csharp
  [TestCase]
  public async Task LateJoinReconstructsCurrentWorldBeforeNestedSpawnHistory()
  {
      NetworkFixture fixture = await GameSessionTestFixtures.Connect();
      PackedScene nestedWorld = GD.Load<PackedScene>(NestedWorldPath);

      AssertThat(fixture.Server.GameSession.Travel(nestedWorld)).IsTrue();
      await WaitForActive(fixture);

      NestedSpawnerWorld serverWorld =
          (NestedSpawnerWorld)fixture.Server.GameSession.CurrentWorld!;
      AssertThat(serverWorld.SpawnReplicatedNode("ExistingNestedActor") is not null).IsTrue();
      await fixture.Pump(8);

      LatePeer late = await fixture.JoinLate();
      await fixture.Pump(24);

      AssertThat(late.GameSession.CurrentWorld is NestedSpawnerWorld).IsTrue();
      AssertThat(
          late.GameSession.CurrentWorld!
              .GetNode("NestedRoot")
              .HasNode("ExistingNestedActor")
      ).IsTrue();
  }
  ```

  This test is the automated proof for the architectural reason `WorldSpawner` is persistent.

- [ ] **Step 3: Create a test-only replicated PlayerState scene.**

  `ReplicatedTestPlayerState.cs`:

  ```csharp
  using Godot;
  using QuestWorld.GameSession;

  namespace QuestWorld.Tests.GameSessionTests.Fixtures;

  public partial class ReplicatedTestPlayerState : PlayerState
  {
      [Export]
      public string SelectionId { get; set; } = "default";
  }
  ```

  Author `ReplicatedTestPlayerState.tscn` with a child `MultiplayerSynchronizer` whose replication config synchronizes `.:SelectionId` from server authority.

- [ ] **Step 4: Add a persistent derived-PlayerState test that mutates synchronized test data.**

  Update the network fixture so `Connect(...)` can receive an optional custom `PlayerStateScene`, then run:

  ```csharp
  [TestCase]
  public async Task DerivedPlayerStatePropertySurvivesTwoWorldTravels()
  {
      PackedScene playerStateScene = GD.Load<PackedScene>(ReplicatedPlayerStatePath);
      NetworkFixture fixture = await GameSessionTestFixtures.Connect(playerStateScene: playerStateScene);
      ReplicatedTestPlayerState state =
          (ReplicatedTestPlayerState)fixture.Server.GameSession.PlayerStates[0];
      ulong instanceId = state.GetInstanceId();
      state.SelectionId = "red";
      await fixture.Pump(6);

      await TravelAndWait(fixture, WorldA);
      await TravelAndWait(fixture, WorldB);

      ReplicatedTestPlayerState after =
          (ReplicatedTestPlayerState)fixture.Server.GameSession.PlayerStates[0];
      AssertThat(after.GetInstanceId()).IsEqual(instanceId);
      AssertThat(after.SelectionId).IsEqual("red");

      ReplicatedTestPlayerState remote =
          (ReplicatedTestPlayerState)fixture.Client.GameSession.PlayerStates[0];
      AssertThat(remote.SelectionId).IsEqual("red");
  }
  ```

  This test must remain entirely inside `addons/game_session/tests`; it must not reference `QuestWorldPlayerState` or any QuestWorld project type.

- [ ] **Step 5: Add travel+disconnect coverage with the nested fixture.**

  Start a second global travel while a remote participant exists, disconnect it before readiness completion, and assert:

  ```text
  server returns Active
  disconnected PlayerState is absent before PlayerWorldReady iteration
  remaining host PlayerWorldReady fires exactly once
  WorldContainer contains exactly one new world
  ```

- [ ] **Step 6: Run focused and full network validation.**

  ```bash
  task test:suite SUITE=GameSessionTravelNetworkTest
  task test:suite SUITE=GameSessionNetworkTest
  task format
  task build
  task test:network
  ```

- [ ] **Step 7: Commit the proof fixtures/tests.**

  ```bash
  git add addons/game_session/tests docs/memory
  git commit -m "test(game-session): cover nested spawner late join"
  ```

---

### Task 8: Final API cleanup, docs, real QuestWorld smoke, and verification gate

**Files:**
- Modify: `addons/game_session/scripts/GameSession.cs`
- Modify: `addons/game_session/README.md`
- Modify: `docs/feature/game_session/game-session.md`
- Modify: `docs/feature/game_session/planned/game-session-design.md`
- Modify: `docs/feature/network_session/network_session.md`
- Modify: `docs/feature/character/replication.md`
- Modify: only implementation/tests required by final review findings
- Delete or archive nothing under `planned/` until the refactor is actually merged and verified.

**Interfaces:**
- Final public GameSession RPC surface contains `BeginTravel`, `WorldReady`, `CompleteTravel`, `WorldLoadFailed` and no old dual readiness RPCs.
- Final public lifecycle remains `PlayerJoined`, `PlayerLeft`, `PlayerWorldReady`, `TravelStarted`, `WorldLoaded`, `TravelCompleted`, `TravelFailed`.
- Final `NetworkSession` public transport control includes `SetAcceptingConnections` and `DisconnectPeer`.

- [ ] **Step 1: Remove obsolete fields/methods and verify `GameSession.cs` no longer carries parallel duplicate state.**

  Delete all of:

  ```text
  _globalTravelActive
  _expectedGlobalTravelId
  _pendingTravelPeers
  _worldLoadedEmitted
  TravelReady(...)
  CurrentWorldReady(...)
  ReconcileCurrentNetworkSession(...)
  ```

  Expected remaining conceptual runtime state:

  ```text
  _participants
  _activeTravel?
  _pendingLateJoinPeers
  _currentWorld
  _isCurrentWorldReady
  _nextParticipantId
  _nextTravelId
  State
  ```

- [ ] **Step 2: Run repository searches for forbidden legacy coupling.**

  ```bash
  git grep "TravelReady" -- ':!docs/superpowers/plans/2026-09-07-game-session.md'
  git grep "CurrentWorldReady" -- ':!docs/superpowers/plans/2026-09-07-game-session.md'
  git grep "Multiplayer\.MultiplayerPeer" -- addons/game_session
  git grep "GetTree().CurrentScene as IWorldSpawner" -- .
  git grep "IdentityWasInitializedBeforeReady" -- .
  ```

  Expected: no runtime/test hits except intentional historical documentation if retained temporarily.

- [ ] **Step 3: Update durable documentation to describe the hardened behavior as current architecture.**

  `docs/feature/game_session/game-session.md` must explicitly document:

  ```text
  initialize integrations before NetworkSession.Start
  local PlayerJoined/PlayerLeft semantics
  disconnect clears current runtime session
  strict empty WorldContainer at initialization
  one WorldReady ACK protocol
  persistent WorldSpawner/nested-spawner late-join guarantee
  server-only PlayerWorldReady meaning
  ```

  `docs/feature/network_session/network_session.md` documents transport control ownership. `docs/feature/character/replication.md` documents server-authoritative Character destruction and world-local replica indexing.

- [ ] **Step 4: Run the complete local verification gate.**

  First focused suites:

  ```bash
  task test:suite SUITE=NetworkSessionControlTest
  task test:suite SUITE=GameSessionConfigurationTest
  task test:suite SUITE=GameSessionParticipantTest
  task test:suite SUITE=GameSessionNetworkTest
  task test:suite SUITE=GameSessionTravelTest
  task test:suite SUITE=GameSessionTravelNetworkTest
  task test:suite SUITE=QuestWorldNetworkPlayersTest
  ```

  Then cross-feature gates:

  ```bash
  task format
  task build
  task test:network
  task test:runtime
  task test:game
  task ci
  git diff --check
  ```

- [ ] **Step 5: Run standalone QuestWorld root smoke checks outside shared GdUnit scene loading.**

  Offline persistent-root smoke:

  ```bash
  godot --headless --path . --scene res://quest_world/game/Game.tscn --quit-after 180 -- --offline
  ```

  Listen-server smoke in terminal A:

  ```bash
  godot --headless --path . --scene res://quest_world/game/Game.tscn -- --host --port=7017
  ```

  Client smoke in terminal B:

  ```bash
  godot --headless --path . --scene res://quest_world/game/Game.tscn --quit-after 300 -- --client --connect=127.0.0.1 --port=7017
  ```

  Inspect logs for duplicate spawn/despawn errors, invalid NodePath/RPC errors, stale-object access, or duplicated PlayerState/Character creation. Stop the host after the client exits.

- [ ] **Step 6: Re-read the original 40 design behaviors plus this plan's regression list.**

  Explicitly prove/document these previously under-proven cases before closing the plan:

  ```text
  [ ] misconfigured SpawnPath is rejected
  [ ] WorldContainer with authored child is rejected
  [ ] client receives local PlayerJoined for replicated states
  [ ] disconnect during travel cannot emit PlayerWorldReady for the departed participant
  [ ] disconnect during Traveling clears the local runtime session
  [ ] same GameSession node can participate in a fresh NetworkSession after normal Stop/Start
  [ ] CurrentWorld is cleared when its replicated world exits
  [ ] derived synchronized PlayerState property survives two travels and replicates
  [ ] late join reconstructs an already-populated nested MultiplayerSpawner under current world
  [ ] clients never authoritatively QueueFree another participant's Character
  ```

- [ ] **Step 7: Request a fresh code review and fix every Critical/Important finding with a failing regression test first.**

  Review range should start at the commit immediately before this refactor and end at the final implementation commit. Re-run Step 4 after every important fix.

- [ ] **Step 8: Commit final cleanup/docs.**

  ```bash
  git add addons quest_world docs
  git commit -m "docs(game-session): finalize hardened V1 lifecycle"
  ```

  Once this refactor is merged and verified, migrate durable decisions out of `planned/` according to `AGENTS.md` and delete completed planned artifacts in a separate documentation cleanup commit.

---

# Review findings this plan must close

The implementation is not complete until each reviewed issue maps to a passing regression test or an explicit documented non-goal:

1. `GameSession` stuck in `Traveling` after network disconnect.
2. Runtime participants/world surviving disconnect and breaking reuse/restart.
3. Client missing `PlayerJoined` while receiving `PlayerLeft`.
4. Deferred `QueueFree` allowing a disconnected participant into `PlayerWorldReady` completion iteration.
5. QuestWorld clients freeing replicated Characters on `PlayerLeft` instead of waiting for authoritative spawner lifecycle.
6. Character replica index stale after actual Character tree exit/respawn.
7. Client `CurrentWorld` retaining a freed world wrapper between replicated despawn/spawn.
8. Spawner `SpawnPath` validation checking a computed path rather than the authored property.
9. Pre-authored world child accepted although never adopted as `CurrentWorld`.
10. `GameSession` manipulating `MultiplayerPeer` directly instead of using the extracted transport boundary.
11. Parallel travel fields allowing invalid combinations instead of one explicit active-travel object.
12. Dual `TravelReady`/`CurrentWorldReady` protocol duplicating correlation logic.
13. Production `PlayerState` carrying test-only `IdentityWasInitializedBeforeReady` API.
14. No real proof that derived synchronized `PlayerState` content persists across world replacement.
15. No automated proof that a late join reconstructs a world containing already-populated nested `MultiplayerSpawner` state.

# Expected end state

```text
Game.tscn persistent
├── NetworkSession
│   └── owns ENet / transport controls
├── GameSession
│   ├── participant registry
│   ├── optional ActiveTravel state
│   ├── Players + PlayerStateSpawner
│   └── WorldContainer + WorldSpawner
├── QuestWorldNetworkPlayers
│   └── server-side participant -> Character materialization
└── PlayerController

Global travel:
server Travel
 -> ActiveTravel created
 -> admission transport closed
 -> old world retires
 -> WorldSpawner replicates new world
 -> each process WorldLoaded
 -> remote client WorldReady(id)
 -> server interprets against ActiveTravel
 -> barrier complete
 -> CompleteTravel
 -> PlayerWorldReady(current participants)

Late join:
server admits PlayerState
 -> persistent PlayerStateSpawner reconstructs states
 -> persistent WorldSpawner reconstructs current world
 -> nested world spawners reconstruct their state
 -> joining client WorldLoaded
 -> WorldReady(current id)
 -> server interprets against PendingLateJoinPeers
 -> PlayerWorldReady(new participant only)

Disconnect:
NetworkSession.Disconnected
 -> clear runtime participants/world/travel/pending state
 -> GameSession remains initialized
 -> Idle
 -> later NetworkSession.Start may begin a fresh runtime session
```

The refactor should stop here. Do not use the cleanup as an excuse to introduce reconnect, GameMode/GameState, async loading, generic policies, or other future framework work.