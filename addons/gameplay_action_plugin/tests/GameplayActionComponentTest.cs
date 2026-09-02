namespace QuestWorld.Tests.GameplayActions;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Rules;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class GameplayActionComponentTest
{
    [TestCase]
    public void AvailabilityHasNoActionHookOutsideTheRulesCollection()
    {
        MethodInfo? hook = typeof(GameplayAction).GetMethod(
            "EvaluateAdditionalAvailability",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        AssertThat(hook is null).IsTrue();
    }

    [TestCase]
    public void DefinitionKeepsStableIdentityAndOptionalPresentationMetadata()
    {
        GameplayActionDefinition definition = new()
        {
            Id = new StringName("heal"),
            Label = "Heal",
            Description = "Restore health.",
        };

        AssertThat(definition.Id).IsEqual(new StringName("heal"));
        AssertThat(definition.Label).IsEqual("Heal");
        AssertThat(definition.Description).IsEqual("Restore health.");
    }

    [TestCase]
    public void ReadyRegistersOnlyExplicitAuthoredActions()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayAction authored = CreateAction("heal");
        GameplayAction unlistedChild = CreateAction("drop");
        component.AddChild(authored);
        component.AddChild(unlistedChild);
        component.Actions.Add(authored);

        component._Ready();

        AssertThat(component.ResolveAction(new StringName("heal")) == authored).IsTrue();
        AssertThat(component.ResolveAction(new StringName("drop")) is null).IsTrue();
    }

    [TestCase]
    public void RuntimeActionIsOwnedParentedAndResolvable()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayAction action = CreateAction("heal");

        AssertThat(component.AddAction(action)).IsTrue();

        AssertThat(action.GetParent() == component).IsTrue();
        AssertThat(action.Component == component).IsTrue();
        AssertThat(component.ResolveAction(new StringName("heal")) == action).IsTrue();
    }

    [TestCase]
    public void MissingEmptyDuplicateAndMultiHostActionsAreRejected()
    {
        GameplayActionComponent first = AutoFree(new GameplayActionComponent());
        GameplayActionComponent second = AutoFree(new GameplayActionComponent());
        GameplayAction missingDefinition = AutoFree(new GameplayAction());
        GameplayAction emptyId = CreateAction(string.Empty);
        GameplayAction missingExecutor = CreateAction("drop");
        missingExecutor.Executor = null;
        GameplayAction registered = CreateAction("heal");
        GameplayAction duplicate = CreateAction("heal");

        AssertThat(first.AddAction(missingDefinition)).IsFalse();
        AssertThat(first.AddAction(emptyId)).IsFalse();
        AssertThat(first.AddAction(missingExecutor)).IsFalse();
        AssertThat(first.AddAction(registered)).IsTrue();
        AssertThat(first.AddAction(duplicate)).IsFalse();
        AssertThat(second.AddAction(registered)).IsFalse();
        AssertThat(second.ResolveAction(new StringName("heal")) is null).IsTrue();
    }

    [TestCase]
    public void RulesRunInOrderAndStopAtFirstUnavailableResult()
    {
        List<string> calls = new();
        GameplayAction action = CreateAction("heal");
        action.Rules.Add(new RecordingRule("first", calls, new GameplayActionAllowed()));
        action.Rules.Add(
            new RecordingRule("second", calls, new GameplayActionBlocked("No charge."))
        );
        action.Rules.Add(new RecordingRule("third", calls, new GameplayActionHidden()));
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        component.AddAction(action);

        GameplayActionAvailability availability = component.EvaluateAction(new StringName("heal"));

        AssertThat(Describe(availability)).IsEqual("No charge.");
        AssertThat(calls).ContainsExactly(new[] { "first", "second" });
    }

    [TestCase]
    public void NullRuleEntriesAreIgnoredWithoutBreakingTheOrderedPipeline()
    {
        List<string> calls = new();
        GameplayAction action = CreateAction("heal");
        action.Rules.Add(new RecordingRule("first", calls, new GameplayActionAllowed()));
        action.Rules.Add(null!);
        action.Rules.Add(
            new RecordingRule("second", calls, new GameplayActionBlocked("No charge."))
        );
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        component.AddAction(action);

        GameplayActionAvailability availability = component.EvaluateAction(new StringName("heal"));

        AssertThat(Describe(availability)).IsEqual("No charge.");
        AssertThat(calls).ContainsExactly(new[] { "first", "second" });
    }

    [TestCase]
    public void ProgrammaticExecutionKeepsRulesAndInvokesOneExecutorAfterReservation()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor executor = new();
        GameplayAction action = CreateAction("heal", executor);
        action.Rules.Add(
            new RecordingRule("allowed", new List<string>(), new GameplayActionAllowed())
        );
        component.AddAction(action);

        GameplayActionExecutionResult result = component.ExecuteProgrammatic(
            new StringName("heal"),
            out ulong executionId
        );

        AssertThat(result is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(executionId).IsGreater(0UL);
        AssertThat(executor.ExecuteCount).IsEqual(1);
        AssertThat(executor.WasReservedWhenExecuted).IsTrue();
        AssertThat(executor.CompletedCount).IsEqual(1);
        AssertThat(component.IsActionExecuting(new StringName("heal"))).IsFalse();
    }

    [TestCase]
    public void ProgrammaticExecutionStopsAtRulesBeforeAllocatingOrInvoking()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor executor = new();
        GameplayAction action = CreateAction("heal", executor);
        action.Rules.Add(
            new RecordingRule(
                "blocked",
                new List<string>(),
                new GameplayActionBlocked("No health missing.")
            )
        );
        component.AddAction(action);

        GameplayActionExecutionResult result = component.ExecuteProgrammatic(
            new StringName("heal"),
            out ulong executionId
        );

        AssertThat(result is GameplayActionExecutionRejected).IsTrue();
        string reason = result switch
        {
            GameplayActionExecutionRejected rejected => rejected.Reason,
            _ => string.Empty,
        };
        AssertThat(reason).IsEqual("No health missing.");
        AssertThat(executionId).IsEqual(0UL);
        AssertThat(executor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public void ExecutorExceptionBecomesFailedAndReleasesItsReservation()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor executor = new()
        {
            ExceptionToThrow = new InvalidOperationException("Broken executor."),
        };
        component.AddAction(CreateAction("heal", executor));

        GameplayActionExecutionResult result = component.ExecuteProgrammatic(
            new StringName("heal"),
            out ulong executionId
        );

        AssertThat(result is GameplayActionExecutionFailed).IsTrue();
        AssertThat(executionId).IsGreater(0UL);
        AssertThat(component.IsActionExecuting(new StringName("heal"))).IsFalse();
        AssertThat(executor.FailedCount).IsEqual(1);
    }

    [TestCase]
    public void OneActionIdCannotExecuteTwiceWhileRunning()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayAction action = CreateRunningAction("heal");
        component.AddAction(action);

        GameplayActionExecutionResult first = component.ExecuteProgrammatic(
            new StringName("heal"),
            out ulong firstId
        );
        GameplayActionExecutionResult second = component.ExecuteProgrammatic(
            new StringName("heal"),
            out ulong secondId
        );

        AssertThat(first is GameplayActionExecutionRunning).IsTrue();
        AssertThat(firstId).IsGreater(0UL);
        AssertThat(second is GameplayActionExecutionRejected).IsTrue();
        AssertThat(secondId).IsEqual(0UL);
    }

    [TestCase]
    public void ConcurrencyGroupsConflictOnlyInsideOneHost()
    {
        GameplayActionComponent firstHost = AutoFree(new GameplayActionComponent());
        GameplayActionComponent secondHost = AutoFree(new GameplayActionComponent());
        firstHost.AddAction(CreateRunningAction("heal", "actor"));
        firstHost.AddAction(CreateRunningAction("drop", "actor"));
        secondHost.AddAction(CreateRunningAction("heal", "actor"));

        GameplayActionExecutionResult first = firstHost.ExecuteProgrammatic(
            new StringName("heal"),
            out _
        );
        GameplayActionExecutionResult sameHost = firstHost.ExecuteProgrammatic(
            new StringName("drop"),
            out _
        );
        GameplayActionExecutionResult otherHost = secondHost.ExecuteProgrammatic(
            new StringName("heal"),
            out _
        );

        AssertThat(first is GameplayActionExecutionRunning).IsTrue();
        AssertThat(sameHost is GameplayActionExecutionRejected).IsTrue();
        AssertThat(otherHost is GameplayActionExecutionRunning).IsTrue();
    }

    [TestCase]
    public void DifferentGroupsOnOneHostCanExecuteTogether()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        component.AddAction(CreateRunningAction("heal", "body"));
        component.AddAction(CreateRunningAction("inspect", "inspection"));

        GameplayActionExecutionResult first = component.ExecuteProgrammatic(
            new StringName("heal"),
            out _
        );
        GameplayActionExecutionResult second = component.ExecuteProgrammatic(
            new StringName("inspect"),
            out _
        );

        AssertThat(first is GameplayActionExecutionRunning).IsTrue();
        AssertThat(second is GameplayActionExecutionRunning).IsTrue();
    }

    [TestCase]
    public async Task RemovingRunningActionRetiresItUntilItsExecutionEnds()
    {
        Node root = new();
        GameplayActionComponent component = new() { Name = "Actions" };
        GameplayAction action = CreateRunningAction("heal");
        root.AddChild(component);
        component.AddAction(action);
        ISceneRunner runner = ISceneRunner.Load(root);
        await runner.SimulateFrames(1);
        component.ExecuteProgrammatic(new StringName("heal"), out ulong executionId);

        AssertThat(component.RemoveAction(new StringName("heal"))).IsTrue();
        AssertThat(component.ResolveAction(new StringName("heal")) is null).IsTrue();
        AssertThat(action.Component == component).IsTrue();
        AssertThat(action.IsQueuedForDeletion()).IsFalse();

        AssertThat(component.CompleteExecution(executionId)).IsTrue();
        AssertThat(action.IsQueuedForDeletion()).IsTrue();
    }

    [TestCase]
    public void RetiringActionKeepsItsIdReservedUntilItsExecutionEnds()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        component.AddAction(CreateRunningAction("heal"));
        component.ExecuteProgrammatic(new StringName("heal"), out ulong executionId);
        component.RemoveAction(new StringName("heal"));

        AssertThat(component.AddAction(CreateAction("heal"))).IsFalse();

        component.CompleteExecution(executionId);

        AssertThat(component.AddAction(CreateAction("heal"))).IsTrue();
    }

    private static GameplayAction CreateAction(
        string id,
        TestGameplayActionExecutor? executor = null
    )
    {
        GameplayAction action = new()
        {
            Name = $"{id}Action",
            Definition = new GameplayActionDefinition { Id = new StringName(id), Label = id },
        };
        executor ??= new TestGameplayActionExecutor();
        action.AddChild(executor);
        action.Executor = executor;
        return AutoFree(action);
    }

    private static GameplayAction CreateRunningAction(string id, string group = "default")
    {
        TestGameplayActionExecutor executor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction action = CreateAction(id, executor);
        action.HostConcurrencyGroup = new StringName(group);
        return action;
    }

    private static string Describe(GameplayActionAvailability availability) =>
        availability switch
        {
            GameplayActionAllowed => "allowed",
            GameplayActionBlocked blocked => blocked.Reason,
            GameplayActionHidden => "hidden",
        };

    private sealed partial class RecordingRule(
        string name,
        List<string> calls,
        GameplayActionAvailability result
    ) : GameplayActionRule
    {
        public override GameplayActionAvailability Evaluate(in GameplayActionContext context)
        {
            calls.Add(name);
            return result;
        }
    }
}
