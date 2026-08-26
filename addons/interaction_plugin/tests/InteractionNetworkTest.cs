namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.State;
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
    private static readonly StringName TuneInput = new("tune");
    private static readonly StringName SwitchInput = new("switch");
    private static readonly StringName ActivateAction = new("activate");
    private static readonly StringName IdleState = new("idle");
    private static readonly StringName ActivatingState = new("activating");
    private static readonly StringName ActivatedState = new("activated");
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

    [TestCase]
    public async Task TwoClientsRequestingTheSameActionInOneFrameStartASingleExecution()
    {
        // The race the doc calls out: both peers ask before anything replicated, so both believe
        // they may. Exactly one command runs, and the loser learns it alone.
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(1);
            List<string> a = session.KindsA();
            List<string> b = session.KindsB();
            AssertThat(a.Count + b.Count).IsEqual(2);
            AssertThat(a.Contains("started") ^ b.Contains("started")).IsTrue();
            AssertThat(a.Contains("rejected") ^ b.Contains("rejected")).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TheClientThatLostTheRaceClearsItsPredictionAtOnce()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsB()).IsEqual(new List<string> { "rejected" });
            AssertThat(session.ClientB.InteractorB.TryGetExecutionProgress(out _, out _)).IsFalse();
            // The winner keeps drawing its own bar.
            AssertThat(session.ClientA.InteractorA.TryGetExecutionProgress(out _, out _)).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AReleasedLongActionFreesTheTargetForTheOtherClient()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.ClientA.InteractorA.TryEndInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "cancelled" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(2);
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AnInteractorLeavingTheTreeFreesTheTargetForTheOtherClient()
    {
        // A disconnection reaches the plugin as the departure of the interactor node, which is what
        // the project spawn layer does when a player leaves. Nothing else releases the reservation.
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.Server.InteractorA.GetParent().RemoveChild(session.Server.InteractorA);
            await session.Pump(RoundTripFrames);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(2);
        }
        finally
        {
            session.Server.InteractorA.QueueFree();
            session.Close();
        }
    }

    [TestCase]
    public async Task AClientLeavingTheAuthoritativeWindowLosesItsExecution()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.UnfocusAOnAuthority();
            await session.Pump(RoundTripFrames);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "cancelled" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoClientsOnTwoTargetsBothStartWithoutHearingEachOther()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.FocusA(session.Server.Interactive, session.ClientA.Interactive);
            session.FocusB(session.Server.SecondInteractive, session.ClientB.SecondInteractive);

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.AcksA[0].Target == session.ClientA.Interactive).IsTrue();
            AssertThat(session.AcksB[0].Target == session.ClientB.SecondInteractive).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoClientsOnDistinctConcurrencyGroupsOfOneTargetBothStart()
    {
        // Exclusivity is a property of the concurrency group, not of the target: two commands that
        // do not exclude each other may be held at once by two different players.
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            session.ClientB.InteractorB.TryStartInteractionInput(TuneInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.AcksA[0].ActionId.ToString()).IsEqual("activate");
            AssertThat(session.AcksB[0].ActionId.ToString()).IsEqual("tune");
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ALongActionCompletedByItsOwnClockIsAcknowledgedToItsRequesterAlone()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 0.2f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(30, millisecondsPerFrame: 20);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "completed" });
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AReplicatedTransitionPlaysTheFeedbackOfEachPeerExactlyOnce()
    {
        Session session = await Connect();
        try
        {
            StateLog onServer = session.WatchState(session.Server);
            StateLog onA = session.WatchState(session.ClientA);
            StateLog onB = session.WatchState(session.ClientB);

            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            foreach (PeerScene scene in new[] { session.Server, session.ClientA, session.ClientB })
            {
                AssertThat(scene.Stateful.State.ToString()).IsEqual("activating");
            }

            foreach (StateLog log in new[] { onServer, onA, onB })
            {
                AssertThat(log.Changed).IsEqual(new List<string> { "activating" });
                AssertThat(log.Presentation).IsEqual(new List<string> { "activating" });
            }

            // The server of this harness is a listen host: it is the only peer with authority, and
            // it plays its presentation like anybody else.
            AssertThat(onServer.Authority).IsEqual(new List<string> { "activating" });
            AssertThat(onA.Authority).IsEmpty();
            AssertThat(onB.Authority).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AStateSetTwiceToTheSameValueReplicatesNothing()
    {
        Session session = await Connect();
        try
        {
            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            StateLog onServer = session.WatchState(session.Server);
            StateLog onA = session.WatchState(session.ClientA);

            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);

            AssertThat(onServer.Changed).IsEmpty();
            AssertThat(onA.Changed).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoTransitionsSeparatedByAFrameArriveInOrderAndPlayOnce()
    {
        Session session = await Connect();
        try
        {
            StateLog onA = session.WatchState(session.ClientA);
            StateLog onB = session.WatchState(session.ClientB);

            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            List<string> expected = new() { "activating", "activated" };
            AssertThat(onA.Presentation).IsEqual(expected);
            AssertThat(onB.Presentation).IsEqual(expected);
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoTransitionsInsideOneFrameReachClientsAsTheLastValueOnly()
    {
        // A replicated property carries a value, not a history. A one-shot feedback keyed on a
        // transition a client never receives simply never plays, which is why a pose is applied from
        // the current state and only sounds and effects are left to transitions.
        Session session = await Connect();
        try
        {
            StateLog onA = session.WatchState(session.ClientA);

            session.Server.Stateful.SetState(ActivatingState);
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            AssertThat(session.ClientA.Stateful.State.ToString()).IsEqual("activated");
            AssertThat(onA.Presentation).IsEqual(new List<string> { "activated" });
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AnInteractionReplicatesItsStateToEveryPeerWhileTheAckStaysWithItsRequester()
    {
        // The whole point of the two channels, in one scenario: what is true of the world reaches
        // everybody, what is true only of one player's request reaches only that player.
        Session session = await Connect();
        try
        {
            StateLog onA = session.WatchState(session.ClientA);
            StateLog onB = session.WatchState(session.ClientB);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(SwitchInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.ClientB.Stateful.State.ToString()).IsEqual("activating");
            AssertThat(onB.Presentation).IsEqual(new List<string> { "activating" });
            AssertThat(onA.Presentation).IsEqual(new List<string> { "activating" });

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "completed" });
            AssertThat(session.KindsB()).IsEmpty();

            // And B presents the action as busy from the state alone, having received no interaction
            // event of any kind.
            InteractionActionPresentation switchOnB = PresentedAction(
                session.ClientB.Interactive.GetPresentation(
                    session.ClientB.InteractorB,
                    isFocused: true
                ),
                "switch"
            );
            AssertThat(switchOnB.Availability is InteractionBlocked).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    private static InteractionActionPresentation PresentedAction(
        in InteractionTargetPresentation presentation,
        string actionId
    )
    {
        foreach (InteractionActionPresentation action in presentation.Actions)
        {
            if (action.ActionId.ToString() == actionId)
            {
                return action;
            }
        }

        throw new InvalidOperationException($"{actionId} is not presented.");
    }

    [TestCase]
    public async Task ALateJoinerArrivesAtTheCurrentStateWithoutTheStatesItMissed()
    {
        Session session = await Connect();
        try
        {
            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            LatePeer late = await session.JoinLate("ClientC");

            AssertThat(late.Scene.Stateful.State.ToString()).IsEqual("activated");
            // The intermediate state is gone: replication carries the current value, so a peer that
            // was not there never learns the world passed through `activating`.
            AssertThat(late.Log.Changed).IsEqual(new List<string> { "activated" });
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ALateJoinerCannotTellItsArrivalFromARealTransition()
    {
        // Freezes a confirmed hole rather than a guarantee. The component applies the initial state in
        // _Ready, then the first replicated value arrives through the very same setter as any later
        // one, so it is dispatched as `idle > activated`. A presentation that plays a one-shot on
        // StateChangedPresentation — the pattern the docs recommend — therefore plays the "activated"
        // sound to a player who was not there when it happened. Distinguishing the two would take a
        // first-sync marker the framework does not have yet.
        Session session = await Connect();
        try
        {
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            LatePeer late = await session.JoinLate("ClientC");

            AssertThat(late.Log.Transitions).IsEqual(new List<string> { "idle>activated" });
            AssertThat(late.Log.Presentation).IsEqual(new List<string> { "activated" });
            AssertThat(late.Log.Authority).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ALateJoinerPresentsAnActionAlreadyTakenAsBusy()
    {
        // The half of the late join that does hold: the pose and the availability come from the
        // current state, so a peer arriving mid-action reads the world correctly.
        Session session = await Connect();
        try
        {
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(SwitchInput);
            await session.Pump(RoundTripFrames);

            LatePeer late = await session.JoinLate("ClientC");

            AssertThat(late.Scene.Stateful.State.ToString()).IsEqual("activating");
            InteractionActionPresentation onLate = PresentedAction(
                late.Scene.Interactive.GetPresentation(late.Scene.InteractorA, isFocused: true),
                "switch"
            );
            AssertThat(onLate.Availability is InteractionBlocked).IsTrue();
            // And the acknowledgement of somebody else's request never reached it.
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ADroppedPeerLeavesItsExecutionReservedOnTheAuthority()
    {
        // Freezes a confirmed hole rather than a guarantee. The plugin ends an execution when the
        // interactor node leaves the tree, which is what a project spawn layer does when it despawns a
        // departed player. Nothing listens to the session itself, so a peer that simply drops — and
        // whose node nobody removes — keeps its reservation forever and locks the target for everybody
        // else. The interactor already knows its OwnerPeerId; only the subscription is missing.
        Session session = await Connect();
        try
        {
            session.Arm(new InteractionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);
            ulong reserved = session.Server.Executor.LastExecutionId;

            session.Peers[1].Close();
            await session.Pump(60);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Interactive.IsExecutionActive(reserved)).IsTrue();
            AssertThat(session.KindsB()).IsEqual(new List<string> { "rejected" });
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(1);
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
        Node3D serverRoot = new() { Name = "Server" };
        Node3D clientRootA = new() { Name = "ClientA" };
        Node3D clientRootB = new() { Name = "ClientB" };
        world.AddChild(serverRoot);
        world.AddChild(clientRootA);
        world.AddChild(clientRootB);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        SceneTree tree = world.GetTree();
        ENetMultiplayerPeer serverPeer = new();
        AssertThat(serverPeer.CreateServer(port, 8)).IsEqual(Error.Ok);
        MultiplayerApi serverApi = Attach(tree, serverRoot, serverPeer);
        ENetMultiplayerPeer peerA = new();
        AssertThat(peerA.CreateClient("127.0.0.1", port)).IsEqual(Error.Ok);
        MultiplayerApi apiA = Attach(tree, clientRootA, peerA);
        ENetMultiplayerPeer peerB = new();
        AssertThat(peerB.CreateClient("127.0.0.1", port)).IsEqual(Error.Ok);
        MultiplayerApi apiB = Attach(tree, clientRootB, peerB);

        // Each branch is populated only once its own MultiplayerApi is in place. A
        // MultiplayerSynchronizer binds to the API of its branch when it enters the tree, so a node
        // added first would stay bound to the peerless default and report "the multiplayer instance
        // isn't currently active" for the rest of the run.
        PeerScene server = BuildPeerScene(serverRoot);
        PeerScene clientA = BuildPeerScene(clientRootA);
        PeerScene clientB = BuildPeerScene(clientRootB);
        await runner.SimulateFrames(1);

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
            port,
            new List<ENetMultiplayerPeer> { serverPeer, peerA, peerB },
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
    /// The three branches are structurally identical because Godot routes an RPC — and a
    /// <see cref="MultiplayerSynchronizer"/> — by the path of its node relative to the multiplayer
    /// root: <c>InteractorA</c> only ever reaches <c>InteractorA</c>.
    /// <para>
    /// Each action carries its own input name. Three actions sharing one input would be departed by
    /// availability, then priority, then identifier, so the test would always get the same one and
    /// could never ask for a specific concurrency group.
    /// </para>
    /// </remarks>
    private static PeerScene BuildPeerScene(Node3D root)
    {
        StateSchema schema = new()
        {
            States = new Godot.Collections.Array<StringName>
            {
                IdleState,
                ActivatingState,
                ActivatedState,
            },
        };
        StatefulComponent stateful = new()
        {
            Name = "StatefulComponent",
            Schema = schema,
            InitialState = IdleState,
        };
        stateful.AddChild(BuildSynchronizer());

        Target first = BuildTarget(root, "Actor", stateful);
        TestScriptedExecutor activate = AddScriptedAction(
            first.Interactive,
            ActivateAction,
            InteractInput,
            InteractionAction.DefaultConcurrencyGroup,
            sustained: true
        );
        TestScriptedExecutor tune = AddScriptedAction(
            first.Interactive,
            new StringName("tune"),
            TuneInput,
            new StringName("tuning"),
            sustained: false
        );
        AddSetStateAction(first.Interactive, stateful);

        Target second = BuildTarget(root, "SecondActor", stateful: null);
        TestScriptedExecutor secondActivate = AddScriptedAction(
            second.Interactive,
            ActivateAction,
            InteractInput,
            InteractionAction.DefaultConcurrencyGroup,
            sustained: true
        );

        return new PeerScene(
            root,
            first.Interactive,
            second.Interactive,
            stateful,
            activate,
            tune,
            secondActivate,
            BuildInteractor(root, "InteractorA"),
            BuildInteractor(root, "InteractorB")
        );
    }

    /// <summary>Replicates the one technical property the stateful component exposes to the network.</summary>
    private static MultiplayerSynchronizer BuildSynchronizer()
    {
        SceneReplicationConfig config = new();
        NodePath property = new(".:ReplicatedState");
        config.AddProperty(property);
        config.PropertySetSpawn(property, true);
        config.PropertySetReplicationMode(
            property,
            SceneReplicationConfig.ReplicationMode.OnChange
        );
        return new MultiplayerSynchronizer
        {
            Name = "MultiplayerSynchronizer",
            ReplicationConfig = config,
        };
    }

    private static Target BuildTarget(Node3D root, string name, StatefulComponent? stateful)
    {
        Node3D actor = new() { Name = name };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = actor,
        };
        actor.AddChild(area);
        if (stateful is not null)
        {
            actor.AddChild(stateful);
        }

        actor.AddChild(interactive);
        root.AddChild(actor);
        return new Target(actor, interactive);
    }

    private static TestScriptedExecutor AddScriptedAction(
        InteractiveComponent interactive,
        StringName id,
        StringName input,
        StringName concurrencyGroup,
        bool sustained
    )
    {
        InteractionAction action = NewAction(id, input, concurrencyGroup, sustained);
        TestScriptedExecutor executor = new() { Name = $"{id}Executor" };
        action.AddChild(executor);
        action.Executor = executor;
        interactive.Actions.Add(action);
        interactive.AddChild(action);
        return executor;
    }

    /// <summary>Adds the production path from an accepted command to a replicated world state.</summary>
    private static void AddSetStateAction(
        InteractiveComponent interactive,
        StatefulComponent stateful
    )
    {
        InteractionAction action = NewAction(
            new StringName("switch"),
            SwitchInput,
            new StringName("switching"),
            sustained: false
        );
        // The rule is what turns the replicated state into presentation: every peer reads its own
        // copy of the state and answers the same thing, without any interaction event crossing.
        action.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = new NodePath("../StatefulComponent"),
                ExpectedStates = new Godot.Collections.Array<StringName> { IdleState },
                MismatchAvailability = InteractionUnavailableKind.Blocked,
                BlockReason = "Somebody is already using this.",
            }
        );
        SetStateInteractionExecutor executor = new()
        {
            Name = "SwitchExecutor",
            Stateful = stateful,
            TargetState = ActivatingState,
        };
        action.AddChild(executor);
        action.Executor = executor;
        interactive.Actions.Add(action);
        interactive.AddChild(action);
    }

    private static InteractionAction NewAction(
        StringName id,
        StringName input,
        StringName concurrencyGroup,
        bool sustained
    ) =>
        new()
        {
            Name = $"{id}Action",
            ConcurrencyGroup = concurrencyGroup,
            Definition = new InteractionActionDefinition
            {
                Id = id,
                Label = id.ToString(),
                InputActionName = input,
                CancelOnInputReleased = sustained,
            },
        };

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

    private sealed record Target(Node3D Actor, InteractiveComponent Interactive);

    private sealed record LatePeer(PeerScene Scene, StateLog Log);

    /// <summary>Records the three state signals of one branch, to count them per peer.</summary>
    private sealed class StateLog
    {
        public StateLog(StatefulComponent stateful)
        {
            stateful.StateChanged += (oldState, newState) =>
            {
                Changed.Add(newState.ToString());
                Transitions.Add($"{oldState}>{newState}");
            };
            stateful.StateChangedAuthority += (_, newState) => Authority.Add(newState.ToString());
            stateful.StateChangedPresentation += (_, newState) =>
                Presentation.Add(newState.ToString());
        }

        public List<string> Changed { get; } = new();

        public List<string> Transitions { get; } = new();

        public List<string> Authority { get; } = new();

        public List<string> Presentation { get; } = new();
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
        InteractiveComponent SecondInteractive,
        StatefulComponent Stateful,
        TestScriptedExecutor Executor,
        TestScriptedExecutor TuneExecutor,
        TestScriptedExecutor SecondExecutor,
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
        int Port,
        List<ENetMultiplayerPeer> Peers,
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
                scene.TuneExecutor.Result = result;
                scene.TuneExecutor.Duration = duration;
                scene.SecondExecutor.Result = result;
                scene.SecondExecutor.Duration = duration;
            }
        }

        /// <summary>Detects the first target in each branch, for the interactors that peer drives.</summary>
        /// <remarks>
        /// Detection is written per branch and per interactor rather than globally: the authority
        /// validates the request with its own copy of the window, so a scenario is only meaningful
        /// when the server sees what the client claims to see.
        /// </remarks>
        public void Focus()
        {
            FocusA(Server.Interactive, ClientA.Interactive);
            FocusB(Server.Interactive, ClientB.Interactive);
        }

        /// <summary>Points the interactor A owns at one target, on the authority and on its own client.</summary>
        public void FocusA(InteractiveComponent onServer, InteractiveComponent onClient)
        {
            Detect(Server.A, onServer);
            Detect(ClientA.A, onClient);
        }

        public void FocusB(InteractiveComponent onServer, InteractiveComponent onClient)
        {
            Detect(Server.B, onServer);
            Detect(ClientB.B, onClient);
        }

        /// <summary>Stops the authority from seeing the target of the interactor A owns.</summary>
        /// <remarks>
        /// Only the authoritative window is cleared: the point of the scenario is a player who walked
        /// away, which the server discovers on its own rather than being told by the client.
        /// </remarks>
        public void UnfocusAOnAuthority()
        {
            Server.A.Detector.ClearDetection(Server.Interactive);
        }

        public StateLog WatchState(PeerScene scene) => new(scene.Stateful);

        /// <summary>Connects a fresh peer to the running session and populates its branch.</summary>
        /// <remarks>
        /// The state log is attached before the handshake completes, so it records everything the new
        /// peer is told — which is the whole question a late join asks.
        /// </remarks>
        public async Task<LatePeer> JoinLate(string name)
        {
            Node3D root = new() { Name = name };
            World.AddChild(root);
            ENetMultiplayerPeer peer = new();
            AssertThat(peer.CreateClient("127.0.0.1", Port)).IsEqual(Error.Ok);
            MultiplayerApi api = Attach(World.GetTree(), root, peer);
            Peers.Add(peer);
            PeerScene scene = BuildPeerScene(root);
            StateLog log = new(scene.Stateful);

            for (int frame = 0; frame < ConnectFrames; frame++)
            {
                await Pump(1);
                if (api.GetUniqueId() > 1)
                {
                    break;
                }
            }

            AssertThat(api.GetUniqueId()).IsGreater(1);
            await Pump(RoundTripFrames);
            return new LatePeer(scene, log);
        }

        public async Task Pump(int frames) => await Runner.SimulateFrames((uint)frames);

        public async Task Pump(int frames, int millisecondsPerFrame) =>
            await Runner.SimulateFrames((uint)frames, (uint)millisecondsPerFrame);

        public List<string> KindsA() => Kinds(AcksA);

        public List<string> KindsB() => Kinds(AcksB);

        public void Close()
        {
            foreach (ENetMultiplayerPeer peer in Peers)
            {
                peer.Close();
            }
        }

        private static void Detect(PeerInteractor peer, InteractiveComponent interactive)
        {
            peer.Detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
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
