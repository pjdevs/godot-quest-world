using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class DropExecutor : GameplayActionExecutor
{
    [Export]
    public StringName InventoryItem { get; set; } = "";

    [Export]
    public StringName ItemSpawnDefinitionId { get; set; } = "";

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        IOriented? owner = context.GetInstigator<IOriented>();
        IInventoryOwner? inventoryOwner = context.GetInstigator<IInventoryOwner>();
        IWorldSpawner? worldSpawner = context.GetWorld<IWorldSpawner>();
        if (owner is null || inventoryOwner is null || worldSpawner is null)
        {
            return new GameplayActionExecutionFailed("Battery drop context is incomplete.");
        }

        if (inventoryOwner.Inventory.RemoveItem(InventoryItem) != 1)
        {
            return new GameplayActionExecutionFailed("The item is not available to drop.");
        }

        SpawnRequest request = new(owner.VisualTransform.Translated(owner.ForwardVector * 1.5f));
        if (!worldSpawner.TrySpawn(ItemSpawnDefinitionId, request, out _))
        {
            inventoryOwner.Inventory.AddItem(InventoryItem);
            return new GameplayActionExecutionFailed("BatterySpawner failed to spawn a battery.");
        }

        return new GameplayActionExecutionCompleted();
    }
}
