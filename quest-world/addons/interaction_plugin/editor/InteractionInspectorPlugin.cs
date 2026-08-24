#if TOOLS

using Godot;

namespace InteractionPlugin.Editor;

[Tool]
public partial class InteractionInspectorPlugin : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj)
    {
        return InteractionValidator.CanHandle(obj);
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
