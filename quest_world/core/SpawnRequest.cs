using Godot;

public readonly record struct SpawnRequest(Transform3D Transform, string? Name = null);
