using Godot;
using QuestWorld.Inventory;

[GlobalClass]
public partial class CarriableItemDefinition : InventoryItemDefinition
{
    [Export]
    public StringName SpawnDefinitionId { get; set; } = new(string.Empty);

    public StringName DropActionId => new($"drop_{Id}");

    public string DropActionLabel => $"Drop {DisplayName}";

    public StringName GetSpawnDefinitionId() => SpawnDefinitionId.IsEmpty ? Id : SpawnDefinitionId;
}
