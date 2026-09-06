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
public sealed partial class InteractionBehaviorTest
{
    private static readonly StringName InteractInput = new("interact");
    private static readonly StringName IdleState = new("idle");
    private static readonly StringName ActivatingState = new("activating");
    private static readonly StringName ActivatedState = new("activated");

    [TestCase]
    public async Task RunningExecutionIsProjectedAsAnAuthorityPresentationAndRemovedOnCompletion()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        int invalidations = 0;
        testWorld.Interactive.ExecutionPresentationChanged += _ => invalidations++;
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );

        AssertThat(testWorld.Interactive.GetExecutionPresentations().Count).IsEqual(1);
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    new StringName("activate"),
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.ExecutionId).IsEqual(executionId);
        AssertThat(presentation.ActionId).IsEqual(new StringName("activate"));
        AssertThat(presentation.Progress.HasValue).IsTrue();
        AssertThat(invalidations).IsEqual(1);

        AssertThat(testWorld.Interactive.CompleteExecution(executionId)).IsTrue();

        AssertThat(testWorld.Interactive.GetExecutionPresentations().Count).IsEqual(0);
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(new StringName("activate"), out _)
            )
            .IsFalse();
        AssertThat(invalidations).IsEqual(2);
    }

    [TestCase]
    public async Task IndefiniteRunningExecutionIsProjectedWithUnknownProgress()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );

        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    new StringName("activate"),
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.ExecutionId).IsEqual(executionId);
        AssertThat(presentation.Progress.HasValue).IsFalse();
    }

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
    public void OfflineInteractorKeepsLocalControlWithoutMultiplayerPeer()
    {
        InteractionInteractor interactor = new();

        try
        {
            AssertThat(interactor.IsLocallyControlled).IsTrue();
        }
        finally
        {
            interactor.Free();
        }
    }

    [TestCase]
    public void AnOfflineTargetStillExecutesItsActionWithoutAMultiplayerPeer()
    {
        // Outside any tree, Multiplayer is null, which is the peerless game: asking the API for an id
        // it does not have would push an error and answer that nobody is the server, so every
        // authoritative path would refuse itself.
        TestInteractiveActor owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionAnchor = owner,
        };
        InteractionAction action = CreateAction("activate");
        GameplayActionComponent actionComponent = new();
        interactive.ActionComponent = actionComponent;
        action.PrepareForInteractive(interactive, interactive.TargetRules);
        actionComponent.AddAction(action);
        InteractionInteractor interactor = new();

        try
        {
            GameplayActionExecutionResult result = interactive.ExecuteAction(interactor, action);

            AssertThat(result is GameplayActionExecutionCompleted).IsTrue();
        }
        finally
        {
            interactive.Free();
            actionComponent.Free();
            area.Free();
            owner.Free();
            interactor.Free();
        }
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
    public async Task RunningExecutionReservesObjectAndRejectsACompetitorRelease()
    {
        TestWorld testWorld = BuildWorld();
        Node3D secondView = new() { Name = "ViewOrigin" };
        InteractionInteractor secondInteractor = new();
        secondInteractor.AddChild(secondView);
        TestInteractionDetector secondDetector = AttachDetector(secondInteractor, secondView);
        testWorld.World.AddChild(secondInteractor);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Detect(testWorld.Interactive);
        secondDetector.SetDetection(testWorld.Interactive, InteractionDetectionKind.Interactible);
        secondInteractor.RecalculateFocus();
        AssertThat(
                testWorld.Interactive.ExecuteAction(
                    testWorld.Interactor,
                    testWorld.Action,
                    out ulong executionId
                ) is GameplayActionExecutionRunning
            )
            .IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(
                testWorld.Interactive.ExecuteAction(secondInteractor, testWorld.Action)
                    is GameplayActionExecutionRejected
            )
            .IsTrue();

        AssertThat(testWorld.Interactive.CancelExecution(executionId + 1)).IsFalse();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        testWorld.Stateful.SetState(ActivatedState);
        AssertThat(testWorld.Interactive.CompleteExecution(executionId)).IsTrue();

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatedState);
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task ObserversOfTheNotificationsNeverRunTheExecutorASecondTime()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        RecordingInteractionExecutor executor = ExecutorOf(door.Open);
        int startedCount = 0;
        int completedCount = 0;
        for (int observer = 0; observer < 3; observer++)
        {
            door.Interactive.InteractionActionStarted += (_, _) => startedCount++;
            door.Interactive.InteractionActionCompleted += (_, _) => completedCount++;
            door.Interactive.InteractionActionCancelled += (_, _, _) => { };
            door.Interactive.InteractionActionRejected += (_, _, _) => { };
        }

        GameplayActionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(result is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(executor.ExecuteCount).IsEqual(1);
        AssertThat(startedCount).IsEqual(3);
        AssertThat(completedCount).IsEqual(3);
        AssertThat(executor.LastInteractor == door.Interactor).IsTrue();
        AssertThat(executor.LastAction == door.Open).IsTrue();
    }

    [TestCase]
    public async Task TheTargetIsAlreadyReservedWhenTheExecutorRuns()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        RecordingInteractionExecutor executor = ExecutorOf(door.Open);

        door.Interactive.ExecuteAction(door.Interactor, door.Open);

        AssertThat(executor.ReservedInteractorDuringExecute == door.Interactor).IsTrue();
    }

    [TestCase]
    public async Task StatefulRuleResolvesItsPathRelativeToTheOwningAction()
    {
        DoorWorld door = BuildDoorWorld();
        StatefulStateInteractionRule rule = (StatefulStateInteractionRule)door.Open.Rules[1];
        rule.StatefulPath = new NodePath("../../StatefulComponent");
        await door.Runner.SimulateFrames(1);

        GameplayActionAvailability availability = door.Interactive.EvaluateAvailability(
            door.Interactor,
            door.Open
        );

        AssertThat(availability is GameplayActionAllowed).IsTrue();
    }

    [TestCase]
    public async Task ARunningExecutionRefusesTheSameInteractorWithItsOwnReason()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);
        InteractionAction inspect = CreateAction("inspect");
        testWorld.Interactive.AddAction(inspect);
        string rejectedReason = string.Empty;
        testWorld.Interactive.InteractionActionRejected += (_, _, reason) =>
            rejectedReason = reason;

        GameplayActionExecutionResult result = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            inspect
        );

        AssertThat(result is GameplayActionExecutionRejected).IsTrue();
        AssertThat(rejectedReason).IsEqual("This is already in use.");
        AssertThat(ExecutorOf(inspect).ExecuteCount).IsEqual(0);
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
    }

    [TestCase]
    public async Task CompletedExecutionHoldsNoReservation()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);

        door.Interactive.ExecuteAction(door.Interactor, door.Open, out ulong executionId);

        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(door.Interactive.IsExecutionActive(executionId)).IsFalse();
        AssertThat(door.Interactive.CompleteExecution(executionId)).IsFalse();
    }

    [TestCase]
    public async Task RejectedExecutionIsNeverAnnouncedAsStarted()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        ExecutorOf(door.Open).Result = new GameplayActionExecutionRejected("The hinges are stuck.");
        List<string> notifications = new();
        string rejectedReason = string.Empty;
        door.Interactive.InteractionActionStarted += (_, _) => notifications.Add("started");
        door.Interactive.InteractionActionCompleted += (_, _) => notifications.Add("completed");
        door.Interactive.InteractionActionCancelled += (_, _, _) => notifications.Add("cancelled");
        door.Interactive.InteractionActionRejected += (_, _, reason) =>
        {
            notifications.Add("rejected");
            rejectedReason = reason;
        };

        GameplayActionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(result is GameplayActionExecutionRejected).IsTrue();
        AssertThat(string.Join(",", notifications)).IsEqual("rejected");
        AssertThat(rejectedReason).IsEqual("The hinges are stuck.");
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task FailedExecutionIsAnnouncedAsStartedThenFailed()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        ExecutorOf(door.Open).Result = new GameplayActionExecutionFailed("The door came off.");
        List<string> notifications = new();
        string failedReason = string.Empty;
        door.Interactive.InteractionActionStarted += (_, _) => notifications.Add("started");
        door.Interactive.InteractionActionCompleted += (_, _) => notifications.Add("completed");
        door.Interactive.InteractionActionRejected += (_, _, _) => notifications.Add("rejected");
        door.Interactive.InteractionActionFailed += (_, _, reason) =>
        {
            notifications.Add("failed");
            failedReason = reason;
        };

        GameplayActionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(result is GameplayActionExecutionFailed).IsTrue();
        AssertThat(string.Join(",", notifications)).IsEqual("started,failed");
        AssertThat(failedReason).IsEqual("The door came off.");
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task ActionWithoutExecutorIsBlockedAndNeverRuns()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        GameplayActionExecutor executor = door.Open.Executor!;
        door.Open.Executor = null;
        int rejectedCount = 0;
        int startedCount = 0;
        door.Interactive.InteractionActionRejected += (_, _, _) => rejectedCount++;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;

        GameplayActionAvailability availability = door.Interactive.EvaluateAvailability(
            door.Interactor,
            door.Open
        );
        GameplayActionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(Describe(availability)).IsEqual("Interaction is not configured.");
        AssertThat(result is GameplayActionExecutionRejected).IsTrue();
        AssertThat(((RecordingInteractionExecutor)executor).ExecuteCount).IsEqual(0);
        AssertThat(rejectedCount).IsEqual(1);
        AssertThat(startedCount).IsEqual(0);
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task CancellingARunningExecutionFreesTheTargetForAnotherInteractor()
    {
        TestWorld testWorld = BuildWorld();
        Node3D secondView = new() { Name = "ViewOrigin" };
        InteractionInteractor secondInteractor = new();
        secondInteractor.AddChild(secondView);
        AttachDetector(secondInteractor, secondView);
        testWorld.World.AddChild(secondInteractor);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );
        string cancelledReason = string.Empty;
        testWorld.Interactive.InteractionActionCancelled += (_, _, reason) =>
            cancelledReason = reason;

        AssertThat(testWorld.Interactive.CancelExecution(executionId, "Interrupted.")).IsTrue();

        AssertThat(cancelledReason).IsEqual("Interrupted.");
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        testWorld.Stateful.SetState(IdleState);
        AssertThat(
                testWorld.Interactive.ExecuteAction(secondInteractor, testWorld.Action)
                    is GameplayActionExecutionRunning
            )
            .IsTrue();
    }

    [TestCase]
    public async Task InteractiveWithoutWorldStateSupportsInstantInteractionOnly()
    {
        TestInteractiveActor owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionAnchor = owner,
        };
        InteractionAction action = CreateActivationAction("activate", owner);
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddAction(action);
        owner.Interactive = interactive;
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        AssertThat(interactive.EvaluateAvailability(interactor) is GameplayActionAllowed).IsTrue();
        AssertThat(
                interactive.ExecuteAction(interactor, action, out ulong executionId)
                    is GameplayActionExecutionCompleted
            )
            .IsTrue();
        AssertThat(interactive.ActiveInteractor == null).IsTrue();
        AssertThat(interactive.CompleteExecution(executionId)).IsFalse();
        AssertThat(owner.StartCount).IsEqual(1);
    }

    [TestCase]
    public async Task OfflineInputUsesAuthoritativeStartAndEndPath()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task BlockedAvailabilityStopsRequestBeforeAuthoritativeDispatch()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Interactive.TargetRules.Add(
            new AlwaysBlockedInteractionRule { Reason = "Locked" }
        );
        bool requestEmitted = false;
        testWorld.Interactor.InteractionRequested += (_, _) => requestEmitted = true;
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsFalse();
        AssertThat(requestEmitted).IsFalse();
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
    }

    [TestCase]
    public async Task ServerReleasesRemoteOwnerInteractionWhenCandidateLeavesRange()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Undetect(testWorld.Interactive);
        // Presence is now validated by the authoritative frame instead of by an overlap callback, so
        // the release happens on the next one rather than inside the detection change itself.
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task ServerReleasesInteractionWhenRemoteInteractorExitsTree()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Interactor.QueueFree();
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task AWorldOwnedExecutionOutlivesTheInteractorLeavingItsWindow()
    {
        TestWorld testWorld = BuildWorld();
        // A machine that was switched on, not a channel: nobody holds a key for it and the world owns
        // the transition from the moment it started.
        testWorld.Action.DefaultBindingConfig!.InputRequirement =
            GameplayActionInputRequirement.None;
        ActivationExecutorOf(testWorld.Action).RequiresPresence = false;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Undetect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(2);

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task AWorldOwnedExecutionOutlivesTheInteractorLeavingTheTree()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Action.DefaultBindingConfig!.InputRequirement =
            GameplayActionInputRequirement.None;
        ActivationExecutorOf(testWorld.Action).RequiresPresence = false;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Interactor.QueueFree();
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor != null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task AnActionCancelledOnInputReleaseStaysBoundToPresenceAnyway()
    {
        TestWorld testWorld = BuildWorld();
        // The definition holds the player's key, so the executor cannot hand this one to the world.
        ActivationExecutorOf(testWorld.Action).RequiresPresence = false;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Undetect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task GameplayActionRunnerNetworkAuthorityRemainsOnServerForRemoteOwner()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2, inheritedAuthority: true);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.Runner!.GetMultiplayerAuthority())
            .IsEqual(testWorld.Interactor.Runner.ServerPeerId);
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
    public async Task OneInputResolvesToTheActionAllowedByTheCurrentWorldState()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        List<string> startedActions = new();
        door.Interactive.InteractionActionStarted += (_, action) =>
            startedActions.Add(action.Definition!.Id.ToString());

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        door.State.SetState(new StringName("open"));
        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        AssertThat(string.Join(",", startedActions)).IsEqual("open,close");
    }

    [TestCase]
    public async Task InputResolutionPrefersAllowedThenPriorityThenIdentifier()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.State.SetState(new StringName("locked"));
        InteractionAction zulu = CreateAction("zulu");
        InteractionAction alpha = CreateAction("alpha");
        InteractionAction blocked = CreateAction(
            "blocked",
            new AlwaysBlockedInteractionRule { Reason = "Locked" }
        );
        blocked.DefaultBindingConfig!.Priority = 10;
        door.Interactive.AddAction(zulu);
        door.Interactive.AddAction(alpha);
        door.Interactive.AddAction(blocked);
        door.Detect(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(ExecutorOf(alpha).ExecuteCount).IsEqual(1);
        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        zulu.DefaultBindingConfig!.Priority = 5;
        door.Interactor.RefreshFocusedBindings(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(ExecutorOf(zulu).ExecuteCount).IsEqual(1);
        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        zulu.Rules.Add(new AlwaysBlockedInteractionRule { Reason = "Locked" });
        alpha.Rules.Add(new AlwaysBlockedInteractionRule { Reason = "Locked" });

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsFalse();
    }

    [TestCase]
    public async Task OwnedAndFocusedInteractionBindingsCompeteByPriority()
    {
        TestWorld testWorld = BuildWorld();
        InputGameplayAction ownedAction = new()
        {
            Name = "OwnedAction",
            Definition = new GameplayActionDefinition
            {
                Id = new StringName("owned"),
                Label = "Owned",
            },
            DefaultBindingConfig = new GameplayActionBindingConfig
            {
                InputActionName = InteractInput,
                ActivationMode = GameplayActionActivationMode.Press,
                Priority = 10,
            },
        };
        TestGameplayActionExecutor ownedExecutor = new() { Name = "OwnedExecutor" };
        ownedAction.AddChild(ownedExecutor);
        ownedAction.Executor = ownedExecutor;
        testWorld.Interactor.Runner!.OwnedActionComponent!.AddAction(ownedAction);
        testWorld.Action.DefaultBindingConfig!.Priority = 20;

        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(ownedExecutor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task AnInputWithoutAnyMatchingActionRequestsNothing()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        int startedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;

        AssertThat(door.Interactor.TryStartInteractionInput(new StringName("inspect"))).IsFalse();
        AssertThat(startedCount).IsEqual(0);
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task ServerRejectsAnActionTheClientBelievesIsAllowed()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        door.Open.Rules.Insert(
            0,
            new AlwaysBlockedInteractionRule { Reason = "Requires a keycard." }
        );
        int startedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;
        string rejectedActionId = string.Empty;
        string rejectedReason = string.Empty;
        door.Interactor.InteractionRejected += (_, actionId, reason) =>
        {
            rejectedActionId = actionId.ToString();
            rejectedReason = reason;
        };

        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName("open")
        );

        AssertThat(startedCount).IsEqual(0);
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(0);
        AssertThat(rejectedActionId).IsEqual("open");
        AssertThat(rejectedReason).IsEqual("Requires a keycard.");
    }

    [TestCase]
    public async Task ServerRejectsAnActionHiddenByItsOwnWorldState()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        int startedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;
        string rejectedReason = string.Empty;
        door.Interactor.InteractionRejected += (_, _, reason) => rejectedReason = reason;

        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName("close")
        );

        AssertThat(startedCount).IsEqual(0);
        AssertThat(ExecutorOf(door.Close).ExecuteCount).IsEqual(0);
        AssertThat(rejectedReason).IsEqual("Interaction unavailable.");
    }

    [TestCase]
    public async Task ServerRejectsAnActionIdentifierItsOwnTargetDoesNotDeclare()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        int startedCount = 0;
        int rejectedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;
        door.Interactor.InteractionRejected += (_, _, _) => rejectedCount++;

        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName("teleport")
        );
        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName(string.Empty)
        );

        AssertThat(startedCount).IsEqual(0);
        AssertThat(rejectedCount).IsEqual(2);
    }

    [TestCase]
    public async Task ReleasingAnotherInputKeepsTheActiveInteraction()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(new StringName("inspect")))
            .IsFalse();

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task ServerKeepsAReservationWhenAnotherInputIsReleased()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Interactor.ServerTryEndInteraction(new StringName("inspect"));

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);

        testWorld.Interactor.ServerTryEndInteraction(InteractInput);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task ReleaseEndsTheStartedExecutionWithoutReResolvingTheInput()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction alternative = CreateAction("alternative");
        // A group of its own, so the running execution leaves it available and a fresh resolution
        // really would pick it. Sharing the default group would block it like everything else.
        alternative.HostConcurrencyGroup = new StringName("inspection");
        testWorld.Interactive.AddAction(alternative);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, alternative)
                    is GameplayActionAllowed
            )
            .IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task AutomaticActionStartsOnFocusAndStaysOutOfPrompts()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Action.DefaultBindingConfig!.ActivationMode =
            GameplayActionActivationMode.Automatic;
        testWorld.Action.DefaultBindingConfig.InputActionName = new StringName();
        testWorld.Action.DefaultBindingConfig.InputRequirement =
            GameplayActionInputRequirement.None;
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Detect(testWorld.Interactive);

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        InteractionTargetPresentation presentation = testWorld.Interactive.GetPresentation(
            testWorld.Interactor,
            true
        );
        AssertThat(presentation.Actions.Count).IsEqual(1);
        AssertThat(presentation.Actions[0].IsAutomatic).IsTrue();
        AssertThat(presentation.HasPromptableAction).IsFalse();
    }

    [TestCase]
    public async Task AutomaticActionDoesNotAnswerAPlayerInput()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction automatic = CreateAction("automatic");
        automatic.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Automatic;
        testWorld.Interactive.AddAction(automatic);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
    }

    [TestCase]
    public async Task StateRuleAllowsEveryStateOfTheExpectedPhase()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Open.Rules.Clear();
        door.Open.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = new NodePath("../../StatefulComponent"),
                ExpectedStates = States("closed", "opening"),
            }
        );

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("allowed");

        door.State.SetState(new StringName("opening"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("allowed");

        door.State.SetState(new StringName("open"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("hidden");
    }

    [TestCase]
    public async Task StateRuleBlocksWithItsOwnReasonWhenTheMismatchIsBlocked()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Open.Rules.Clear();
        door.Open.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = new NodePath("../../StatefulComponent"),
                ExpectedStates = States("closed"),
                MismatchAvailability = GameplayActionUnavailableKind.Blocked,
                BlockReason = "The door is moving.",
            }
        );

        door.State.SetState(new StringName("opening"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("The door is moving.");
        AssertThat(door.Interactive.GetPresentation(door.Interactor, true).Actions.Count)
            .IsEqual(1);
    }

    [TestCase]
    public async Task StateRuleInvertsTheExpectedStatesWhenAsked()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Open.Rules.Clear();
        door.Open.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = new NodePath("../../StatefulComponent"),
                ExpectedStates = States("jammed"),
                Invert = true,
            }
        );

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("allowed");

        door.State.SetState(new StringName("jammed"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("hidden");
    }

    [TestCase]
    public async Task StateRuleWithoutAnyResolvableStateIsNotConfigured()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        StatefulStateInteractionRule rule = new() { ExpectedStates = States("closed") };
        door.Open.Rules.Clear();
        door.Open.Rules.Add(rule);

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("Interaction is not configured.");

        rule.StatefulPath = new NodePath("../../MissingStateful");

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("Interaction is not configured.");

        rule.StatefulPath = new NodePath("../../StatefulComponent");
        rule.ExpectedStates.Clear();

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("Interaction is not configured.");
    }

    [TestCase]
    public async Task StateRuleReadsTheStateOfAnotherObject()
    {
        DoorWorld door = BuildDoorWorld();
        Node3D wall = new() { Name = "LeverWall" };
        StatefulComponent wallState = new()
        {
            Name = "StatefulComponent",
            InitialState = new StringName("lowered"),
        };
        wall.AddChild(wallState);
        door.World.AddChild(wall);
        await door.Runner.SimulateFrames(1);
        door.Open.Rules.Clear();
        door.Open.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = new NodePath("../../../LeverWall/StatefulComponent"),
                ExpectedStates = States("lowered"),
            }
        );

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("allowed");

        wallState.SetState(new StringName("raised"));

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("hidden");
        AssertThat(door.State.State.ToString()).IsEqual("closed");
    }

    [TestCase]
    public async Task GenericStatePrimitivesRunTheWholeOpenCloseCycleWithoutGlue()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        BindSetStateExecutor(door.Open, door.State, "open");
        BindSetStateExecutor(door.Close, door.State, "closed");
        int stateChanges = 0;
        door.State.StateChanged += (_, _, _) => stateChanges++;

        InteractionAction first = door.Open;
        GameplayActionExecutionResult openResult = door.Interactive.ExecuteAction(
            door.Interactor,
            first
        );

        AssertThat(first == door.Open).IsTrue();
        AssertThat(openResult is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("open");
        AssertThat(stateChanges).IsEqual(1);

        InteractionAction second = door.Close;
        GameplayActionExecutionResult closeResult = door.Interactive.ExecuteAction(
            door.Interactor,
            second!
        );

        AssertThat(second == door.Close).IsTrue();
        AssertThat(closeResult is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("closed");
        AssertThat(stateChanges).IsEqual(2);
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task SetStateExecutorFailsWhenNothingWouldChange()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        SetStateGameplayActionExecutor executor = new()
        {
            Stateful = door.State,
            TargetState = new StringName("closed"),
        };

        GameplayActionExecutionResult result = executor.Execute(DoorContext(door));

        AssertThat(result is GameplayActionExecutionFailed).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("closed");
        executor.Free();
    }

    [TestCase]
    public async Task SetStateExecutorFailsWithoutTargetOrOutsideTheSchema()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        SetStateGameplayActionExecutor orphan = new() { TargetState = new StringName("open") };
        door.State.Schema = new StateSchema { States = States("closed", "open") };
        SetStateGameplayActionExecutor undeclared = new()
        {
            Stateful = door.State,
            TargetState = new StringName("melted"),
        };
        GameplayActionContext context = DoorContext(door);

        AssertThat(orphan.Execute(context) is GameplayActionExecutionFailed).IsTrue();
        AssertThat(undeclared.Execute(context) is GameplayActionExecutionFailed).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("closed");
        orphan.Free();
        undeclared.Free();
    }

    [TestCase]
    public async Task GenericStateTransitionWaitsForGameplayCompletionWithoutProgress()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Stateful.Schema = new StateSchema
        {
            States = States("idle", "working", "completed"),
        };
        InteractionAction action = NewAction("transition", Array.Empty<InteractionRule>());
        TransitionStateGameplayActionExecutor executor = new()
        {
            Name = "TransitionExecutor",
            Stateful = testWorld.Stateful,
            RunningState = new StringName("working"),
            CompletedState = new StringName("completed"),
            CancelledState = IdleState,
        };
        action.AddChild(executor);
        action.Executor = executor;
        testWorld.Interactive.AddAction(action);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, action, out ulong executionId);
        await testWorld.Runner.SimulateFrames(10);

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(new StringName("working"));
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    action.Definition!.Id,
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.Progress.HasValue).IsFalse();

        AssertThat(testWorld.Interactive.CompleteExecution(executionId)).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(new StringName("completed"));
    }

    [TestCase]
    public async Task FailedStateTransitionRestoresItsCancelledState()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Stateful.Schema = new StateSchema { States = States("idle", "working") };
        InteractionAction action = NewAction("transition", Array.Empty<InteractionRule>());
        TransitionStateGameplayActionExecutor executor = new()
        {
            Name = "TransitionExecutor",
            Stateful = testWorld.Stateful,
            RunningState = new StringName("working"),
            CompletedState = IdleState,
            CancelledState = IdleState,
        };
        action.AddChild(executor);
        action.Executor = executor;
        testWorld.Interactive.AddAction(action);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, action, out ulong executionId);
        AssertThat(testWorld.Stateful.State).IsEqual(new StringName("working"));

        AssertThat(testWorld.Interactive.FailExecution(executionId, "The machine jammed."))
            .IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(IdleState);
    }

    [TestCase]
    public async Task TheInteractorReportsWhichInputsAreWorthSampling()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction inspect = CreateAction("inspect");
        inspect.DefaultBindingConfig!.InputActionName = new StringName("inspect");
        inspect.HostConcurrencyGroup = new StringName("inspection");
        InteractionAction pickup = CreateAction("pickup");
        pickup.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Automatic;
        testWorld.Interactive.AddAction(inspect);
        testWorld.Interactive.AddAction(pickup);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        List<StringName> focused = new(testWorld.Interactor.Runner!.GetRelevantInputs());

        // Both bound inputs of the focused target, and only once each. The automatic action shares
        // the interact input but is not what puts it there: no key requests an automatic action.
        AssertThat(focused.Count).IsEqual(2);
        AssertThat(focused.Contains(InteractInput)).IsTrue();
        AssertThat(focused.Contains(new StringName("inspect"))).IsTrue();

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        testWorld.Undetect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);

        // Nothing is focused any more, yet the input this interactor believes it is sustaining stays
        // reportable, so a release is still forwarded instead of being silently dropped.
        List<StringName> sustained = new(testWorld.Interactor.Runner!.GetRelevantInputs());

        AssertThat(sustained.Contains(InteractInput)).IsTrue();
    }

    [TestCase]
    public async Task DialogueLikeConcurrencyHidesTheRequesterButBlocksObservers()
    {
        TestWorld testWorld = BuildWorld();
        InteractionInteractor other = AddOtherInteractor(testWorld);
        testWorld.Action.WhenExecutingBySelf = GameplayActionUnavailableKind.Hidden;
        testWorld.Action.WhenExecutingByOther = GameplayActionUnavailableKind.Blocked;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(other, testWorld.Action)
                    is GameplayActionBlocked blocked
                    && blocked.Reason == "Someone else is using this."
            )
            .IsTrue();
    }

    [TestCase]
    public async Task FullyHiddenConcurrencyHidesTheActionForEveryone()
    {
        TestWorld testWorld = BuildWorld();
        InteractionInteractor other = AddOtherInteractor(testWorld);
        testWorld.Action.WhenExecutingBySelf = GameplayActionUnavailableKind.Hidden;
        testWorld.Action.WhenExecutingByOther = GameplayActionUnavailableKind.Hidden;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(other, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
    }

    [TestCase]
    public async Task InverseConcurrencyHidesObserversButBlocksTheRequester()
    {
        TestWorld testWorld = BuildWorld();
        InteractionInteractor other = AddOtherInteractor(testWorld);
        testWorld.Action.WhenExecutingBySelf = GameplayActionUnavailableKind.Blocked;
        testWorld.Action.WhenExecutingByOther = GameplayActionUnavailableKind.Hidden;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, testWorld.Action)
                    is GameplayActionBlocked blocked
                    && blocked.Reason == "This is already in use."
            )
            .IsTrue();
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(other, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
    }

    [TestCase]
    public async Task ConcurrencyPolicyUsesTheRunningSiblingGroup()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction sibling = CreateAction("sibling");
        sibling.HostConcurrencyGroup = testWorld.Action.GetHostConcurrencyGroup();
        sibling.WhenExecutingBySelf = GameplayActionUnavailableKind.Hidden;
        sibling.WhenExecutingByOther = GameplayActionUnavailableKind.Blocked;
        testWorld.Interactive.AddAction(sibling);
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, sibling)
                    is GameplayActionHidden
            )
            .IsTrue();
    }

    [TestCase]
    public async Task AnOpenEndedExecutionStaysPresentedAndBlockedForEveryone()
    {
        DoorWorld door = BuildDoorWorld();
        Node3D otherView = new() { Name = "ViewOrigin" };
        InteractionInteractor other = new() { Name = "Other" };
        other.AddChild(otherView);
        AttachDetector(other, otherView);
        door.World.AddChild(other);
        await door.Runner.SimulateFrames(1);

        // The shape of a dialogue: the executor opens it and holds the execution with no deadline,
        // until whatever owns the conversation completes it.
        ExecutorOf(door.Open).Result = new GameplayActionExecutionRunning();
        door.Interactive.ExecuteAction(door.Interactor, door.Open, out ulong executionId);

        InteractionTargetPresentation owner = door.Interactive.GetPresentation(
            door.Interactor,
            true
        );

        // Still presented, so a prompt keeps somewhere to explain it, but no longer available: a
        // prompt never claims an action the target would immediately refuse.
        AssertThat(owner.Actions.Count).IsEqual(1);
        AssertThat(owner.Actions[0].ActionId).IsEqual(new StringName("open"));
        AssertThat(owner.Actions[0].IsAllowed).IsFalse();
        AssertThat(owner.Actions[0].BlockReason).IsEqual("This is already in use.");
        AssertThat(owner.HasAllowedAction).IsFalse();
        AssertThat(door.Interactive.HasVisibleAction(door.Interactor)).IsTrue();

        // Close was hidden by its own state rule and stays hidden: concurrency is evaluated after
        // the rules, so a running sibling never drags a hidden action into the prompt.
        AssertThat(
                door.Interactive.EvaluateAvailability(door.Interactor, door.Close)
                    is GameplayActionHidden
            )
            .IsTrue();

        // Anybody else gets a different wording, because the two situations are not the same.
        AssertThat(Describe(door.Interactive.EvaluateAvailability(other, door.Open)))
            .IsEqual("Someone else is using this.");

        AssertThat(door.Interactive.CompleteExecution(executionId)).IsTrue();
        AssertThat(
                door.Interactive.EvaluateAvailability(door.Interactor, door.Open)
                    is GameplayActionAllowed
            )
            .IsTrue();
    }

    [TestCase]
    public async Task ANamedConcurrencyGroupLetsAnUnrelatedActionRunDuringALongOne()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.HostConcurrencyGroup = new StringName("controls");
        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong controls
        );
        InteractionAction inspect = CreateAction("inspect");
        inspect.HostConcurrencyGroup = new StringName("inspection");
        testWorld.Interactive.AddAction(inspect);
        ExecutorOf(inspect).Result = new GameplayActionExecutionRunning();

        GameplayActionExecutionResult result = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            inspect,
            out ulong inspection
        );

        AssertThat(result is GameplayActionExecutionRunning).IsTrue();
        AssertThat(testWorld.Interactive.IsExecutionActive(controls)).IsTrue();
        AssertThat(testWorld.Interactive.IsExecutionActive(inspection)).IsTrue();
        AssertThat(inspection == controls).IsFalse();
    }

    [TestCase]
    public async Task AnExecutorLearnsAboutItsOwnEndWithoutSubscribingToAnySignal()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        InteractionAction hack = CreateAction("hack");
        InteractionAction inspect = CreateAction("inspect");
        hack.HostConcurrencyGroup = new StringName("controls");
        inspect.HostConcurrencyGroup = new StringName("inspection");
        testWorld.Interactive.AddAction(hack);
        testWorld.Interactive.AddAction(inspect);
        ExecutorOf(hack).Result = new GameplayActionExecutionRunning();
        ExecutorOf(inspect).Result = new GameplayActionExecutionRunning();
        testWorld.Interactive.ExecuteAction(testWorld.Interactor, hack, out ulong hackId);
        testWorld.Interactive.ExecuteAction(testWorld.Interactor, inspect, out ulong inspectId);

        AssertThat(testWorld.Interactive.CancelExecution(hackId, "Interrupted.")).IsTrue();

        AssertThat(ExecutorOf(hack).CancelledCount).IsEqual(1);
        AssertThat(ExecutorOf(hack).LastCancelReason).IsEqual("Interrupted.");
        AssertThat(ExecutorOf(hack).LastExecutionId).IsEqual(hackId);

        // The sibling running at the very same moment is never told about somebody else's end,
        // which is what an executor had to filter out by hand while this went through a signal.
        AssertThat(ExecutorOf(inspect).CancelledCount).IsEqual(0);
        AssertThat(ExecutorOf(inspect).CompletedCount).IsEqual(0);

        AssertThat(testWorld.Interactive.CompleteExecution(inspectId)).IsTrue();
        AssertThat(ExecutorOf(inspect).CompletedCount).IsEqual(1);
        AssertThat(ExecutorOf(inspect).LastExecutionId).IsEqual(inspectId);
    }

    [TestCase]
    public async Task AnInstantActionReceivesItsImmediateTerminalCallbackExactlyOnce()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);

        door.Interactive.ExecuteAction(door.Interactor, door.Open);

        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(1);
        AssertThat(ExecutorOf(door.Open).CompletedCount).IsEqual(1);
        AssertThat(ExecutorOf(door.Open).CancelledCount).IsEqual(0);
    }

    [TestCase]
    public async Task AnAutomaticActionRetriesWhenItBecomesAllowedWithoutRefocusing()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Owner.GameplayBlocked = true;
        testWorld.Interactive.TargetRules.Add(new InteractiveParentGameplayRule());
        testWorld.Action.DefaultBindingConfig!.ActivationMode =
            GameplayActionActivationMode.Automatic;
        testWorld.Action.DefaultBindingConfig.InputActionName = new StringName();
        testWorld.Action.DefaultBindingConfig.InputRequirement =
            GameplayActionInputRequirement.None;
        int rejectedCount = 0;
        testWorld.Interactor.InteractionRejected += (_, _, _) => rejectedCount++;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(3);

        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(rejectedCount).IsEqual(0);

        // Focus never moves: only the rule flips, and the action must still start by itself.
        testWorld.Owner.GameplayBlocked = false;
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);

        await testWorld.Runner.SimulateFrames(3);

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
    }

    [TestCase]
    public async Task ReleasingAnInputNeverEndsAnotherInteractorsExecution()
    {
        TestWorld testWorld = BuildWorld();
        Node3D secondView = new() { Name = "ViewOrigin" };
        InteractionInteractor secondInteractor = new() { Name = "SecondInteractor" };
        secondInteractor.AddChild(secondView);
        TestInteractionDetector secondDetector = AttachDetector(secondInteractor, secondView);
        testWorld.World.AddChild(secondInteractor);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        secondDetector.SetDetection(testWorld.Interactive, InteractionDetectionKind.Interactible);
        secondInteractor.RecalculateFocus();
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        AssertThat(secondInteractor.TryEndInteractionInput(InteractInput)).IsFalse();
        secondInteractor.ServerTryEndInteraction(InteractInput);

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task TimedExecutionOwnsTheClockAndCompletesTheGenericExecution()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 0.05f;
        await testWorld.Runner.SimulateFrames(1);
        int completedCount = 0;
        testWorld.Interactive.InteractionActionCompleted += (_, _) => completedCount++;

        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsTrue();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    testWorld.Action.Definition!.Id,
                    out GameplayActionExecutionPresentation startedPresentation
                )
            )
            .IsTrue();
        AssertThat(startedPresentation.ExecutionId).IsEqual(executionId);
        AssertThat(startedPresentation.Progress.HasValue).IsTrue();
        AssertThat(startedPresentation.Progress!.Value < 1.0f).IsTrue();

        for (
            int frame = 0;
            frame < 300 && testWorld.Interactive.IsExecutionActive(executionId);
            frame++
        )
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsFalse();
        AssertThat(completedCount).IsEqual(1);
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task TimedExecutionUsesRealTimeWhenItsInteractiveStopsProcessing()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 0.05f;
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );
        testWorld.Interactive.ProcessMode = Node.ProcessModeEnum.Disabled;

        for (
            int frame = 0;
            frame < 300 && testWorld.Interactive.IsExecutionActive(executionId);
            frame++
        )
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsFalse();
    }

    [TestCase]
    public async Task TimedExecutionCanBeComposedByAGenericExecutor()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction action = NewAction("composed", Array.Empty<InteractionRule>());
        ComposedTimedExecutor executor = new() { Name = "ComposedExecutor", Duration = 0.05f };
        action.AddChild(executor);
        action.Executor = executor;
        testWorld.Interactive.AddAction(action);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, action, out ulong executionId);

        AssertThat(executor.Timer.IsActive).IsTrue();
        AssertThat(executor.Timer.ExecutionId).IsEqual(executionId);
        AssertThat(executor.Timer.GetProgress() < 1.0f).IsTrue();

        for (
            int frame = 0;
            frame < 300 && testWorld.Interactive.IsExecutionActive(executionId);
            frame++
        )
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsFalse();
        AssertThat(executor.Timer.IsActive).IsFalse();
    }

    [TestCase]
    public async Task SharedTimedExecutorCannotAbandonItsFirstActiveClock()
    {
        TestWorld testWorld = BuildWorld();
        TestActivationExecutor executor = ActivationExecutorOf(testWorld.Action);
        executor.Duration = 0.05f;
        InteractionAction second = NewAction("second", Array.Empty<InteractionRule>());
        second.HostConcurrencyGroup = new StringName("other");
        second.Executor = executor;
        testWorld.Interactive.AddAction(second);
        await testWorld.Runner.SimulateFrames(1);

        GameplayActionExecutionResult firstResult = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong firstExecutionId
        );
        GameplayActionExecutionResult secondResult = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            second
        );

        AssertThat(firstResult is GameplayActionExecutionRunning).IsTrue();
        AssertThat(secondResult is GameplayActionExecutionFailed).IsTrue();

        for (
            int frame = 0;
            frame < 300 && testWorld.Interactive.IsExecutionActive(firstExecutionId);
            frame++
        )
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(testWorld.Interactive.IsExecutionActive(firstExecutionId)).IsFalse();
    }

    [TestCase]
    public async Task AZeroDurationTimedExecutorFailsInsteadOfBecomingOpenEnded()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 0.0f;
        await testWorld.Runner.SimulateFrames(1);

        GameplayActionExecutionResult result = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );

        AssertThat(result is GameplayActionExecutionFailed).IsTrue();
        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsFalse();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    testWorld.Action.Definition!.Id,
                    out _
                )
            )
            .IsFalse();
    }

    [TestCase]
    public async Task ANonFiniteTimedDurationFailsInsteadOfBecomingOpenEnded()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = float.NaN;
        await testWorld.Runner.SimulateFrames(1);

        GameplayActionExecutionResult result = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );

        AssertThat(result is GameplayActionExecutionFailed).IsTrue();
        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsFalse();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    testWorld.Action.Definition!.Id,
                    out _
                )
            )
            .IsFalse();
    }

    [TestCase]
    public async Task ARunningExecutionPublishesDiscreteProgressThroughItsGenericPresentation()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction action = CreateAction("hack");
        testWorld.Interactive.AddAction(action);
        ExecutorOf(action).Result = new GameplayActionExecutionRunning();
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, action, out ulong executionId);

        AssertThat(
                testWorld.Interactive.SetExecutionProgressSource(
                    executionId,
                    Callable.From(() => 0.42f)
                )
            )
            .IsTrue();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    action.Definition!.Id,
                    out GameplayActionExecutionPresentation sourcedPresentation
                )
            )
            .IsTrue();
        AssertThat(sourcedPresentation.Progress!.Value).IsEqualApprox(0.42f, 0.0005f);
        AssertThat(testWorld.Interactive.ClearExecutionProgressSource(executionId)).IsTrue();

        AssertThat(testWorld.Interactive.ReportExecutionProgress(executionId, -1.0f)).IsTrue();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    action.Definition!.Id,
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.Progress).IsEqual(0.0f);

        AssertThat(testWorld.Interactive.ReportExecutionProgress(executionId, 0.33f)).IsTrue();
        AssertThat(testWorld.Interactive.ReportExecutionProgress(executionId, 0.66f)).IsTrue();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    action.Definition.Id,
                    out presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.Progress!.Value).IsEqualApprox(0.66f, 0.001f);
        AssertThat(testWorld.Interactive.ReportExecutionProgress(executionId, null)).IsTrue();
        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    action.Definition!.Id,
                    out presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.Progress.HasValue).IsFalse();

        AssertThat(testWorld.Interactive.CompleteExecution(executionId)).IsTrue();
        AssertThat(testWorld.Interactive.ReportExecutionProgress(executionId, 1.0f)).IsFalse();
    }

    [TestCase]
    public async Task ReplicatedSnapshotAppliesCurrentProgressRejectsStaleStateAndRemovesAbsence()
    {
        TestWorld authority = BuildWorld();
        authority.Action.ExecutionVisibility = GameplayActionExecutionVisibility.Replicated;
        InteractiveComponent receiver = AddPresentationReceiver(
            authority.World,
            authority.Action.Definition!.Id,
            GameplayActionExecutionVisibility.Replicated
        );
        await authority.Runner.SimulateFrames(1);
        authority.Interactive.ExecuteAction(
            authority.Interactor,
            authority.Action,
            out ulong executionId
        );
        GameplayActionExecutionSynchronizer source = AutoFree(
            new GameplayActionExecutionSynchronizer
            {
                Component = authority.Interactive.ActionComponent,
            }
        );
        GameplayActionExecutionSynchronizer destination = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = receiver.ActionComponent }
        );

        Godot.Collections.Dictionary started = source.CaptureSnapshot();
        AssertThat(destination.ApplySnapshot(started)).IsTrue();
        AssertThat(
                receiver.TryGetExecutionPresentation(
                    authority.Action.Definition.Id,
                    out GameplayActionExecutionPresentation initial
                )
            )
            .IsTrue();
        AssertThat(initial.ExecutionId).IsEqual(executionId);

        AssertThat(authority.Interactive.ReportExecutionProgress(executionId, 0.66f)).IsTrue();
        Godot.Collections.Dictionary progressed = source.CaptureSnapshot();
        AssertThat(destination.ApplySnapshot(progressed)).IsTrue();
        AssertThat(destination.ApplySnapshot(started)).IsFalse();
        AssertThat(
                receiver.TryGetExecutionPresentation(
                    authority.Action.Definition.Id,
                    out GameplayActionExecutionPresentation current
                )
            )
            .IsTrue();
        AssertThat(current.Progress!.Value).IsEqualApprox(0.66f, 0.001f);

        AssertThat(authority.Interactive.CompleteExecution(executionId)).IsTrue();
        AssertThat(destination.ApplySnapshot(source.CaptureSnapshot())).IsTrue();
        AssertThat(receiver.TryGetExecutionPresentation(authority.Action.Definition.Id, out _))
            .IsFalse();
    }

    [TestCase]
    public async Task SharedGameplaySessionDrivesAWorldConsumerWithoutOwningItsParticipants()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction repair = CreateAction("repair");
        testWorld.Interactive.AddAction(repair);
        ExecutorOf(repair).Result = new GameplayActionExecutionRunning();
        Node participantA = new() { Name = "ParticipantA" };
        Node participantB = new() { Name = "ParticipantB" };
        testWorld.World.AddChild(participantA);
        testWorld.World.AddChild(participantB);
        FakeRepairSession repairSession = new(participantA, participantB, stepCount: 3);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.ExecuteAction(testWorld.Interactor, repair, out ulong executionId);
        AssertThat(
                testWorld.Interactive.SetExecutionProgressSource(
                    executionId,
                    Callable.From(repairSession.GetProgress)
                )
            )
            .IsTrue();
        WorldExecutionGauge gauge = new(testWorld.Interactive, repair.Definition!.Id);

        repairSession.CompleteStep();
        repairSession.CompleteStep();

        AssertThat(gauge.Read()).IsEqualApprox(2.0f / 3.0f, 0.001f);
        AssertThat(participantA.GetParent() == testWorld.World).IsTrue();
        AssertThat(participantB.GetParent() == testWorld.World).IsTrue();
    }

    [TestCase]
    public async Task ARunningExecutionCanFailOnceAndNotCompleteAfterwards()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction action = CreateAction("fail");
        testWorld.Interactive.AddAction(action);
        RecordingInteractionExecutor executor = ExecutorOf(action);
        executor.Result = new GameplayActionExecutionRunning();
        await testWorld.Runner.SimulateFrames(1);
        List<string> notifications = new();
        testWorld.Interactive.InteractionActionFailed += (_, _, reason) =>
        {
            notifications.Add(reason);
        };

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, action, out ulong executionId);

        AssertThat(testWorld.Interactive.FailExecution(executionId, "The session expired."))
            .IsTrue();
        AssertThat(testWorld.Interactive.FailExecution(executionId, "Too late.")).IsFalse();
        AssertThat(testWorld.Interactive.CompleteExecution(executionId)).IsFalse();
        AssertThat(executor.FailedCount).IsEqual(1);
        AssertThat(executor.LastFailureReason).IsEqual("The session expired.");
        AssertThat(notifications).IsEqual(new List<string> { "The session expired." });
    }

    [TestCase]
    public async Task HoldingOneInputSelectsTheActionThatAsksForTheHold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        force.DefaultBindingConfig!.HoldDuration = 0.05f;
        testWorld.Interactive.AddAction(force);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        // Pressing only started the hold: nothing is selected while the threshold is not reached.
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(0);
        GameplayActionPresentation forcePresentation = PresentedAction(
            testWorld.Interactor.GetInteractionPresentation()!.Value,
            "force"
        );
        AssertThat(forcePresentation.HoldProgress.HasValue).IsTrue();
        AssertThat(forcePresentation.HoldElapsed.HasValue).IsTrue();

        for (int frame = 0; frame < 300 && ExecutorOf(force).ExecuteCount == 0; frame++)
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(1);
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(
                PresentedAction(
                    testWorld.Interactor.GetInteractionPresentation()!.Value,
                    "force"
                ).HoldProgress.HasValue
            )
            .IsFalse();
    }

    [TestCase]
    public async Task AConsumedUnlockHoldCannotOpenTheDoorBeforeRelease()
    {
        DoorWorld door = BuildDoorWorld();
        AssertThat(door.State.SetState(new StringName("locked"))).IsTrue();
        InteractionAction unlock = CreateAction("unlock", DoorStateRule("locked"));
        unlock.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        unlock.DefaultBindingConfig!.HoldDuration = 0.001f;
        BindSetStateExecutor(unlock, door.State, "closed");
        door.Interactive.AddAction(unlock);
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        for (int frame = 0; frame < 10 && door.State.State.ToString() != "closed"; frame++)
        {
            await door.Runner.SimulateFrames(1);
        }

        AssertThat(door.State.State.ToString()).IsEqual("closed");
        door.Detector.ClearDetection(door.Interactive);
        door.Interactor.RecalculateFocus();
        AssertThat(
                new List<StringName>(door.Interactor.Runner!.GetRelevantInputs()).Contains(
                    InteractInput
                )
            )
            .IsTrue();
        door.Detect(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsFalse();
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(0);

        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(1);
    }

    [TestCase]
    public async Task ReleasingBeforeTheThresholdSelectsTheActionThatAsksForNoHold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        force.DefaultBindingConfig!.HoldDuration = 3600.0f;
        testWorld.Interactive.AddAction(force);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task TheRequestingPeerDrawsItsProgressWithoutAnyReplication()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        await testWorld.Runner.SimulateFrames(2);

        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    testWorld.Action.Definition!.Id,
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.ActionId).IsEqual(new StringName("activate"));
        AssertThat(presentation.Progress.HasValue).IsTrue();
        AssertThat(presentation.Progress!.Value > 0.0f).IsTrue();
        AssertThat(presentation.Progress!.Value < 1.0f).IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    testWorld.Action.Definition!.Id,
                    out _
                )
            )
            .IsFalse();
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
    public async Task EveryHeldActionFillsOnItsOwnThreshold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        force.DefaultBindingConfig!.HoldDuration = 3600.0f;
        InteractionAction pry = CreateAction("pry");
        pry.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        pry.DefaultBindingConfig!.HoldDuration = 0.001f;
        testWorld.Interactive.AddAction(force);
        testWorld.Interactive.AddAction(pry);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        await testWorld.Runner.SimulateFrames(2);
        InteractionTargetPresentation presentation = testWorld.Interactive.GetPresentation(
            testWorld.Interactor,
            true
        );

        // Normalised on the threshold of each action and not on the longest one of the input: a bar
        // drawn around the key reaches one when the action it belongs to becomes selectable, which the
        // shorter of two actions sharing an input would otherwise never do.
        AssertThat(PresentedAction(presentation, "pry").HoldProgress!.Value).IsEqual(1.0f);
        AssertThat(PresentedAction(presentation, "force").HoldProgress!.Value > 0.0f).IsTrue();
        AssertThat(PresentedAction(presentation, "force").HoldProgress!.Value < 1.0f).IsTrue();

        // The raw seconds come along because a widget cannot rebuild them from the ratio: the
        // threshold it would multiply by is not part of the presentation.
        AssertThat(PresentedAction(presentation, "pry").HoldElapsed!.Value)
            .IsEqual(PresentedAction(presentation, "force").HoldElapsed!.Value);
        AssertThat(PresentedAction(presentation, "pry").HoldElapsed!.Value > 0.0f).IsTrue();

        // The hold is a selection between the actions sharing an input, so the one asking for no
        // threshold reports nothing: its bar would promise a hold that selects it, and none does.
        AssertThat(PresentedAction(presentation, "activate").HoldProgress.HasValue).IsFalse();
        AssertThat(PresentedAction(presentation, "activate").HoldElapsed.HasValue).IsFalse();
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

    [TestCase]
    public async Task AMultiPhaseObjectIsBuiltFromAuthoredPartsWithoutABespokeExecutor()
    {
        CoreWorld core = BuildCoreWorld();
        await core.Runner.SimulateFrames(1);
        int doorsOpened = 0;
        core.State.StateChanged += (_, newState, _) =>
        {
            if (newState == ActivatedState)
            {
                doorsOpened++;
            }
        };

        // Phase one is the only thing offered: the second charge is not a choice yet, so it is
        // absent from the prompt instead of being explained.
        AssertThat(Presented(core).Count).IsEqual(1);
        core.Interactive.ExecuteAction(core.Interactor, core.Activate, out ulong first);
        AssertThat(core.State.State.ToString()).IsEqual("charging");

        await WaitUntilExecutionEnds(core, first);

        AssertThat(core.State.State.ToString()).IsEqual("primed");

        // Phase two exists but the player lacks the resonator, so it is presentable and explained.
        List<GameplayActionPresentation> primed = Presented(core);
        AssertThat(primed.Count).IsEqual(1);
        AssertThat(primed[0].ActionId).IsEqual(new StringName("reactivate"));
        AssertThat(primed[0].IsAllowed).IsFalse();
        AssertThat(primed[0].BlockReason).IsEqual("You need the resonator.");

        core.Key.HasKey = true;
        core.Interactive.ExecuteAction(core.Interactor, core.Reactivate, out ulong second);
        AssertThat(core.State.State.ToString()).IsEqual("recharging");

        await WaitUntilExecutionEnds(core, second);

        AssertThat(core.State.State.ToString()).IsEqual("activated");

        // Fully interacted: every action is hidden, so the object stops being focusable at all.
        AssertThat(Presented(core).Count).IsEqual(0);
        AssertThat(core.Interactive.HasVisibleAction(core.Interactor)).IsFalse();

        // The quest reacted to world state, never to an interaction notification.
        AssertThat(doorsOpened).IsEqual(1);

        // The two phases differ only by authored data: same generic executor, same generic rules.
        AssertThat(core.Activate.Executor is TimedTransitionStateGameplayActionExecutor).IsTrue();
        AssertThat(core.Reactivate.Executor is TimedTransitionStateGameplayActionExecutor).IsTrue();
    }

    private static async Task WaitUntilExecutionEnds(CoreWorld core, ulong executionId)
    {
        for (int frame = 0; frame < 300 && core.Interactive.IsExecutionActive(executionId); frame++)
        {
            await core.Runner.SimulateFrames(1);
        }

        AssertThat(core.Interactive.IsExecutionActive(executionId)).IsFalse();
    }

    private static List<GameplayActionPresentation> Presented(CoreWorld core) =>
        new(core.Interactive.GetPresentation(core.Interactor, true).Actions);

    private static CoreWorld BuildCoreWorld()
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
        InteractionAction activate = CoreAction(
            "activate",
            0.05f,
            "charging",
            "primed",
            "dormant",
            state,
            DoorStateRule("dormant")
        );
        InteractionAction reactivate = CoreAction(
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

    private static InteractionAction CoreAction(
        string id,
        float duration,
        string runningState,
        string completedState,
        string cancelledState,
        StatefulComponent stateful,
        params InteractionRule[] rules
    )
    {
        InteractionAction action = NewAction(id, rules);
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

    private static string Describe(GameplayActionAvailability availability) =>
        availability switch
        {
            GameplayActionAllowed => "allowed",
            GameplayActionBlocked blocked => blocked.Reason,
            GameplayActionHidden => "hidden",
        };

    private static TestWorld BuildWorld(int ownerPeerId = 1, bool inheritedAuthority = false)
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
        InteractionAction action = CreateActivationAction(
            "activate",
            owner,
            ActorStateRule("This is already activated.", IdleState, ActivatingState),
            ActorStateRule("This is busy.", IdleState)
        );
        owner.AddChild(area);
        owner.AddChild(stateful);
        owner.AddChild(interactive);
        interactive.AddAction(action);
        interactive.InteractionActionCancelled += owner.OnInteractionActionCancelled;
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

    private static TestInteractionDetector AttachDetector(
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

    private static InteractiveComponent AddPresentationReceiver(
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
        InteractionAction action = CreateAction(actionId.ToString());
        action.ExecutionVisibility = visibility;
        actor.AddChild(area);
        actor.AddChild(interactive);
        interactive.AddAction(action);
        parent.AddChild(actor);
        return interactive;
    }

    private static InteractionAction CreateActivationAction(
        string id,
        TestInteractiveActor owner,
        params InteractionRule[] rules
    )
    {
        InteractionAction action = NewAction(id, rules);
        // The activation is the sustained action of these worlds: the player stays engaged and
        // releasing the input ends it, which is exactly what the definition now declares.
        action.DefaultBindingConfig!.InputRequirement = GameplayActionInputRequirement.Pressed;
        TestActivationExecutor executor = new() { Name = $"{id}Executor", Actor = owner };
        action.AddChild(executor);
        action.Executor = executor;
        return action;
    }

    private static GameplayActionPresentation PresentedAction(
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

    private static InteractionAction CreateAction(string id, params InteractionRule[] rules)
    {
        InteractionAction action = NewAction(id, rules);
        RecordingInteractionExecutor executor = new() { Name = $"{id}Executor" };
        action.AddChild(executor);
        action.Executor = executor;
        return action;
    }

    private static InteractionInteractor AddOtherInteractor(TestWorld testWorld)
    {
        Node3D view = new() { Name = "OtherViewOrigin" };
        InteractionInteractor other = new() { Name = "Other" };
        other.AddChild(view);
        AttachDetector(other, view);
        testWorld.World.AddChild(other);
        return other;
    }

    private static InteractionAction NewAction(string id, InteractionRule[] rules)
    {
        InteractionAction action = new()
        {
            Name = $"{id}Action",
            Definition = new GameplayActionDefinition { Id = new StringName(id), Label = id },
            DefaultBindingConfig = new GameplayActionBindingConfig
            {
                InputActionName = new StringName("interact"),
                ActivationMode = GameplayActionActivationMode.Press,
            },
        };
        foreach (InteractionRule rule in rules)
        {
            action.Rules.Add(rule);
        }

        return action;
    }

    private static Godot.Collections.Array<StringName> States(params string[] states)
    {
        Godot.Collections.Array<StringName> array = new();
        foreach (string state in states)
        {
            array.Add(new StringName(state));
        }

        return array;
    }

    private static GameplayActionContext DoorContext(DoorWorld door) =>
        new(
            1,
            door.Interactor,
            door.Interactor.Runner,
            door.Interactive.ActionComponent!,
            door.Open
        );

    private static void BindSetStateExecutor(
        InteractionAction action,
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

    private static StatefulStateInteractionRule ActorStateRule(
        string blockReason,
        params StringName[] expectedStates
    )
    {
        StatefulStateInteractionRule rule = new()
        {
            StatefulPath = new NodePath("../../StatefulComponent"),
            MismatchAvailability = GameplayActionUnavailableKind.Blocked,
            BlockReason = blockReason,
        };
        foreach (StringName state in expectedStates)
        {
            rule.ExpectedStates.Add(state);
        }

        return rule;
    }

    private static StatefulStateInteractionRule DoorStateRule(string expectedState) =>
        new()
        {
            StatefulPath = new NodePath("../../StatefulComponent"),
            ExpectedStates = { new StringName(expectedState) },
        };

    private static RecordingInteractionExecutor ExecutorOf(InteractionAction action) =>
        (RecordingInteractionExecutor)action.Executor!;

    private static TestActivationExecutor ActivationExecutorOf(InteractionAction action) =>
        (TestActivationExecutor)action.Executor!;

    private static DoorWorld BuildDoorWorld()
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
        InteractionAction open = CreateAction("open", DoorStateRule("closed"));
        InteractionAction close = CreateAction("close", DoorStateRule("open"));
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

    private sealed record DoorWorld(
        Node3D World,
        ISceneRunner Runner,
        StatefulComponent State,
        InteractiveComponent Interactive,
        InteractionAction Open,
        InteractionAction Close,
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

    private sealed record CoreWorld(
        Node3D World,
        ISceneRunner Runner,
        StatefulComponent State,
        InteractiveComponent Interactive,
        InteractionAction Activate,
        InteractionAction Reactivate,
        CarriesKeyInteractionRule Key,
        InteractionInteractor Interactor,
        TestInteractionDetector Detector
    );

    private sealed partial class CarriesKeyInteractionRule : InteractionRule
    {
        public bool HasKey { get; set; }

        public override GameplayActionAvailability Evaluate(in InteractionContext context) =>
            HasKey
                ? new GameplayActionAllowed()
                : new GameplayActionBlocked("You need the resonator.");
    }

    private sealed record TestWorld(
        Node3D World,
        ISceneRunner Runner,
        TestInteractiveActor Owner,
        StatefulComponent Stateful,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor,
        TestInteractionDetector Detector,
        InteractionAction Action
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

    private sealed partial class TestInteractiveActor : Node3D
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

        public void OnInteractionActionCancelled(
            InteractionInteractor interactor,
            InteractionAction action,
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

    private sealed partial class TestActivationExecutor : InteractionActionExecutor
    {
        private readonly TimedExecution _timedExecution = new();

        public TestInteractiveActor? Actor { get; set; }

        public float? Duration { get; set; }

        public bool RequiresPresence { get; set; } = true;

        public override bool RequiresInteractorPresence => RequiresPresence;

        public override GameplayActionExecutionResult Execute(
            in InteractionExecutionContext context
        )
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
                _timedExecution.Start(
                    context.Interactive.ActionComponent!,
                    context.ExecutionId,
                    Duration.Value
                ) == TimedExecutionStartResult.Started
                ? Running()
                : new GameplayActionExecutionFailed("The activation timer could not start.");
        }

        internal override GameplayActionProgressSample? GetInteractionPredictionSample(
            in InteractionContext context
        ) => Duration.HasValue ? TimedExecution.BuildPredictionSample(Duration.Value) : null;

        protected internal override void OnExecutionCompleted(
            in InteractionExecutionContext context
        ) => _timedExecution.Stop(context.ExecutionId);

        protected internal override void OnExecutionCancelled(
            in InteractionExecutionContext context,
            string reason
        ) => _timedExecution.Stop(context.ExecutionId);

        protected internal override void OnExecutionFailed(
            in InteractionExecutionContext context,
            string reason
        ) => _timedExecution.Stop(context.ExecutionId);
    }

    private sealed partial class ComposedTimedExecutor : InteractionActionExecutor
    {
        public float Duration { get; set; }

        public TimedExecution Timer { get; } = new();

        public override GameplayActionExecutionResult Execute(
            in InteractionExecutionContext context
        )
        {
            return
                Timer.Start(context.Interactive.ActionComponent!, context.ExecutionId, Duration)
                == TimedExecutionStartResult.Started
                ? Running()
                : new GameplayActionExecutionFailed("The timer could not start.");
        }
    }

    private sealed partial class RecordingInteractionExecutor : InteractionActionExecutor
    {
        public GameplayActionExecutionResult Result { get; set; } =
            new GameplayActionExecutionCompleted();

        public int ExecuteCount { get; private set; }

        public InteractionInteractor? LastInteractor { get; private set; }

        public InteractionAction? LastAction { get; private set; }

        public InteractionInteractor? ReservedInteractorDuringExecute { get; private set; }

        public ulong LastExecutionId { get; private set; }

        public int CompletedCount { get; private set; }

        public int CancelledCount { get; private set; }

        public int FailedCount { get; private set; }

        public string LastCancelReason { get; private set; } = string.Empty;

        public string LastFailureReason { get; private set; } = string.Empty;

        public override GameplayActionExecutionResult Execute(
            in InteractionExecutionContext context
        )
        {
            ExecuteCount++;
            LastInteractor = context.Interactor;
            LastAction = context.Action;
            LastExecutionId = context.ExecutionId;
            ReservedInteractorDuringExecute = context.Interactive.ActiveInteractor;
            return Result;
        }

        protected internal override void OnExecutionCompleted(
            in InteractionExecutionContext context
        )
        {
            base.OnExecutionCompleted(context);
            CompletedCount++;
            LastExecutionId = context.ExecutionId;
        }

        protected internal override void OnExecutionCancelled(
            in InteractionExecutionContext context,
            string reason
        )
        {
            base.OnExecutionCancelled(context, reason);
            CancelledCount++;
            LastExecutionId = context.ExecutionId;
            LastCancelReason = reason;
        }

        protected internal override void OnExecutionFailed(
            in InteractionExecutionContext context,
            string reason
        )
        {
            base.OnExecutionFailed(context, reason);
            FailedCount++;
            LastExecutionId = context.ExecutionId;
            LastFailureReason = reason;
        }
    }

    private sealed class FakeRepairSession
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

    private sealed class WorldExecutionGauge
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

    private sealed partial class InteractiveParentGameplayRule : InteractionRule
    {
        public override GameplayActionAvailability Evaluate(in InteractionContext context)
        {
            return context.Interactive.GetParent() is TestInteractiveActor { GameplayBlocked: true }
                ? new GameplayActionBlocked("Gameplay condition is blocked.")
                : new GameplayActionAllowed();
        }
    }
}
