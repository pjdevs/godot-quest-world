namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Integration.Stateful;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.State;
using static GdUnit4.Assertions;

public abstract partial class InteractionNetworkTestBase
{
    private const int ConnectFrames = 240;
    protected const int RoundTripFrames = 12;
    protected static readonly StringName InteractInput = new("interact");
    protected static readonly StringName TuneInput = new("tune");
    protected static readonly StringName SwitchInput = new("switch");
    protected static readonly StringName ActivateAction = new("activate");
    protected static readonly StringName IdleState = new("idle");
    protected static readonly StringName ActivatingState = new("activating");
    protected static readonly StringName ActivatedState = new("activated");
    protected static int _nextPort = 47820;

    protected static GameplayActionPresentation PresentedAction(
        in InteractionTargetPresentation presentation,
        string actionId
    )
    {
        foreach (GameplayActionPresentation action in presentation.Actions)
        {
            if (action.ActionId.ToString() == actionId)
            {
                return action;
            }
        }

        throw new InvalidOperationException($"{actionId} is not presented.");
    }

    protected static async Task<Session> Connect()
    {
        int port = _nextPort++;
        Node world = new() { Name = "NetworkWorld" };
        Node3D serverRoot = new() { Name = "Server" };
        Node3D clientRootA = new() { Name = "ClientA" };
        Node3D clientRootB = new() { Name = "ClientB" };
        world.AddChild(serverRoot);
        world.AddChild(clientRootA);
        world.AddChild(clientRootB);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
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

    protected static MultiplayerApi Attach(SceneTree tree, Node root, MultiplayerPeer peer)
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
    protected static PeerScene BuildPeerScene(Node3D root)
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
            GameplayAction.DefaultHostConcurrencyGroup,
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
            GameplayAction.DefaultHostConcurrencyGroup,
            sustained: true
        );

        return new PeerScene(
            root,
            first.Interactive,
            first.ExecutionSynchronizer,
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
    protected static MultiplayerSynchronizer BuildSynchronizer()
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

    protected static Target BuildTarget(Node3D root, string name, StatefulComponent? stateful)
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

        // The host and its synchronizer sit beside the interactive, exactly like the authored
        // scenes, and both are wired before the interactive enters the live tree.
        interactive.ConfigureActionHost(actor);
        GameplayActionExecutionSynchronizer executionSynchronizer = new()
        {
            Name = "GameplayActionExecutionSynchronizer",
            Component = interactive.ActionComponent,
        };
        actor.AddChild(executionSynchronizer);
        actor.AddChild(interactive);
        root.AddChild(actor);
        return new Target(actor, interactive, executionSynchronizer);
    }

    protected static TestScriptedExecutor AddScriptedAction(
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
        interactive.AddAction(action);
        return executor;
    }

