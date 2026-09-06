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
public sealed partial class InteractionExecutionBehaviorTest : InteractionTestBase
{
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
}
