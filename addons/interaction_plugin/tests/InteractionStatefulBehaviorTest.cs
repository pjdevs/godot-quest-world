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
public sealed partial class InteractionStatefulBehaviorTest : InteractionTestBase
{
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
}
