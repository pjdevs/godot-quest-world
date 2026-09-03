namespace QuestWorld.Tests.GameplayActions;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Rules;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class GameplayActionExecutionTest
{
    [TestCase]
    public void ReplicatedExecutionCodecExposesTypedEntries()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());

        Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> entries =
            component.BuildReplicatedExecutionEntries();

        AssertThat(entries).IsEmpty();
    }

    [TestCase]
    public void MalformedTypedSnapshotDoesNotConsumeItsRevision()
    {
        GameplayActionComponent authority = CreateRunningComponent("replicated");
        authority.ResolveAction("replicated")!.ExecutionVisibility =
            GameplayActionExecutionVisibility.Replicated;
        GameplayActionExecutionSynchronizer source = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = authority }
        );
        authority.ExecuteAction("replicated", out _);
        Godot.Collections.Dictionary valid = source.CaptureSnapshot();
        GameplayActionComponent receiver = CreateRunningComponent("replicated");
        GameplayActionExecutionSynchronizer destination = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = receiver }
        );
        Godot.Collections.Dictionary malformed = new()
        {
            ["revision"] = valid["revision"],
            ["entries"] = new Godot.Collections.Array { 42 },
        };

        AssertThat(destination.ApplySnapshot(malformed)).IsFalse();
        AssertThat(destination.ApplySnapshot(valid)).IsTrue();
    }

    [TestCase]
    public void CompletedExecutionNotifiesItsOwnerThenEmitsStartedAndCompletedAfterRelease()
    {
        List<string> calls = new();
        RecordingExecutor executor = new(calls) { Result = new GameplayActionExecutionCompleted() };
        GameplayActionComponent component = CreateComponentWithAction("heal", executor);
        Node instigator = AutoFree(new Node());
        List<long> observedIds = new();
        component.GameplayActionStarted += (
            executionId,
            action,
            receivedInstigator,
            receivedRequester
        ) =>
        {
            AssertThat(component.IsExecutionActive((ulong)executionId)).IsFalse();
            AssertThat(action == component.ResolveAction(new StringName("heal"))).IsTrue();
            AssertThat(receivedInstigator == instigator).IsTrue();

            // Nobody requested this execution, so nobody is waiting to be acknowledged for it.
            AssertThat(receivedRequester == null).IsTrue();
            observedIds.Add(executionId);
            calls.Add("started");
        };
        component.GameplayActionCompleted += (executionId, _, _, _) =>
        {
            observedIds.Add(executionId);
            calls.Add("completed");
        };

        GameplayActionExecutionResult result = component.ExecuteAction(
            new StringName("heal"),
            out ulong executionId,
            instigator
        );

        AssertThat(result is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(calls)
            .ContainsExactly(new[] { "execute", "owner-completed", "started", "completed" });
        AssertThat(observedIds)
            .ContainsExactly(new[] { checked((long)executionId), checked((long)executionId) });
    }

    [TestCase]
    public void RunningExecutionEmitsOneTerminalOutcomeAndIgnoresStaleTerminalCalls()
    {
        List<string> calls = new();
        RecordingExecutor executor = new(calls) { Result = new GameplayActionExecutionRunning() };
        GameplayActionComponent component = CreateComponentWithAction("repair", executor);
        component.GameplayActionStarted += (_, _, _, _) => calls.Add("started");
        component.GameplayActionCancelled += (_, _, _, _, reason) =>
            calls.Add($"cancelled:{reason}");
        component.GameplayActionCompleted += (_, _, _, _) => calls.Add("completed");
        component.GameplayActionFailed += (_, _, _, _, reason) => calls.Add($"failed:{reason}");

        component.ExecuteAction(new StringName("repair"), out ulong executionId);

        AssertThat(component.CancelExecution(executionId, "Interrupted.")).IsTrue();
        AssertThat(component.CompleteExecution(executionId)).IsFalse();
        AssertThat(component.FailExecution(executionId, "Too late.")).IsFalse();
        AssertThat(calls)
            .ContainsExactly(
                new[]
                {
                    "execute",
                    "started",
                    "owner-cancelled:Interrupted.",
                    "cancelled:Interrupted.",
                }
            );
    }

    [TestCase]
    public void FailedAndRejectedResultsKeepTheirDistinctLifecycle()
    {
        List<string> failedCalls = new();
        RecordingExecutor failedExecutor = new(failedCalls)
        {
            Result = new GameplayActionExecutionFailed("Jammed."),
        };
        GameplayActionComponent failed = CreateComponentWithAction("repair", failedExecutor);
        failed.GameplayActionStarted += (_, _, _, _) => failedCalls.Add("started");
        failed.GameplayActionFailed += (_, _, _, _, reason) => failedCalls.Add($"failed:{reason}");

        List<string> rejectedCalls = new();
        RecordingExecutor rejectedExecutor = new(rejectedCalls)
        {
            Result = new GameplayActionExecutionRejected("No charge."),
        };
        GameplayActionComponent rejected = CreateComponentWithAction("repair", rejectedExecutor);
        List<long> rejectedIds = new();
        rejected.GameplayActionStarted += (_, _, _, _) => rejectedCalls.Add("started");
        rejected.GameplayActionRejected += (executionId, _, _, _, reason) =>
        {
            rejectedIds.Add(executionId);
            rejectedCalls.Add($"rejected:{reason}");
        };

        failed.ExecuteAction(new StringName("repair"), out _);
        rejected.ExecuteAction(new StringName("repair"), out ulong rejectedExecutionId);

        AssertThat(failedCalls)
            .ContainsExactly(
                new[] { "execute", "owner-failed:Jammed.", "started", "failed:Jammed." }
            );
        AssertThat(rejectedCalls).ContainsExactly(new[] { "execute", "rejected:No charge." });
        AssertThat(rejectedIds).ContainsExactly(new[] { checked((long)rejectedExecutionId) });
    }

    [TestCase]
    public void RuleRefusalEmitsRejectedWithTheZeroExecutionSentinel()
    {
        List<string> calls = new();
        RecordingExecutor executor = new(calls);
        GameplayActionComponent component = CreateComponentWithAction("heal", executor);
        component
            .ResolveAction(new StringName("heal"))!
            .Rules.Add(new FixedRule(new GameplayActionBlocked("Already healthy.")));
        long observedId = -1;
        component.GameplayActionRejected += (executionId, _, _, _, reason) =>
        {
            observedId = executionId;
            calls.Add($"rejected:{reason}");
        };

        GameplayActionExecutionResult result = component.ExecuteAction(
            new StringName("heal"),
            out ulong executionId
        );

        AssertThat(result is GameplayActionExecutionRejected).IsTrue();
        AssertThat(executionId).IsEqual(0UL);
        AssertThat(observedId).IsEqual(0L);
        AssertThat(calls).ContainsExactly(new[] { "rejected:Already healthy." });
    }

    [TestCase]
    public void RunningExecutionOwnsAVisibleReadModelUntilItsTerminalOutcome()
    {
        GameplayActionComponent component = CreateComponentWithAction(
            "repair",
            new RecordingExecutor(new List<string>())
            {
                Result = new GameplayActionExecutionRunning(),
            }
        );
        List<StringName> changed = new();
        component.ExecutionPresentationChanged += actionId => changed.Add(actionId);

        component.ExecuteAction(new StringName("repair"), out ulong executionId);

        AssertThat(
                component.TryGetExecutionPresentation(
                    new StringName("repair"),
                    out GameplayActionExecutionPresentation running
                )
            )
            .IsTrue();
        AssertThat(running.ExecutionId).IsEqual(executionId);
        AssertThat(running.ActionId).IsEqual(new StringName("repair"));
        AssertThat(running.Progress.HasValue).IsFalse();
        AssertThat(changed).ContainsExactly(new[] { new StringName("repair") });

        AssertThat(component.CompleteExecution(executionId)).IsTrue();
        AssertThat(component.TryGetExecutionPresentation(new StringName("repair"), out _))
            .IsFalse();
        AssertThat(changed)
            .ContainsExactly(new[] { new StringName("repair"), new StringName("repair") });
    }

    [TestCase]
    public void RunningExecutionPublishesClampedDiscreteAndCallableProgress()
    {
        GameplayActionComponent component = CreateRunningComponent("repair");
        component.ExecuteAction(new StringName("repair"), out ulong executionId);

        AssertThat(component.ReportExecutionProgress(executionId, -2.0f)).IsTrue();
        AssertProgress(component, "repair", 0.0f);
        AssertThat(component.ReportExecutionProgress(executionId, 0.66f)).IsTrue();
        AssertProgress(component, "repair", 0.66f);
        AssertThat(component.SetExecutionProgressSource(executionId, Callable.From(() => 0.42f)))
            .IsTrue();
        AssertProgress(component, "repair", 0.42f);
        AssertThat(component.ClearExecutionProgressSource(executionId)).IsTrue();
        AssertProgress(component, "repair", 0.66f);
        AssertThat(component.ReportExecutionProgress(executionId, null)).IsTrue();
        AssertThat(
                component.TryGetExecutionPresentation(
                    new StringName("repair"),
                    out GameplayActionExecutionPresentation cleared
                )
            )
            .IsTrue();
        AssertThat(cleared.Progress.HasValue).IsFalse();
        AssertThat(component.ReportExecutionProgress(executionId, float.NaN)).IsFalse();

        component.CompleteExecution(executionId);

        AssertThat(component.ReportExecutionProgress(executionId, 1.0f)).IsFalse();
    }

    [TestCase]
    public void SynchronizerHidesItsTransportPropertyButKeepsItsComponentReferenceEditable()
    {
        GameplayActionExecutionSynchronizer synchronizer = AutoFree(
            new GameplayActionExecutionSynchronizer()
        );

        PropertyUsageFlags componentUsage = PropertyUsageOf(synchronizer, "Component");
        PropertyUsageFlags snapshotUsage = PropertyUsageOf(synchronizer, "ReplicatedSnapshot");

        AssertThat(componentUsage.HasFlag(PropertyUsageFlags.Editor)).IsTrue();
        AssertThat(snapshotUsage.HasFlag(PropertyUsageFlags.Storage)).IsTrue();
        AssertThat(snapshotUsage.HasFlag(PropertyUsageFlags.Editor)).IsFalse();
    }

    [TestCase]
    public void ReplicatedSnapshotFiltersVisibilityRejectsStaleStateAndRemovesAbsence()
    {
        GameplayActionComponent authority = AutoFree(new GameplayActionComponent());
        AddRunningAction(
            authority,
            "replicated",
            "replicated",
            GameplayActionExecutionVisibility.Replicated
        );
        AddRunningAction(
            authority,
            "requester",
            "requester",
            GameplayActionExecutionVisibility.RequesterOnly
        );
        AddRunningAction(
            authority,
            "authority",
            "authority",
            GameplayActionExecutionVisibility.AuthorityOnly
        );
        authority.ExecuteAction(new StringName("replicated"), out ulong executionId);
        authority.ExecuteAction(new StringName("requester"), out _);
        authority.ExecuteAction(new StringName("authority"), out _);

        GameplayActionComponent receiver = AutoFree(new GameplayActionComponent());
        AddRunningAction(
            receiver,
            "replicated",
            "replicated",
            GameplayActionExecutionVisibility.Replicated
        );
        AddRunningAction(
            receiver,
            "requester",
            "requester",
            GameplayActionExecutionVisibility.RequesterOnly
        );
        AddRunningAction(
            receiver,
            "authority",
            "authority",
            GameplayActionExecutionVisibility.AuthorityOnly
        );
        GameplayActionExecutionSynchronizer source = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = authority }
        );
        GameplayActionExecutionSynchronizer destination = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = receiver }
        );

        Godot.Collections.Dictionary started = source.CaptureSnapshot();
        AssertThat(destination.ApplySnapshot(started)).IsTrue();
        AssertThat(
                receiver.TryGetExecutionPresentation(
                    new StringName("replicated"),
                    out GameplayActionExecutionPresentation initial
                )
            )
            .IsTrue();
        AssertThat(initial.ExecutionId).IsEqual(executionId);
        AssertThat(receiver.TryGetExecutionPresentation(new StringName("requester"), out _))
            .IsFalse();
        AssertThat(receiver.TryGetExecutionPresentation(new StringName("authority"), out _))
            .IsFalse();

        authority.ReportExecutionProgress(executionId, 0.66f);
        Godot.Collections.Dictionary progressed = source.CaptureSnapshot();
        AssertThat(destination.ApplySnapshot(progressed)).IsTrue();
        AssertThat(destination.ApplySnapshot(started)).IsFalse();
        AssertProgress(receiver, "replicated", 0.66f);

        GameplayActionComponent sampleReceiver = AutoFree(new GameplayActionComponent());
        AddRunningAction(
            sampleReceiver,
            "replicated",
            "replicated",
            GameplayActionExecutionVisibility.Replicated
        );
        GameplayActionExecutionSynchronizer sampleDestination = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = sampleReceiver }
        );
        AssertThat(sampleDestination.ApplySnapshot(progressed)).IsTrue();
        started["revision"] = 99L;
        AssertThat(sampleDestination.ApplySnapshot(started)).IsTrue();
        AssertProgress(sampleReceiver, "replicated", 0.66f);

        authority.CompleteExecution(executionId);
        AssertThat(destination.ApplySnapshot(source.CaptureSnapshot())).IsTrue();
        AssertThat(receiver.TryGetExecutionPresentation(new StringName("replicated"), out _))
            .IsFalse();
    }

    [TestCase]
    public void RemovingAReplicatedActionImmediatelyPurgesItsLocalPresentation()
    {
        GameplayActionComponent authority = AutoFree(new GameplayActionComponent());
        AddRunningAction(
            authority,
            "repair",
            "repair",
            GameplayActionExecutionVisibility.Replicated
        );
        authority.ExecuteAction(new StringName("repair"), out _);
        GameplayActionExecutionSynchronizer source = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = authority }
        );

        GameplayActionComponent receiver = AutoFree(new GameplayActionComponent());
        AddRunningAction(
            receiver,
            "repair",
            "repair",
            GameplayActionExecutionVisibility.Replicated
        );
        GameplayActionExecutionSynchronizer destination = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = receiver }
        );
        destination.ApplySnapshot(source.CaptureSnapshot());
        AssertThat(receiver.TryGetExecutionPresentation(new StringName("repair"), out _)).IsTrue();

        AssertThat(receiver.RemoveAction(new StringName("repair"))).IsTrue();

        AssertThat(receiver.TryGetExecutionPresentation(new StringName("repair"), out _)).IsFalse();
    }

    [TestCase]
    public void RetiringActiveActionRemainsPresentableUntilItsTerminalOutcome()
    {
        GameplayActionComponent component = CreateRunningComponent("repair");
        component.ExecuteAction(new StringName("repair"), out ulong executionId);

        AssertThat(component.RemoveAction(new StringName("repair"))).IsTrue();

        AssertThat(component.GetExecutionPresentations().Count).IsEqual(1);
        AssertThat(component.GetExecutionPresentations()[0].ExecutionId).IsEqual(executionId);

        AssertThat(component.CompleteExecution(executionId)).IsTrue();
        AssertThat(component.GetExecutionPresentations()).IsEmpty();
    }

    [TestCase]
    public async Task TimedExecutorUsesMonotonicTimeAndCompletesWhileItsComponentIsDisabled()
    {
        Node root = new();
        GameplayActionComponent component = new() { Name = "Actions" };
        TestTimedExecutor executor = new() { Duration = 0.05f };
        root.AddChild(component);
        AddAction(component, "charge", executor);
        ISceneRunner runner = ISceneRunner.Load(root);
        await runner.SimulateFrames(1);

        GameplayActionExecutionResult result = component.ExecuteAction(
            new StringName("charge"),
            out ulong executionId
        );
        component.ProcessMode = Node.ProcessModeEnum.Disabled;

        AssertThat(result is GameplayActionExecutionRunning).IsTrue();
        AssertThat(component.IsExecutionActive(executionId)).IsTrue();
        AssertThat(
                component.TryGetExecutionPresentation(
                    new StringName("charge"),
                    out GameplayActionExecutionPresentation started
                )
            )
            .IsTrue();
        AssertThat(started.Progress.HasValue).IsTrue();
        AssertThat(started.Progress!.Value < 1.0f).IsTrue();

        for (int frame = 0; frame < 300 && component.IsExecutionActive(executionId); frame++)
        {
            await runner.SimulateFrames(1);
        }

        AssertThat(component.IsExecutionActive(executionId)).IsFalse();
        AssertThat(executor.CompletedCount).IsEqual(1);
        AssertThat(executor.TimerIsActive).IsFalse();
    }

    [TestCase]
    public async Task ComposableTimerRejectsInvalidDurationsWithoutStealingTheExecution()
    {
        Node root = new();
        GameplayActionComponent component = new() { Name = "Actions" };
        RecordingExecutor executor = new(new List<string>())
        {
            Result = new GameplayActionExecutionRunning(),
        };
        root.AddChild(component);
        AddAction(component, "charge", executor);
        ISceneRunner runner = ISceneRunner.Load(root);
        await runner.SimulateFrames(1);
        component.ExecuteAction(new StringName("charge"), out ulong executionId);
        TimedExecution timer = new();

        AssertThat(timer.Start(component, executionId, 0.0f))
            .IsEqual(TimedExecutionStartResult.InvalidDuration);
        AssertThat(timer.Start(component, executionId, float.NaN))
            .IsEqual(TimedExecutionStartResult.InvalidDuration);
        AssertThat(timer.IsActive).IsFalse();
        AssertThat(component.IsExecutionActive(executionId)).IsTrue();

        component.CancelExecution(executionId);
        timer.Dispose();
    }

    private static GameplayActionComponent CreateRunningComponent(string id)
    {
        return CreateComponentWithAction(
            id,
            new RecordingExecutor(new List<string>())
            {
                Result = new GameplayActionExecutionRunning(),
            }
        );
    }

    private static GameplayActionComponent CreateComponentWithAction(
        string id,
        GameplayActionExecutor executor
    )
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        AddAction(component, id, executor);
        return component;
    }

    private static GameplayAction AddRunningAction(
        GameplayActionComponent component,
        string id,
        string group,
        GameplayActionExecutionVisibility visibility
    )
    {
        GameplayAction action = AddAction(
            component,
            id,
            new RecordingExecutor(new List<string>())
            {
                Result = new GameplayActionExecutionRunning(),
            }
        );
        action.HostConcurrencyGroup = new StringName(group);
        action.ExecutionVisibility = visibility;
        return action;
    }

    private static GameplayAction AddAction(
        GameplayActionComponent component,
        string id,
        GameplayActionExecutor executor
    )
    {
        GameplayAction action = new()
        {
            Name = $"{id}Action",
            Definition = new GameplayActionDefinition { Id = new StringName(id), Label = id },
        };
        action.AddChild(executor);
        action.Executor = executor;
        AssertThat(component.AddAction(action)).IsTrue();
        return action;
    }

    private static void AssertProgress(
        GameplayActionComponent component,
        string actionId,
        float expected
    )
    {
        AssertThat(
                component.TryGetExecutionPresentation(
                    new StringName(actionId),
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.Progress.HasValue).IsTrue();
        AssertThat(presentation.Progress!.Value).IsEqualApprox(expected, 0.001f);
    }

    private static PropertyUsageFlags PropertyUsageOf(GodotObject owner, string propertyName)
    {
        foreach (Godot.Collections.Dictionary property in owner.GetPropertyList())
        {
            if (property["name"].AsString() == propertyName)
            {
                return property["usage"].As<PropertyUsageFlags>();
            }
        }

        return PropertyUsageFlags.None;
    }

    private sealed partial class FixedRule(GameplayActionAvailability result) : GameplayActionRule
    {
        public override GameplayActionAvailability Evaluate(in GameplayActionContext context) =>
            result;
    }

    private sealed partial class RecordingExecutor(List<string> calls) : GameplayActionExecutor
    {
        public GameplayActionExecutionResult Result { get; set; } =
            new GameplayActionExecutionCompleted();

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            calls.Add("execute");
            return Result;
        }

        protected internal override void OnExecutionCompleted(in GameplayActionContext context) =>
            calls.Add("owner-completed");

        protected internal override void OnExecutionCancelled(
            in GameplayActionContext context,
            string reason
        ) => calls.Add($"owner-cancelled:{reason}");

        protected internal override void OnExecutionFailed(
            in GameplayActionContext context,
            string reason
        ) => calls.Add($"owner-failed:{reason}");
    }

    private sealed partial class TestTimedExecutor : TimedGameplayActionExecutor
    {
        public int CompletedCount { get; private set; }

        public bool TimerIsActive => IsTimerActive;

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context) =>
            RunningTimed(context);

        protected internal override void OnExecutionCompleted(in GameplayActionContext context)
        {
            CompletedCount++;
            base.OnExecutionCompleted(context);
        }
    }
}
