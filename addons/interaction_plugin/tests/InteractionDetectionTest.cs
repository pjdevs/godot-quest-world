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
    public async Task ATargetBehindTheInteractorIsIndicatedRatherThanUndetected()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, 2));
        await world.Runner.SimulateFrames(1);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        // Reaching the interaction volume is not looking at it, but the object is still there: losing
        // the window costs the focus, never the indication.
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

        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Indicated);
    }

    [TestCase]
    public async Task TurningAwayFromTheFocusedTargetDemotesItWithoutRetractingItsIndication()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        await world.Runner.SimulateFrames(1);
        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );
        await world.Runner.SimulateFrames(1);
        int removed = 0;
        world.Interactor.InteractiveIndicationRemoved += _ => removed++;
        AssertThat(world.Interactor.FocusedInteractive == world.Interactive).IsTrue();

        world.Detector.ViewOrigin!.RotateY(Mathf.Pi);
        await world.Runner.SimulateFrames(1);

        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Indicated);
        AssertThat(world.Interactor.FocusedInteractive == null).IsTrue();
        AssertThat(removed).IsEqual(0);
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
        // The overlap may land after the interactor already processed the frame it arrived in, so the
        // pipeline is run explicitly rather than betting on the physics sampling order.
        interactor.RecalculateFocus();
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

    [TestCase]
    public async Task AWallBetweenTheViewAndTheTargetRemovesItFromDetectionEntirely()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        AddOccluder(world.Target.GetParent(), new Vector3(0, 0, -1), 2);
        int added = 0;
        world.Interactor.InteractiveIndicationAdded += _ => added++;
        await world.Runner.SimulateFrames(4);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );
        await world.Runner.SimulateFrames(2);

        // Losing the window costs the focus; losing the line of sight costs the existence. An object
        // one turns away from is still there, an object behind a wall is not there to indicate.
        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.None);
        AssertThat(world.Interactor.FocusedInteractive == null).IsTrue();
        AssertThat(added).IsEqual(0);
    }

    [TestCase]
    public async Task AnUnknownTargetIsCastForOnTheSpotRatherThanAssumedVisible()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        AddOccluder(world.Target.GetParent(), new Vector3(0, 0, -1), 2);
        await world.Runner.SimulateFrames(4);

        // The authoritative peer validates a one-shot command outside any physics frame, and this is
        // the first time its detector hears about the target: no loop has run for it, and no refresh
        // is waited for. Answering "occluded until the next physics frame" would refuse a legitimate
        // command for a reason no player could see.
        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        AssertThat(world.Detector.Detect(world.Interactive)).IsEqual(InteractionDetectionKind.None);
    }

    [TestCase]
    public async Task LosingSightWaitsOutTheGraceWhileRegainingItIsImmediate()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        AddOccluder(world.Target.GetParent(), new Vector3(0, 0, -1), 2);
        world.Detector.LineOfSightLossGrace = 0.2f;
        await world.Runner.SimulateFrames(4);
        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        // Aiming the mask at an empty layer is what makes the wall stop counting, and aiming it back is
        // what a pole crossing the view does — without moving a body mid-frame.
        world.Detector.OcclusionMask = 4;
        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Interactible);

        world.Detector.OcclusionMask = 2;
        world.Detector._PhysicsProcess(0.1);
        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Interactible);

        world.Detector._PhysicsProcess(0.1);
        AssertThat(world.Detector.Detect(world.Interactive)).IsEqual(InteractionDetectionKind.None);

        world.Detector.OcclusionMask = 4;
        world.Detector._PhysicsProcess(0.1);
        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Interactible);
    }

    [TestCase]
    public async Task AGrateKeptOffTheOcclusionLayerLetsTheInteractionThrough()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        AddOccluder(world.Target.GetParent(), new Vector3(0, 0, -1), 4);
        await world.Runner.SimulateFrames(4);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        // Occluding is a property of the occluder: the grate stops bodies like any wall and simply does
        // not carry the occlusion layer, so no target has to declare an exemption for it.
        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Interactible);
    }

    [TestCase]
    public async Task ADetectorWithoutOcclusionLayerNeverRefusesOnSight()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        AddOccluder(world.Target.GetParent(), new Vector3(0, 0, -1), 2);
        world.Detector.OcclusionMask = 0;
        await world.Runner.SimulateFrames(4);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Interactible);
    }

    [TestCase]
    public async Task ATargetIsNeverOccludedByItsOwnGeometry()
    {
        DetectionWorld world = BuildWorld(new Vector3(0, 0, -2));
        AddOccluder(world.Target, Vector3.Zero, 2);
        await world.Runner.SimulateFrames(4);

        world.Detector.OnEnteredTargetArea(
            world.Interactive,
            InteractionDetectionKind.Interactible
        );

        // Stopping on the target itself is reaching it, which is what makes an anchor authored inside
        // the mesh that carries it usable at all.
        AssertThat(world.Detector.Detect(world.Interactive))
            .IsEqual(InteractionDetectionKind.Interactible);
    }

    // Smoke checks for the two spike detectors: they prove the model runs at all, nothing more, and
    // they are meant to be deleted along with their detector if the spike is dropped.

    [TestCase]
    public async Task ProximitySpikeDetectsWithoutAnyPhysics()
    {
        Node3D world = new();
        Node3D target = new() { Name = "Target", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = BuildInteractive(target, area);
        target.AddChild(area);
        target.AddChild(interactive);
        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        ProximityInteractionDetector detector = new() { Name = "Detector", ViewOrigin = view };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        world.AddChild(target);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        AssertThat(detector.Detect(interactive)).IsEqual(InteractionDetectionKind.Interactible);
        AssertThat(interactor.FocusedInteractive == interactive).IsTrue();

        // Indication is omnidirectional, and the target authors its own reach.
        target.Position = new Vector3(0, 0, 6);
        interactor.RecalculateFocus();
        AssertThat(detector.Detect(interactive)).IsEqual(InteractionDetectionKind.Indicated);

        interactive.InteractionRadius = 8.0f;
        AssertThat(detector.Detect(interactive)).IsEqual(InteractionDetectionKind.Indicated);
        target.Position = new Vector3(0, 0, -6);
        AssertThat(detector.Detect(interactive)).IsEqual(InteractionDetectionKind.Interactible);
    }

    [TestCase]
    public async Task AimSpikeDetectsThroughItsOwnCast()
    {
        Node3D world = new();
        Node3D target = new() { Name = "Target", Position = new Vector3(0, 0, -3) };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 1.0f } });
        InteractiveComponent interactive = BuildInteractive(target, area);
        target.AddChild(area);
        target.AddChild(interactive);
        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        AimInteractionDetector detector = new() { Name = "Detector", ViewOrigin = view };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        world.AddChild(target);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);

        await runner.SimulateFrames(4);
        interactor.RecalculateFocus();

        AssertThat(detector.GetCandidates().Contains(interactive)).IsTrue();
        AssertThat(interactor.FocusedInteractive == interactive).IsTrue();

        // The cast is the source, so looking away empties it instead of filtering it.
        view.RotateY(Mathf.Pi);
        await runner.SimulateFrames(4);
        interactor.RecalculateFocus();

        AssertThat(detector.GetCandidates().Any()).IsFalse();
        AssertThat(interactor.FocusedInteractive == null).IsTrue();
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

    private static StaticBody3D AddOccluder(Node parent, Vector3 position, uint layer)
    {
        StaticBody3D occluder = new()
        {
            Name = "Occluder",
            Position = position,
            CollisionLayer = layer,
        };
        occluder.AddChild(
            new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4, 4, 0.2f) } }
        );
        parent.AddChild(occluder);
        return occluder;
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
