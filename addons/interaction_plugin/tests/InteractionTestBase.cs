namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Integration.Stateful;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Execution;
using GameplayActionPlugin.Runtime.Rules;
using GdUnit4;
using Godot;
using InteractionPlugin;
using InteractionPlugin.Examples.Rules;
using InteractionPlugin.Integration.Stateful;
using InteractionPlugin.Runtime.Interactive;
using InteractionPlugin.Runtime.Interactor;
using InteractionPlugin.Runtime.Offers;
using QuestWorld.Tests.GameplayActions;
using StatefulPlugin;
using static GdUnit4.Assertions;

public abstract partial class InteractionTestBase
{
    protected static readonly StringName InteractInput = new("interact");
    protected static readonly StringName IdleState = new("idle");
    protected static readonly StringName ActivatingState = new("activating");
    protected static readonly StringName ActivatedState = new("activated");

    protected static async Task WaitUntilExecutionEnds(CoreWorld core, ulong executionId)
    {
        for (int frame = 0; frame < 300 && core.Interactive.IsExecutionActive(executionId); frame++)
        {
            await core.Runner.SimulateFrames(1);
        }

        AssertThat(core.Interactive.IsExecutionActive(executionId)).IsFalse();
    }

    protected static List<GameplayActionPresentation> Presented(CoreWorld core) =>
        new(core.Interactive.GetPresentation(core.Interactor, true).Actions);

    protected static CoreWorld BuildCoreWorld()
    {
        Node3D world = new();
        Node3D reactor = new() { Name = "Reactor", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        StatefulComponent state = new()
        {
            Name = "StatefulComponent",
            InitialState = new StringName("dormant"),
            Schema = new StateSchema
            {
                States = States("dormant", "charging", "primed", "recharging", "activated"),
            },
        };
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = reactor,
            DisplayName = "Reactor core",
        };

        CarriesKeyInteractionRule key = new();
        GameplayAction activate = CoreAction(
            "activate",
            0.05f,
            "charging",
            "primed",
            "dormant",
            state,
            DoorStateRule("dormant")
        );
        GameplayAction reactivate = CoreAction(
            "reactivate",
            0.05f,
            "recharging",
            "activated",
            "primed",
            state,
            DoorStateRule("primed"),
            key
        );
        reactor.AddChild(area);
        reactor.AddChild(state);
        reactor.AddChild(interactive);
        interactive.AddAction(activate);
        interactive.AddAction(reactivate);

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(reactor);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);

