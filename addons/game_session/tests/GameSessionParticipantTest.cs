namespace QuestWorld.Tests.GameSessionTests;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using QuestWorld.Network;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class GameSessionParticipantTest
{
    [TestCase]
    public async Task OfflineInitializationCreatesOnePersistentParticipant()
    {
        Fixture fixture = CreateFixture();
        ISceneRunner runner = ISceneRunner.Load(fixture.Root, autoFree: true);
        await runner.SimulateFrames(1);

        AssertThat(fixture.GameSession.Initialize()).IsTrue();
        AssertThat(fixture.Network.Start(CreateOfflineOptions())).IsTrue();

        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Active);
        AssertThat(fixture.GameSession.PlayerStates.Count).IsEqual(1);
        PlayerState playerState = fixture.GameSession.PlayerStates[0];
        AssertThat(playerState.ParticipantId).IsEqual(1L);
        AssertThat(playerState.PeerId).IsEqual(1L);
        AssertThat(playerState is ReadyProbePlayerState).IsTrue();
        AssertThat(((ReadyProbePlayerState)playerState).HadIdentityInReady).IsTrue();
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

    [TestCase]
    public void StaleReplicaCannotRemoveItsReplacementFromTheRegistry()
    {
        GameSessionParticipantRegistry registry = new();
        PlayerState stale = new();
        stale.InitializeIdentity(1, 1);
        PlayerState replacement = new();
        replacement.InitializeIdentity(1, 1);

        AssertThat(registry.TryAdd(stale)).IsTrue();
        registry.Clear();
        AssertThat(registry.TryAdd(replacement)).IsTrue();

        AssertThat(registry.Remove(stale)).IsFalse();
        AssertThat(registry.TryGetByParticipantId(1, out PlayerState? registered)).IsTrue();
        AssertThat(ReferenceEquals(registered, replacement)).IsTrue();

        stale.Free();
        replacement.Free();
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
        ReadyProbePlayerState playerState = new();
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

    private sealed partial class ReadyProbePlayerState : PlayerState
    {
        public bool HadIdentityInReady { get; private set; }

        public override void _Ready()
        {
            HadIdentityInReady = ParticipantId > 0 && PeerId > 0;
        }
    }
}
