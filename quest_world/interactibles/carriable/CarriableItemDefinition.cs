using GameplayActionPlugin.Runtime.Bindings;
using Godot;
using InventoryPlugin;

[GlobalClass]
public partial class CarriableItemDefinition : InventoryItemDefinition
{
    [Export]
    public SpawnDefinition? SpawnDefinition { get; set; }

    [Export]
    public GameplayActionBindingConfig? DropBindingConfig { get; set; }

    [Export]
    public PackedScene? ItemVisualScene { get; set; }

    public StringName DropActionId => new($"drop_{Id}");

    public string DropActionLabel => $"Drop {DisplayName}";
}
