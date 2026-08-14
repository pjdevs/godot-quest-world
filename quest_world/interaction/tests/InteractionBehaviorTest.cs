namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionBehaviorTest
{
    [TestCase]
    public void StatusUsesExhaustiveAllowedAndBlockedCases()
    {
        InteractionStatus allowed = InteractionAllowed.Instance;
        InteractionStatus blocked = new InteractionBlocked("Needs a key");

        AssertThat(Describe(allowed)).IsEqual("allowed");
        AssertThat(Describe(blocked)).IsEqual("Needs a key");
    }

    [TestCase]
    public async Task RulesStopAtFirstBlockBeforeCustomHandler()
    {
        TestInteractionOwner owner = new();
        InteractiveComponent interactive = new()
        {
            InteractionAreaPath = new NodePath("InteractionArea"),
            InteractionRules = new Godot.Collections.Array<InteractionRule>
            {
                new AlwaysBlockedInteractionRule { Reason = "First reason" },
                new AlwaysBlockedInteractionRule { Reason = "Second reason" }
            }
        };
        owner.AddChild(new Area3D { Name = "InteractionArea" });
        owner.AddChild(interactive);
        InteractionInteractor interactor = new();
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.ViewOriginPath = new NodePath("ViewOrigin");
        interactor.AddChild(view);
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        InteractionStatus status = interactive.EvaluateStatus(interactor);

        AssertThat(status is InteractionBlocked blocked && blocked.Reason == "First reason").IsTrue();
        AssertThat(owner.CustomStatusEvaluationCount).IsEqual(0);
    }

    [TestCase]
    public async Task FocusUsesViewAlignmentAndDistanceAndEmitsStatus()
    {
        TestWorld testWorld = BuildWorld();
        bool focusChanged = false;
        testWorld.Interactor.FocusedInteractiveChanged += _ => focusChanged = true;

        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.FocusedInteractive == testWorld.Interactive).IsTrue();
        AssertThat(focusChanged).IsTrue();
        AssertThat(testWorld.Interactor.GetInteractionPresentation().IsAllowed).IsTrue();
    }

    [TestCase]
    public async Task LongPhaseReservesObjectAndOnlyActiveInteractorCanEndIt()
    {
        TestWorld testWorld = BuildWorld();
        InteractionInteractor secondInteractor = new() { ViewOriginPath = new NodePath("ViewOrigin") };
        secondInteractor.AddChild(new Node3D { Name = "ViewOrigin" });
        testWorld.World.AddChild(secondInteractor);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        secondInteractor.AddInteractive(testWorld.Interactive);
        AssertThat(testWorld.Interactive.StartInteraction(testWorld.Interactor)).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activating);
        AssertThat(testWorld.Interactive.StartInteraction(secondInteractor)).IsFalse();

        testWorld.Interactive.EndInteraction(secondInteractor, InteractionState.Idle);
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activating);
        testWorld.Interactive.EndInteraction(testWorld.Interactor, InteractionState.Activated);

        AssertThat(testWorld.Stateful.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activated);
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task SnapshotRestoreUsesCommonStateApplicationAndRejectsUnknownVersion()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Stateful.SetState(InteractionState.Activated);
        InteractionSavedState saved = testWorld.Stateful.SaveState();
        testWorld.Stateful.SetState(InteractionState.Idle);

        testWorld.Stateful.LoadState(saved);

        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activated);
        AssertThat(testWorld.Owner.PresentationStateChanges).IsGreater(0);
        bool rejectedUnknownVersion = false;
        try
        {
            testWorld.Stateful.LoadState(new InteractionSavedState(999, InteractionState.Idle));
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectedUnknownVersion = true;
        }

        AssertThat(rejectedUnknownVersion).IsTrue();
    }

    [TestCase]
    public async Task OfflineInputUsesAuthoritativeStartAndEndPath()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput()).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activating);

        AssertThat(testWorld.Interactor.TryEndInteractionInput()).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
        AssertThat(testWorld.Stateful.ActiveInteractor == null).IsTrue();
    }

    private static string Describe(InteractionStatus status) => status switch
    {
        InteractionAllowed => "allowed",
        InteractionBlocked blocked => blocked.Reason,
    };

    private static TestWorld BuildWorld()
    {
        Node3D world = new();
        TestInteractionOwner owner = new() { Name = "InteractiveOwner", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        InteractionStateful stateful = new() { Name = "Stateful" };
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionAreaPath = new NodePath("../InteractionArea"),
            StatefulPath = new NodePath("../Stateful")
        };
        owner.AddChild(area);
        owner.AddChild(stateful);
        owner.AddChild(interactive);

        InteractionInteractor interactor = new() { Name = "Interactor", ViewOriginPath = new NodePath("ViewOrigin") };
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.AddChild(view);
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        return new TestWorld(world, runner, owner, stateful, interactive, interactor);
    }

    private sealed record TestWorld(
        Node3D World,
        ISceneRunner Runner,
        TestInteractionOwner Owner,
        InteractionStateful Stateful,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor);

    private sealed partial class TestInteractionOwner : Node3D, IInteractionHandler, IInteractionStateHandler
    {
        public int CustomStatusEvaluationCount { get; private set; }
        public int StartCount { get; private set; }
        public int EndCount { get; private set; }
        public int PresentationStateChanges { get; private set; }

        public InteractionStatus EvaluateCustomInteractionStatus(in InteractionContext context)
        {
            CustomStatusEvaluationCount++;
            return InteractionAllowed.Instance;
        }

        public void OnStartInteractionInput(in InteractionContext context)
        {
            StartCount++;
            context.Interactive.Stateful.StartInteractionPhase(context.Interactor);
        }

        public void OnEndInteractionInput(in InteractionContext context) => EndCount++;

        public void OnInteractionStateChangedAuthority(InteractionState oldState, InteractionState newState)
        {
        }

        public void OnInteractionStateChangedPresentation(InteractionState oldState, InteractionState newState)
        {
            PresentationStateChanges++;
        }
    }
}
