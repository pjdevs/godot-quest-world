using Godot;
using QuestWorld.Inventory;

[GlobalClass]
public partial class CarriableItemDefinition : InventoryItemDefinition
{
    [Export]
    public SpawnDefinition? SpawnDefinition { get; set; }

    public StringName DropActionId => new($"drop_{Id}");

    public string DropActionLabel => $"Drop {DisplayName}";
}
