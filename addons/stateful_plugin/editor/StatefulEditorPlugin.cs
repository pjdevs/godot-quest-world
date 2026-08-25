#if TOOLS

using Godot;

namespace StatefulPlugin.Editor;

[Tool]
public partial class StatefulEditorPlugin : EditorPlugin
{
    private StatefulInspectorPlugin _inspector = null!;

    public override void _EnterTree()
    {
        _inspector = new StatefulInspectorPlugin();
        AddInspectorPlugin(_inspector);
    }

    public override void _ExitTree()
    {
        RemoveInspectorPlugin(_inspector);
    }
}

#endif
