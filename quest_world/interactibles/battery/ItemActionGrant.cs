using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.Inventory;

[GlobalClass]
public partial class ItemActionGrant : Node
{
    [Export]
    public InventoryComponent? Inventory { get; set; } = null;

    [Export]
    public GameplayActionComponent? ActionComponent { get; set; } = null;

    private Dictionary<StringName, GameplayAction> _grantedActionsByItemId = new();

    public override void _Ready()
    {
        if (Inventory is null)
        {
            GD.PushWarning(
                $"ItemActionGrant: Inventory is null on {GetPath()}. Actions will not be granted."
            );
        }

        if (ActionComponent is null)
        {
            GD.PushWarning(
                $"ItemActionGrant: ActionComponent is null on {GetPath()}. Actions will not be granted."
            );
        }

        Inventory?.ItemQuantityChanged += OnItemQuantityChanged;
    }

    private void OnItemQuantityChanged(
        StringName itemId,
        int oldQuantity,
        int newQuantity,
        bool isSynchronization
    )
    {
        if (Inventory?.Catalog?.GetItem(itemId) is not CarriableItemDefinition item)
        {
            return;
        }

        if (oldQuantity <= 0 && newQuantity > 0)
        {
            DropExecutor executor = new() { Name = "DropExecutor", Item = item };
            InputGameplayAction action = new()
            {
                Name = item.DropActionId.ToString(),
                Definition = new GameplayActionDefinition
                {
                    Id = item.DropActionId,
                    Label = item.DropActionLabel,
                },
                DefaultBindingConfig = item.DropBindingConfig,
                Executor = executor,
                ExecutionVisibility = GameplayActionExecutionVisibility.AuthorityOnly,
            };
            action.AddChild(executor);

            if (ActionComponent?.AddAction(action) == true)
            {
                _grantedActionsByItemId[itemId] = action;
            }
            else
            {
                action.Free();
            }
        }
        else if (oldQuantity > 0 && newQuantity <= 0)
        {
            if (
                _grantedActionsByItemId.TryGetValue(itemId, out GameplayAction? action)
                && action.Definition is not null
            )
            {
                ActionComponent?.RemoveAction(action.Definition.Id);
                _grantedActionsByItemId.Remove(itemId);
            }
        }
    }
}
