namespace QuestWorld.Tests.GameSessionTests;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using QuestWorld.Network;
using static GdUnit4.Assertions;
using ProjectCharacter = global::Character;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Game")]
[TestCategory("Network")]
public sealed class QuestWorldNetworkPlayersTest
{
    private const string TestWorldPath =
        "res://quest_world/tests/fixtures/NetworkPlayersTestWorld.tscn";

    [TestCase]
    public async Task ClientPlayerLeftDoesNotAuthoritativelyFreeReplicatedCharacter()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await StartWorld();
        try
        {
            QuestWorldNetworkPlayers integration = AttachIntegration(
                fixture.Root.GetNode("Client"),
                fixture.Client.GameSession
            );
            integration.Initialize();
            World world = (World)fixture.Client.GameSession.CurrentWorld!;
            Spawner spawner = world.PlayerSpawner!;
            Node3D players = spawner.GetSpawnRoot()!;
            long peerId = fixture.ClientApi.GetUniqueId();
            NetworkPlayersTestCharacter character = new()
            {
                Name = QuestWorldNetworkIdentity.GetPlayerName((int)peerId),
            };
            players.AddChild(character, true);
            spawner.EmitSignal(MultiplayerSpawner.SignalName.Spawned, character);

            fixture.Client.GameSession.EmitSignal(
                QuestWorld.GameSession.GameSession.SignalName.PlayerLeft,
                2L,
                peerId
            );
            await fixture.Pump();

            AssertThat(GodotObject.IsInstanceValid(character)).IsTrue();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task CharacterTreeExitAllowsAReplacementForTheSamePeer()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await StartWorld();
        try
        {
            QuestWorldNetworkPlayers integration = AttachIntegration(
                fixture.Root.GetNode("Server"),
                fixture.Server.GameSession
            );
            integration.Initialize();
            World world = (World)fixture.Server.GameSession.CurrentWorld!;
            Node3D players = world.PlayerSpawner!.GetSpawnRoot()!;
            PlayerState hostState = fixture.Server.GameSession.PlayerStates[0];
            string playerName = QuestWorldNetworkIdentity.GetPlayerName((int)hostState.PeerId);
            ProjectCharacter first = players.GetNode<ProjectCharacter>(playerName);

            first.QueueFree();
            await fixture.Pump();
            fixture.Server.GameSession.EmitSignal(
                QuestWorld.GameSession.GameSession.SignalName.PlayerWorldReady,
                hostState
            );
            await fixture.Pump();

            ProjectCharacter? replacement = players.GetNodeOrNull<ProjectCharacter>(playerName);
            AssertThat(replacement is not null).IsTrue();
            AssertThat(replacement == first).IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }

    private static QuestWorldNetworkPlayers AttachIntegration(
        Node branch,
        QuestWorld.GameSession.GameSession gameSession
    )
    {
        QuestWorldNetworkPlayers integration = new()
        {
            Name = "QuestWorldNetworkPlayers",
            GameSession = gameSession,
        };
        branch.AddChild(integration);
        return integration;
    }

    private static async Task<GameSessionTestFixtures.NetworkFixture> StartWorld()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        PackedScene world = GD.Load<PackedScene>(TestWorldPath);
        AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
        for (int frame = 0; frame < 120; frame++)
        {
            await fixture.Pump();
            if (
                fixture.Server.GameSession.State == GameSessionState.Active
                && fixture.Client.GameSession.State == GameSessionState.Active
            )
            {
                break;
            }
        }

        AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
        AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Active);
        return fixture;
    }
}
