namespace QuestWorld.Tests.Interaction;

using GdUnit4;
using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Actions;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionInputMigrationTest
{
    [TestCase]
    public void InteractionActionUsesInputGameplayActionDefaultsInsteadOfLegacyInputExports()
    {
        InteractionAction action = AutoFree(new InteractionAction());
        System.Reflection.Assembly assembly = typeof(InteractionAction).Assembly;
        AssertThat(
                assembly.GetType(
                    "QuestWorld.Interaction.Runtime.Actions.InteractionActionDefinition"
                )
                    is null
            )
            .IsTrue();
        AssertThat(
                assembly.GetType(
                    "QuestWorld.Interaction.Runtime.Actions.InteractionActionBindingConfig"
                )
                    is null
            )
            .IsTrue();

        AssertThat(action is InputGameplayAction).IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("Priority") is null).IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("Automatic") is null).IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("Definition")?.PropertyType)
            .IsEqual(typeof(GameplayActionDefinition));
        AssertThat(typeof(InteractionAction).GetProperty("InteractionDefinition") is null).IsTrue();
        AssertThat(action.DefaultBindingConfig is null).IsTrue();
    }
}
