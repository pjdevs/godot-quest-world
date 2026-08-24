#if TOOLS

using Godot;

namespace InteractionPlugin.Editor;

[Tool]
public partial class InteractionEditorPlugin : EditorPlugin
{
    private InteractionInspectorPlugin _inspector = null!;

    public override void _EnterTree()
    {
        _inspector = new InteractionInspectorPlugin();
        AddInspectorPlugin(_inspector);
    }

    public override void _ExitTree()
    {
        RemoveInspectorPlugin(_inspector);
    }
}

#endif
