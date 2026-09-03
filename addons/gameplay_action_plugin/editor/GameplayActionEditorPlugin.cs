#if TOOLS

using Godot;

namespace QuestWorld.GameplayActions.Editor;

[Tool]
public partial class GameplayActionEditorPlugin : EditorPlugin
{
    private GameplayActionInspectorPlugin _inspector = null!;

    public override void _EnterTree()
    {
        _inspector = new GameplayActionInspectorPlugin();
        AddInspectorPlugin(_inspector);
    }

    public override void _ExitTree()
    {
        RemoveInspectorPlugin(_inspector);
    }
}

#endif
