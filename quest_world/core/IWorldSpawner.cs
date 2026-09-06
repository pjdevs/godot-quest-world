using Godot;

public interface IWorldSpawner
{
    public bool TrySpawn(StringName definitionId, in SpawnRequest request, out Node3D? spawned);
}
