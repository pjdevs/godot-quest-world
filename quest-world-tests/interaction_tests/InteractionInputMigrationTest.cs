namespace QuestWorld.Tests.Interaction;

using GameplayActionPlugin.Runtime.Actions;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class InteractionInputMigrationTest
{
    [TestCase]
    public void InputGameplayActionUsesGenericActionMetadataAndOptionalInputDefaults()
    {
        InputGameplayAction action = AutoFree(new InputGameplayAction());
        System.Reflection.Assembly assembly = typeof(GameplayAction).Assembly;
        AssertThat(
                assembly.GetType("InteractionPlugin.Runtime.Actions.InteractionActionDefinition")
                    is null
            )
            .IsTrue();
        AssertThat(
                assembly.GetType("InteractionPlugin.Runtime.Actions.InteractionActionBindingConfig")
                    is null
            )
            .IsTrue();

        AssertThat(action is InputGameplayAction).IsTrue();
        AssertThat(typeof(GameplayAction).GetProperty("Priority") is null).IsTrue();
        AssertThat(typeof(GameplayAction).GetProperty("Automatic") is null).IsTrue();
        AssertThat(typeof(GameplayAction).GetProperty("Definition")?.PropertyType)
            .IsEqual(typeof(GameplayActionDefinition));
        AssertThat(typeof(GameplayAction).GetProperty("InteractionDefinition") is null).IsTrue();
        AssertThat(action.DefaultBindingConfig is null).IsTrue();
    }
}
