using System;
using System.Threading.Tasks;
using DummyCharacterPlugin;
using Godot;
using InventoryPlugin;

public readonly record struct CarryOperation(
    StringName ItemId,
    ulong StartedAtServerTimeMsec
);

[GlobalClass]
public partial class CarryComponent : Node, ICarrier
{
    [Export]
    public Node3D? CarriableAnchor { get; set; }

    [Export]
    public InventoryComponent? Inventory { get; set; }

    [Export]
    public CharacterAnimationController? AnimationController { get; set; }

    public IWorldSpawner? WorldSpawner { get; set; }

    public IOriented? Carrier { get; set; }

    private CarryOperation? _currentCarryOperation;

    [ExportGroup("Replication")]
    [Export]
    public Godot.Collections.Dictionary<string, Variant>? CurrentCarryOperationVariant
    {
        get =>
            _currentCarryOperation is CarryOperation op
                ? new Godot.Collections.Dictionary<string, Variant>()
                {
                    { "item_id", op.ItemId },
                    { "started_at_server_time_msec", op.StartedAtServerTimeMsec },
                }
                : null;
        set
        {
            if (value is null)
            {
                _currentCarryOperation = null;
                return;
            }

            if (
                !value.TryGetValue("item_id", out Variant itemIdVariant)
                || !value.TryGetValue(
                    "started_at_server_time_msec",
                    out Variant startedAtServerTimeMsecVariant
                )
            )
            {
                GD.PushWarning($"{GetPath()}: invalid carry operation variant.");
                _currentCarryOperation = null;
                return;
            }

            StringName itemId;
            ulong startedAtServerTimeMsec;
            try
            {
                itemId = (StringName)itemIdVariant;
                startedAtServerTimeMsec = (ulong)startedAtServerTimeMsecVariant;
            }
            catch (InvalidCastException)
            {
                GD.PushWarning($"{GetPath()}: invalid carry operation variant.");
                _currentCarryOperation = null;
                return;
            }

            CarryOperation? lastOperation = _currentCarryOperation;
            _currentCarryOperation = new CarryOperation(
                itemId,
                startedAtServerTimeMsec
            );

            OnCarryOperationChanged(lastOperation, _currentCarryOperation);
        }
    }

    [Export]
    public StringName? CarriedItemId
    {
        get => _carriedItemId;
        set
        {
            StringName? normalized = value is StringName itemId && !itemId.IsEmpty ? itemId : null;
            if (_carriedItemId == normalized)
            {
                return;
            }

            _carriedItemId = normalized;
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

    public async Task<bool> TryTakeAsync(StringName itemId, Node3D carriableObject)
    {
        if (
            !IsAuthoritative
            || carriableObject is null
            || !TryGetCarriableDefinition(itemId, out CarriableItemDefinition definition)
            || Inventory is null
        )
        {
            return false;
        }

        _currentCarryOperation = new CarryOperation(itemId, Time.GetTicksMsec());
        CurrentCarryOperationVariant = CurrentCarryOperationVariant; // Replicate to clients

        await Task.Delay(700);

        if (!TryTake(itemId, carriableObject))
        {
            return false;
        }

        // TODO: wait for animation finishg
        await Task.Delay(700);

        _currentCarryOperation = null;
        CurrentCarryOperationVariant = null; // Replicate to clients

        return true;
    }

    public bool TryTake(StringName itemId, Node3D carriableObject)
    {
        if (
            !IsAuthoritative
            || carriableObject is null
            || !TryGetCarriableDefinition(itemId, out CarriableItemDefinition definition)
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

        AnimationController?.PlayOneShot(definition.CustomDropAnimationName);

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

    private void OnCarryOperationChanged(
        CarryOperation? lastOperation,
        CarryOperation? currentCarryOperation
    )
    {
        // Started carrying
        if (
            currentCarryOperation is not null
            && TryGetCarriableDefinition(
                currentCarryOperation.Value.ItemId,
                out CarriableItemDefinition definition
            )
        )
        {
            AnimationController?.PlayOneShot(definition.CustomCarryAnimationName);
        }
    }
}
