namespace QuestWorld.Tests.GameplayActions;

using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Runner;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class GameplayActionInputLifecycleTest
{
    [TestCase]
    public void ActionComponentReportsAddedAndRemovedAfterItsMutation()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        InputGameplayAction action = CreateInputAction("heal", GameplayActionActivationMode.Press);
        bool addedAfterMutation = false;
        bool removedAfterMutation = false;
        component.GameplayActionAdded += added =>
        {
            addedAfterMutation =
                added == action
                && component.ResolveAction("heal") == action
                && component.Actions.Contains(action);
        };
        component.GameplayActionRemoved += removed =>
        {
            removedAfterMutation =
                removed == action
                && component.ResolveAction("heal") is null
                && !component.Actions.Contains(action);
        };

        AssertThat(component.AddAction(action)).IsTrue();
        AssertThat(component.RemoveAction("heal")).IsTrue();
        AssertThat(addedAfterMutation).IsTrue();
        AssertThat(removedAfterMutation).IsTrue();
    }

    [TestCase]
    public void RunnerScansOwnedInputActionsThatAlreadyExistAtReady()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        InputGameplayAction action = CreateInputAction("heal", GameplayActionActivationMode.Press);
        component.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );

        runner._Ready();

        AssertThat(runner.GetBindings()).HasSize(1);
        AssertThat(runner.GetBindings()[0].Source == action).IsTrue();
    }

    [TestCase]
    public void RunnerBindsAndUnbindsInputActionsAddedToItsOwnedComponent()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        runner._Ready();
        InputGameplayAction action = CreateInputAction("heal", GameplayActionActivationMode.Press);

        AssertThat(component.AddAction(action)).IsTrue();
        AssertThat(runner.GetBindings()).HasSize(1);
        AssertThat(runner.GetBindings()[0].Source == action).IsTrue();

        AssertThat(component.RemoveAction("heal")).IsTrue();
        AssertThat(runner.GetBindings()).HasSize(0);
    }

    [TestCase]
    public void RunnerDoesNotObserveInputActionsFromAnotherComponent()
    {
        GameplayActionComponent owned = AutoFree(new GameplayActionComponent());
        GameplayActionComponent external = AutoFree(new GameplayActionComponent());
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = owned }
        );
        runner._Ready();

        AssertThat(
                external.AddAction(CreateInputAction("heal", GameplayActionActivationMode.Press))
            )
            .IsTrue();

        AssertThat(runner.GetBindings()).HasSize(0);
    }

    [TestCase]
    public void RefusedActionAdditionDoesNotCreateAnInputBinding()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        runner._Ready();
        InputGameplayAction invalid = AutoFree(
            new InputGameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "heal" },
                DefaultBindingConfig = new GameplayActionBindingConfig
                {
                    InputActionName = "heal",
                    ActivationMode = GameplayActionActivationMode.Press,
                },
            }
        );

        AssertThat(component.AddAction(invalid)).IsFalse();
        AssertThat(runner.GetBindings()).HasSize(0);
    }

    [TestCase]
    public void AutomaticOwnedActionRunsOnItsLocalRunnerWhenAdded()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        runner._Ready();
        InputGameplayAction action = CreateInputAction(
            "refresh",
            GameplayActionActivationMode.Automatic
        );
        TestGameplayActionExecutor executor = (TestGameplayActionExecutor)action.Executor!;

        AssertThat(component.AddAction(action)).IsTrue();
        AssertThat(executor.ExecuteCount).IsEqual(1);
    }

    private static InputGameplayAction CreateInputAction(
        string id,
        GameplayActionActivationMode activationMode
    )
    {
        TestGameplayActionExecutor executor = new();
        InputGameplayAction action = AutoFree(
            new InputGameplayAction
            {
                Definition = new GameplayActionDefinition { Id = id },
                Executor = executor,
                DefaultBindingConfig = new GameplayActionBindingConfig
                {
                    InputActionName =
                        activationMode == GameplayActionActivationMode.Automatic
                            ? new()
                            : new StringName(id),
                    ActivationMode = activationMode,
                },
            }
        );
        action.AddChild(executor);
        return action;
    }
}
