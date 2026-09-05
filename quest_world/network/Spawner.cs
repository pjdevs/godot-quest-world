using Godot;

public partial class Spawner<T> : MultiplayerSpawner, IWorldSystem
    where T : Node3D
{
    [Export]
    public PackedScene? Scene { get; set; } = null;

    [Export]
    public bool AutoSpawn { get; set; } = false;

    public void InitializeAuthority()
    {
        if (AutoSpawn)
        {
            Spawn(Vector3.Zero);
        }
    }

    public T? Spawn(Vector3 position, string? name = null)
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
        player.GlobalPosition = position;

        return player;
    }

    public Node3D? GetSpawnRoot() =>
        SpawnPath.IsEmpty ? GetParent() as Node3D : GetNodeOrNull<Node3D>(SpawnPath);

    private bool IsServer() =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();
}
