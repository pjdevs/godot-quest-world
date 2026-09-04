using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.Inventory;

[GlobalClass]
public partial class DropBatteryExecutor : GameplayActionExecutor
{
    [Export]
    public StringName? BatteryItem { get; set; } = null;

    [Export]
    public PackedScene? BatteryScene { get; set; } = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        InventoryComponent? inventory = context
            .Instigator?.GetParent()
            .GetNode<InventoryComponent>("InventoryComponent");

        if (inventory is not null && BatteryItem is not null)
        {
            inventory?.RemoveItem(BatteryItem);
        }
        else
        {
            return new GameplayActionExecutionFailed("InventoryComponent or BatteryItem is null.");
        }

        Node3D? instigatorNode = context.Instigator?.GetParent<Node3D>();

        if (instigatorNode is null)
        {
            return new GameplayActionExecutionFailed("Instigator is not a Node3D.");
        }

        Node3D? batteryNode = BatteryScene?.Instantiate<Node3D>();

        if (batteryNode is null)
        {
            return new GameplayActionExecutionFailed(
                "BatteryScene is null or failed to instantiate."
            );
        }

        Window window = GetTree().Root;
        Node3D batteriesRoot = window.GetNode<Node3D>("test_world/Batteries");

        if (batteriesRoot is null)
        {
            return new GameplayActionExecutionFailed("Batteries root node not found.");
        }

        batteriesRoot.AddChild(batteryNode);
        batteryNode?.GlobalPosition = instigatorNode.GlobalPosition + new Vector3(0f, 0f, 1f);

        return new GameplayActionExecutionCompleted();
    }
}
