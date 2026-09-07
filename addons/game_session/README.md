# Game Session Addon

`game_session` composes `network_session` and owns persistent participants and world travel.
`PlayerState` identity is allocated by the server and survives world replacement; project-specific
state belongs in a derived `PlayerState` scene and is replicated by the project.

The addon is explicitly initialized with authored `NetworkSession`, `Players`, `PlayerStateScene`,
`PlayerStateSpawner`, `WorldContainer`, and `WorldSpawner` references. Pawn spawning, possession,
match rules, and world-specific gameplay remain outside this addon.
