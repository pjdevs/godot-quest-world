using System.Collections.Generic;
using Godot;

[GlobalClass]
public partial class World : Node3D, IWorldSpawner
{
    [Export]
    public Godot.Collections.Array<Spawner> Spawners { get; set; } = new();

    private readonly Dictionary<StringName, Spawner> _spawnersById = new();

    public Spawner? PlayerSpawner => GetNodeOrNull<Spawner>("PlayerSpawner");

    public override void _Ready()
    {
        if (!IndexSpawners())
        {
            return;
        }
    }

    public void InitializeAuthority()
    {
        foreach (Spawner spawner in Spawners)
        {
            spawner.InitializeAuthority();
        }
    }

    public bool TrySpawn(StringName definitionId, in SpawnRequest request, out Node3D? spawned)
    {
        if (!_spawnersById.TryGetValue(definitionId, out Spawner? spawner))
        {
            spawned = null;
            return false;
        }

        spawned = spawner.Spawn(request.Transform, request.Name);
        return spawned is not null;
    }

    private bool IndexSpawners()
    {
        _spawnersById.Clear();

        foreach (Spawner spawner in Spawners)
        {
            if (spawner.Definition is null || spawner.Definition.Id.IsEmpty)
            {
                GD.PushError(
                    $"{GetPath()}: every authored Spawner requires a Definition with an Id."
                );
                return false;
            }

            if (!_spawnersById.TryAdd(spawner.Definition.Id, spawner))
            {
                GD.PushError(
                    $"{GetPath()}: SpawnDefinition Id '{spawner.Definition.Id}' is declared more than once."
                );
                return false;
            }
        }

        return true;
    }
}
