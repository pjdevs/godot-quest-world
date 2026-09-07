# Network Session

## Responsibility

The Network Session feature owns the lifecycle of the local Godot multiplayer peer. It supports
offline, listen-server, dedicated-server and client launch modes while keeping gameplay ownership
outside the generic session layer.

## Current architecture

`addons/network_session/scripts/NetworkSession.cs` is the generic runtime boundary. It exposes:

- `Start(NetworkLaunchOptions options)` and `Stop()`;
- `LocalPeerId`, `IsServer`, `IsDedicatedServer` and `State`;
- peer-connected and peer-disconnected signals;
- connected, disconnected and failed session signals.

It is responsible for creating and closing `ENetMultiplayerPeer`, wiring Godot multiplayer signals,
and transitioning `SessionState`. It does not know about characters, spawners, controllers or
QuestWorld gameplay.

`quest_world/network/scripts/QuestWorldNetworkSession.cs` is the current QuestWorld integration
facade. It parses the project command line, keeps the authored `PlayerSpawner` and
`LocalPlayerController` references, and temporarily retains player spawning, despawning and local
possession while the QuestWorld player glue is extracted.

`NetworkPlayerIdentity` remains QuestWorld-owned. Its player naming and spawn-position conventions
are not part of the generic addon contract.

## Launch modes

`NetworkLaunchOptions` parses the project arguments after Godot's `--` separator:

- `--offline`
- `--host`
- `--server`
- `--client`
- `--connect <address>` or `--connect=<address>`
- `--port <port>` or `--port=<port>`
- `--max-players <count>` or `--max-players=<count>`

Without an explicit mode, the session starts offline. Offline and host sessions use local peer ID
`1`; clients receive their ENet peer ID when the server connection completes.

## Architecture decisions

### Generic session owns peer lifecycle only

The addon emits peer and session lifecycle signals instead of spawning gameplay nodes. Integrations
subscribe to those signals and decide what a peer means in their own domain.

### QuestWorld keeps the compatibility facade during extraction

The current scene contract remains `World.NetworkSession` and the authored node remains named
`NetworkSession`. The script now derives from the generic addon session so the player lifecycle can
move into a dedicated QuestWorld manager without changing the network transport contract.
