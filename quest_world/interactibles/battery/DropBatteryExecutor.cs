using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class DropBatteryExecutor : GameplayActionExecutor
{
    [Export]
    public StringName? BatteryItem { get; set; } = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        Character? character = context.GetInstigator<Character>();
        World? world = context.GetWorld<World>();
        if (character?.Inventory is null || BatteryItem is null || world?.BatterySpawner is null)
        {
            return new GameplayActionExecutionFailed("Battery drop context is incomplete.");
        }

        character.Inventory.RemoveItem(BatteryItem);
        Node3D? battery = world.BatterySpawner.Spawn(
            character.VisualTransform.Translated(character.ForwardVector * 1.5f)
        );

        if (battery is null)
        {
            return new GameplayActionExecutionFailed("BatterySpawner failed to spawn a battery.");
        }

        return new GameplayActionExecutionCompleted();
    }
}
