using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.Inventory;

[GlobalClass]
public partial class ItemActionGrant : Node
{
    [Export]
    public Godot.Collections.Dictionary<StringName, PackedScene?> ItemActions { get; set; } = [];

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

        Inventory?.ItemQuantityChanged += OnItemQuantityChanged;
    }

    private void OnItemQuantityChanged(
        StringName itemId,
        int oldQuantity,
        int newQuantity,
        bool isSynchronization
    )
    {
        if (!ItemActions.TryGetValue(itemId, out PackedScene? actionScene))
        {
            return;
        }

        if (newQuantity > 0)
        {
            GameplayAction? action = actionScene?.Instantiate<GameplayAction>();

            if (action is not null)
            {
                ActionComponent?.AddAction(action);
            }
            else
            {
                GD.PushWarning(
                    $"ItemActionGrant: Failed to instantiate action for item {itemId} on {GetPath()}."
                );
            }
        }
        else
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
