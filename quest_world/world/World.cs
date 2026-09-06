using System.Collections.Generic;
using Godot;

[GlobalClass]
public partial class World : Node3D, IWorldSpawner
{
    [Export]
    public Godot.Collections.Array<Spawner> Spawners { get; set; } = new();

    [Export]
    public NetworkSession? NetworkSession { get; set; }

    private readonly Dictionary<StringName, Spawner> _spawnersById = new();

    public override void _Ready()
    {
        if (!IndexSpawners())
        {
            return;
        }

        if (NetworkSession is null)
        {
            GD.PushError("QuestWorldWorld: QuestWorldWorld.NetworkSession is required.");
            return;
        }

        NetworkSession.Initialize();

        if (NetworkSession.IsServer)
        {
            InitializeAuthority();
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
            if (spawner.Id.IsEmpty)
            {
                GD.PushError($"{GetPath()}: every authored Spawner requires an Id.");
                return false;
            }

            if (!_spawnersById.TryAdd(spawner.Id, spawner))
            {
                GD.PushError($"{GetPath()}: Spawner Id '{spawner.Id}' is declared more than once.");
                return false;
            }
        }

        return true;
    }
}
