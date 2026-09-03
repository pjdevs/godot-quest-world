#if TOOLS

using Godot;

namespace QuestWorld.GameplayActions.Editor;

[Tool]
public partial class GameplayActionInspectorPlugin : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj) => GameplayActionValidator.CanHandle(obj);

    public override void _ParseBegin(GodotObject obj)
    {
        foreach (string warning in GameplayActionValidator.Validate(obj))
        {
            AddCustomControl(new Label { Text = $"⚠ {warning}" });
        }
    }
}

#endif
