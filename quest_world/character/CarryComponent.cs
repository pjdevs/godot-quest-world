using Godot;
using QuestWorld.Inventory;

[GlobalClass]
public partial class CarryComponent : Node, ICarrier
{
    [Export]
    public Node3D? CarriableAnchor { get; set; }

    [Export]
    public InventoryComponent? Inventory { get; set; }

    public IWorldSpawner? WorldSpawner { get; set; }

    public IOriented? Carrier { get; set; }

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
    private Node3D? _itemVisualInstance;

    public override void _EnterTree()
    {
        SetMultiplayerAuthority(1);
    }

    public override void _ExitTree()
    {
        if (IsAuthoritative && CarriedItemId is StringName itemId && !TryDrop(itemId))
        {
            GD.PushWarning($"{GetPath()}: carried item '{itemId}' could not be dropped on exit.");
        }
    }

    public bool TryTake(StringName itemId, Node3D carriableObject)
    {
        if (
            !IsAuthoritative
            || carriableObject is null
            || !TryGetCarriableDefinition(itemId, out _)
            || Inventory is null
        )
        {
            return false;
        }

        if (!Inventory.AddItem(itemId))
        {
            return false;
        }

        if (CarriedItemId is StringName carriedItemId && !TryDrop(carriedItemId))
        {
            if (Inventory.RemoveItem(itemId) != 1)
            {
                GD.PushError($"{GetPath()}: failed to roll back pickup of '{itemId}'.");
            }

            return false;
        }

        CarriedItemId = itemId;
        carriableObject.QueueFree();
        return true;
    }

    public bool TryDrop(StringName itemId)
    {
        if (
            !IsAuthoritative
            || CarriedItemId is null
            || CarriedItemId != itemId
            || Inventory is null
            || WorldSpawner is null
            || Carrier is null
            || !TryGetCarriableDefinition(itemId, out CarriableItemDefinition definition)
        )
        {
            return false;
        }

        if (Inventory.RemoveItem(itemId) != 1)
        {
            return false;
        }

        SpawnRequest request = new(
            Carrier.VisualTransform.Translated(Carrier.ForwardVector * 1.5f)
        );
        if (!WorldSpawner.TrySpawn(definition.SpawnDefinition!.Id, request, out _))
        {
            if (!Inventory.AddItem(itemId))
            {
                GD.PushError($"{GetPath()}: failed to restore '{itemId}' after a failed drop.");
            }

            return false;
        }

        CarriedItemId = null;
        return true;
    }

    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    private bool TryGetCarriableDefinition(
        StringName itemId,
        out CarriableItemDefinition definition
    )
    {
        CarriableItemDefinition? candidate =
            Inventory?.Catalog?.GetItem(itemId) as CarriableItemDefinition;
        if (candidate?.SpawnDefinition is not null && !candidate.SpawnDefinition.Id.IsEmpty)
        {
            definition = candidate;
            return true;
        }

        definition = null!;
        GD.PushWarning(
            $"{GetPath()}: item '{itemId}' requires a catalogued carriable definition and spawn definition."
        );
        return false;
    }

    private void OnCarriedItemChanged()
    {
        RemoveItemVisual();
        ApplyItemVisual();
    }

    private void ApplyItemVisual()
    {
        if (CarriedItemId is not StringName itemId)
        {
            return;
        }

        PackedScene? visualScene = (
            Inventory?.Catalog?.GetItem(itemId) as CarriableItemDefinition
        )?.ItemVisualScene;

        if (CarriableAnchor is null || visualScene is null)
        {
            GD.PushWarning($"{GetPath()}: item '{itemId}' has no usable carry visual.");
            return;
        }

        Node visualInstance = visualScene.Instantiate();
        if (visualInstance is not Node3D itemVisualInstance)
        {
            visualInstance.Free();
            GD.PushWarning($"{GetPath()}: carry visual for '{itemId}' must inherit Node3D.");
            return;
        }

        CarriableAnchor.AddChild(itemVisualInstance);
        _itemVisualInstance = itemVisualInstance;
    }

    private void RemoveItemVisual()
    {
        if (_itemVisualInstance is null || !IsInstanceValid(_itemVisualInstance))
        {
            _itemVisualInstance = null;
            return;
        }

        _itemVisualInstance.QueueFree();
        _itemVisualInstance = null;
    }
}
