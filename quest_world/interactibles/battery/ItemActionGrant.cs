using System;
using System.Collections.Generic;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;
using InventoryPlugin;

[GlobalClass]
public partial class ItemActionGrant : Node
{
    [Export]
    public InventoryComponent? Inventory { get; set; } = null;

    [Export]
    public GameplayActionComponent? ActionComponent { get; set; } = null;

    // TODO Make a packed scene association to grant anything

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
            // TODO grant with packed scene
            return;
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
