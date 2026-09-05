using System.Collections.Generic;
using Godot;

public partial class Spawner<T> : MultiplayerSpawner, IWorldSystem
    where T : Node3D
{
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

    public T? Spawn(Transform3D transform, string? name = null)
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

        T? player = Scene.Instantiate<T>();
        if (player is null)
        {
            return null;
        }

        if (name is not null)
        {
            player.Name = name;
        }
        spawnRoot.AddChild(player);
        player.GlobalTransform = transform;

        return player;
    }

    public Node3D? GetSpawnRoot() =>
        SpawnPath.IsEmpty ? GetParent() as Node3D : GetNodeOrNull<Node3D>(SpawnPath);

    private bool IsServer() =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();
}
