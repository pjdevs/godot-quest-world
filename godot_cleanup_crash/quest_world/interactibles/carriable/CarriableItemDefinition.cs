using Godot;
using InventoryPlugin;

[GlobalClass]
public partial class CarriableItemDefinition : InventoryItemDefinition
{
    [Export]
    public SpawnDefinition? SpawnDefinition { get; set; }

    [Export]
    public CarryKind CarryKind { get; set; } = CarryKind.Ground;

    [Export]
    public PackedScene? ItemVisualScene { get; set; }
}