    /// <summary>Adds the production path from an accepted command to a replicated world state.</summary>
    protected static void AddSetStateAction(
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
                StatefulPath = new NodePath("../../StatefulComponent"),
                ExpectedStates = new Godot.Collections.Array<StringName> { IdleState },
                MismatchAvailability = GameplayActionUnavailableKind.Blocked,
                BlockReason = "Somebody is already using this.",
            }
        );
        SetStateGameplayActionExecutor executor = new()
        {
            Name = "SwitchExecutor",
            Stateful = stateful,
            TargetState = ActivatingState,
        };
        action.AddChild(executor);
        action.Executor = executor;
        interactive.AddAction(action);
    }

    protected static InteractionAction NewAction(
        StringName id,
        StringName input,
        StringName concurrencyGroup,
        bool sustained
    ) =>
        new()
        {
            Name = $"{id}Action",
            HostConcurrencyGroup = concurrencyGroup,
            Definition = new GameplayActionDefinition { Id = id, Label = id.ToString() },
            DefaultBindingConfig = new GameplayActionBindingConfig
            {
                InputActionName = input,
                ActivationMode = GameplayActionActivationMode.Press,
                InputRequirement = sustained
                    ? GameplayActionInputRequirement.Pressed
                    : GameplayActionInputRequirement.None,
            },
        };

    protected static PeerInteractor BuildInteractor(Node3D root, string name)
    {
        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = name };
        interactor.AddChild(view);
        TestInteractionDetector detector = new() { Name = "Detector", ViewOrigin = view };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        interactor.ConfigureActionRunner();
        root.AddChild(interactor);
        return new PeerInteractor(interactor, detector);
    }

    protected sealed record Target(
        Node3D Actor,
        InteractiveComponent Interactive,
        GameplayActionExecutionSynchronizer ExecutionSynchronizer
    );

    protected sealed record LatePeer(PeerScene Scene, StateLog Log);

    /// <summary>Records the three state signals of one branch, to count them per peer.</summary>
    protected sealed class StateLog
    {
        public StateLog(StatefulComponent stateful)
        {
            stateful.StateChanged += (oldState, newState, isSynchronization) =>
            {
                Changed.Add(newState.ToString());
                Transitions.Add($"{oldState}>{newState}");
                Synchronizations.Add(isSynchronization);
            };
            stateful.StateChangedAuthority += (_, newState, _) =>
                Authority.Add(newState.ToString());
            stateful.StateChangedPresentation += (_, newState, isSynchronization) =>
            {
                Presentation.Add(newState.ToString());
                PresentationSynchronizations.Add(isSynchronization);
            };
        }

        public List<string> Changed { get; } = new();
        public List<bool> Synchronizations { get; } = new();
        public List<bool> PresentationSynchronizations { get; } = new();

        public List<string> Transitions { get; } = new();

        public List<string> Authority { get; } = new();

        public List<string> Presentation { get; } = new();
    }

    protected sealed record Ack(
        string Kind,
        Node? Target,
        StringName ActionId,
        ulong ExecutionId,
        string Reason
    );

    protected sealed record PeerInteractor(
        InteractionInteractor Interactor,
        TestInteractionDetector Detector
    );

    protected sealed record PeerScene(
        Node3D Root,
        InteractiveComponent Interactive,
        GameplayActionExecutionSynchronizer ExecutionSynchronizer,
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

    protected sealed record Session(
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
        private GameplayActionExecutionVisibility _executionVisibility =
            GameplayActionExecutionVisibility.RequesterOnly;

        /// <summary>Tells every copy of an interactor which peer is allowed to drive it.</summary>
        public void Own(int peerA, int peerB)
        {
            foreach (PeerScene scene in new[] { Server, ClientA, ClientB })
            {
                scene.InteractorA.Runner!.OwnerPeerId = peerA;
                scene.InteractorB.Runner!.OwnerPeerId = peerB;
            }
        }

        public void Listen()
        {
            Record(ClientA.InteractorA, AcksA);
            Record(ClientB.InteractorB, AcksB);
        }

        /// <summary>Gives the same outcome to every copy, so the clients predict what the server runs.</summary>
        public void Arm(GameplayActionExecutionResult result, float duration)
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

        public void SetExecutionVisibility(GameplayActionExecutionVisibility visibility)
        {
            _executionVisibility = visibility;
            foreach (PeerScene scene in new[] { Server, ClientA, ClientB })
            {
                SetExecutionVisibility(scene, visibility);
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
            SetExecutionVisibility(scene, _executionVisibility);
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

        private static void SetExecutionVisibility(
            PeerScene scene,
            GameplayActionExecutionVisibility visibility
        )
        {
            scene.Interactive.ResolveAction(ActivateAction)!.ExecutionVisibility = visibility;
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
            interactor.InteractionStarted += (target, actionId, executionId) =>
                acks.Add(new Ack("started", target, actionId, executionId, string.Empty));
            interactor.InteractionCompleted += (target, actionId) =>
                acks.Add(new Ack("completed", target, actionId, 0ul, string.Empty));
            interactor.InteractionCancelled += (target, actionId, reason) =>
                acks.Add(new Ack("cancelled", target, actionId, 0ul, reason));
            interactor.InteractionFailed += (target, actionId, reason) =>
                acks.Add(new Ack("failed", target, actionId, 0ul, reason));
            interactor.InteractionRejected += (target, actionId, reason) =>
                acks.Add(new Ack("rejected", target, actionId, 0ul, reason));
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
