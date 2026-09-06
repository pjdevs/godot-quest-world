using System.Collections.Generic;
using Godot;

public partial class Spawner : MultiplayerSpawner
{
    [Export]
    public StringName Id { get; set; } = new(string.Empty);

    [Export]
    public PackedScene? Scene { get; set; } = null;

    [Export]
    public bool AutoSpawn { get; set; } = false;

    private readonly List<Transform3D> _initialTransforms = [];

    public override void _Ready()
    {
        base._Ready();

        if (AutoSpawn)
        {
            Node3D? spawnRoot = GetSpawnRoot();

            if (spawnRoot is not null)
            {
                foreach (var child in spawnRoot.GetChildren())
                {
                    if (child is Marker3D marker)
                    {
                        _initialTransforms.Add(marker.GlobalTransform);
                        spawnRoot.RemoveChild(marker);
                        marker.QueueFree();
                    }
                }
            }
        }
    }

    public void InitializeAuthority()
    {
        if (AutoSpawn)
        {
            foreach (var transform in _initialTransforms)
            {
                Spawn(transform);
            }
        }
    }

    public Node3D? Spawn(Transform3D transform, string? name = null)
    {
        if (!IsServer() || Scene is null)
        {
            return null;
        }

        Node3D? spawnRoot = GetSpawnRoot();
        if (spawnRoot is null)
        {
            return null;
        }

        Node3D? spawned = Scene.Instantiate<Node3D>();
        if (spawned is null)
        {
            return null;
        }

        if (name is not null)
        {
            spawned.Name = name;
        }
        spawnRoot.AddChild(spawned);
        spawned.GlobalTransform = transform;

        return spawned;
    }

    public Node3D? GetSpawnRoot() =>
        SpawnPath.IsEmpty ? GetParent() as Node3D : GetNodeOrNull<Node3D>(SpawnPath);

    private bool IsServer() =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();
}
