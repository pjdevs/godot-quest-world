namespace QuestWorld.Tests;

using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionDetectionTest
{
    [TestCase]
    public async Task InteractionOverlapInsideTheWindowIsInteractibleAndTakesFocus()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        await world.Runner.SimulateFrames(1);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Interactible);
        await world.Runner.SimulateFrames(1);
        AssertThat(world.Interactor.FocusedInteractive == world.Interactive).IsTrue();
    }

    [TestCase]
    public async Task ATargetBehindTheInteractorIsNotIndicatedWithoutItsOwnIndicationArea()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, 2));
        await world.Runner.SimulateFrames(1);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        // Reaching the interaction volume is not looking at it, and an object that authored no
        // indication volume has nothing to say from behind the player.
        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.None);

        world.Detector.OnEnteredTargetArea(world.Interactive, InteractionDetectionKind.Indicated);

        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Indicated);
        await world.Runner.SimulateFrames(1);
        AssertThat(world.Interactor.FocusedInteractive == null).IsTrue();
    }

    [TestCase]
    public async Task ATargetBeyondTheMaximumDistanceFallsBackToItsIndicationTier()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        world.Detector.MaxDistance = 1.0f;
        await world.Runner.SimulateFrames(1);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );
        world.Detector.OnEnteredTargetArea(world.Interactive, InteractionDetectionKind.Indicated);

        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Indicated);
    }

    [TestCase]
    public async Task DetectionTiersAreCumulativeForIndicationPresentation()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        await world.Runner.SimulateFrames(1);
        int added = 0;
        int removed = 0;
        world.Interactor.InteractiveIndicationAdded += _ => added++;
        world.Interactor.InteractiveIndicationRemoved += _ => removed++;

        world.Detector.OnEnteredTargetArea(world.Interactive, InteractionDetectionKind.Indicated);
        await world.Runner.SimulateFrames(1);
        AssertThat(added).IsEqual(1);

        // Becoming usable must not retract the indication: the tiers stack, and hiding the widget of
        // the focused target is the presenter's decision.
        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );
        await world.Runner.SimulateFrames(1);
        AssertThat(world.Interactor.FocusedInteractive == world.Interactive).IsTrue();
        AssertThat(added).IsEqual(1);
        AssertThat(removed).IsEqual(0);

        world.Detector.OnExitedTargetArea(world.Interactive, InteractionDetectionKind.Interactible);
        world.Detector.OnExitedTargetArea(world.Interactive, InteractionDetectionKind.Indicated);
        await world.Runner.SimulateFrames(1);

        AssertThat(removed).IsEqual(1);
        AssertThat(world.Interactor.FocusedInteractive == null).IsTrue();
    }

    [TestCase]
    public async Task ATargetLeavingTheTreeIsForgottenByTheDetectorItself()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        await world.Runner.SimulateFrames(1);
        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );
        await world.Runner.SimulateFrames(1);
        AssertThat(world.Interactor.FocusedInteractive == world.Interactive).IsTrue();

        // An area cannot report the overlap it loses by being freed, so the target says so itself.
        world.Target.GetParent().RemoveChild(world.Target);

        AssertThat(world.Detector.GetCandidates().Any()).IsFalse();
        AssertThat(world.Interactor.FocusedInteractive == null).IsTrue();
        world.Target.QueueFree();
        await world.Runner.SimulateFrames(1);
    }

    [TestCase]
    public async Task TheTargetsOwnAreaFeedsTheDetectorOfEveryOverlappingInteractor()
    {
        Node3D world = new();
        Node3D target = new() { Name = "Target", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        InteractiveComponent interactive = BuildInteractive(target, area);
        target.AddChild(area);
        target.AddChild(interactive);

        CharacterBody3D character = new() { Name = "Character" };
        character.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.5f } });
        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        AreaInteractionDetector detector = new() { Name = "Detector", ViewOrigin = view };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        character.AddChild(interactor);
        world.AddChild(target);
        world.AddChild(character);
        ISceneRunner runner = ISceneRunner.Load(world);

        await runner.SimulateFrames(4);

        AssertThat(detector.Detect(interactive)).IsEqual(InteractionDetectionKind.Interactible);
        AssertThat(interactor.FocusedInteractive == interactive).IsTrue();
    }

    [TestCase]
    public void AnInteractorWithoutDetectorDetectsNothingInsteadOfGuessing()
    {
        InteractionInteractor interactor = new();

        AssertThat(interactor.RecalculateFocus()).IsFalse();
        AssertThat(interactor.FocusedInteractive == null).IsTrue();
        AssertThat(interactor.TryStartInteractionInput(new StringName("interact"))).IsFalse();
        interactor.QueueFree();
    }

    private static DetectionWorld BuildWorld(Vector3 targetPosition)
    {
        Node3D world = new();
        Node3D target = new() { Name = "Target", Position = targetPosition };
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = BuildInteractive(target, area);
        target.AddChild(area);
        target.AddChild(interactive);

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        AreaInteractionDetector detector = new() { Name = "Detector", ViewOrigin = view };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        world.AddChild(target);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        return new DetectionWorld(runner, target, interactive, interactor, detector);
    }

    private static InteractiveComponent BuildInteractive(Node3D target, Area3D area)
    {
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = target,
            DisplayName = "Target",
        };
        InteractionAction action = new()
        {
            Name = "activateAction",
            Definition = new InteractionActionDefinition
            {
                Id = new StringName("activate"),
                Label = "Activate",
                InputActionName = new StringName("interact"),
            },
        };
        NoopInteractionExecutor executor = new() { Name = "activateExecutor" };
        action.AddChild(executor);
        action.Executor = executor;
        interactive.Actions.Add(action);
        interactive.AddChild(action);
        return interactive;
    }

    private sealed record DetectionWorld(
        ISceneRunner Runner,
        Node3D Target,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor,
        AreaInteractionDetector Detector
    );

    private sealed partial class NoopInteractionExecutor : InteractionActionExecutor
    {
        public override InteractionExecutionResult Execute(
            in InteractionExecutionContext context
        ) => new InteractionExecutionCompleted();
    }
}
