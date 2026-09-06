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
public sealed partial class GameplayActionInputTest
{
    [TestCase]
    public void GameplayActionDoesNotExposeInputConfiguration()
    {
        AssertThat(typeof(GameplayAction).GetProperty("DefaultBindingConfig") is null).IsTrue();
    }

    [TestCase]
    public void InputGameplayActionMayOmitItsDefaultBindingConfiguration()
    {
        InputGameplayAction action = AutoFree(new InputGameplayAction());

        AssertThat(action.DefaultBindingConfig is null).IsTrue();
    }

    [TestCase]
    public void BindingSnapshotsTheDefaultBindingConfigurationValues()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor executor = new();
        InputGameplayAction action = AutoFree(
            new InputGameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "heal" },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        component.AddAction(action);

        GameplayActionBindingConfig config = new()
        {
            InputActionName = "heal",
            ActivationMode = GameplayActionActivationMode.Hold,
            HoldDuration = 0.5f,
            InputRequirement = GameplayActionInputRequirement.Pressed,
            Priority = 12,
        };
        action.DefaultBindingConfig = config;
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );

        GameplayActionBinding? binding = runner.BindAction(component, "heal", action, config);

        config.InputActionName = "changed";
        config.HoldDuration = 2.0f;
        config.InputRequirement = GameplayActionInputRequirement.None;
        config.Priority = 1;

        AssertThat(binding is not null).IsTrue();
        AssertThat(binding!.InputActionName).IsEqual(new StringName("heal"));
        AssertThat(binding.HoldDuration).IsEqual(0.5f);
        AssertThat(binding.InputRequirement).IsEqual(GameplayActionInputRequirement.Pressed);
        AssertThat(binding.Priority).IsEqual(12);
    }
}
