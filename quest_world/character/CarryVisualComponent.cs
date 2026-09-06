using System;
using Godot;
using QuestWorld.Inventory;

[GlobalClass]
public partial class CarryVisualComponent : Node, ICarrier
{
    [Export]
    public Node3D? CarriableAnchor { get; set; }

    [Export]
    public InventoryCatalog? InventoryCatalog { get; set; }

    [Export]
    public StringName? CarriedItemId
    {
        get => _carriedItemId;
        set
        {
            if (_carriedItemId == value)
            {
                return;
            }

            _carriedItemId = value;
            OnCarriedItemChanged();
        }
    }

    public bool IsCarrying => CarriedItemId is not null;

    private StringName? _carriedItemId;

    public override void _EnterTree()
    {
        SetMultiplayerAuthority(1);
    }

    private void OnCarriedItemChanged()
    {
        RemoveItemVisual();
        ApplyItemVisual();
    }

    public bool TryCarryVisual(StringName ItemId)
    {
        if (IsCarrying)
        {
            return false;
        }

        CarriedItemId = ItemId;
        return true;
    }

    public bool TryDropVisual()
    {
        if (!IsCarrying)
        {
            return false;
        }

        CarriedItemId = null;
        return true;
    }

    private bool ApplyItemVisual()
    {
        if (CarriableAnchor is null || CarriedItemId is null)
        {
            return false;
        }

        Node3D? itemVisualInstance = (
            InventoryCatalog?.GetItem(CarriedItemId) as CarriableItemDefinition
        )?.ItemVisualScene?.Instantiate<Node3D>();

        if (itemVisualInstance is null)
        {
            return false;
        }

        CarriableAnchor.AddChild(itemVisualInstance);
        return true;
    }

    private bool RemoveItemVisual()
    {
        if (CarriableAnchor is null)
        {
            return false;
        }

        foreach (var node in CarriableAnchor.GetChildren())
        {
            CarriableAnchor.RemoveChild(node);
            node.QueueFree();
        }

        return true;
    }
}
