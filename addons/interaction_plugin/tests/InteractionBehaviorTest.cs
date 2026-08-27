namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Examples.Rules;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;
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
    public async Task ExecutionReservationCoreMutatesWithoutRunningAnythingExternal()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        int stateSignalCount = 0;
        int startedCount = 0;
        testWorld.Stateful.StateChanged += (_, _, _) => stateSignalCount++;
        testWorld.Interactive.InteractionActionStarted += (_, _) => startedCount++;

        InteractionExecution? reservation = testWorld.Interactive.ReserveExecutionCore(
            testWorld.Interactor,
            testWorld.Action
        );

        AssertThat(reservation.HasValue).IsTrue();
        AssertThat(reservation?.Interactor == testWorld.Interactor).IsTrue();
        AssertThat(reservation?.Action == testWorld.Action).IsTrue();
        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(testWorld.Stateful.State).IsEqual(IdleState);
        AssertThat(stateSignalCount).IsEqual(0);
        AssertThat(startedCount).IsEqual(0);
    }

    [TestCase]
    public async Task ReservedExecutionRefusesASecondReservation()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.ReserveExecutionCore(testWorld.Interactor, testWorld.Action);

        InteractionExecution? second = testWorld.Interactive.ReserveExecutionCore(
            testWorld.Interactor,
            testWorld.Action
        );

        AssertThat(second.HasValue).IsFalse();
        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
    }

    [TestCase]
    public async Task ExecutionResultCoreReleasesTheReservationWithoutDispatch()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        InteractionExecution reservation = testWorld
            .Interactive.ReserveExecutionCore(testWorld.Interactor, testWorld.Action)!
            .Value;
        int startedCount = 0;
        int completedCount = 0;
        int statusSignalCount = 0;
        testWorld.Interactive.InteractionActionStarted += (_, _) => startedCount++;
        testWorld.Interactive.InteractionActionCompleted += (_, _) => completedCount++;
        testWorld.Interactive.InteractiveStatusChanged += () => statusSignalCount++;

        InteractionExecutionDispatch dispatch = testWorld.Interactive.ApplyExecutionResultCore(
            reservation,
            new InteractionExecutionCompleted()
        );

        AssertThat(dispatch.Execution.Interactor == testWorld.Interactor).IsTrue();
        AssertThat(dispatch.Result is InteractionExecutionCompleted).IsTrue();
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(startedCount).IsEqual(0);
        AssertThat(completedCount).IsEqual(0);
        AssertThat(statusSignalCount).IsEqual(0);
    }

    [TestCase]
    public async Task ExecutionResultCoreKeepsARunningReservation()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        InteractionExecution reservation = testWorld
            .Interactive.ReserveExecutionCore(testWorld.Interactor, testWorld.Action)!
            .Value;

        testWorld.Interactive.ApplyExecutionResultCore(
            reservation,
            new InteractionExecutionRunning()
        );

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
    }

    [TestCase]
    public async Task ExecutionEndCoreReleasesBeforeExternalDispatch()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );
        int cancelledCount = 0;
        int completedCount = 0;
        int statusSignalCount = 0;
        testWorld.Interactive.InteractionActionCancelled += (_, _, _) => cancelledCount++;
        testWorld.Interactive.InteractionActionCompleted += (_, _) => completedCount++;
        testWorld.Interactive.InteractiveStatusChanged += () => statusSignalCount++;

        InteractionExecution? execution = testWorld.Interactive.EndExecutionCore(executionId);

        AssertThat(execution.HasValue).IsTrue();
        AssertThat(execution?.Interactor == testWorld.Interactor).IsTrue();
        AssertThat(execution?.Action == testWorld.Action).IsTrue();
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(cancelledCount).IsEqual(0);
        AssertThat(completedCount).IsEqual(0);
        AssertThat(statusSignalCount).IsEqual(0);
    }

    [TestCase]
    public async Task ExecutionEndCoreRefusesAnIdentifierItDoesNotHold()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );

        InteractionExecution? execution = testWorld.Interactive.EndExecutionCore(executionId + 1);

        AssertThat(execution.HasValue).IsFalse();
        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsTrue();
        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
    }

    [TestCase]
    public async Task ExecutionDispatchEmitsStartedThenCompletedExactlyOnce()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        InteractionExecution reservation = testWorld
            .Interactive.ReserveExecutionCore(testWorld.Interactor, testWorld.Action)!
            .Value;
        InteractionExecutionDispatch dispatch = testWorld.Interactive.ApplyExecutionResultCore(
            reservation,
            new InteractionExecutionCompleted()
        );
        List<string> notifications = new();
        testWorld.Interactive.InteractionActionStarted += (_, _) => notifications.Add("started");
        testWorld.Interactive.InteractionActionCompleted += (_, _) =>
            notifications.Add("completed");
        testWorld.Interactive.InteractionActionCancelled += (_, _, _) =>
            notifications.Add("cancelled");
        testWorld.Interactive.InteractionActionRejected += (_, _, _) =>
            notifications.Add("rejected");

        testWorld.Interactive.DispatchExecutionResult(dispatch);

        AssertThat(string.Join(",", notifications)).IsEqual("started,completed");
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
        testWorld.Interactor.OwnerPeerId = 1;
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
        testWorld.Interactor.OwnerPeerId = 1;
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
        testWorld.Interactor.OwnerPeerId = 1;
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
        InteractionAvailability allowed = new InteractionAllowed();
        InteractionAvailability blocked = new InteractionBlocked("Needs a key");
        InteractionAvailability hidden = new InteractionHidden();

        AssertThat(Describe(allowed)).IsEqual("allowed");
        AssertThat(Describe(blocked)).IsEqual("Needs a key");
        AssertThat(Describe(hidden)).IsEqual("hidden");
    }

    [TestCase]
    public void OfflineInteractorKeepsLocalControlWithoutMultiplayerPeer()
    {
        InteractionInteractor interactor = new() { OwnerPeerId = 1 };

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
        interactive.Actions.Add(action);
        InteractionInteractor interactor = new();

        try
        {
            InteractionExecutionResult result = interactive.ExecuteAction(interactor, action);

            AssertThat(result is InteractionExecutionCompleted).IsTrue();
        }
        finally
        {
            interactive.Free();
            action.Free();
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
        interactive.Actions.Add(action);
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddChild(action);
        InteractionInteractor interactor = new();
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.AddChild(view);
        AttachDetector(interactor, view);
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        InteractionAvailability availability = interactive.EvaluateAvailability(interactor, action);

        AssertThat(availability is InteractionBlocked blocked && blocked.Reason == "First reason")
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
        interactive.Actions.Add(action);
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddChild(action);
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        InteractionAvailability availability = interactive.EvaluateAvailability(interactor, action);

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
        interactive.Actions.Add(action);
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddChild(action);
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        InteractionAvailability blockedAvailability = interactive.EvaluateAvailability(
            interactor,
            action
        );
        owner.GameplayBlocked = false;
        InteractionAvailability allowedAvailability = interactive.EvaluateAvailability(
            interactor,
            action
        );

        AssertThat(Describe(blockedAvailability)).IsEqual("Gameplay condition is blocked.");
        AssertThat(allowedAvailability is InteractionAllowed).IsTrue();
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
                ) is InteractionExecutionRunning
            )
            .IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(
                testWorld.Interactive.ExecuteAction(secondInteractor, testWorld.Action)
                    is InteractionExecutionRejected
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

        InteractionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(result is InteractionExecutionCompleted).IsTrue();
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
    public async Task ARunningExecutionRefusesTheSameInteractorWithItsOwnReason()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);
        InteractionAction inspect = CreateAction("inspect");
        AddAction(testWorld.Interactive, inspect);
        string rejectedReason = string.Empty;
        testWorld.Interactive.InteractionActionRejected += (_, _, reason) =>
            rejectedReason = reason;

        InteractionExecutionResult result = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            inspect
        );

        AssertThat(result is InteractionExecutionRejected).IsTrue();
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
        ExecutorOf(door.Open).Result = new InteractionExecutionRejected("The hinges are stuck.");
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

        InteractionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(result is InteractionExecutionRejected).IsTrue();
        AssertThat(string.Join(",", notifications)).IsEqual("rejected");
        AssertThat(rejectedReason).IsEqual("The hinges are stuck.");
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task FailedExecutionIsAnnouncedAsStartedThenCancelled()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        ExecutorOf(door.Open).Result = new InteractionExecutionFailed("The door came off.");
        List<string> notifications = new();
        string cancelledReason = string.Empty;
        door.Interactive.InteractionActionStarted += (_, _) => notifications.Add("started");
        door.Interactive.InteractionActionCompleted += (_, _) => notifications.Add("completed");
        door.Interactive.InteractionActionRejected += (_, _, _) => notifications.Add("rejected");
        door.Interactive.InteractionActionCancelled += (_, _, reason) =>
        {
            notifications.Add("cancelled");
            cancelledReason = reason;
        };

        InteractionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(result is InteractionExecutionFailed).IsTrue();
        AssertThat(string.Join(",", notifications)).IsEqual("started,cancelled");
        AssertThat(cancelledReason).IsEqual("The door came off.");
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task ActionWithoutExecutorIsBlockedAndNeverRuns()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        InteractionActionExecutor executor = door.Open.Executor!;
        door.Open.Executor = null;
        int rejectedCount = 0;
        int startedCount = 0;
        door.Interactive.InteractionActionRejected += (_, _, _) => rejectedCount++;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;

        InteractionAvailability availability = door.Interactive.EvaluateAvailability(
            door.Interactor,
            door.Open
        );
        InteractionExecutionResult result = door.Interactive.ExecuteAction(
            door.Interactor,
            door.Open
        );

        AssertThat(Describe(availability)).IsEqual("Interaction is not configured.");
        AssertThat(result is InteractionExecutionRejected).IsTrue();
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
                    is InteractionExecutionRunning
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
        interactive.Actions.Add(action);
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.AddChild(action);
        owner.Interactive = interactive;
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        AssertThat(interactive.EvaluateAvailability(interactor) is InteractionAllowed).IsTrue();
        AssertThat(
                interactive.ExecuteAction(interactor, action, out ulong executionId)
                    is InteractionExecutionCompleted
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
        testWorld.Action.Definition!.CancelOnInputReleased = false;
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
        testWorld.Action.Definition!.CancelOnInputReleased = false;
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
    public async Task InteractorNetworkAuthorityRemainsOnServerForRemoteOwner()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.GetMultiplayerAuthority())
            .IsEqual(testWorld.Interactor.ServerPeerId);
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

        InteractionAvailability first = door.Interactive.EvaluateAvailability(
            door.Interactor,
            door.Open
        );
        InteractionAvailability second = door.Interactive.EvaluateAvailability(
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
        door.Interactive.Actions.Add(undefined);
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
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        InteractionAction foreign = CreateAction("foreign");
        try
        {
            AssertThat(interactive.EvaluateAvailability(interactor) is InteractionHidden).IsTrue();
            AssertThat(interactive.ResolveAction(new StringName("foreign")) == null).IsTrue();
            AssertThat(
                    interactive.ExecuteAction(interactor, foreign) is InteractionExecutionRejected
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
        crateInteractive.Actions.Add(inspect);
        crate.AddChild(crateArea);
        crate.AddChild(crateInteractive);
        crateInteractive.AddChild(inspect);
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
        blocked.Priority = 10;
        AddAction(door.Interactive, zulu);
        AddAction(door.Interactive, alpha);
        AddAction(door.Interactive, blocked);

        AssertThat(door.Interactive.ResolveActionForInput(door.Interactor, InteractInput) == alpha)
            .IsTrue();

        zulu.Priority = 5;

        AssertThat(door.Interactive.ResolveActionForInput(door.Interactor, InteractInput) == zulu)
            .IsTrue();

        zulu.Rules.Add(new AlwaysBlockedInteractionRule { Reason = "Locked" });
        alpha.Rules.Add(new AlwaysBlockedInteractionRule { Reason = "Locked" });

        AssertThat(
                door.Interactive.ResolveActionForInput(door.Interactor, InteractInput) == blocked
            )
            .IsTrue();
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
        alternative.ConcurrencyGroup = new StringName("inspection");
        AddAction(testWorld.Interactive, alternative);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(
                testWorld.Interactive.ResolveActionForInput(testWorld.Interactor, InteractInput)
                    == alternative
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
        testWorld.Action.Automatic = true;
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
        automatic.Automatic = true;
        AddAction(testWorld.Interactive, automatic);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ResolveAutomaticAction(testWorld.Interactor) == automatic)
            .IsTrue();
        AssertThat(
                testWorld.Interactive.ResolveActionForInput(testWorld.Interactor, InteractInput)
                    == testWorld.Action
            )
            .IsTrue();
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
                StatefulPath = new NodePath("../StatefulComponent"),
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
                StatefulPath = new NodePath("../StatefulComponent"),
                ExpectedStates = States("closed"),
                MismatchAvailability = InteractionUnavailableKind.Blocked,
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
                StatefulPath = new NodePath("../StatefulComponent"),
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

        rule.StatefulPath = new NodePath("../MissingStateful");

        AssertThat(Describe(door.Interactive.EvaluateAvailability(door.Interactor, door.Open)))
            .IsEqual("Interaction is not configured.");

        rule.StatefulPath = new NodePath("../StatefulComponent");
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
                StatefulPath = new NodePath("../../LeverWall/StatefulComponent"),
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

        InteractionAction? first = door.Interactive.ResolveActionForInput(
            door.Interactor,
            InteractInput
        );
        InteractionExecutionResult openResult = door.Interactive.ExecuteAction(
            door.Interactor,
            first!
        );

        AssertThat(first == door.Open).IsTrue();
        AssertThat(openResult is InteractionExecutionCompleted).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("open");
        AssertThat(stateChanges).IsEqual(1);

        InteractionAction? second = door.Interactive.ResolveActionForInput(
            door.Interactor,
            InteractInput
        );
        InteractionExecutionResult closeResult = door.Interactive.ExecuteAction(
            door.Interactor,
            second!
        );

        AssertThat(second == door.Close).IsTrue();
        AssertThat(closeResult is InteractionExecutionCompleted).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("closed");
        AssertThat(stateChanges).IsEqual(2);
        AssertThat(door.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task SetStateExecutorFailsWhenNothingWouldChange()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        SetStateInteractionExecutor executor = new()
        {
            Stateful = door.State,
            TargetState = new StringName("closed"),
        };

        InteractionExecutionResult result = executor.Execute(
            new InteractionExecutionContext(1, door.Interactor, door.Interactive, door.Open)
        );

        AssertThat(result is InteractionExecutionFailed).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("closed");
        executor.Free();
    }

    [TestCase]
    public async Task SetStateExecutorFailsWithoutTargetOrOutsideTheSchema()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        SetStateInteractionExecutor orphan = new() { TargetState = new StringName("open") };
        door.State.Schema = new StateSchema { States = States("closed", "open") };
        SetStateInteractionExecutor undeclared = new()
        {
            Stateful = door.State,
            TargetState = new StringName("melted"),
        };
        InteractionExecutionContext context = new(1, door.Interactor, door.Interactive, door.Open);

        AssertThat(orphan.Execute(context) is InteractionExecutionFailed).IsTrue();
        AssertThat(undeclared.Execute(context) is InteractionExecutionFailed).IsTrue();
        AssertThat(door.State.State.ToString()).IsEqual("closed");
        orphan.Free();
        undeclared.Free();
    }

    [TestCase]
    public async Task TheInteractorReportsWhichInputsAreWorthSampling()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction inspect = CreateAction("inspect");
        inspect.Definition!.InputActionName = new StringName("inspect");
        inspect.ConcurrencyGroup = new StringName("inspection");
        InteractionAction pickup = CreateAction("pickup");
        pickup.Automatic = true;
        AddAction(testWorld.Interactive, inspect);
        AddAction(testWorld.Interactive, pickup);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        List<StringName> focused = new(testWorld.Interactor.GetRelevantInputs());

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
        List<StringName> sustained = new(testWorld.Interactor.GetRelevantInputs());

        AssertThat(sustained.Contains(InteractInput)).IsTrue();
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
        ExecutorOf(door.Open).Result = new InteractionExecutionRunning();
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
                    is InteractionHidden
            )
            .IsTrue();

        // Anybody else gets a different wording, because the two situations are not the same.
        AssertThat(Describe(door.Interactive.EvaluateAvailability(other, door.Open)))
            .IsEqual("Someone else is using this.");

        AssertThat(door.Interactive.CompleteExecution(executionId)).IsTrue();
        AssertThat(
                door.Interactive.EvaluateAvailability(door.Interactor, door.Open)
                    is InteractionAllowed
            )
            .IsTrue();
    }

    [TestCase]
    public async Task ANamedConcurrencyGroupLetsAnUnrelatedActionRunDuringALongOne()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.ConcurrencyGroup = new StringName("controls");
        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong controls
        );
        InteractionAction inspect = CreateAction("inspect");
        inspect.ConcurrencyGroup = new StringName("inspection");
        AddAction(testWorld.Interactive, inspect);
        ExecutorOf(inspect).Result = new InteractionExecutionRunning();

        InteractionExecutionResult result = testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            inspect,
            out ulong inspection
        );

        AssertThat(result is InteractionExecutionRunning).IsTrue();
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
        hack.ConcurrencyGroup = new StringName("controls");
        inspect.ConcurrencyGroup = new StringName("inspection");
        AddAction(testWorld.Interactive, hack);
        AddAction(testWorld.Interactive, inspect);
        ExecutorOf(hack).Result = new InteractionExecutionRunning();
        ExecutorOf(inspect).Result = new InteractionExecutionRunning();
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
    public async Task AnInstantActionIsNeverReportedAsEndingLater()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);

        door.Interactive.ExecuteAction(door.Interactor, door.Open);

        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(1);
        AssertThat(ExecutorOf(door.Open).CompletedCount).IsEqual(0);
        AssertThat(ExecutorOf(door.Open).CancelledCount).IsEqual(0);
    }

    [TestCase]
    public async Task AnAutomaticActionRetriesWhenItBecomesAllowedWithoutRefocusing()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Owner.GameplayBlocked = true;
        testWorld.Interactive.TargetRules.Add(new InteractiveParentGameplayRule());
        testWorld.Action.Automatic = true;
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
    public async Task TheTargetOwnsTheClockOfARunningActionAndCompletesItItself()
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
        AssertThat(testWorld.Interactive.TryGetExecutionProgress(executionId, out float started))
            .IsTrue();
        AssertThat(started < 1.0f).IsTrue();

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
    public async Task AnExecutorMayTakeOverTheClockItKnowsBetterThanTheScene()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        InteractionAction hack = CreateAction("hack");
        ExecutorOf(hack).Duration = 3600.0f;
        AddAction(testWorld.Interactive, hack);

        // The scene authored an hour; the executor knows the animation it just started is shorter.
        ExecutorOf(hack).Result = new InteractionExecutionRunning(0.05f);
        testWorld.Interactive.ExecuteAction(testWorld.Interactor, hack, out ulong executionId);

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsTrue();

        for (
            int frame = 0;
            frame < 300 && testWorld.Interactive.IsExecutionActive(executionId);
            frame++
        )
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsFalse();
        AssertThat(ExecutorOf(hack).CompletedCount).IsEqual(1);
    }

    [TestCase]
    public async Task AnExecutorWithoutDurationWaitsForAnExternalEvent()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactive.ExecuteAction(
            testWorld.Interactor,
            testWorld.Action,
            out ulong executionId
        );
        await testWorld.Runner.SimulateFrames(10);

        AssertThat(testWorld.Interactive.IsExecutionActive(executionId)).IsTrue();
        AssertThat(testWorld.Interactive.TryGetExecutionProgress(executionId, out float progress))
            .IsTrue();
        AssertThat(progress).IsEqual(0.0f);
        AssertThat(testWorld.Interactive.CompleteExecution(executionId)).IsTrue();
    }

    [TestCase]
    public async Task HoldingOneInputSelectsTheActionThatAsksForTheHold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.Definition!.HoldThreshold = 0.05f;
        AddAction(testWorld.Interactive, force);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        // Pressing only started the hold: nothing is selected while the threshold is not reached.
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(0);
        AssertThat(testWorld.Interactor.TryGetGestureProgress(out StringName held, out _)).IsTrue();
        AssertThat(held).IsEqual(InteractInput);

        for (int frame = 0; frame < 300 && ExecutorOf(force).ExecuteCount == 0; frame++)
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(1);
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(testWorld.Interactor.TryGetGestureProgress(out _, out _)).IsFalse();
    }

    [TestCase]
    public async Task ReleasingBeforeTheThresholdSelectsTheActionThatAsksForNoHold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.Definition!.HoldThreshold = 3600.0f;
        AddAction(testWorld.Interactive, force);
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
                testWorld.Interactor.TryGetExecutionProgress(
                    out StringName actionId,
                    out float progress
                )
            )
            .IsTrue();
        AssertThat(actionId).IsEqual(new StringName("activate"));
        AssertThat(progress > 0.0f).IsTrue();
        AssertThat(progress < 1.0f).IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Interactor.TryGetExecutionProgress(out _, out _)).IsFalse();
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
        force.Definition!.HoldThreshold = 3600.0f;
        InteractionAction pry = CreateAction("pry");
        pry.Definition!.HoldThreshold = 0.001f;
        AddAction(testWorld.Interactive, force);
        AddAction(testWorld.Interactive, pry);
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
        AssertThat(PresentedAction(presentation, "force").ExecutionProgress.HasValue).IsFalse();
    }

    [TestCase]
    public async Task ExecutionProgressIsCarriedByItsOwnActionAlone()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        AddAction(testWorld.Interactive, CreateAction("inspect"));
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        await testWorld.Runner.SimulateFrames(2);
        InteractionTargetPresentation presentation = testWorld.Interactive.GetPresentation(
            testWorld.Interactor,
            true
        );

        // Per action and not per target: a widget reads its own progress without filtering by
        // identifier, and the neighbour blocked by the same execution shows no bar at all.
        AssertThat(PresentedAction(presentation, "activate").ExecutionProgress.HasValue).IsTrue();
        AssertThat(PresentedAction(presentation, "activate").ExecutionProgress!.Value > 0.0f)
            .IsTrue();
        AssertThat(PresentedAction(presentation, "inspect").ExecutionProgress.HasValue).IsFalse();
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
        List<InteractionActionPresentation> primed = Presented(core);
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
        AssertThat(core.Activate.Executor is TransitionStateInteractionExecutor).IsTrue();
        AssertThat(core.Reactivate.Executor is TransitionStateInteractionExecutor).IsTrue();
    }

    private static async Task WaitUntilExecutionEnds(CoreWorld core, ulong executionId)
    {
        for (int frame = 0; frame < 300 && core.Interactive.IsExecutionActive(executionId); frame++)
        {
            await core.Runner.SimulateFrames(1);
        }

        AssertThat(core.Interactive.IsExecutionActive(executionId)).IsFalse();
    }

    private static List<InteractionActionPresentation> Presented(CoreWorld core) =>
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
        interactive.Actions.Add(activate);
        interactive.Actions.Add(reactivate);
        reactor.AddChild(area);
        reactor.AddChild(state);
        reactor.AddChild(interactive);
        interactive.AddChild(activate);
        interactive.AddChild(reactivate);

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(reactor);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);

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
        TransitionStateInteractionExecutor executor = new()
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

    private static string Describe(InteractionAvailability availability) =>
        availability switch
        {
            InteractionAllowed => "allowed",
            InteractionBlocked blocked => blocked.Reason,
            InteractionHidden => "hidden",
        };

    private static TestWorld BuildWorld(int ownerPeerId = 1)
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
        interactive.Actions.Add(action);
        owner.AddChild(area);
        owner.AddChild(stateful);
        owner.AddChild(interactive);
        interactive.AddChild(action);
        interactive.InteractionActionCancelled += owner.OnInteractionActionCancelled;
        stateful.StateChangedAuthority += owner.OnStateChangedAuthority;
        stateful.StateChangedPresentation += owner.OnStateChangedPresentation;
        owner.Interactive = interactive;
        owner.Stateful = stateful;

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor", OwnerPeerId = ownerPeerId };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
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
        Node3D viewOrigin
    )
    {
        TestInteractionDetector detector = new() { Name = "Detector", ViewOrigin = viewOrigin };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        return detector;
    }

    private static void AddAction(InteractiveComponent interactive, InteractionAction action)
    {
        interactive.AddChild(action);
        interactive.Actions.Add(action);
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
        action.Definition!.CancelOnInputReleased = true;
        TestActivationExecutor executor = new() { Name = $"{id}Executor", Actor = owner };
        action.AddChild(executor);
        action.Executor = executor;
        return action;
    }

    private static InteractionActionPresentation PresentedAction(
        in InteractionTargetPresentation presentation,
        string actionId
    )
    {
        foreach (InteractionActionPresentation action in presentation.Actions)
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

    private static InteractionAction NewAction(string id, InteractionRule[] rules)
    {
        InteractionAction action = new()
        {
            Name = $"{id}Action",
            Definition = new InteractionActionDefinition
            {
                Id = new StringName(id),
                Label = id,
                InputActionName = new StringName("interact"),
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

    private static void BindSetStateExecutor(
        InteractionAction action,
        StatefulComponent stateful,
        string targetState
    )
    {
        SetStateInteractionExecutor executor = new()
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
            StatefulPath = new NodePath("../StatefulComponent"),
            MismatchAvailability = InteractionUnavailableKind.Blocked,
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
            StatefulPath = new NodePath("../StatefulComponent"),
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
        interactive.Actions.Add(open);
        interactive.Actions.Add(close);
        door.AddChild(area);
        door.AddChild(state);
        door.AddChild(interactive);
        interactive.AddChild(open);
        interactive.AddChild(close);

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(door);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);

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

        public override InteractionAvailability Evaluate(in InteractionContext context) =>
            HasKey ? new InteractionAllowed() : new InteractionBlocked("You need the resonator.");
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

        public InteractionExecutionResult BeginActivation()
        {
            StartCount++;
            if (Stateful is null)
            {
                return new InteractionExecutionCompleted();
            }

            Stateful.SetState(ActivatingState);
            return new InteractionExecutionRunning();
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
        public TestInteractiveActor? Actor { get; set; }

        public float Duration { get; set; }

        public bool RequiresPresence { get; set; } = true;

        public override float ExpectedDuration => Duration;

        public override bool RequiresInteractorPresence => RequiresPresence;

        public override InteractionExecutionResult Execute(
            in InteractionExecutionContext context
        ) => Actor is null ? new InteractionExecutionFailed("No actor.") : Actor.BeginActivation();
    }

    private sealed partial class RecordingInteractionExecutor : InteractionActionExecutor
    {
        public InteractionExecutionResult Result { get; set; } =
            new InteractionExecutionCompleted();

        public float Duration { get; set; }

        public override float ExpectedDuration => Duration;

        public int ExecuteCount { get; private set; }

        public InteractionInteractor? LastInteractor { get; private set; }

        public InteractionAction? LastAction { get; private set; }

        public InteractionInteractor? ReservedInteractorDuringExecute { get; private set; }

        public ulong LastExecutionId { get; private set; }

        public int CompletedCount { get; private set; }

        public int CancelledCount { get; private set; }

        public string LastCancelReason { get; private set; } = string.Empty;

        public override InteractionExecutionResult Execute(in InteractionExecutionContext context)
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
            CompletedCount++;
            LastExecutionId = context.ExecutionId;
        }

        protected internal override void OnExecutionCancelled(
            in InteractionExecutionContext context,
            string reason
        )
        {
            CancelledCount++;
            LastExecutionId = context.ExecutionId;
            LastCancelReason = reason;
        }
    }

    private sealed partial class InteractiveParentGameplayRule : InteractionRule
    {
        public override InteractionAvailability Evaluate(in InteractionContext context)
        {
            return context.Interactive.GetParent() is TestInteractiveActor { GameplayBlocked: true }
                ? new InteractionBlocked("Gameplay condition is blocked.")
                : new InteractionAllowed();
        }
    }
}
