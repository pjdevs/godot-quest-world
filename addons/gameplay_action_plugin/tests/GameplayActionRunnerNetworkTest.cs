namespace QuestWorld.Tests.GameplayActions;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Access;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Runner;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class GameplayActionRunnerNetworkTest
{
    private const int ConnectFrames = 240;
    private const int RoundTripFrames = 16;
    private static readonly StringName OpenAction = new("open");
    private static int _nextPort = 49120;

    [TestCase]
    public async Task RequesterPredictionIsAcknowledgedWhileOnlyTheAuthorityExecutes()
    {
        Session session = await Connect(serverAllowsAccess: true);
        try
        {
            int requesterStarts = 0;
            int observerStarts = 0;
            int requesterCompletions = 0;
            session.Client.Runner.GameplayActionStarted += (_, actionId, executionId) =>
            {
                AssertThat(actionId).IsEqual(OpenAction);
                AssertThat(executionId).IsGreater(0L);
                requesterStarts++;
            };
            session.Observer.Runner.GameplayActionStarted += (_, _, _) => observerStarts++;
            session.Client.Runner.GameplayActionCompleted += (_, _, _) => requesterCompletions++;

            AssertThat(session.Client.Runner.TryStartActionInput("use")).IsTrue();
            AssertThat(
                    session.Client.ExternalActions.TryGetExecutionPresentation(
                        OpenAction,
                        out GameplayActionExecutionPresentation prediction
                    )
                )
                .IsTrue();
            AssertThat(prediction.ExecutionId).IsEqual(0UL);

            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(1);
            AssertThat(session.Client.Executor.ExecuteCount).IsEqual(0);
            AssertThat(session.Observer.Executor.ExecuteCount).IsEqual(0);
            AssertThat(requesterStarts).IsEqual(1);
            AssertThat(observerStarts).IsEqual(0);
            AssertThat(
                    session.Client.ExternalActions.TryGetExecutionPresentation(
                        OpenAction,
                        out GameplayActionExecutionPresentation confirmed
                    )
                )
                .IsTrue();
            AssertThat(confirmed.ExecutionId).IsEqual(session.Server.Executor.ExecutionId);
            AssertThat(
                    session.Observer.ExternalActions.TryGetExecutionPresentation(OpenAction, out _)
                )
                .IsFalse();

            session.Server.ExternalActions.CompleteExecution(session.Server.Executor.ExecutionId);
            await session.Pump(RoundTripFrames);

            AssertThat(requesterCompletions).IsEqual(1);
            AssertThat(
                    session.Client.ExternalActions.TryGetExecutionPresentation(OpenAction, out _)
                )
                .IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task FabricatedClientBindingCannotBypassTheAuthorityAccessProvider()
    {
        Session session = await Connect(serverAllowsAccess: false);
        try
        {
            int rejections = 0;
            session.Client.Runner.GameplayActionRejected += (_, actionId, reason) =>
            {
                AssertThat(actionId).IsEqual(OpenAction);
                AssertThat(reason).IsEqual(GameplayActionAvailabilityExtensions.UnavailableReason);
                rejections++;
            };

            AssertThat(session.Client.Runner.TryStartActionInput("use")).IsTrue();
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(0);
            AssertThat(session.Client.Executor.ExecuteCount).IsEqual(0);
            AssertThat(rejections).IsEqual(1);
            AssertThat(
                    session.Client.ExternalActions.TryGetExecutionPresentation(OpenAction, out _)
                )
                .IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ReleasingARequestedInputCancelsItsAuthoritativeExecution()
    {
        Session session = await Connect(serverAllowsAccess: true, sustainedInput: true);
        try
        {
            int cancellations = 0;
            session.Client.Runner.GameplayActionCancelled += (_, actionId, _, _) =>
            {
                AssertThat(actionId).IsEqual(OpenAction);
                cancellations++;
            };
            session.Client.Runner.TryStartActionInput("use");
            await session.Pump(RoundTripFrames);

            AssertThat(session.Client.Runner.TryEndActionInput("use")).IsTrue();
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.CancelledCount).IsEqual(1);
            AssertThat(session.Server.ExternalActions.IsActionExecuting(OpenAction)).IsFalse();
            AssertThat(cancellations).IsEqual(1);
            AssertThat(
                    session.Client.ExternalActions.TryGetExecutionPresentation(OpenAction, out _)
                )
                .IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task NonOwnerPeerCannotRequestThroughAnotherPlayersRunner()
    {
        Session session = await Connect(serverAllowsAccess: true);
        try
        {
            int observerRejections = 0;
            session.Observer.Runner.GameplayActionRejected += (_, actionId, _) =>
            {
                AssertThat(actionId).IsEqual(OpenAction);
                observerRejections++;
            };

            session.Observer.Runner.RpcId(
                1,
                nameof(GameplayActionRunner.ServerTryStartAction),
                new NodePath("Door/Actions"),
                OpenAction
            );
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(0);
            AssertThat(observerRejections).IsEqual(1);
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task RequesterDisconnectCancelsPresenceOwnedAuthoritativeExecution()
    {
        Session session = await Connect(serverAllowsAccess: true);
        try
        {
            session.Client.Runner.TryStartActionInput("use");
            await session.Pump(RoundTripFrames);
            AssertThat(session.Server.ExternalActions.IsActionExecuting(OpenAction)).IsTrue();

            session.Peers[1].Close();
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.CancelledCount).IsEqual(1);
            AssertThat(session.Server.ExternalActions.IsActionExecuting(OpenAction)).IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AuthorityOnlyAcknowledgementStillBlocksDuplicateLocalRequests()
    {
        Session session = await Connect(
            serverAllowsAccess: true,
            visibility: GameplayActionExecutionVisibility.AuthorityOnly
        );
        try
        {
            AssertThat(session.Client.Runner.TryStartActionInput("use")).IsTrue();
            session.Client.Runner.TryEndActionInput("use");
            await session.Pump(RoundTripFrames);

            AssertThat(
                    session.Client.ExternalActions.TryGetExecutionPresentation(OpenAction, out _)
                )
                .IsFalse();
            AssertThat(session.Client.Runner.TryStartActionInput("use")).IsFalse();
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(1);

            session.Server.ExternalActions.CompleteExecution(session.Server.Executor.ExecutionId);
            await session.Pump(RoundTripFrames);

            AssertThat(session.Client.Runner.TryStartActionInput("use")).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    private static async Task<Session> Connect(
        bool serverAllowsAccess,
        bool sustainedInput = false,
        GameplayActionExecutionVisibility visibility =
            GameplayActionExecutionVisibility.RequesterOnly
    )
    {
        int port = _nextPort++;
        Node world = new() { Name = "GameplayActionRunnerNetworkWorld" };
        Node serverRoot = new() { Name = "Server" };
        Node clientRoot = new() { Name = "Client" };
        Node observerRoot = new() { Name = "Observer" };
        world.AddChild(serverRoot);
        world.AddChild(clientRoot);
        world.AddChild(observerRoot);
        ISceneRunner sceneRunner = ISceneRunner.Load(world);
        await sceneRunner.SimulateFrames(1);

        SceneTree tree = world.GetTree();
        ENetMultiplayerPeer serverPeer = new();
        AssertThat(serverPeer.CreateServer(port, 8)).IsEqual(Error.Ok);
        MultiplayerApi serverApi = Attach(tree, serverRoot, serverPeer);
        ENetMultiplayerPeer clientPeer = new();
        AssertThat(clientPeer.CreateClient("127.0.0.1", port)).IsEqual(Error.Ok);
        MultiplayerApi clientApi = Attach(tree, clientRoot, clientPeer);
        ENetMultiplayerPeer observerPeer = new();
        AssertThat(observerPeer.CreateClient("127.0.0.1", port)).IsEqual(Error.Ok);
        MultiplayerApi observerApi = Attach(tree, observerRoot, observerPeer);

        PeerScene server = BuildPeerScene(serverRoot, serverAllowsAccess, visibility);
        PeerScene client = BuildPeerScene(clientRoot, allowsAccess: true, visibility);
        PeerScene observer = BuildPeerScene(observerRoot, allowsAccess: true, visibility);
        await sceneRunner.SimulateFrames(1);

        for (int frame = 0; frame < ConnectFrames; frame++)
        {
            await sceneRunner.SimulateFrames(1);
            if (
                serverApi.GetPeers().Length == 2
                && clientApi.GetUniqueId() > 1
                && observerApi.GetUniqueId() > 1
            )
            {
                break;
            }
        }

        int ownerPeerId = clientApi.GetUniqueId();
        server.Runner.OwnerPeerId = ownerPeerId;
        client.Runner.OwnerPeerId = ownerPeerId;
        observer.Runner.OwnerPeerId = ownerPeerId;
        GameplayActionBindingConfig config = new()
        {
            InputActionName = "use",
            ActivationMode = GameplayActionActivationMode.Press,
            InputRequirement = sustainedInput
                ? GameplayActionInputRequirement.Pressed
                : GameplayActionInputRequirement.None,
        };
        client.Runner.BindAction(
            client.ExternalActions,
            OpenAction,
            client.ExternalActions,
            config
        );

        AssertThat(serverApi.IsServer()).IsTrue();
        AssertThat(clientApi.IsServer()).IsFalse();
        AssertThat(observerApi.IsServer()).IsFalse();
        AssertThat(serverApi.GetPeers().Length).IsEqual(2);
        await sceneRunner.SimulateFrames(1);
        return new Session(
            sceneRunner,
            server,
            client,
            observer,
            new List<ENetMultiplayerPeer> { serverPeer, clientPeer, observerPeer }
        );
    }

    private static MultiplayerApi Attach(SceneTree tree, Node root, MultiplayerPeer peer)
    {
        MultiplayerApi api = MultiplayerApi.CreateDefaultInterface();
        api.MultiplayerPeer = peer;
        tree.SetMultiplayer(api, root.GetPath());
        return api;
    }

    private static PeerScene BuildPeerScene(
        Node root,
        bool allowsAccess,
        GameplayActionExecutionVisibility visibility
    )
    {
        Node actor = new() { Name = "Actor" };
        GameplayActionComponent ownedActions = new() { Name = "OwnedActions" };
        GameplayActionRunner runner = new()
        {
            Name = "GameplayActionRunner",
            OwnedActionComponent = ownedActions,
        };
        actor.AddChild(ownedActions);
        actor.AddChild(runner);
        root.AddChild(actor);

        Node door = new() { Name = "Door" };
        GameplayActionComponent externalActions = new() { Name = "Actions" };
        NetworkRecordingExecutor executor = new() { Name = "OpenExecutor" };
        NetworkAccessAction action = new()
        {
            Name = "OpenAction",
            Definition = new GameplayActionDefinition { Id = OpenAction },
            Executor = executor,
            ExecutionVisibility = visibility,
        };
        action.AddChild(executor);
        externalActions.AddAction(action);
        door.AddChild(externalActions);
        root.AddChild(door);

        runner.RegisterAccessProvider(
            NetworkAccessAction.ProviderId,
            new NetworkAccessProvider { Allowed = allowsAccess }
        );
        return new PeerScene(runner, externalActions, executor);
    }

    private sealed partial class NetworkAccessAction : GameplayAction
    {
        public static readonly StringName ProviderId = new("network-test-access");

        public override StringName AccessProviderId => ProviderId;
    }

    private sealed class NetworkAccessProvider : IGameplayActionAccessProvider
    {
        public bool Allowed { get; set; }

        public bool CanRequest(in GameplayActionAccessContext context) => Allowed;
    }

    private sealed partial class NetworkRecordingExecutor : GameplayActionExecutor
    {
        public int ExecuteCount { get; private set; }

        public int CancelledCount { get; private set; }

        public ulong ExecutionId { get; private set; }

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            ExecuteCount++;
            ExecutionId = context.ExecutionId;
            return new GameplayActionExecutionRunning();
        }

        internal override GameplayActionProgressSample? GetPredictionSample(
            in GameplayActionContext context
        ) => new GameplayActionProgressSample(0.0f, 1.0f, 0L);

        protected internal override void OnExecutionCancelled(
            in GameplayActionContext context,
            string reason
        ) => CancelledCount++;
    }

    private sealed record PeerScene(
        GameplayActionRunner Runner,
        GameplayActionComponent ExternalActions,
        NetworkRecordingExecutor Executor
    );

    private sealed record Session(
        ISceneRunner Runner,
        PeerScene Server,
        PeerScene Client,
        PeerScene Observer,
        List<ENetMultiplayerPeer> Peers
    )
    {
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
