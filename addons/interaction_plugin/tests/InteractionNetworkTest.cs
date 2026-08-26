namespace QuestWorld.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using static GdUnit4.Assertions;

/// <summary>Runs a real server and two real clients to check the acknowledgement over the wire.</summary>
/// <remarks>
/// Godot gives one <see cref="MultiplayerApi"/> per subtree, so three peers can share one process:
/// each owns a branch holding an identical copy of the scene, and the ENet loopback carries genuine
/// serialized RPCs between them. This is what the in-tree acknowledgement tests cannot prove — that
/// the RPC declarations, the payload types and the targeting are right, and that a client only ever
/// receives the lifecycle of its own request.
/// <para>
/// It also depends on interaction paths being named relative to the multiplayer root rather than to
/// the scene root: the three copies of one target differ only by their branch.
/// </para>
/// </remarks>
[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionNetworkTest
{
    private const int ConnectFrames = 240;
    private const int RoundTripFrames = 12;
    private static readonly StringName InteractInput = new("interact");
    private static readonly StringName ActivateAction = new("activate");
    private static int _nextPort = 47820;

    [TestCase]
    public async Task AStartedActionIsAcknowledgedToTheRequesterAlone()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 2.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEmpty();
            Ack started = session.AcksA[0];
            AssertThat(started.Duration).IsEqual(2.0f);
            AssertThat(started.ExecutionId).IsGreater(0ul);
            // Each peer resolves the path in its own branch, so the requester is handed its own copy.
            AssertThat(started.Target == session.ClientA.Interactive).IsTrue();
            // The command ran once, on the authority, and nowhere else.
            AssertThat(session.Server.Executor.LastExecutionId).IsGreater(0ul);
            AssertThat(session.ClientA.Executor.LastExecutionId).IsEqual(0ul);
            AssertThat(session.ClientB.Executor.LastExecutionId).IsEqual(0ul);
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ACompletionIsAcknowledgedToTheRequesterAlone()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 2.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.Server.Interactive.CompleteExecution(session.Server.Executor.LastExecutionId);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "completed" });
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ASecondClientIsRefusedWhileTheFirstHoldsTheAction()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 2.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            // One execution started, and the peer that lost learns it without the winner hearing a
            // thing about a request that was never its own.
            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "rejected" });
            AssertThat(
                    session.Server.Interactive.IsExecutionActive(
                        session.Server.Executor.LastExecutionId
                    )
                )
                .IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AFailureCrossesTheNetworkAsStartedThenFailed()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionFailed("The socket is welded shut."), 0.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "failed" });
            AssertThat(session.AcksA[1].Reason).IsEqual("The socket is welded shut.");
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ARefusalCrossesTheNetworkWithoutEverStarting()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRejected("The till is closed."), 0.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "rejected" });
            AssertThat(session.AcksA[0].Reason).IsEqual("The till is closed.");
            AssertThat(session.ClientA.InteractorA.TryGetExecutionProgress(out _, out _)).IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    private static async Task<Session> Connect()
    {
        int port = _nextPort++;
        Node world = new() { Name = "NetworkWorld" };
        PeerScene server = BuildPeerScene("Server");
        PeerScene clientA = BuildPeerScene("ClientA");
        PeerScene clientB = BuildPeerScene("ClientB");
        world.AddChild(server.Root);
        world.AddChild(clientA.Root);
        world.AddChild(clientB.Root);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        SceneTree tree = world.GetTree();
        ENetMultiplayerPeer serverPeer = new();
        AssertThat(serverPeer.CreateServer(port, 8)).IsEqual(Error.Ok);
        MultiplayerApi serverApi = Attach(tree, server.Root, serverPeer);
        ENetMultiplayerPeer peerA = new();
        AssertThat(peerA.CreateClient("127.0.0.1", port)).IsEqual(Error.Ok);
        MultiplayerApi apiA = Attach(tree, clientA.Root, peerA);
        ENetMultiplayerPeer peerB = new();
        AssertThat(peerB.CreateClient("127.0.0.1", port)).IsEqual(Error.Ok);
        MultiplayerApi apiB = Attach(tree, clientB.Root, peerB);

        for (int frame = 0; frame < ConnectFrames; frame++)
        {
            await runner.SimulateFrames(1);
            if (
                serverApi.GetPeers().Length >= 2
                && apiA.GetUniqueId() > 1
                && apiB.GetUniqueId() > 1
            )
            {
                break;
            }
        }

        AssertThat(serverApi.GetPeers().Length).IsEqual(2);
        // Guards the harness itself: without three distinct peers the whole suite would quietly
        // degrade into the local calls the in-tree acknowledgement tests already cover.
        AssertThat(serverApi.IsServer()).IsTrue();
        AssertThat(apiA.IsServer()).IsFalse();
        AssertThat(apiB.IsServer()).IsFalse();

        Session session = new(
            runner,
            world,
            server,
            clientA,
            clientB,
            new[] { serverPeer, peerA, peerB },
            new List<Ack>(),
            new List<Ack>()
        );
        session.Own((int)apiA.GetUniqueId(), (int)apiB.GetUniqueId());
        session.Listen();
        await runner.SimulateFrames(1);
        return session;
    }

    private static MultiplayerApi Attach(SceneTree tree, Node root, MultiplayerPeer peer)
    {
        MultiplayerApi api = MultiplayerApi.CreateDefaultInterface();
        api.MultiplayerPeer = peer;
        tree.SetMultiplayer(api, root.GetPath());
        return api;
    }

    /// <summary>Builds one branch holding the copy of the scene a single peer owns.</summary>
    /// <remarks>
    /// The three branches are structurally identical because Godot routes an RPC by the path of its
    /// node relative to the multiplayer root: <c>InteractorA</c> only reaches <c>InteractorA</c>.
    /// </remarks>
    private static PeerScene BuildPeerScene(string name)
    {
        Node3D root = new() { Name = name };
        Node3D actor = new() { Name = "Actor" };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = actor,
        };
        InteractionAction action = new()
        {
            Name = "ActivateAction",
            Definition = new InteractionActionDefinition
            {
                Id = ActivateAction,
                Label = "Activate",
                InputActionName = InteractInput,
            },
        };
        TestScriptedExecutor executor = new() { Name = "ActivateExecutor" };
        action.AddChild(executor);
        action.Executor = executor;
        interactive.Actions.Add(action);
        actor.AddChild(area);
        actor.AddChild(interactive);
        interactive.AddChild(action);
        root.AddChild(actor);

        return new PeerScene(
            root,
            interactive,
            executor,
            BuildInteractor(root, "InteractorA"),
            BuildInteractor(root, "InteractorB")
        );
    }

    private static PeerInteractor BuildInteractor(Node3D root, string name)
    {
        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = name, OwnerPeerId = 1 };
        interactor.AddChild(view);
        TestInteractionDetector detector = new() { Name = "Detector", ViewOrigin = view };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        root.AddChild(interactor);
        return new PeerInteractor(interactor, detector);
    }

    private sealed record Ack(
        string Kind,
        Node? Target,
        StringName ActionId,
        ulong ExecutionId,
        float Duration,
        string Reason
    );

    private sealed record PeerInteractor(
        InteractionInteractor Interactor,
        TestInteractionDetector Detector
    );

    private sealed record PeerScene(
        Node3D Root,
        InteractiveComponent Interactive,
        TestScriptedExecutor Executor,
        PeerInteractor A,
        PeerInteractor B
    )
    {
        public InteractionInteractor InteractorA => A.Interactor;

        public InteractionInteractor InteractorB => B.Interactor;
    }

    private sealed record Session(
        ISceneRunner Runner,
        Node World,
        PeerScene Server,
        PeerScene ClientA,
        PeerScene ClientB,
        ENetMultiplayerPeer[] Peers,
        List<Ack> AcksA,
        List<Ack> AcksB
    )
    {
        /// <summary>Tells every copy of an interactor which peer is allowed to drive it.</summary>
        public void Own(int peerA, int peerB)
        {
            foreach (PeerScene scene in new[] { Server, ClientA, ClientB })
            {
                scene.InteractorA.OwnerPeerId = peerA;
                scene.InteractorB.OwnerPeerId = peerB;
            }
        }

        public void Listen()
        {
            Record(ClientA.InteractorA, AcksA);
            Record(ClientB.InteractorB, AcksB);
        }

        /// <summary>Gives the same outcome to every copy, so the clients predict what the server runs.</summary>
        public void Arm(InteractionExecutionResult result, float duration)
        {
            foreach (PeerScene scene in new[] { Server, ClientA, ClientB })
            {
                scene.Executor.Result = result;
                scene.Executor.Duration = duration;
            }
        }

        /// <summary>Detects the target in each branch, for the interactors that peer drives.</summary>
        public void Focus()
        {
            Detect(Server, Server.A);
            Detect(Server, Server.B);
            Detect(ClientA, ClientA.A);
            Detect(ClientB, ClientB.B);
        }

        public async Task Pump(int frames) => await Runner.SimulateFrames((uint)frames);

        public List<string> KindsA() => Kinds(AcksA);

        public List<string> KindsB() => Kinds(AcksB);

        public void Close()
        {
            foreach (ENetMultiplayerPeer peer in Peers)
            {
                peer.Close();
            }
        }

        private static void Detect(PeerScene scene, PeerInteractor peer)
        {
            peer.Detector.SetDetection(scene.Interactive, InteractionDetectionKind.Interactible);
            peer.Interactor.RecalculateFocus();
        }

        private static void Record(InteractionInteractor interactor, List<Ack> acks)
        {
            interactor.InteractionStarted += (target, actionId, executionId, duration) =>
                acks.Add(new Ack("started", target, actionId, executionId, duration, string.Empty));
            interactor.InteractionCompleted += (target, actionId) =>
                acks.Add(new Ack("completed", target, actionId, 0ul, 0.0f, string.Empty));
            interactor.InteractionCancelled += (target, actionId, reason) =>
                acks.Add(new Ack("cancelled", target, actionId, 0ul, 0.0f, reason));
            interactor.InteractionFailed += (target, actionId, reason) =>
                acks.Add(new Ack("failed", target, actionId, 0ul, 0.0f, reason));
            interactor.InteractionRejected += (target, actionId, reason) =>
                acks.Add(new Ack("rejected", target, actionId, 0ul, 0.0f, reason));
        }

        private static List<string> Kinds(List<Ack> acks)
        {
            List<string> kinds = new();
            foreach (Ack ack in acks)
            {
                kinds.Add(ack.Kind);
            }

            return kinds;
        }
    }
}
