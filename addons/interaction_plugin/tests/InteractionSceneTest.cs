namespace QuestWorld.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using InteractionPlugin.Editor;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Integration.Stateful;
using QuestWorld.GameplayActions.Presentation.UI;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionSceneTest
{
    private const string LongActionScenePath =
        "res://addons/interaction_plugin/integration/stateful/examples/LongActionExample.tscn";
    private const string PromptScenePath =
        "res://addons/interaction_plugin/scenes/InteractionPrompt.tscn";
    private const string ActionPromptScenePath =
        "res://addons/gameplay_action_plugin/scenes/GameplayActionPrompt.tscn";
    private const string IndicatorScenePath =
        "res://addons/interaction_plugin/scenes/InteractionIndicator.tscn";
    private const string ButtonScenePath = "res://quest_world/interactibles/button/Button.tscn";
    private const string DoorScenePath = "res://quest_world/interactibles/door/Door.tscn";
    private const string LeverWallScenePath =
        "res://quest_world/interactibles/lever_wall/LeverWall.tscn";

    private static readonly StringName LoweredState = new("lowered");
    private static readonly StringName RaisingState = new("raising");
    private static readonly StringName RaisedState = new("raised");
    private static readonly StringName LoweringState = new("lowering");

    [TestCase]
    public async Task LongActionSceneProvidesComposableRuntimePartsWithoutAnyOwnerScript()
    {
        PackedScene scene = GD.Load<PackedScene>(LongActionScenePath);
        Node3D actor = scene.Instantiate<Node3D>();
        ISceneRunner runner = ISceneRunner.Load(actor, autoFree: true);
        await runner.SimulateFrames(1);

        InteractiveComponent interactive = actor.GetNode<InteractiveComponent>("Interactive");
        StatefulComponent stateful = actor.GetNode<StatefulComponent>("StatefulComponent");

        AssertThat(interactive.InteractionArea != null).IsTrue();
        AssertThat(interactive.InteractionAnchor != null).IsTrue();
        AssertThat(stateful.State.ToString()).IsEqual("idle");
        AssertThat(stateful.Schema != null).IsTrue();
        AssertThat(stateful.Schema!.Contains(new StringName("activating"))).IsTrue();
        AssertThat(stateful.Schema!.Contains(new StringName("activated"))).IsTrue();
        AssertThat(interactive.Actions.Count()).IsEqual(1);
        InteractionAction action = interactive.ActionAt(0);
        AssertThat(action == actor.GetNode<InteractionAction>("GameplayActions/ActivateAction"))
            .IsTrue();
        AssertThat(action.Definition?.Id.ToString()).IsEqual("activate");
        AssertThat(action.DefaultBindingConfig?.InputActionName.ToString()).IsEqual("interact");
        AssertThat(action.Executor != null).IsTrue();
        AssertThat(
                action.Executor
                    == actor.GetNode<GameplayActionExecutor>(
                        "GameplayActions/ActivateAction/ActivateExecutor"
                    )
            )
            .IsTrue();
        AssertThat(action.Rules.Count).IsEqual(3);
        AssertThat(action.Rules[0] is InteractionTargetRulesAdapter).IsTrue();
        AssertThat(interactive.TargetRules.Count).IsEqual(1);
        AssertThat(interactive.ActionPromptScene != null).IsTrue();
        MultiplayerSynchronizer synchronizer = actor.GetNode<MultiplayerSynchronizer>(
            "StatefulComponent/MultiplayerSynchronizer"
        );
        AssertThat(synchronizer != null).IsTrue();
        AssertThat(synchronizer!.ReplicationConfig != null).IsTrue();
        AssertThat(actor.GetScript().AsGodotObject() == null).IsTrue();
        AssertThat(action.Executor is TimedTransitionStateGameplayActionExecutor).IsTrue();
        AssertThat(action.ExecutionVisibility)
            .IsEqual(GameplayActionExecutionVisibility.Replicated);
        GameplayActionExecutionSynchronizer executionSynchronizer =
            actor.GetNode<GameplayActionExecutionSynchronizer>(
                "GameplayActionExecutionSynchronizer"
            );
        AssertThat(executionSynchronizer.Component == interactive.ActionComponent).IsTrue();
        AssertThat(executionSynchronizer.RootPath).IsEqual(new NodePath("."));
        AssertThat(executionSynchronizer.ReplicationConfig != null).IsTrue();
    }

    [TestCase]
    public async Task DoorSynchronizationConvergesPresentationWithoutReplayingUnlockAudio()
    {
        Node3D door = GD.Load<PackedScene>(DoorScenePath).Instantiate<Node3D>();
        door.Set("IsLocked", true);
        ISceneRunner runner = ISceneRunner.Load(door, autoFree: true);
        await runner.SimulateFrames(1);
        StatefulComponent stateful = door.GetNode<StatefulComponent>(
            "Interaction/StatefulComponent"
        );
        AudioStreamPlayer3D audio = door.GetNode<AudioStreamPlayer3D>("AudioPlayer");
        Node3D pivot = door.GetNode<Node3D>("Visual/Pivot");

        stateful.Set("ReplicatedState", new StringName("closed"));

        AssertThat(audio.Playing).IsFalse();
        AssertThat(pivot.Rotation).IsEqual(Vector3.Zero);
    }

    [TestCase]
    public async Task GameplayTemplatesUseValidCurrentInteractionAuthoring()
    {
        Node3D root = new();
        Node3D door = GD.Load<PackedScene>(DoorScenePath).Instantiate<Node3D>();
        Node3D button = GD.Load<PackedScene>(ButtonScenePath).Instantiate<Node3D>();
        Node3D longAction = GD.Load<PackedScene>(LongActionScenePath).Instantiate<Node3D>();
        root.AddChild(door);
        root.AddChild(button);
        root.AddChild(longAction);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);

        InteractiveComponent[] templates =
        {
            door.GetNode<InteractiveComponent>("Interaction/InteractiveComponent"),
            button.GetNode<InteractiveComponent>("InteractiveComponent"),
            longAction.GetNode<InteractiveComponent>("Interactive"),
        };
        foreach (InteractiveComponent interactive in templates)
        {
            AssertThat(InteractionValidator.Validate(interactive).ToArray()).IsEmpty();
        }
    }

    [TestCase]
    public async Task DefaultActionPromptWidgetShowsInputWhenAllowedAndReasonWhenBlocked()
    {
        GameplayActionPromptWidget widget = GD.Load<PackedScene>(ActionPromptScenePath)
            .Instantiate<GameplayActionPromptWidget>();
        ISceneRunner runner = ISceneRunner.Load(widget, autoFree: true);
        await runner.SimulateFrames(1);

        widget.Bind(
            new GameplayActionPresentation(
                "open",
                "Open",
                "Open it",
                "interact",
                new GameplayActionAllowed(),
                GameplayActionActivationMode.Press
            ),
            null
        );

        AssertThat(widget.ActionKeyLabel!.Text).IsNotEqual("???");
        AssertThat(widget.ActionNameLabel!.Text).IsEqual("Open");

        widget.Bind(
            new GameplayActionPresentation(
                "open",
                "Open",
                "Open it",
                "interact",
                new GameplayActionBlocked("Locked"),
                GameplayActionActivationMode.Press
            ),
            null
        );

        AssertThat(widget.ActionNameLabel.Text).IsEqual("Open: Locked");

        widget.Bind(
            new GameplayActionPresentation(
                "open",
                "Open",
                "Open it",
                "interact",
                new GameplayActionBlocked("Locked"),
                GameplayActionActivationMode.Press
            ),
            new GameplayActionExecutionPresentation(
                42,
                "open",
                Relation: GameplayActionExecutionRelation.RequestedLocally
            )
        );

        AssertThat(widget.ActionNameLabel.Text).IsEqual("Open");
    }

    [TestCase]
    public async Task DefaultActionPromptKeepsAnIdleHoldBarVisibleAtZero()
    {
        GameplayActionPromptWidget widget = new();
        Label actionName = new();
        Label actionKey = new();
        ProgressBar progress = new();
        widget.AddChild(actionName);
        widget.AddChild(actionKey);
        widget.AddChild(progress);
        widget.ActionNameLabel = actionName;
        widget.ActionKeyLabel = actionKey;
        widget.ActionProgress = progress;
        ISceneRunner runner = ISceneRunner.Load(widget, autoFree: true);
        await runner.SimulateFrames(1);

        widget.Bind(
            new GameplayActionPresentation(
                "unlock",
                "Unlock",
                "Unlock it",
                "interact",
                new GameplayActionAllowed(),
                GameplayActionActivationMode.Hold
            ),
            null
        );

        AssertThat(progress.Visible).IsTrue();
        AssertThat(progress.Value).IsEqual(0.0);

        widget.Bind(
            new GameplayActionPresentation(
                "open",
                "Open",
                "Open it",
                "interact",
                new GameplayActionAllowed(),
                GameplayActionActivationMode.Press
            ),
            null
        );

        AssertThat(progress.Visible).IsFalse();
    }

    [TestCase]
    public async Task DefaultPromptContainerShowsTheTargetNameAndExposesItsActionSlot()
    {
        InteractionPromptWidget widget = GD.Load<PackedScene>(PromptScenePath)
            .Instantiate<InteractionPromptWidget>();
        ISceneRunner runner = ISceneRunner.Load(widget, autoFree: true);
        await runner.SimulateFrames(1);

        InteractiveComponent interactive = AutoFree(new InteractiveComponent());
        widget.Bind(
            new InteractionTargetPresentation(
                interactive,
                "Door",
                "A heavy door",
                new List<GameplayActionPresentation>(),
                true
            )
        );

        AssertThat(widget.GetNode<Label>("Content/Label").Text).IsEqual("Door");
        AssertThat(widget.ActionsContainer == widget.GetNode<Control>("Content/Actions")).IsTrue();
    }

    [TestCase]
    public async Task FocusedTargetStacksOneActionPromptPerPresentedAction()
    {
        Node3D world = new();
        Node3D character = new() { Name = "Character" };
        Camera3D camera = new() { Name = "Camera" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        TestInteractionDetector detector = AttachDetector(interactor, camera);
        InteractionPresenter presenter = new()
        {
            Name = "Presenter",
            Interactor = interactor,
            Camera = camera,
            PromptContainerScene = GD.Load<PackedScene>(PromptScenePath),
        };
        character.AddChild(interactor);
        character.AddChild(camera);
        character.AddChild(presenter);
        world.AddChild(character);
        TestInteractiveActor owner = CreateInteractiveActor(
            "Console",
            new Vector3(0, 0, -2),
            "open",
            "close"
        );
        world.AddChild(owner);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);
        InteractiveComponent interactive = owner.GetNode<InteractiveComponent>("Interactive");

        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        await runner.SimulateFrames(1);

        InteractionPromptWidget container = presenter
            .GetChildren()
            .OfType<InteractionPromptWidget>()
            .Single();
        AssertThat(container.GetNode<Label>("Content/Label").Text).IsEqual("Console");
        GameplayActionPromptWidget[] actions = container
            .ActionsContainer.GetChildren()
            .OfType<GameplayActionPromptWidget>()
            .ToArray();
        AssertThat(actions.Length).IsEqual(2);
        AssertThat(actions[0].ActionNameLabel!.Text).IsEqual("open");
        AssertThat(actions[1].ActionNameLabel!.Text).IsEqual("close");
    }

    [TestCase]
    public async Task RebindingThePromptEveryFrameNeverRecreatesItsActionWidgets()
    {
        Node3D world = new();
        Node3D character = new() { Name = "Character" };
        Camera3D camera = new() { Name = "Camera" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        TestInteractionDetector detector = AttachDetector(interactor, camera);
        InteractionPresenter presenter = new()
        {
            Name = "Presenter",
            Interactor = interactor,
            Camera = camera,
            PromptContainerScene = GD.Load<PackedScene>(PromptScenePath),
        };
        character.AddChild(interactor);
        character.AddChild(camera);
        character.AddChild(presenter);
        world.AddChild(character);
        TestInteractiveActor owner = CreateInteractiveActor(
            "Terminal",
            new Vector3(0, 0, -2),
            "open",
            "force"
        );
        world.AddChild(owner);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);
        InteractiveComponent interactive = owner.GetNode<InteractiveComponent>("Interactive");
        interactive.ActionAt(1).DefaultBindingConfig!.ActivationMode =
            GameplayActionActivationMode.Hold;
        interactive.ActionAt(1).DefaultBindingConfig!.HoldDuration = 3600.0f;

        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        await runner.SimulateFrames(1);
        AssertThat(interactor.TryStartInteractionInput(new StringName("interact"))).IsTrue();
        InteractionPromptWidget container = presenter
            .GetChildren()
            .OfType<InteractionPromptWidget>()
            .Single();
        string before = WidgetIdentities(container);

        await runner.SimulateFrames(3);

        // The prompt is rebound from the frame loop so a hold can fill it, which makes the rebind path
        // a hot one: it must only call Bind. Rebuilding the list would restart the animation of every
        // widget on every frame, and the bar would never move.
        AssertThat(WidgetIdentities(container)).IsEqual(before);
        AssertThat(
                presenter.GetChildren().OfType<InteractionPromptWidget>().Single().GetInstanceId()
            )
            .IsEqual(container.GetInstanceId());
        GameplayActionPresentation heldAction = interactor
            .GetInteractionPresentation()!
            .Value.Actions.Single(action => action.IsHoldable);
        AssertThat(heldAction.HoldProgress.HasValue).IsTrue();
        AssertThat(heldAction.HoldProgress!.Value > 0.0f).IsTrue();
    }

    [TestCase]
    public async Task PresenterKeepsAllIndicationsExceptFocusedInteractive()
    {
        Node3D world = new();
        Node3D character = new() { Name = "Character" };
        Camera3D camera = new() { Name = "Camera" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        TestInteractionDetector detector = AttachDetector(interactor, camera);
        InteractionPresenter presenter = new()
        {
            Name = "Presenter",
            Interactor = interactor,
            Camera = camera,
            PromptContainerScene = GD.Load<PackedScene>(PromptScenePath),
        };
        character.AddChild(interactor);
        character.AddChild(camera);
        character.AddChild(presenter);
        world.AddChild(character);
        TestInteractiveActor firstOwner = CreateInteractiveActor("First", new Vector3(0, 0, -2));
        TestInteractiveActor secondOwner = CreateInteractiveActor("Second", new Vector3(1, 0, -3));
        world.AddChild(firstOwner);
        world.AddChild(secondOwner);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);
        InteractiveComponent first = firstOwner.GetNode<InteractiveComponent>("Interactive");
        InteractiveComponent second = secondOwner.GetNode<InteractiveComponent>("Interactive");

        detector.SetDetection(first, InteractionDetectionKind.Indicated);
        detector.SetDetection(second, InteractionDetectionKind.Indicated);
        await runner.SimulateFrames(1);
        AssertThat(presenter.GetChildren().OfType<InteractionIndicatorWidget>().Count()).IsEqual(2);

        detector.SetDetection(first, InteractionDetectionKind.Interactible);
        await runner.SimulateFrames(1);
        AssertThat(interactor.FocusedInteractive == first).IsTrue();
        AssertThat(presenter.GetChildren().OfType<InteractionPromptWidget>().Count()).IsEqual(1);
        AssertThat(presenter.GetChildren().OfType<InteractionIndicatorWidget>().Count()).IsEqual(1);

        detector.SetDetection(first, InteractionDetectionKind.Indicated);
        await runner.SimulateFrames(1);
        AssertThat(presenter.GetChildren().OfType<InteractionPromptWidget>().Count()).IsEqual(0);
        AssertThat(presenter.GetChildren().OfType<InteractionIndicatorWidget>().Count()).IsEqual(2);
    }

    [TestCase]
    public async Task PresenterDoesNotRenderForRemoteOwner()
    {
        Node3D world = new();
        Node3D character = new() { Name = "RemoteCharacter" };
        Camera3D camera = new() { Name = "Camera" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        TestInteractionDetector detector = AttachDetector(interactor, camera);
        interactor.Runner!.OwnerPeerId = 2;
        InteractionPresenter presenter = new()
        {
            Name = "Presenter",
            Interactor = interactor,
            Camera = camera,
        };
        character.AddChild(interactor);
        character.AddChild(camera);
        character.AddChild(presenter);
        world.AddChild(character);
        TestInteractiveActor owner = CreateInteractiveActor("RemoteTarget", new Vector3(0, 0, -2));
        world.AddChild(owner);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);
        InteractiveComponent interactive = owner.GetNode<InteractiveComponent>("Interactive");

        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        await runner.SimulateFrames(1);

        AssertThat(presenter.GetChildCount()).IsEqual(0);
    }

    [TestCase]
    public async Task LongActionSceneGatesItsActionWithGenericStateRules()
    {
        LongActionWorld world = BuildLongActionWorld();
        await world.Runner.SimulateFrames(1);

        AssertThat(Describe(world.Interactive.EvaluateAvailability(world.Interactor, world.Action)))
            .IsEqual("allowed");

        world.State.SetState(new StringName("activating"));

        AssertThat(Describe(world.Interactive.EvaluateAvailability(world.Interactor, world.Action)))
            .IsEqual("This is busy.");

        world.State.SetState(new StringName("activated"));

        AssertThat(Describe(world.Interactive.EvaluateAvailability(world.Interactor, world.Action)))
            .IsEqual("This is already activated.");
    }

    [TestCase]
    public async Task LongActionKeepsTheExecutionReservedThenCompletesItself()
    {
        LongActionWorld world = BuildLongActionWorld();
        await world.Runner.SimulateFrames(1);
        world.Executor.Duration = 0.05f;
        int completedCount = 0;
        int cancelledCount = 0;
        world.Interactive.InteractionActionCompleted += (_, _) => completedCount++;
        world.Interactive.InteractionActionCancelled += (_, _, _) => cancelledCount++;

        GameplayActionExecutionResult result = world.Interactive.ExecuteAction(
            world.Interactor,
            world.Action
        );

        AssertThat(result is GameplayActionExecutionRunning).IsTrue();
        AssertThat(world.State.State.ToString()).IsEqual("activating");
        AssertThat(world.Interactive.ActiveInteractor == world.Interactor).IsTrue();

        for (int frame = 0; frame < 300 && world.State.State.ToString() != "activated"; frame++)
        {
            await world.Runner.SimulateFrames(1);
        }

        AssertThat(world.State.State.ToString()).IsEqual("activated");
        AssertThat(world.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(completedCount).IsEqual(1);
        AssertThat(cancelledCount).IsEqual(0);
    }

    [TestCase]
    public async Task LongActionRestoresItsStateWhenTheExecutionIsCancelled()
    {
        LongActionWorld world = BuildLongActionWorld();
        await world.Runner.SimulateFrames(1);
        world.Executor.Duration = 3600.0f;
        string cancelledReason = string.Empty;
        world.Interactive.InteractionActionCancelled += (_, _, reason) => cancelledReason = reason;
        world.Interactive.ExecuteAction(world.Interactor, world.Action, out ulong executionId);

        AssertThat(world.State.State.ToString()).IsEqual("activating");

        AssertThat(world.Interactive.CancelExecution(executionId, "Interrupted.")).IsTrue();

        AssertThat(world.State.State.ToString()).IsEqual("idle");
        AssertThat(world.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(cancelledReason).IsEqual("Interrupted.");
    }

    private static LongActionWorld BuildLongActionWorld()
    {
        Node3D world = new();
        Node3D actor = GD.Load<PackedScene>(LongActionScenePath).Instantiate<Node3D>();
        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        AttachDetector(interactor, view);
        interactor.AddToGroup("Player");
        world.AddChild(actor);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        InteractiveComponent interactive = actor.GetNode<InteractiveComponent>("Interactive");

        return new LongActionWorld(
            runner,
            interactive,
            interactor,
            actor.GetNode<StatefulComponent>("StatefulComponent"),
            interactive.ActionAt(0),
            (TimedTransitionStateGameplayActionExecutor)interactive.ActionAt(0).Executor!
        );
    }

    private sealed record LongActionWorld(
        ISceneRunner Runner,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor,
        StatefulComponent State,
        InteractionAction Action,
        TimedTransitionStateGameplayActionExecutor Executor
    );

    [TestCase]
    public async Task PromptFollowsARuleThatFlipsWithoutAnyStatusNotification()
    {
        Node3D world = new();
        Node3D character = new() { Name = "Character" };
        Camera3D camera = new() { Name = "Camera" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        TestInteractionDetector detector = AttachDetector(interactor, camera);
        InteractionPresenter presenter = new()
        {
            Name = "Presenter",
            Interactor = interactor,
            Camera = camera,
            PromptContainerScene = GD.Load<PackedScene>(PromptScenePath),
        };
        character.AddChild(interactor);
        character.AddChild(camera);
        character.AddChild(presenter);
        world.AddChild(character);
        TestInteractiveActor owner = CreateInteractiveActor(
            "Villager",
            new Vector3(0, 0, -2),
            "talk"
        );
        InteractiveComponent interactive = owner.GetNode<InteractiveComponent>("Interactive");
        interactive.ActionAt(0).Rules.Add(new DialogInteractionRule());
        world.AddChild(owner);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);
        int statusNotifications = 0;
        interactive.InteractiveStatusChanged += () => statusNotifications++;
        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        await runner.SimulateFrames(1);

        AssertThat(PromptLabels(presenter)).IsEqual("talk");

        // No state component, no signal, no explicit invalidation: only the gameplay condition moved.
        owner.DialogRunning = true;
        await runner.SimulateFrames(1);

        AssertThat(PromptLabels(presenter)).IsEqual("talk: Someone is talking.");

        owner.DialogRunning = false;
        await runner.SimulateFrames(1);

        AssertThat(PromptLabels(presenter)).IsEqual("talk");
        AssertThat(statusNotifications).IsEqual(0);
    }

    private static string Describe(GameplayActionAvailability availability) =>
        availability switch
        {
            GameplayActionAllowed => "allowed",
            GameplayActionBlocked blocked => blocked.Reason,
            GameplayActionHidden => "hidden",
        };

    private static string WidgetIdentities(InteractionPromptWidget container) =>
        string.Join(
            ",",
            container.ActionsContainer.GetChildren().Select(child => child.GetInstanceId())
        );

    private static string PromptLabels(InteractionPresenter presenter)
    {
        InteractionPromptWidget container = presenter
            .GetChildren()
            .OfType<InteractionPromptWidget>()
            .Single();
        return string.Join(
            " | ",
            container
                .ActionsContainer.GetChildren()
                .OfType<GameplayActionPromptWidget>()
                .Select(widget => widget.ActionNameLabel!.Text)
        );
    }

    [TestCase]
    public async Task WallControlButtonOffersTheOneActionItsDistantStateAllows()
    {
        WallControlWorld world = BuildWallControlWorld();
        await world.Runner.SimulateFrames(1);

        AssertThat(world.WallState.State.ToString()).IsEqual("lowered");
        AssertThat(Presented(world)).IsEqual("[interact] Raise wall");

        GameplayActionExecutionResult raise = world.Interactive.ExecuteAction(
            world.Interactor,
            world.Interactive.ResolveAction(new StringName("raise"))!
        );

        AssertThat(raise is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(world.WallState.State).IsEqual(RaisingState);
        AssertThat(Presented(world)).IsEqual("Raise wall: The wall is moving.");

        world.WallState.SetState(RaisedState);

        AssertThat(Presented(world)).IsEqual("[interact] Lower wall");

        GameplayActionExecutionResult lower = world.Interactive.ExecuteAction(
            world.Interactor,
            world.Interactive.ResolveAction(new StringName("lower"))!
        );

        AssertThat(lower is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(world.WallState.State).IsEqual(LoweringState);
        AssertThat(Presented(world)).IsEqual("Lower wall: The wall is moving.");
    }

    [TestCase]
    public async Task LeverWallOwnsItsTransitionAndReachesTheEndStateOnItsOwn()
    {
        WallControlWorld world = BuildWallControlWorld();
        await world.Runner.SimulateFrames(1);

        world.Interactive.ExecuteAction(
            world.Interactor,
            world.Interactive.ResolveAction(new StringName("raise"))!
        );

        AssertThat(world.WallState.State).IsEqual(RaisingState);

        for (int frame = 0; frame < 300 && world.WallState.State != RaisedState; frame++)
        {
            await world.Runner.SimulateFrames(1);
        }

        AssertThat(world.WallState.State).IsEqual(RaisedState);
        AssertThat(Presented(world)).IsEqual("[interact] Lower wall");
    }

    private static string Presented(WallControlWorld world)
    {
        InteractionTargetPresentation presentation = world.Interactive.GetPresentation(
            world.Interactor,
            true
        );
        return string.Join(
            " | ",
            presentation.Actions.Select(action =>
                action.IsAllowed
                    ? $"[{action.InputActionName}] {action.Label}"
                    : $"{action.Label}: {action.BlockReason}"
            )
        );
    }

    private static WallControlWorld BuildWallControlWorld()
    {
        Node3D world = new();
        Node3D level = new() { Name = "Level" };
        Node3D wall = GD.Load<PackedScene>(LeverWallScenePath).Instantiate<Node3D>();
        Node3D button = GD.Load<PackedScene>(ButtonScenePath).Instantiate<Node3D>();
        button.Position = new Vector3(0, 0, -2);
        level.AddChild(wall);
        level.AddChild(button);
        world.AddChild(level);

        StatefulComponent wallState = wall.GetNode<StatefulComponent>("StatefulComponent");
        InteractiveComponent interactive = button.GetNode<InteractiveComponent>(
            "InteractiveComponent"
        );
        WireWallControlAction(interactive.ActionAt(0), wallState, LoweredState, RaisingState);
        WireWallControlAction(interactive.ActionAt(1), wallState, RaisedState, LoweringState);

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);

        return new WallControlWorld(runner, interactive, interactor, wallState);
    }

    private static void WireWallControlAction(
        InteractionAction action,
        StatefulComponent wallState,
        StringName readyState,
        StringName movingState
    )
    {
        ((SetStateGameplayActionExecutor)action.Executor!).Stateful = wallState;
        NodePath statefulPath = new("../../../LeverWall/StatefulComponent");
        action.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = statefulPath,
                ExpectedStates = { readyState, movingState },
            }
        );
        action.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = statefulPath,
                ExpectedStates = { readyState },
                MismatchAvailability = GameplayActionUnavailableKind.Blocked,
                BlockReason = "The wall is moving.",
            }
        );
    }

    private sealed record WallControlWorld(
        ISceneRunner Runner,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor,
        StatefulComponent WallState
    );

    private static TestInteractionDetector AttachDetector(
        InteractionInteractor interactor,
        Node3D viewOrigin
    )
    {
        TestInteractionDetector detector = new() { Name = "Detector", ViewOrigin = viewOrigin };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        interactor.ConfigureActionRunner();
        return detector;
    }

    private static TestInteractiveActor CreateInteractiveActor(
        string displayName,
        Vector3 position,
        params string[] actionIds
    )
    {
        TestInteractiveActor owner = new() { Name = displayName, Position = position };
        Area3D area = new() { Name = "InteractionArea" };
        owner.AddChild(area);
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = owner,
            DisplayName = displayName,
            ActionPromptScene = GD.Load<PackedScene>(ActionPromptScenePath),
            IndicationScene = GD.Load<PackedScene>(IndicatorScenePath),
        };
        owner.AddChild(interactive);
        foreach (string actionId in actionIds.Length == 0 ? new[] { "activate" } : actionIds)
        {
            InteractionAction action = new()
            {
                Name = $"{actionId}Action",
                Definition = new GameplayActionDefinition
                {
                    Id = new StringName(actionId),
                    Label = actionId,
                },
                DefaultBindingConfig = new GameplayActionBindingConfig
                {
                    InputActionName = new StringName("interact"),
                    ActivationMode = GameplayActionActivationMode.Press,
                },
            };
            NoopInteractionExecutor executor = new() { Name = $"{actionId}Executor" };
            action.AddChild(executor);
            action.Executor = executor;
            interactive.AddAction(action);
        }

        return owner;
    }

    private sealed partial class TestInteractiveActor : Node3D
    {
        public bool DialogRunning { get; set; }
    }

    private sealed partial class DialogInteractionRule : InteractionRule
    {
        public override GameplayActionAvailability Evaluate(in InteractionContext context) =>
            context.Interactive.GetParent() is TestInteractiveActor { DialogRunning: true }
                ? new GameplayActionBlocked("Someone is talking.")
                : new GameplayActionAllowed();
    }

    private sealed partial class NoopInteractionExecutor : InteractionActionExecutor
    {
        public override GameplayActionExecutionResult Execute(
            in InteractionExecutionContext context
        ) => new GameplayActionExecutionCompleted();
    }
}
