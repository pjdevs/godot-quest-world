using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class DropExecutor : GameplayActionExecutor
{
    [Export]
    public CarriableItemDefinition? Item { get; set; }

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        IOriented? owner = context.GetInstigator<IOriented>();
        IInventoryOwner? inventoryOwner = context.GetInstigator<IInventoryOwner>();
        ICarrier? carrier = context.GetInstigator<ICarrier>();
        IWorldSpawner? worldSpawner = context.GetWorld<IWorldSpawner>();
        SpawnDefinition? spawnDefinition = Item?.SpawnDefinition;
        if (
            owner is null
            || carrier is null
            || inventoryOwner is null
            || worldSpawner is null
            || Item is null
            || spawnDefinition is null
            || spawnDefinition.Id.IsEmpty
        )
        {
            return new GameplayActionExecutionFailed("Carriable drop context is incomplete.");
        }

        if (inventoryOwner.Inventory.RemoveItem(Item.Id) != 1)
        {
            return new GameplayActionExecutionFailed("The item is not available to drop.");
        }

        SpawnRequest request = new(owner.VisualTransform.Translated(owner.ForwardVector * 1.5f));
        if (!worldSpawner.TrySpawn(spawnDefinition.Id, request, out _))
        {
            inventoryOwner.Inventory.AddItem(Item.Id);
            return new GameplayActionExecutionFailed("The carriable could not be spawned.");
        }

        carrier.TryDropVisual();

        return new GameplayActionExecutionCompleted();
    }
}
