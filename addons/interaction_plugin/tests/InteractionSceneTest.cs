namespace QuestWorld.Tests;

using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Presentation.UI;
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
        AssertThat(interactive.InteractionOwner == actor).IsTrue();
        AssertThat(stateful.State).IsEqual(InteractionState.Idle);
        MultiplayerSynchronizer synchronizer = actor.GetNode<MultiplayerSynchronizer>(
            "Stateful/MultiplayerSynchronizer"
        );
        AssertThat(synchronizer != null).IsTrue();
        AssertThat(synchronizer!.ReplicationConfig != null).IsTrue();
    }

    [TestCase]
    public async Task DefaultPromptWidgetAcceptsAllowedAndBlockedPresentation()
    {
        InteractionPromptWidget widget = new();
        ISceneRunner runner = ISceneRunner.Load(widget);
        await runner.SimulateFrames(1);

        widget.Bind(
            new InteractionPresentation(
                new InteractiveComponent(),
                "Door",
                "Open it",
                "use",
                new InteractionBlocked("Locked"),
                true
            )
        );

        Label label = widget.GetNode<Label>("Label");
        AssertThat(label.Text).IsEqual("Door: Locked");
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
        };
        character.AddChild(interactor);
        character.AddChild(camera);
        character.AddChild(presenter);
        world.AddChild(character);
        TestInteractionOwner firstOwner = CreateInteractiveOwner("First", new Vector3(0, 0, -2));
        TestInteractionOwner secondOwner = CreateInteractiveOwner("Second", new Vector3(1, 0, -3));
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
        TestInteractionOwner owner = CreateInteractiveOwner("RemoteTarget", new Vector3(0, 0, -2));
        world.AddChild(owner);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);
        InteractiveComponent interactive = owner.GetNode<InteractiveComponent>("Interactive");

        interactor.AddInteractiveIndication(interactive);
        interactor.AddInteractive(interactive);
        await runner.SimulateFrames(1);

        AssertThat(presenter.GetChildCount()).IsEqual(0);
    }

    private static TestInteractionOwner CreateInteractiveOwner(string displayName, Vector3 position)
    {
        TestInteractionOwner owner = new() { Name = displayName, Position = position };
        Area3D area = new() { Name = "InteractionArea" };
        owner.AddChild(area);
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionOwner = owner,
            DisplayName = displayName,
            PromptScene = GD.Load<PackedScene>(
                "res://addons/interaction_plugin/scenes/InteractionPrompt.tscn"
            ),
            IndicationScene = GD.Load<PackedScene>(
                "res://addons/interaction_plugin/scenes/InteractionIndicator.tscn"
            ),
        };
        owner.AddChild(interactive);
        return owner;
    }

    private sealed partial class TestInteractionOwner : Node3D { }
}
