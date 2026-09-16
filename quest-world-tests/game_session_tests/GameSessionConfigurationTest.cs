namespace QuestWorld.Tests.GameSessionTests;

using System.Threading.Tasks;
using GameSessionPlugin;
using GdUnit4;
using Godot;
using NetworkPlugin;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed class GameSessionConfigurationTest
{
    [TestCase]
    public async Task InitializeRejectsMissingDependencies()
    {
        GameSession gameSession = new();
        Node root = new();
        root.AddChild(gameSession);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);

        await runner.SimulateFrames(1);

        AssertThat(gameSession.Initialize()).IsFalse();
        AssertThat(gameSession.State).IsEqual(GameSessionState.Failed);
    }

    [TestCase]
    public async Task InitializeRejectsAPlayerStateSceneWithTheWrongRootType()
    {
        ConfigurationFixture fixture = await CreateValidFixture();

        Node wrongRoot = new();
        PackedScene wrongScene = new();
        AssertThat(wrongScene.Pack(wrongRoot)).IsEqual(Error.Ok);
        wrongRoot.Free();
        fixture.GameSession.PlayerStateScene = wrongScene;

        AssertThat(fixture.GameSession.Initialize()).IsFalse();
        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Failed);
    }

    [TestCase]
    public async Task InitializeRejectsMisconfiguredSpawnerPaths()
    {
        ConfigurationFixture fixture = await CreateValidFixture();
        fixture.PlayerStateSpawner.SpawnPath = new NodePath("../WorldContainer");

        AssertThat(fixture.GameSession.Initialize()).IsFalse();
        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Failed);
    }

    [TestCase]
    public async Task InitializeRejectsPreAuthoredWorldChild()
    {
        ConfigurationFixture fixture = await CreateValidFixture();
        fixture.WorldContainer.AddChild(new Node { Name = "AuthoredWorld" });

        AssertThat(fixture.GameSession.Initialize()).IsFalse();
        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Failed);
    }

    [TestCase]
    public async Task InitializeBeforeNetworkStartRemainsIdle()
    {
        ConfigurationFixture fixture = await CreateValidFixture();

        AssertThat(fixture.GameSession.Initialize()).IsTrue();
        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Idle);

        AssertThat(fixture.Network.Start(CreateOfflineOptions())).IsTrue();
        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Active);

        fixture.Network.Stop();
    }

    private static async Task<ConfigurationFixture> CreateValidFixture()
    {
        Node root = new() { Name = "Game" };
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
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);
        return new ConfigurationFixture(
            runner,
            network,
            gameSession,
            playerStateSpawner,
            worldContainer
        );
    }

    private static PackedScene CreatePlayerStateScene()
    {
        PlayerState playerState = new();
        PackedScene scene = new();
        AssertThat(scene.Pack(playerState)).IsEqual(Error.Ok);
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

    private sealed record ConfigurationFixture(
        ISceneRunner Runner,
        NetworkSession Network,
        GameSession GameSession,
        MultiplayerSpawner PlayerStateSpawner,
        Node WorldContainer
    );
}
