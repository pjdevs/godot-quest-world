namespace QuestWorld.Tests;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
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

        AssertThat(interactive.IsConfigurationValid).IsTrue();
        AssertThat(interactive.InteractionArea != null).IsTrue();
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
                null!,
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
}
