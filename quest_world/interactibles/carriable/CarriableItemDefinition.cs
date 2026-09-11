using Godot;
using InventoryPlugin;

[GlobalClass]
public partial class CarriableItemDefinition : InventoryItemDefinition
{
    [Export]
    public SpawnDefinition? SpawnDefinition { get; set; }

    [Export]
    public StringName CustomCarryAnimationName { get; set; } = "PickUp_Kneeling";

    [Export]
    public StringName CustomDropAnimationName { get; set; } = "PickUp_Kneeling";

    [Export]
    public PackedScene? ItemVisualScene { get; set; }
}
