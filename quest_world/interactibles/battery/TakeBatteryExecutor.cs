using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Inventory;

[GlobalClass]
public partial class TakeBatteryExecutor : InteractionActionExecutor
{
    [Export]
    public StringName? BatteryItem { get; set; } = null;

    public override GameplayActionExecutionResult Execute(in InteractionExecutionContext context)
    {
        InventoryComponent inventory = context
            .Interactor.GetParent()
            .GetNode<InventoryComponent>("InventoryComponent");

        if (inventory is not null && BatteryItem is not null)
        {
            inventory?.AddItem(BatteryItem);
        }
        else
        {
            return new GameplayActionExecutionFailed("InventoryComponent or BatteryItem is null.");
        }

        context.Interactive.GetParent().QueueFree();

        return new GameplayActionExecutionCompleted();
    }
}
