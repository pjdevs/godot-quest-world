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
public sealed partial class InteractionTimedExecutionTest : InteractionTestBase
{
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
}
