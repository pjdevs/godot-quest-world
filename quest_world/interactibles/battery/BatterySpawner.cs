using Godot;

[GlobalClass]
public partial class BatterySpawner : MultiplayerSpawner
{
    public Node3D? Spawn(PackedScene? scene, Vector3 position)
    {
        if (scene is null)
        {
            return null;
        }

        Node3D? battery = scene.Instantiate<Node3D>();
        Node3D? spawnRoot = ResolveSpawnRoot();
        if (battery is null || spawnRoot is null)
        {
            battery?.QueueFree();
            return null;
        }

        spawnRoot.AddChild(battery);
        battery.GlobalPosition = position;
        return battery;
    }

    private Node3D? ResolveSpawnRoot() =>
        SpawnPath.IsEmpty ? GetParent() as Node3D : GetNodeOrNull<Node3D>(SpawnPath);
}
