namespace QuestWorld.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionSceneTest
{
    private const string ActorScenePath =
        "res://addons/interaction_plugin/scenes/InteractiveActor.tscn";
    private const string PromptScenePath =
        "res://addons/interaction_plugin/scenes/InteractionPrompt.tscn";
    private const string ActionPromptScenePath =
        "res://addons/interaction_plugin/scenes/InteractionActionPrompt.tscn";
    private const string IndicatorScenePath =
        "res://addons/interaction_plugin/scenes/InteractionIndicator.tscn";

    [TestCase]
    public async Task InteractiveActorSceneProvidesComposableRuntimeParts()
    {
        PackedScene scene = GD.Load<PackedScene>(ActorScenePath);
        Node3D actor = scene.Instantiate<Node3D>();
        ISceneRunner runner = ISceneRunner.Load(actor);
        await runner.SimulateFrames(1);

        InteractiveComponent interactive = actor.GetNode<InteractiveComponent>("Interactive");
        InteractionStateful stateful = actor.GetNode<InteractionStateful>("Stateful");

        AssertThat(interactive.InteractionArea != null).IsTrue();
        AssertThat(interactive.InteractionAnchor != null).IsTrue();
        AssertThat(stateful.State).IsEqual(InteractionState.Idle);
        AssertThat(interactive.Actions.Count).IsEqual(1);
        InteractionAction action = interactive.Actions[0];
        AssertThat(action == actor.GetNode<InteractionAction>("Interactive/ActivateAction"))
            .IsTrue();
        AssertThat(action.Definition?.Id.ToString()).IsEqual("activate");
        AssertThat(action.Definition?.InputActionName.ToString()).IsEqual("interact");
        AssertThat(action.Executor != null).IsTrue();
        AssertThat(
                action.Executor
                    == actor.GetNode<InteractionActionExecutor>(
                        "Interactive/ActivateAction/ActivateExecutor"
                    )
            )
            .IsTrue();
        AssertThat(action.Rules.Count).IsEqual(1);
        AssertThat(interactive.TargetRules.Count).IsEqual(1);
        AssertThat(interactive.ActionPromptScene != null).IsTrue();
        MultiplayerSynchronizer synchronizer = actor.GetNode<MultiplayerSynchronizer>(
            "Stateful/MultiplayerSynchronizer"
        );
        AssertThat(synchronizer != null).IsTrue();
        AssertThat(synchronizer!.ReplicationConfig != null).IsTrue();
    }

    [TestCase]
    public async Task DefaultActionPromptWidgetShowsInputWhenAllowedAndReasonWhenBlocked()
    {
        InteractionActionPromptWidget widget = new();
        ISceneRunner runner = ISceneRunner.Load(widget);
        await runner.SimulateFrames(1);
        Label label = widget.GetNode<Label>("Label");

        widget.Bind(
            new InteractionActionPresentation(
                "open",
                "Open",
                "Open it",
                "use",
                new InteractionAllowed()
            )
        );

        AssertThat(label.Text).IsEqual("[use] Open");

        widget.Bind(
            new InteractionActionPresentation(
                "open",
                "Open",
                "Open it",
                "use",
                new InteractionBlocked("Locked")
            )
        );

        AssertThat(label.Text).IsEqual("Open: Locked");
    }

    [TestCase]
    public async Task DefaultPromptContainerShowsTheTargetNameAndExposesItsActionSlot()
    {
        InteractionPromptWidget widget = GD.Load<PackedScene>(PromptScenePath)
            .Instantiate<InteractionPromptWidget>();
        ISceneRunner runner = ISceneRunner.Load(widget);
        await runner.SimulateFrames(1);

        widget.Bind(
            new InteractionTargetPresentation(
                new InteractiveComponent(),
                "Door",
                "A heavy door",
                new List<InteractionActionPresentation>(),
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
        InteractionInteractor interactor = new() { Name = "Interactor", ViewOrigin = camera };
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
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);
        InteractiveComponent interactive = owner.GetNode<InteractiveComponent>("Interactive");

        interactor.AddInteractive(interactive);
        await runner.SimulateFrames(1);

        InteractionPromptWidget container = presenter
            .GetChildren()
            .OfType<InteractionPromptWidget>()
            .Single();
        AssertThat(container.GetNode<Label>("Content/Label").Text).IsEqual("Console");
        InteractionActionPromptWidget[] actions = container
            .ActionsContainer.GetChildren()
            .OfType<InteractionActionPromptWidget>()
            .ToArray();
        AssertThat(actions.Length).IsEqual(2);
        AssertThat(actions[0].GetNode<Label>("Label").Text).IsEqual("[interact] open");
        AssertThat(actions[1].GetNode<Label>("Label").Text).IsEqual("[interact] close");
    }

    [TestCase]
    public async Task PresenterKeepsAllIndicationsExceptFocusedInteractive()
    {
        Node3D world = new();
        Node3D character = new() { Name = "Character" };
        Camera3D camera = new() { Name = "Camera" };
        InteractionInteractor interactor = new() { Name = "Interactor", ViewOrigin = camera };
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
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);
        InteractiveComponent first = firstOwner.GetNode<InteractiveComponent>("Interactive");
        InteractiveComponent second = secondOwner.GetNode<InteractiveComponent>("Interactive");

        interactor.AddInteractiveIndication(first);
        interactor.AddInteractiveIndication(second);
        await runner.SimulateFrames(1);
        AssertThat(presenter.GetChildren().OfType<InteractionIndicatorWidget>().Count()).IsEqual(2);

        interactor.AddInteractive(first);
        await runner.SimulateFrames(1);
        AssertThat(interactor.FocusedInteractive == first).IsTrue();
        AssertThat(presenter.GetChildren().OfType<InteractionPromptWidget>().Count()).IsEqual(1);
        AssertThat(presenter.GetChildren().OfType<InteractionIndicatorWidget>().Count()).IsEqual(1);

        interactor.RemoveInteractive(first);
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
        InteractionInteractor interactor = new()
        {
            Name = "Interactor",
            OwnerPeerId = 2,
            ViewOrigin = camera,
        };
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
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);
        InteractiveComponent interactive = owner.GetNode<InteractiveComponent>("Interactive");

        interactor.AddInteractiveIndication(interactive);
        interactor.AddInteractive(interactive);
        await runner.SimulateFrames(1);

        AssertThat(presenter.GetChildCount()).IsEqual(0);
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
                Definition = new InteractionActionDefinition
                {
                    Id = new StringName(actionId),
                    Label = actionId,
                },
            };
            interactive.Actions.Add(action);
            interactive.AddChild(action);
            NoopInteractionExecutor executor = new() { Name = $"{actionId}Executor" };
            action.AddChild(executor);
            action.Executor = executor;
        }

        return owner;
    }

    private sealed partial class TestInteractiveActor : Node3D { }

    private sealed partial class NoopInteractionExecutor : InteractionActionExecutor
    {
        public override InteractionExecutionResult Execute(
            in InteractionExecutionContext context
        ) => new InteractionExecutionCompleted();
    }
}
