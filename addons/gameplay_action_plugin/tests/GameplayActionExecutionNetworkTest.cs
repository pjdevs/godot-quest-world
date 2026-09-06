namespace QuestWorld.Tests.GameplayActions;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Execution;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Network")]
public sealed partial class GameplayActionExecutionNetworkTest
{
    private const int ConnectFrames = 240;
    private const int RoundTripFrames = 16;
    private static readonly StringName RepairAction = new("repair");
    private static int _nextPort = 48920;

    [TestCase]
    public async Task ReplicatedExecutionReachesAnObserverAndALateJoinerThenDisappears()
    {
        Session session = await Connect();
        try
        {
            GameplayActionExecutionResult result = session.Server.Component.ExecuteAction(
                RepairAction,
                out ulong executionId
            );
            await session.Pump(RoundTripFrames);

            AssertThat(result is GameplayActionExecutionRunning).IsTrue();
            AssertPresentation(session.Client.Component, executionId);
            AssertThat(
                    session.Client.Component.TryGetExecutionPresentation(
                        RepairAction,
                        out GameplayActionExecutionPresentation observer
                    )
                )
                .IsTrue();
            AssertThat(observer.Relation).IsEqual(GameplayActionExecutionRelation.Observed);
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(1);
            AssertThat(session.Client.Executor.ExecuteCount).IsEqual(0);

            PeerScene late = await session.JoinLate("LateClient");

            AssertPresentation(late.Component, executionId);
            AssertThat(
                    late.Component.TryGetExecutionPresentation(
                        RepairAction,
                        out GameplayActionExecutionPresentation joined
                    )
                )
                .IsTrue();
            AssertThat(joined.Relation).IsEqual(GameplayActionExecutionRelation.Observed);
            AssertThat(late.Executor.ExecuteCount).IsEqual(0);

            AssertThat(session.Server.Component.CompleteExecution(executionId)).IsTrue();
            await session.Pump(RoundTripFrames);

            AssertThat(session.Client.Component.TryGetExecutionPresentation(RepairAction, out _))
                .IsFalse();
            AssertThat(late.Component.TryGetExecutionPresentation(RepairAction, out _)).IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    private static async Task<Session> Connect()
    {
        int port = _nextPort++;
        Node world = new() { Name = "GameplayActionNetworkWorld" };
        Node serverRoot = new() { Name = "Server" };
        Node clientRoot = new() { Name = "Client" };
        world.AddChild(serverRoot);
        world.AddChild(clientRoot);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        SceneTree tree = world.GetTree();
        ENetMultiplayerPeer serverPeer = new();
        AssertThat(serverPeer.CreateServer(port, 8)).IsEqual(Error.Ok);
        MultiplayerApi serverApi = Attach(tree, serverRoot, serverPeer);
        ENetMultiplayerPeer clientPeer = new();
        AssertThat(clientPeer.CreateClient("127.0.0.1", port)).IsEqual(Error.Ok);
        MultiplayerApi clientApi = Attach(tree, clientRoot, clientPeer);

        PeerScene server = BuildPeerScene(serverRoot);
        PeerScene client = BuildPeerScene(clientRoot);
        await runner.SimulateFrames(1);

        for (int frame = 0; frame < ConnectFrames; frame++)
        {
            await runner.SimulateFrames(1);
            if (serverApi.GetPeers().Length >= 1 && clientApi.GetUniqueId() > 1)
            {
                break;
            }
        }

        AssertThat(serverApi.IsServer()).IsTrue();
        AssertThat(clientApi.IsServer()).IsFalse();
        AssertThat(serverApi.GetPeers().Length).IsEqual(1);
        AssertThat(clientApi.GetUniqueId()).IsGreater(1);
        await runner.SimulateFrames(1);
        return new Session(
            runner,
            world,
            server,
            client,
            port,
            new List<ENetMultiplayerPeer> { serverPeer, clientPeer }
        );
    }

    private static MultiplayerApi Attach(SceneTree tree, Node root, MultiplayerPeer peer)
    {
        MultiplayerApi api = MultiplayerApi.CreateDefaultInterface();
        api.MultiplayerPeer = peer;
        tree.SetMultiplayer(api, root.GetPath());
        return api;
    }

    private static PeerScene BuildPeerScene(Node root)
    {
        Node actor = new() { Name = "Actor" };
        GameplayActionComponent component = new() { Name = "Actions" };
        NetworkRecordingExecutor executor = new() { Name = "RepairExecutor" };
        GameplayAction action = new()
        {
            Name = "RepairAction",
            Definition = new GameplayActionDefinition { Id = RepairAction, Label = "Repair" },
            ExecutionVisibility = GameplayActionExecutionVisibility.Replicated,
        };
        action.AddChild(executor);
        action.Executor = executor;
        AssertThat(component.AddAction(action)).IsTrue();
        GameplayActionExecutionSynchronizer synchronizer = new()
        {
            Name = "GameplayActionExecutionSynchronizer",
            Component = component,
        };
        component.AddChild(synchronizer);
        actor.AddChild(component);
        root.AddChild(actor);
        return new PeerScene(component, executor);
    }

    private static void AssertPresentation(GameplayActionComponent component, ulong executionId)
    {
        AssertThat(
                component.TryGetExecutionPresentation(
                    RepairAction,
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.ExecutionId).IsEqual(executionId);
    }

    private sealed partial class NetworkRecordingExecutor : GameplayActionExecutor
    {
        public int ExecuteCount { get; private set; }

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            ExecuteCount++;
            return new GameplayActionExecutionRunning();
        }
    }

    private sealed record PeerScene(
        GameplayActionComponent Component,
        NetworkRecordingExecutor Executor
    );

    private sealed record Session(
        ISceneRunner Runner,
        Node World,
        PeerScene Server,
        PeerScene Client,
        int Port,
        List<ENetMultiplayerPeer> Peers
    )
    {
        public async Task<PeerScene> JoinLate(string name)
        {
            Node root = new() { Name = name };
            World.AddChild(root);
            ENetMultiplayerPeer peer = new();
            AssertThat(peer.CreateClient("127.0.0.1", Port)).IsEqual(Error.Ok);
            MultiplayerApi api = Attach(World.GetTree(), root, peer);
            Peers.Add(peer);
            PeerScene scene = BuildPeerScene(root);

            for (int frame = 0; frame < ConnectFrames; frame++)
            {
                await Pump(1);
                if (api.GetUniqueId() > 1)
                {
                    break;
                }
            }

            AssertThat(api.IsServer()).IsFalse();
            AssertThat(api.GetUniqueId()).IsGreater(1);
            await Pump(RoundTripFrames);
            return scene;
        }

        public async Task Pump(int frames) => await Runner.SimulateFrames((uint)frames);

        public void Close()
        {
            foreach (ENetMultiplayerPeer peer in Peers)
            {
                peer.Close();
            }
        }
    }
}
