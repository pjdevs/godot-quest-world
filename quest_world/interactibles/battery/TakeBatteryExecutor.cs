using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class TakeBatteryExecutor : GameplayActionExecutor
{
    [Export]
    public StringName? BatteryItem { get; set; } = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        Character? character = context.GetInstigator<Character>();
        Node3D? battery = context.GetHost<Node3D>();
        if (character?.Inventory is null || BatteryItem is null || battery is null)
        {
            return new GameplayActionExecutionFailed("Battery pickup context is incomplete.");
        }

        character.Inventory.AddItem(BatteryItem);
        battery.QueueFree();

        return new GameplayActionExecutionCompleted();
    }
}