        return new CoreWorld(
            world,
            runner,
            state,
            interactive,
            activate,
            reactivate,
            key,
            interactor,
            detector
        );
    }

    protected static InputGameplayAction CoreAction(
        string id,
        float duration,
        string runningState,
        string completedState,
        string cancelledState,
        StatefulComponent stateful,
        params GameplayActionRule[] rules
    )
    {
        InputGameplayAction action = NewAction(id, rules);
        TimedTransitionStateGameplayActionExecutor executor = new()
        {
            Name = $"{id}Executor",
            Stateful = stateful,
            RunningState = new StringName(runningState),
            CompletedState = new StringName(completedState),
            CancelledState = new StringName(cancelledState),
            Duration = duration,
        };
        action.AddChild(executor);
        action.Executor = executor;
        return action;
    }

    protected static string Describe(GameplayActionAvailability availability) =>
        availability switch
        {
            GameplayActionAllowed => "allowed",
            GameplayActionBlocked blocked => blocked.Reason,
            GameplayActionHidden => "hidden",
        };

    protected static TestWorld BuildWorld(int ownerPeerId = 1, bool inheritedAuthority = false)
    {
        Node3D world = new();
        TestInteractiveActor owner = new()
        {
            Name = "InteractiveActor",
            Position = new Vector3(0, 0, -2),
        };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        StatefulComponent stateful = new() { Name = "StatefulComponent", InitialState = IdleState };
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = owner,
        };
        InputGameplayAction action = CreateActivationAction(
            "activate",
            owner,
            ActorStateRule("This is already activated.", IdleState, ActivatingState),
            ActorStateRule("This is busy.", IdleState)
        );
        owner.AddChild(area);
        owner.AddChild(stateful);
        owner.AddChild(interactive);
        interactive.AddAction(action);
        interactive.ActionComponent!.GameplayActionCancelled += owner.OnGameplayActionCancelled;
        stateful.StateChangedAuthority += owner.OnStateChangedAuthority;
        stateful.StateChangedPresentation += owner.OnStateChangedPresentation;
        owner.Interactive = interactive;
        owner.Stateful = stateful;

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        if (inheritedAuthority)
        {
            interactor.SetMultiplayerAuthority(ownerPeerId);
        }
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view, ownerPeerId);
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        return new TestWorld(
            world,
            runner,
            owner,
            stateful,
            interactive,
            interactor,
            detector,
            action
        );
    }

    protected static TestInteractionDetector AttachDetector(
        InteractionInteractor interactor,
        Node3D viewOrigin,
        int ownerPeerId = 1
    )
    {
        TestInteractionDetector detector = new() { Name = "Detector", ViewOrigin = viewOrigin };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        interactor.ConfigureActionRunner(ownerPeerId);
        return detector;
    }

    protected static InteractiveComponent AddPresentationReceiver(
        Node parent,
        StringName actionId,
        GameplayActionExecutionVisibility visibility
    )
    {
        Node3D actor = new() { Name = "PresentationReceiver" };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D() });
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = actor,
        };
        GameplayAction action = CreateAction(actionId.ToString());
        action.ExecutionVisibility = visibility;
        actor.AddChild(area);
        actor.AddChild(interactive);
        interactive.AddAction(action);
        parent.AddChild(actor);
        return interactive;
    }

    protected static InputGameplayAction CreateActivationAction(
        string id,
        TestInteractiveActor owner,
        params GameplayActionRule[] rules
    )
    {
        InputGameplayAction action = NewAction(id, rules);
        // The activation is the sustained action of these worlds: the player stays engaged and
        // releasing the input ends it, which is exactly what the definition now declares.
        ((InputGameplayAction)action)
            .DefaultBindingConfig!
            .InputRequirement = GameplayActionInputRequirement.Pressed;
        TestActivationExecutor executor = new() { Name = $"{id}Executor", Actor = owner };
        action.AddChild(executor);
        action.Executor = executor;
        return action;
    }

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

    protected static InputGameplayAction CreateAction(string id, params GameplayActionRule[] rules)
    {
        InputGameplayAction action = NewAction(id, rules);
        RecordingInteractionExecutor executor = new() { Name = $"{id}Executor" };
        action.AddChild(executor);
        action.Executor = executor;
        return action;
    }

    protected static InteractionInteractor AddOtherInteractor(TestWorld testWorld)
    {
        Node3D view = new() { Name = "OtherViewOrigin" };
        InteractionInteractor other = new() { Name = "Other" };
        other.AddChild(view);
        AttachDetector(other, view);
        testWorld.World.AddChild(other);
        return other;
    }

    protected static InputGameplayAction NewAction(string id, GameplayActionRule[] rules)
    {
        InputGameplayAction action = new()
        {
            Name = $"{id}Action",
            ConfiguredAccessProviderId = InteractionOffer.InteractionAccessProviderId,
            Definition = new GameplayActionDefinition { Id = new StringName(id), Label = id },
            DefaultBindingConfig = new GameplayActionBindingConfig
            {
                InputActionName = new StringName("interact"),
                ActivationMode = GameplayActionActivationMode.Press,
            },
        };
        foreach (GameplayActionRule rule in rules)
        {
            action.Rules.Add(rule);
        }

        return action;
    }

    protected static Godot.Collections.Array<StringName> States(params string[] states)
    {
        Godot.Collections.Array<StringName> array = new();
        foreach (string state in states)
        {
            array.Add(new StringName(state));
        }

        return array;
    }

    protected static GameplayActionContext DoorContext(DoorWorld door) =>
        door.Interactive.ActionComponent!.CreateContext(
            1,
            door.Interactor,
            door.Interactor.Runner,
            door.Open,
            door.Interactive
        );

    protected static void BindSetStateExecutor(
        GameplayAction action,
        StatefulComponent stateful,
        string targetState
    )
    {
        SetStateGameplayActionExecutor executor = new()
        {
            Name = $"{action.Name}SetState",
            Stateful = stateful,
            TargetState = new StringName(targetState),
        };
        action.AddChild(executor);
        action.Executor = executor;
    }

    protected static StatefulStateInteractionRule ActorStateRule(
        string blockReason,
        params StringName[] expectedStates
    )
    {
        StatefulStateInteractionRule rule = new()
        {
            StatefulPath = new NodePath("../StatefulComponent"),
            MismatchAvailability = GameplayActionUnavailableKind.Blocked,
            BlockReason = blockReason,
        };
        foreach (StringName state in expectedStates)
        {
            rule.ExpectedStates.Add(state);
        }

        return rule;
    }

    protected static StatefulStateInteractionRule DoorStateRule(string expectedState) =>
        new()
        {
            StatefulPath = new NodePath("../StatefulComponent"),
            ExpectedStates = { new StringName(expectedState) },
        };

    protected static RecordingInteractionExecutor ExecutorOf(GameplayAction action) =>
        (RecordingInteractionExecutor)action.Executor!;

    protected static TestActivationExecutor ActivationExecutorOf(GameplayAction action) =>
        (TestActivationExecutor)action.Executor!;

    protected static DoorWorld BuildDoorWorld()
    {
        Node3D world = new();
        Node3D door = new() { Name = "Door", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        StatefulComponent state = new()
        {
            Name = "StatefulComponent",
            InitialState = new StringName("closed"),
        };
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = door,
            DisplayName = "Door",
        };
        GameplayAction open = CreateAction("open", DoorStateRule("closed"));
        GameplayAction close = CreateAction("close", DoorStateRule("open"));
        door.AddChild(area);
        door.AddChild(state);
        door.AddChild(interactive);
        interactive.AddAction(open);
        interactive.AddAction(close);

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(door);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);

        return new DoorWorld(world, runner, state, interactive, open, close, interactor, detector);
    }

    protected sealed record DoorWorld(
        Node3D World,
        ISceneRunner Runner,
        StatefulComponent State,
        InteractiveComponent Interactive,
        GameplayAction Open,
        GameplayAction Close,
        InteractionInteractor Interactor,
        TestInteractionDetector Detector
    )
    {
        /// <summary>Detects one target as interactible and runs the pipeline once, like a frame would.</summary>
        public void Detect(InteractiveComponent interactive)
        {
            Detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
            Interactor.RecalculateFocus();
        }
    }

    protected sealed record CoreWorld(
        Node3D World,
        ISceneRunner Runner,
        StatefulComponent State,
        InteractiveComponent Interactive,
        GameplayAction Activate,
        GameplayAction Reactivate,
        CarriesKeyInteractionRule Key,
        InteractionInteractor Interactor,
        TestInteractionDetector Detector
    );

    protected sealed partial class CarriesKeyInteractionRule : GameplayActionRule
    {
        public bool HasKey { get; set; }

        public override GameplayActionAvailability Evaluate(in GameplayActionContext context) =>
            HasKey
                ? new GameplayActionAllowed()
                : new GameplayActionBlocked("You need the resonator.");
    }

    protected sealed record TestWorld(
        Node3D World,
        ISceneRunner Runner,
        TestInteractiveActor Owner,
        StatefulComponent Stateful,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor,
        TestInteractionDetector Detector,
        InputGameplayAction Action
    )
    {
        /// <summary>Detects one target as interactible and runs the pipeline once, like a frame would.</summary>
        public void Detect(InteractiveComponent interactive)
        {
            Detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
            Interactor.RecalculateFocus();
        }

        /// <summary>Stops detecting one target and runs the pipeline once.</summary>
        public void Undetect(InteractiveComponent interactive)
        {
            Detector.ClearDetection(interactive);
            Interactor.RecalculateFocus();
        }
    }

    protected sealed partial class TestInteractiveActor : Node3D
    {
        public InteractiveComponent? Interactive { get; set; }

        public StatefulComponent? Stateful { get; set; }

        public bool GameplayBlocked { get; set; }

        public int StartCount { get; private set; }
        public int EndCount { get; private set; }
        public int AuthorityStateChanges { get; private set; }
        public int PresentationStateChanges { get; private set; }

        public GameplayActionExecutionResult BeginActivation()
        {
            StartCount++;
            if (Stateful is null)
            {
                return new GameplayActionExecutionCompleted();
            }

            Stateful.SetState(ActivatingState);
            return new GameplayActionExecutionRunning();
        }

        public void OnGameplayActionCancelled(
            long executionId,
            GameplayAction action,
            Node? instigator,
            Node? requester,
            string reason
        ) => EndCount++;

        public void OnStateChangedAuthority(
            StringName oldState,
            StringName newState,
            bool isSynchronization
        )
        {
            AuthorityStateChanges++;
        }

        public void OnStateChangedPresentation(
            StringName oldState,
            StringName newState,
            bool isSynchronization
        )
        {
            PresentationStateChanges++;
        }
    }

    protected sealed partial class TestActivationExecutor : GameplayActionExecutor
    {
        private readonly TimedExecution _timedExecution = new();

        public TestInteractiveActor? Actor { get; set; }

        public float? Duration { get; set; }

        public bool RequiresPresence { get; set; } = true;

        public override bool RequiresRequesterPresence => RequiresPresence;

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            if (Actor is null)
            {
                return new GameplayActionExecutionFailed("No actor.");
            }

            GameplayActionExecutionResult result = Actor.BeginActivation();
            if (result is not GameplayActionExecutionRunning || !Duration.HasValue)
            {
                return result;
            }

            return
                _timedExecution.Start(context.Component, context.ExecutionId, Duration.Value)
                == TimedExecutionStartResult.Started
                ? new GameplayActionExecutionRunning()
                : new GameplayActionExecutionFailed("The activation timer could not start.");
        }

        internal override GameplayActionProgressSample? GetPredictionSample(
            in GameplayActionContext context
        ) => Duration.HasValue ? TimedExecution.BuildPredictionSample(Duration.Value) : null;

        protected internal override void OnExecutionCompleted(in GameplayActionContext context) =>
            _timedExecution.Stop(context.ExecutionId);

        protected internal override void OnExecutionCancelled(
            in GameplayActionContext context,
            string reason
        ) => _timedExecution.Stop(context.ExecutionId);

        protected internal override void OnExecutionFailed(
            in GameplayActionContext context,
            string reason
        ) => _timedExecution.Stop(context.ExecutionId);
    }

    protected sealed partial class ComposedTimedExecutor : GameplayActionExecutor
    {
        public float Duration { get; set; }

        public TimedExecution Timer { get; } = new();

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            return
                Timer.Start(context.Component, context.ExecutionId, Duration)
                == TimedExecutionStartResult.Started
                ? new GameplayActionExecutionRunning()
                : new GameplayActionExecutionFailed("The timer could not start.");
        }
    }

    protected sealed partial class RecordingInteractionExecutor : GameplayActionExecutor
    {
        public GameplayActionExecutionResult Result { get; set; } =
            new GameplayActionExecutionCompleted();

        public int ExecuteCount { get; private set; }

        public InteractionInteractor? LastInteractor { get; private set; }

        public GameplayAction? LastAction { get; private set; }

        public InteractionInteractor? ReservedInteractorDuringExecute { get; private set; }

        public ulong LastExecutionId { get; private set; }

        public int CompletedCount { get; private set; }

        public int CancelledCount { get; private set; }

        public int FailedCount { get; private set; }

        public string LastCancelReason { get; private set; } = string.Empty;

        public string LastFailureReason { get; private set; } = string.Empty;

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            ExecuteCount++;
            LastInteractor = InteractionInteractor.ResolveForInstigator(context.Instigator);
            LastAction = context.Action;
            LastExecutionId = context.ExecutionId;
            ReservedInteractorDuringExecute = context
                .GetTarget<InteractiveComponent>()
                ?.ActiveInteractor;
            return Result;
        }

        protected internal override void OnExecutionCompleted(in GameplayActionContext context)
        {
            base.OnExecutionCompleted(context);
            CompletedCount++;
            LastExecutionId = context.ExecutionId;
        }

        protected internal override void OnExecutionCancelled(
            in GameplayActionContext context,
            string reason
        )
        {
            base.OnExecutionCancelled(context, reason);
            CancelledCount++;
            LastExecutionId = context.ExecutionId;
            LastCancelReason = reason;
        }

        protected internal override void OnExecutionFailed(
            in GameplayActionContext context,
            string reason
        )
        {
            base.OnExecutionFailed(context, reason);
            FailedCount++;
            LastExecutionId = context.ExecutionId;
            LastFailureReason = reason;
        }
    }

    protected sealed class FakeRepairSession
    {
        private readonly int _stepCount;
        private int _completedSteps;

        public FakeRepairSession(Node participantA, Node participantB, int stepCount)
        {
            Participants = new[] { participantA, participantB };
            _stepCount = stepCount;
        }

        public IReadOnlyList<Node> Participants { get; }

        public void CompleteStep() => _completedSteps++;

        public float GetProgress() => (float)_completedSteps / _stepCount;
    }

    protected sealed class WorldExecutionGauge
    {
        private readonly InteractiveComponent _interactive;
        private readonly StringName _actionId;

        public WorldExecutionGauge(InteractiveComponent interactive, StringName actionId)
        {
            _interactive = interactive;
            _actionId = actionId;
        }

        public float Read() =>
            _interactive.TryGetExecutionPresentation(
                _actionId,
                out GameplayActionExecutionPresentation presentation
            )
                ? presentation.Progress ?? 0.0f
                : 0.0f;
    }

    protected sealed partial class InteractiveParentGameplayRule : GameplayActionRule
    {
        public override GameplayActionAvailability Evaluate(in GameplayActionContext context)
        {
            return
                context.GetTarget<InteractiveComponent>()?.GetParent()
                    is TestInteractiveActor { GameplayBlocked: true }
                ? new GameplayActionBlocked("Gameplay condition is blocked.")
                : new GameplayActionAllowed();
        }
    }
}
