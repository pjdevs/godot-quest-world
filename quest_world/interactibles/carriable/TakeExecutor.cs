using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class TakeExecutor : GameplayActionExecutor
{
    [Export]
    public CarriableItemDefinition? Item { get; set; }

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        IInventoryOwner? inventoryOwner = context.GetInstigator<IInventoryOwner>();
        Node3D? carriableObject = context.GetHost<Node3D>();
        if (inventoryOwner is null || carriableObject is null || Item is null || Item.Id.IsEmpty)
        {
            return new GameplayActionExecutionFailed("Carriable pickup context is incomplete.");
        }

        if (!inventoryOwner.Inventory.AddItem(Item.Id))
        {
            return new GameplayActionExecutionFailed(
                "The item could not be added to the inventory."
            );
        }

        carriableObject.QueueFree();
        return new GameplayActionExecutionCompleted();
    }
}
