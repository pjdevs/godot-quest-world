using Godot;
using QuestWorld.GameplayActions.Runtime.Bindings;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class InputGameplayAction : GameplayAction
{
    [Export]
    public GameplayActionBindingConfig? DefaultBindingConfig { get; set; }
}
