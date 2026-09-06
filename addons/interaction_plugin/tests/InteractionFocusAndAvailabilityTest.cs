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
using QuestWorld.Interaction.Examples.Rules;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;
using QuestWorld.Tests.GameplayActions;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class InteractionFocusAndAvailabilityTest : InteractionTestBase
{
    [TestCase]
    public async Task FocusCoreMutatesSelectionWithoutDispatch()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detector.SetDetection(
            testWorld.Interactive,
            InteractionDetectionKind.Interactible
        );
        testWorld.Interactor.Runner!.OwnerPeerId = 1;
        int focusSignalCount = 0;
        int statusSignalCount = 0;
        testWorld.Interactor.FocusedInteractiveChanged += _ => focusSignalCount++;
        testWorld.Interactor.InteractionStatusChanged += _ => statusSignalCount++;

        FocusChangeResult? result = testWorld.Interactor.RecalculateFocusCore();

        AssertThat(result.HasValue).IsTrue();
        AssertThat(result?.Previous == null).IsTrue();
        AssertThat(result?.Current == testWorld.Interactive).IsTrue();
        AssertThat(result?.Changed).IsTrue();
        AssertThat(testWorld.Interactor.FocusedInteractive == testWorld.Interactive).IsTrue();
        AssertThat(focusSignalCount).IsEqual(0);
        AssertThat(statusSignalCount).IsEqual(0);
    }

    [TestCase]
    public async Task FocusDispatchEmitsFocusAndStatusExactlyOnce()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detector.SetDetection(
            testWorld.Interactive,
            InteractionDetectionKind.Interactible
        );
        testWorld.Interactor.Runner!.OwnerPeerId = 1;
        int focusSignalCount = 0;
        int statusSignalCount = 0;
        testWorld.Interactor.FocusedInteractiveChanged += _ => focusSignalCount++;
        testWorld.Interactor.InteractionStatusChanged += _ => statusSignalCount++;
        FocusChangeResult? result = testWorld.Interactor.RecalculateFocusCore();

        testWorld.Interactor.DispatchFocusChange(result!.Value);

        AssertThat(focusSignalCount).IsEqual(1);
        AssertThat(statusSignalCount).IsEqual(1);
    }

    [TestCase]
    public async Task UnchangedFocusDispatchNotifiesNothingAtAll()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detector.SetDetection(
            testWorld.Interactive,
            InteractionDetectionKind.Interactible
        );
        testWorld.Interactor.Runner!.OwnerPeerId = 1;
        FocusChangeResult? initialResult = testWorld.Interactor.RecalculateFocusCore();
        testWorld.Interactor.DispatchFocusChange(initialResult!.Value);
        int focusSignalCount = 0;
        int statusSignalCount = 0;
        testWorld.Interactor.FocusedInteractiveChanged += _ => focusSignalCount++;
        testWorld.Interactor.InteractionStatusChanged += _ => statusSignalCount++;

        FocusChangeResult? unchangedResult = testWorld.Interactor.RecalculateFocusCore();
        testWorld.Interactor.DispatchFocusChange(unchangedResult!.Value);

        // A status pushed on every focused frame notified nothing new, and cost every subscriber one
        // snapshot per presented target per frame. The presentation is pulled: a consumer that needs
        // continuous freshness reads it each frame, as the presenter does since the frame rebind.
        AssertThat(unchangedResult?.Changed).IsFalse();
        AssertThat(unchangedResult?.Previous == testWorld.Interactive).IsTrue();
        AssertThat(unchangedResult?.Current == testWorld.Interactive).IsTrue();
        AssertThat(focusSignalCount).IsEqual(0);
        AssertThat(statusSignalCount).IsEqual(0);
    }

    [TestCase]
    public async Task AStableFocusNotifiesOnceAndNotEveryFrame()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        int statusSignalCount = 0;
        testWorld.Interactor.InteractionStatusChanged += _ => statusSignalCount++;

        await testWorld.Runner.SimulateFrames(5);

        // The focus never moved, so there is nothing to announce. A consumer that needs to know
        // whether a rule started refusing pulls the snapshot, which is what the presenter does every
        // frame — pushing it here would have notified five times to say the same thing.
        AssertThat(testWorld.Interactor.FocusedInteractive == testWorld.Interactive).IsTrue();
        AssertThat(statusSignalCount).IsEqual(0);
    }

    [TestCase]
    public void AvailabilityUsesExhaustiveAllowedBlockedAndHiddenCases()
    {
        GameplayActionAvailability allowed = new GameplayActionAllowed();
        GameplayActionAvailability blocked = new GameplayActionBlocked("Needs a key");
        GameplayActionAvailability hidden = new GameplayActionHidden();

        AssertThat(Describe(allowed)).IsEqual("allowed");
        AssertThat(Describe(blocked)).IsEqual("Needs a key");
        AssertThat(Describe(hidden)).IsEqual("hidden");
    }

    [TestCase]
    public async Task TargetRulesStopAtFirstBlock()
    {
        TestInteractiveActor owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionAnchor = owner,
            TargetRules = new Godot.Collections.Array<InteractionRule>
            {
                new AlwaysBlockedInteractionRule { Reason = "First reason" },
                new AlwaysBlockedInteractionRule { Reason = "Second reason" },
            },
        };
        InteractionAction action = CreateAction("activate");
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddAction(action);
        InteractionInteractor interactor = new();
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.AddChild(view);
        AttachDetector(interactor, view);
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        GameplayActionAvailability availability = interactive.EvaluateAvailability(
            interactor,
            action
        );

        AssertThat(
                availability is GameplayActionBlocked blocked && blocked.Reason == "First reason"
            )
            .IsTrue();
    }

    [TestCase]
    public async Task TargetRulesRunBeforeActionRules()
    {
        TestInteractiveActor owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionAnchor = owner,
            TargetRules = new Godot.Collections.Array<InteractionRule>
            {
                new AlwaysBlockedInteractionRule { Reason = "Target reason" },
            },
        };
        InteractionAction action = CreateAction(
            "activate",
            new AlwaysBlockedInteractionRule { Reason = "Action reason" }
        );
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddAction(action);
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        GameplayActionAvailability availability = interactive.EvaluateAvailability(
            interactor,
            action
        );

        AssertThat(Describe(availability)).IsEqual("Target reason");
    }

    [TestCase]
    public async Task CustomRuleCanEvaluateInteractiveParentGameplayState()
    {
        TestInteractiveActor owner = new() { GameplayBlocked = true };
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionAnchor = owner,
        };
        InteractionAction action = CreateAction("activate", new InteractiveParentGameplayRule());
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddAction(action);
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        GameplayActionAvailability blockedAvailability = interactive.EvaluateAvailability(
            interactor,
            action
        );
        owner.GameplayBlocked = false;
        GameplayActionAvailability allowedAvailability = interactive.EvaluateAvailability(
            interactor,
            action
        );

        AssertThat(Describe(blockedAvailability)).IsEqual("Gameplay condition is blocked.");
        AssertThat(allowedAvailability is GameplayActionAllowed).IsTrue();
    }

    [TestCase]
    public async Task FocusUsesViewAlignmentAndDistanceAndEmitsStatus()
    {
        TestWorld testWorld = BuildWorld();
        bool focusChanged = false;
        testWorld.Interactor.FocusedInteractiveChanged += _ => focusChanged = true;

        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.FocusedInteractive == testWorld.Interactive).IsTrue();
        AssertThat(focusChanged).IsTrue();
        InteractionTargetPresentation? presentation =
            testWorld.Interactor.GetInteractionPresentation();
        AssertThat(presentation?.Actions.Count).IsEqual(1);
        AssertThat(presentation?.Actions[0].IsAllowed).IsTrue();
    }

    [TestCase]
    public async Task DoorActionsExposeOppositeAvailabilityPerWorldState()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("allowed");
        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Close)))
            .IsEqual("hidden");

        door.State.SetState(new StringName("open"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("hidden");
        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Close)))
            .IsEqual("allowed");
    }

    [TestCase]
    public async Task TargetAvailabilityPrefersAllowedThenBlockedThenHidden()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.State.SetState(new StringName("locked"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor)))
            .IsEqual("hidden");

        door.Open.Rules.Insert(
            0,
            new AlwaysBlockedInteractionRule { Reason = "Requires a keycard." }
        );

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor)))
            .IsEqual("Requires a keycard.");

        door.State.SetState(new StringName("open"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor)))
            .IsEqual("allowed");
    }

    [TestCase]
    public async Task AvailabilityEvaluationStaysPureAndRepeatable()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        int statusSignalCount = 0;
        RecordingInteractionExecutor executor = ExecutorOf(door.Open);
        door.Interactive.InteractiveStatusChanged += () => statusSignalCount++;

        GameplayActionAvailability first = door.Interactive.EvaluateAvailability(
            door.Interactor,
            door.Open
        );
        GameplayActionAvailability second = door.Interactive.EvaluateAvailability(
            door.Interactor,
            door.Open
        );

        AssertThat(Describe(first)).IsEqual("allowed");
        AssertThat(Describe(second)).IsEqual("allowed");
        AssertThat(door.State.State.ToString()).IsEqual("closed");
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(statusSignalCount).IsEqual(0);
        AssertThat(executor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task ActionWithoutDefinitionOrFromAnotherTargetIsNotConfigured()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        InteractionAction undefined = new() { Name = "UndefinedAction" };
        door.Interactive.ActionComponent!.Actions.Add(undefined);
        InteractionAction foreign = CreateAction("foreign");

        try
        {
            AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, undefined)))
                .IsEqual("Interaction is not configured.");
            AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, foreign)))
                .IsEqual("Interaction is not configured.");
            AssertThat(door.Interactive.GetPresentation(door.Interactor, true).Actions.Count)
                .IsEqual(1);
        }
        finally
        {
            undefined.Free();
            foreign.Free();
        }
    }

    [TestCase]
    public async Task TargetWithoutActionOffersNoInteraction()
    {
        TestInteractiveActor owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionAnchor = owner,
        };
        owner.AddChild(area);
        owner.AddChild(interactive);
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        InteractionAction foreign = CreateAction("foreign");
        try
        {
            AssertThat(interactive.EvaluateAvailability(interactor) is GameplayActionHidden)
                .IsTrue();
            AssertThat(interactive.ResolveAction(new StringName("foreign")) == null).IsTrue();
            AssertThat(
                    interactive.ExecuteAction(interactor, foreign)
                        is GameplayActionExecutionRejected
                )
                .IsTrue();
            AssertThat(ExecutorOf(foreign).ExecuteCount).IsEqual(0);
        }
        finally
        {
            foreign.Free();
        }
    }

    [TestCase]
    public async Task PresentationExposesOneEntryPerVisibleActionAndOmitsHiddenOnes()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);

        InteractionTargetPresentation closed = door.Interactive.GetPresentation(
            door.Interactor,
            true
        );

        AssertThat(closed.DisplayName).IsEqual("Door");
        AssertThat(closed.IsFocused).IsTrue();
        AssertThat(closed.Actions.Count).IsEqual(1);
        AssertThat(closed.Actions[0].ActionId.ToString()).IsEqual("open");
        AssertThat(closed.Actions[0].InputActionName.ToString()).IsEqual("interact");
        AssertThat(closed.Actions[0].IsAllowed).IsTrue();
        AssertThat(closed.HasAllowedAction).IsTrue();

        door.State.SetState(new StringName("open"));
        InteractionTargetPresentation opened = door.Interactive.GetPresentation(
            door.Interactor,
            true
        );

        AssertThat(opened.Actions.Count).IsEqual(1);
        AssertThat(opened.Actions[0].ActionId.ToString()).IsEqual("close");
        AssertThat(opened.Actions[0].IsAllowed).IsTrue();
    }

    [TestCase]
    public async Task BlockedActionStaysPresentedWithItsOwnReason()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Open.Rules.Insert(
            0,
            new AlwaysBlockedInteractionRule { Reason = "Requires a keycard." }
        );

        InteractionTargetPresentation presentation = door.Interactive.GetPresentation(
            door.Interactor,
            true
        );

        AssertThat(presentation.Actions.Count).IsEqual(1);
        AssertThat(presentation.Actions[0].ActionId.ToString()).IsEqual("open");
        AssertThat(presentation.Actions[0].IsAllowed).IsFalse();
        AssertThat(presentation.Actions[0].BlockReason).IsEqual("Requires a keycard.");
        AssertThat(presentation.HasAllowedAction).IsFalse();
    }

    [TestCase]
    public async Task TargetWithEveryActionHiddenIsIgnoredByFocus()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        AssertThat(door.Interactor.FocusedInteractive == door.Interactive).IsTrue();

        door.State.SetState(new StringName("locked"));
        door.Interactor.RecalculateFocus();

        AssertThat(door.Interactive.HasVisibleAction(door.Interactor)).IsFalse();
        AssertThat(door.Interactor.FocusedInteractive == null).IsTrue();
        AssertThat(door.Interactor.GetInteractionPresentation() == null).IsTrue();
    }

    [TestCase]
    public async Task FocusMovesToTheNextTargetWhenTheClosestHidesEveryAction()
    {
        DoorWorld door = BuildDoorWorld();
        Node3D crate = new() { Name = "Crate", Position = new Vector3(0, 0, -4) };
        Area3D crateArea = new() { Name = "InteractionArea" };
        InteractiveComponent crateInteractive = new()
        {
            Name = "Interactive",
            InteractionArea = crateArea,
            InteractionAnchor = crate,
            DisplayName = "Crate",
        };
        InteractionAction inspect = CreateAction("inspect");
        crate.AddChild(crateArea);
        crate.AddChild(crateInteractive);
        crateInteractive.AddAction(inspect);
        door.World.AddChild(crate);
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        door.Detect(crateInteractive);
        AssertThat(door.Interactor.FocusedInteractive == door.Interactive).IsTrue();

        door.State.SetState(new StringName("locked"));
        door.Interactor.RecalculateFocus();

        AssertThat(door.Interactor.FocusedInteractive == crateInteractive).IsTrue();
    }

    [TestCase]
    public async Task PresentationMeasuresTheDistanceFromTheBodyAndNotFromTheView()
    {
        TestWorld testWorld = BuildWorld();
        Node3D body = new() { Name = "Body" };
        testWorld.World.AddChild(body);
        testWorld.Detector.InteractionOrigin = body;
        testWorld.Detector.ViewOrigin!.Position = new Vector3(0, 0, 1);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        InteractionTargetPresentation presentation = testWorld.Interactive.GetPresentation(
            testWorld.Interactor,
            true
        );

        // The target sits two units from the body and three from the camera behind it. The presented
        // distance is the one the range window applies, so a widget animating on it agrees with the
        // moment the interaction becomes possible.
        AssertThat(Mathf.IsEqualApprox(presentation.Distance, 2.0f)).IsTrue();
    }

    [TestCase]
    public async Task IdlePresentationDescribesActionAndHoldData()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Action.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        testWorld.Action.DefaultBindingConfig!.HoldDuration = 2.0f;
        InteractionAction inspect = CreateAction("inspect");
        testWorld.Interactive.AddAction(inspect);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        InteractionTargetPresentation presentation = testWorld.Interactive.GetPresentation(
            testWorld.Interactor,
            true
        );
        GameplayActionPresentation activation = PresentedAction(presentation, "activate");
        GameplayActionPresentation inspection = PresentedAction(presentation, "inspect");

        AssertThat(activation.IsHoldable).IsTrue();
        AssertThat(activation.HoldProgress.HasValue).IsFalse();
        AssertThat(inspection.IsHoldable).IsFalse();
    }

    [TestCase]
    public async Task ExecutionPresentationIsCarriedByItsOwnActionAlone()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        testWorld.Interactive.AddAction(CreateAction("inspect"));
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        await testWorld.Runner.SimulateFrames(2);
        AssertThat(testWorld.Interactive.GetExecutionPresentations().Count).IsEqual(1);
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    new StringName("activate"),
                    out GameplayActionExecutionPresentation activation
                )
            )
            .IsTrue();
        AssertThat(activation.Progress.HasValue).IsTrue();
        AssertThat(activation.Progress!.Value > 0.0f).IsTrue();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(new StringName("inspect"), out _)
            )
            .IsFalse();
    }
}
