using Godot;

namespace QuestWorld.Tests.GameSessionTests.Fixtures;

public partial class NestedSpawnerWorld : Node3D
{
    public MultiplayerSpawner NestedSpawner => GetNode<MultiplayerSpawner>("NestedSpawner");

    public Node NestedRoot => GetNode("NestedRoot");

    public Node? SpawnReplicatedNode(string name)
    {
        if (!Multiplayer.IsServer())
        {
            return null;
        }

        PackedScene scene = GD.Load<PackedScene>(
            "res://addons/game_session/tests/fixtures/NestedSpawnedNode.tscn"
        );
        Node node = scene.Instantiate();
        node.Name = name;
        NestedRoot.AddChild(node, true);
        return node;
    }
}
