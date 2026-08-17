#if TOOLS

using Godot;
using QuestWorld.Interaction.Examples.Interactive;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;

namespace InteractionPlugin.Editor;

[Tool]
public partial class InteractionInspectorPlugin : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj)
    {
        return obj
            is InteractiveComponent
                or InteractionInteractor
                or InteractionStateful
                or InteractionPresenter
                or InteractiveActor;
    }

    public override void _ParseBegin(GodotObject obj)
    {
        var warnings = InteractionValidator.Validate(obj);

        foreach (string warning in warnings)
        {
            var label = new Label { Text = $"⚠ {warning}" };

            AddCustomControl(label);
        }
    }
}

#endif
