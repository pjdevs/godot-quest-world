namespace QuestWorld.Tests.GameSessionTests;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using QuestWorld.Network;
using static GdUnit4.Assertions;
using GameSessionNode = QuestWorld.GameSession.GameSession;

internal static partial class GameSessionTestFixtures
{
    private const int ConnectFrames = 240;
    private static int _nextPort = 47940;

    public static async Task<NetworkFixture> Connect(
        bool acceptingPlayers = true,
        bool rejectRemotePlayers = false
    )
    {
        int port = _nextPort++;
        Node root = new() { Name = "NetworkWorld" };
        Node serverBranch = new() { Name = "Server" };
        Node clientBranch = new() { Name = "Client" };
        root.AddChild(serverBranch);
        root.AddChild(clientBranch);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);

        SceneTree tree = root.GetTree();
        MultiplayerApi serverApi = Attach(tree, serverBranch);
        MultiplayerApi clientApi = Attach(tree, clientBranch);

        PeerSession server = BuildPeer(
            serverBranch,
            acceptingPlayers,
            rejectRemotePlayers,
            new NetworkSession { Name = "NetworkSession" }
        );
        PeerSession client = BuildPeer(
            clientBranch,
            true,
            false,
            new NetworkSession { Name = "NetworkSession" }
        );
        await runner.SimulateFrames(1);

        AssertThat(server.Network.Start(CreateHostOptions(port))).IsTrue();
        AssertThat(server.GameSession.Initialize()).IsTrue();
        AssertThat(client.Network.Start(CreateClientOptions(port))).IsTrue();
        AssertThat(client.GameSession.Initialize()).IsTrue();

        for (int frame = 0; frame < ConnectFrames; frame++)
        {
            await runner.SimulateFrames(1);
            if (
                (acceptingPlayers && !rejectRemotePlayers)
                    ? serverApi.GetPeers().Length >= 1 && clientApi.GetUniqueId() > 1
                    : serverApi.GetPeers().Length == 0
            )
            {
                break;
            }
        }

        AssertThat(serverApi.IsServer()).IsTrue();
        AssertThat(clientApi.IsServer()).IsFalse();
        if (acceptingPlayers && !rejectRemotePlayers)
        {
            AssertThat(serverApi.GetPeers().Length).IsEqual(1);
            AssertThat(clientApi.GetUniqueId()).IsGreater(1);
        }
        else
        {
            AssertThat(serverApi.GetPeers().Length).IsEqual(0);
        }
        await runner.SimulateFrames(12);

        return new NetworkFixture(runner, root, server, client, serverApi, clientApi);
    }

    private static PeerSession BuildPeer(
        Node branch,
        bool acceptingPlayers,
        bool rejectRemotePlayers,
        NetworkSession network
    )
    {
        GameSessionNode gameSession = rejectRemotePlayers
            ? new RejectingGameSession { Name = "GameSession" }
            : new GameSessionNode { Name = "GameSession" };
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
        gameSession.IsAcceptingPlayers = acceptingPlayers;
        branch.AddChild(network);
        branch.AddChild(gameSession);
        return new PeerSession(network, gameSession);
    }

    private static MultiplayerApi Attach(SceneTree tree, Node root)
    {
        MultiplayerApi api = MultiplayerApi.CreateDefaultInterface();
        tree.SetMultiplayer(api, root.GetPath());
        return api;
    }

    private static PackedScene CreatePlayerStateScene()
    {
        PlayerState playerState = new();
        PackedScene scene = new();
        AssertThat(scene.Pack(playerState)).IsEqual(Error.Ok);
        playerState.Free();
        return scene;
    }

    private static NetworkLaunchOptions CreateHostOptions(int port) =>
        ParseOptions("--host", "--port", port.ToString());

    private static NetworkLaunchOptions CreateClientOptions(int port) =>
        ParseOptions("--client", "--port", port.ToString());

    private static NetworkLaunchOptions ParseOptions(params string[] arguments) =>
        NetworkLaunchOptions.TryParse(
            arguments,
            out NetworkLaunchOptions? options,
            out string error
        )
            ? options!
            : throw new System.InvalidOperationException(error);

    internal sealed record NetworkFixture(
        ISceneRunner Runner,
        Node Root,
        PeerSession Server,
        PeerSession Client,
        MultiplayerApi ServerApi,
        MultiplayerApi ClientApi
    )
    {
        public async Task Pump(int frames = 1) => await Runner.SimulateFrames((uint)frames);

        public void Close()
        {
            Client.Network.Stop();
            Server.Network.Stop();
        }
    }

    internal sealed record PeerSession(NetworkSession Network, GameSessionNode GameSession);

    private sealed partial class RejectingGameSession : GameSessionNode
    {
        protected override bool CanJoin(long peerId, out string reason)
        {
            if (peerId != 1)
            {
                reason = "Remote players are disabled for this test.";
                return false;
            }

            return base.CanJoin(peerId, out reason);
        }
    }
}
