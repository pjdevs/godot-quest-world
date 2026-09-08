# Game Session Addon

`game_session` composes `network_session` and owns persistent participants and world travel.
`PlayerState` identity is allocated by the server and survives world replacement; project-specific
state belongs in a derived `PlayerState` scene and is replicated by the project.

The addon is explicitly initialized with authored `NetworkSession`, `Players`, `PlayerStateScene`,
`PlayerStateSpawner`, `WorldContainer`, and `WorldSpawner` references. Pawn spawning, possession,
match rules, and world-specific gameplay remain outside this addon.

Configure both persistent spawners before startup: `PlayerStateSpawner.SpawnPath` must target `Players`,
`WorldSpawner.SpawnPath` must target an initially empty `WorldContainer`, and the project-provided
`PlayerStateScene` root must inherit `PlayerState`. Initialize the addon before starting the transport:

```csharp
if (!gameSession.Initialize())
{
    return;
}

networkSession.Start(options);
```

Only the server calls `Travel(worldScene)`. Every local replica observes `PlayerJoined`, `PlayerLeft`,
`WorldLoaded`, and `TravelCompleted`; server-side gameplay materialization should subscribe to
`PlayerWorldReady`. Disconnect clears runtime participants, world, and travel state while leaving the
authored node initialized so the same composition can participate in a later `NetworkSession.Start()`.
