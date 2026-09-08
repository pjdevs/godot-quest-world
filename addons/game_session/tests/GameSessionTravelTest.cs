namespace QuestWorld.Tests.GameSessionTests;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using QuestWorld.Network;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed class GameSessionTravelTest
{
    [TestCase]
    public void ClientCompletionCanArriveBeforeTheMatchingTravelBegins()
    {
        GameSessionClientTravelState state = new();

        state.ObserveCompletion(2);

        AssertThat(state.CanBegin(2)).IsTrue();
        AssertThat(state.CanComplete(2)).IsTrue();
        AssertThat(state.MarkCompleted(2)).IsTrue();
        AssertThat(state.CanBegin(2)).IsFalse();
        AssertThat(state.CanComplete(2)).IsFalse();
    }

    [TestCase]
    public async Task OfflineTravelLoadsTheWorldAndCompletesAfterWorldReady()
    {
        OfflineFixture fixture = CreateFixture();
        ISceneRunner runner = ISceneRunner.Load(fixture.Root, autoFree: true);
        await runner.SimulateFrames(1);
        AssertThat(fixture.GameSession.Initialize()).IsTrue();
        AssertThat(fixture.Network.Start(CreateOfflineOptions())).IsTrue();
        List<string> events = new();
        fixture.GameSession.TravelStarted += (travelId, path) =>
            events.Add($"started:{travelId}:{path}");
        fixture.GameSession.WorldLoaded += (travelId, _) => events.Add($"loaded:{travelId}");
        fixture.GameSession.TravelCompleted += (travelId, _) => events.Add($"completed:{travelId}");

        PackedScene world = GD.Load<PackedScene>(
            "res://addons/game_session/tests/fixtures/WorldA.tscn"
        );
        AssertThat(fixture.GameSession.Travel(world)).IsTrue();
        await runner.SimulateFrames(2);

        AssertThat(fixture.GameSession.IsLocalWorldReady).IsTrue();
        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Active);
        AssertThat(fixture.GameSession.CurrentTravelId).IsEqual(1L);
        AssertThat(fixture.GameSession.CurrentWorld is not null).IsTrue();
        AssertThat(fixture.GameSession.CurrentWorld!.IsInsideTree()).IsTrue();
        AssertThat(fixture.GameSession.CurrentWorldPath).IsEqual(world.ResourcePath);
        AssertThat(fixture.WorldContainer.GetChildCount()).IsEqual(1);
        AssertThat(events)
            .IsEqual(
                new List<string> { $"started:1:{world.ResourcePath}", "loaded:1", "completed:1" }
            );
        fixture.Close();
    }

    [TestCase]
    public async Task ASecondTravelReplacesOnlyTheCurrentWorld()
    {
        OfflineFixture fixture = await StartOfflineFixture();
        Node firstWorld = fixture.GameSession.CurrentWorld!;
        PackedScene secondScene = GD.Load<PackedScene>(
            "res://addons/game_session/tests/fixtures/WorldB.tscn"
        );

        AssertThat(fixture.GameSession.Travel(secondScene)).IsTrue();
        AssertThat(fixture.GameSession.CurrentWorld == firstWorld).IsFalse();
        await fixture.Runner.SimulateFrames(2);

        AssertThat(GodotObject.IsInstanceValid(firstWorld)).IsFalse();
        AssertThat(fixture.GameSession.CurrentTravelId).IsEqual(2L);
        AssertThat(fixture.GameSession.CurrentWorld!.Name).IsEqual("World_2");
        AssertThat(fixture.WorldContainer.GetChildCount()).IsEqual(1);
        AssertThat(fixture.GameSession.PlayerStates.Count).IsEqual(1);
        fixture.Close();
    }

    [TestCase]
    public async Task FailedPreflightKeepsTheCurrentWorldAndDoesNotCompleteTravel()
    {
        OfflineFixture fixture = await StartOfflineFixture();
        Node currentWorld = fixture.GameSession.CurrentWorld!;
        int completed = 0;
        string? failure = null;
        fixture.GameSession.TravelCompleted += (_, _) => completed++;
        fixture.GameSession.TravelFailed += (_, reason) => failure = reason;

        AssertThat(fixture.GameSession.Travel(new PackedScene())).IsFalse();
        await fixture.Runner.SimulateFrames(1);

        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Active);
        AssertThat(fixture.GameSession.CurrentWorld == currentWorld).IsTrue();
        AssertThat(completed).IsEqual(0);
        AssertThat(string.IsNullOrWhiteSpace(failure)).IsFalse();
        fixture.Close();
    }

    [TestCase]
    public async Task TravelIsRejectedWhileAnotherTravelIsWaitingForReadiness()
    {
        OfflineFixture fixture = CreateFixture();
        fixture.Runner = ISceneRunner.Load(fixture.Root, autoFree: true);
        await fixture.Runner.SimulateFrames(1);
        AssertThat(fixture.GameSession.Initialize()).IsTrue();
        AssertThat(fixture.Network.Start(CreateOfflineOptions())).IsTrue();
        PackedScene firstScene = GD.Load<PackedScene>(
            "res://addons/game_session/tests/fixtures/WorldA.tscn"
        );

        AssertThat(fixture.GameSession.Travel(firstScene)).IsTrue();
        AssertThat(fixture.GameSession.State).IsEqual(GameSessionState.Traveling);
        AssertThat(fixture.GameSession.Travel(firstScene)).IsFalse();
        fixture.Close();
    }

    private static async Task<OfflineFixture> StartOfflineFixture()
    {
        OfflineFixture fixture = CreateFixture();
        fixture.Runner = ISceneRunner.Load(fixture.Root, autoFree: true);
        await fixture.Runner.SimulateFrames(1);
        AssertThat(fixture.GameSession.Initialize()).IsTrue();
        AssertThat(fixture.Network.Start(CreateOfflineOptions())).IsTrue();
        PackedScene firstScene = GD.Load<PackedScene>(
            "res://addons/game_session/tests/fixtures/WorldA.tscn"
        );
        AssertThat(fixture.GameSession.Travel(firstScene)).IsTrue();
        await fixture.Runner.SimulateFrames(2);
        return fixture;
    }

    private static OfflineFixture CreateFixture()
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
        return new OfflineFixture(root, network, gameSession, worldContainer);
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

    private sealed record OfflineFixture(
        Node Root,
        NetworkSession Network,
        GameSession GameSession,
        Node WorldContainer
    )
    {
        public ISceneRunner Runner { get; set; } = null!;

        public void Close()
        {
            GameSession.Reset();
            Network.Stop();
        }
    }
}
