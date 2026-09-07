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
        GameSession gameSession = new();
        Node players = new();
        MultiplayerSpawner playerStateSpawner = new() { SpawnPath = new NodePath("../Players") };
        Node worldContainer = new();
        MultiplayerSpawner worldSpawner = new() { SpawnPath = new NodePath("../WorldContainer") };
        NetworkSession networkSession = new();
        gameSession.AddChild(players);
        gameSession.AddChild(playerStateSpawner);
        gameSession.AddChild(worldContainer);
        gameSession.AddChild(worldSpawner);

        Node wrongRoot = new();
        PackedScene wrongScene = new();
        AssertThat(wrongScene.Pack(wrongRoot)).IsEqual(Error.Ok);
        wrongRoot.Free();

        gameSession.NetworkSession = networkSession;
        gameSession.Players = players;
        gameSession.PlayerStateScene = wrongScene;
        gameSession.PlayerStateSpawner = playerStateSpawner;
        gameSession.WorldContainer = worldContainer;
        gameSession.WorldSpawner = worldSpawner;

        Node root = new();
        root.AddChild(gameSession);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);

        AssertThat(gameSession.Initialize()).IsFalse();
        AssertThat(gameSession.State).IsEqual(GameSessionState.Failed);
    }
}
