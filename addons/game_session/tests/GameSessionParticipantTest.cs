namespace QuestWorld.Tests.GameSession;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using QuestWorld.Network;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed class GameSessionParticipantTest
{
    [TestCase]
    public async Task OfflineInitializationCreatesOnePersistentParticipant()
    {
        Fixture fixture = CreateFixture();
        ISceneRunner runner = ISceneRunner.Load(fixture.Root, autoFree: true);
        await runner.SimulateFrames(1);

        AssertThat(fixture.Network.Start(CreateOfflineOptions())).IsTrue();
        AssertThat(fixture.GameSession.Initialize()).IsTrue();

        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Active);
        AssertThat(fixture.GameSession.PlayerStates.Count).IsEqual(1);
        PlayerState playerState = fixture.GameSession.PlayerStates[0];
        AssertThat(playerState.ParticipantId).IsEqual(1L);
        AssertThat(playerState.PeerId).IsEqual(1L);
        AssertThat(playerState.IdentityWasInitializedBeforeReady).IsTrue();
        AssertThat(fixture.GameSession.TryGetPlayerStateByPeerId(1, out PlayerState? byPeer))
            .IsTrue();
        AssertThat(byPeer == playerState).IsTrue();
        AssertThat(
                fixture.GameSession.TryGetPlayerStateByParticipantId(
                    1,
                    out PlayerState? byParticipant
                )
            )
            .IsTrue();
        AssertThat(byParticipant == playerState).IsTrue();

        fixture.Network.Stop();
    }

    private static Fixture CreateFixture()
    {
        Node root = new();
        NetworkSession network = new() { Name = "NetworkSession" };
        GameSession gameSession = new() { Name = "GameSession" };
        Node players = new() { Name = "Players" };
        MultiplayerSpawner playerStateSpawner = new()
        {
            Name = "PlayerStateSpawner",
            SpawnPath = new NodePath("../Players"),
        };
        Node worldContainer = new() { Name = "WorldContainer" };
        MultiplayerSpawner worldSpawner = new()
        {
            Name = "WorldSpawner",
            SpawnPath = new NodePath("../WorldContainer"),
        };

        gameSession.AddChild(players);
        gameSession.AddChild(playerStateSpawner);
        gameSession.AddChild(worldContainer);
        gameSession.AddChild(worldSpawner);
        gameSession.NetworkSession = network;
        gameSession.Players = players;
        gameSession.PlayerStateScene = CreatePlayerStateScene();
        gameSession.PlayerStateSpawner = playerStateSpawner;
        gameSession.WorldContainer = worldContainer;
        gameSession.WorldSpawner = worldSpawner;

        root.AddChild(network);
        root.AddChild(gameSession);
        return new Fixture(root, network, gameSession);
    }

    private static PackedScene CreatePlayerStateScene()
    {
        PlayerState playerState = new();
        PackedScene scene = new();
        scene.Pack(playerState);
        playerState.Free();
        return scene;
    }

    private static NetworkLaunchOptions CreateOfflineOptions() =>
        NetworkLaunchOptions.TryParse(
            new[] { "--offline" },
            out NetworkLaunchOptions? options,
            out string error
        )
            ? options!
            : throw new System.InvalidOperationException(error);

    private sealed record Fixture(Node Root, NetworkSession Network, GameSession GameSession);
}
