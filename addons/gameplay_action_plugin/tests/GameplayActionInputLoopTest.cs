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
public sealed partial class GameplayActionInputLoopTest
{
    [TestCase]
    public void RunnerReportsAllBoundInputsAndInputsStillConsumedByAReleasedBinding()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayAction action = CreateAction("heal");
        component.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        runner._Ready();
        Node source = AutoFree(new Node());

        runner.BindAction(component, "heal", source, Config("heal"));
        runner.BindAction(
            component,
            "heal",
            source,
            Config("charge", GameplayActionActivationMode.Hold, holdDuration: 1.0f)
        );

        AssertThat(new System.Collections.Generic.List<StringName>(runner.GetRelevantInputs()))
            .ContainsExactly(new[] { new StringName("heal"), new StringName("charge") });

        AssertThat(runner.TryStartActionInput("charge")).IsTrue();
        runner.UnbindSource(source);

        AssertThat(new System.Collections.Generic.List<StringName>(runner.GetRelevantInputs()))
            .ContainsExactly(new[] { new StringName("charge") });
    }

    [TestCase]
    public void OwnedInputActionRunsThroughTheRunnerWithoutAnInteraction()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor executor = new();
        InputGameplayAction action = AutoFree(
            new InputGameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "heal" },
                Executor = executor,
                DefaultBindingConfig = Config("heal"),
            }
        );
        action.AddChild(executor);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        runner._Ready();
        component.AddAction(action);

        AssertThat(runner.TryStartActionInput("heal")).IsTrue();
        AssertThat(executor.ExecuteCount).IsEqual(1);
    }

    private static GameplayAction CreateAction(string id)
    {
        TestGameplayActionExecutor executor = new();
        GameplayAction action = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = id },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        return action;
    }

    private static GameplayActionBindingConfig Config(
        string input,
        GameplayActionActivationMode mode = GameplayActionActivationMode.Press,
        float holdDuration = 0.0f
    ) =>
        new()
        {
            InputActionName = new StringName(input),
            ActivationMode = mode,
            HoldDuration = holdDuration,
        };
}
