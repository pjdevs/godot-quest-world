using Godot;
using QuestWorld.GameplayActions.Runtime.Bindings;

namespace QuestWorld.GameplayActions.Runtime.Actions;

/// <summary>Gameplay action occurrence that may provide a default local input binding.</summary>
[GlobalClass]
public partial class InputGameplayAction : GameplayAction
{
    /// <summary>Gets or sets the binding config the owning runner derives when this action is owned.</summary>
    /// <remarks>A null value is valid and means no default binding is created.</remarks>
    [Export]
    public GameplayActionBindingConfig? DefaultBindingConfig { get; set; }
}
