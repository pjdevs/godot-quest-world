#if TOOLS

using Godot;

namespace StatefulPlugin.Editor;

[Tool]
public partial class StatefulInspectorPlugin : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj)
    {
        return StatefulValidator.CanHandle(obj);
    }

    public override void _ParseBegin(GodotObject obj)
    {
        var warnings = StatefulValidator.Validate(obj);

        foreach (string warning in warnings)
        {
            var label = new Label { Text = $"⚠ {warning}" };

            AddCustomControl(label);
        }
    }
}

#endif
