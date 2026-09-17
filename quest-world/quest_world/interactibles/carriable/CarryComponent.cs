using System;
using DummyCharacterPlugin;
using Godot;
using InventoryPlugin;

[GlobalClass]
public partial class CarryComponent : Node
{
    [Signal]
    public delegate void CarriedItemChangedEventHandler();

    [Export]
    public Node3D? CarriableAnchor { get; set; }

    [Export]
    public InventoryComponent? Inventory { get; set; }

    [Export]
    public CharacterAnimationController? AnimationController { get; set; }

    [Export]
    public CarryAnimationConfig? AnimationConfig { get; set; }

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

    public IWorldSpawner? WorldSpawner { get; set; }
    public IOriented? Carrier { get; set; }
    public bool IsCarrying => CarriedItemId is not null;

    private CarryOperation? _currentCarryOperation;
    private CarryOperation.TakeOperation? _pendingTakeOperation;
    private StringName? _carriedItemId;
    private Node3D? _itemVisualInstance;

    public override void _EnterTree()
    {
        SetMultiplayerAuthority(1);
    }

    public override void _ExitTree()
    {
        if (IsAuthoritative && IsCarrying && !TryCommitDrop())
        {
            GD.PushWarning(
                $"{GetPath()}: carried item '{CarriedItemId}' could not be dropped on exit."
            );
        }
    }

    public CarryOperation? TryStartTake(StringName itemId, Node3D carriableItemObject)
    {
        if (_currentCarryOperation is not null)
        {
            return null;
        }

        if (
            !IsAuthoritative
            || carriableItemObject is null
            || !TryGetCarriableDefinition(itemId, out CarriableItemDefinition definition)
            || Inventory is null
        )
        {
            return null;
        }

        CarryKindAnimationConfig? config = AnimationConfig?.GetConfigForKind(definition.CarryKind);
        if (config is null)
        {
            GD.PushWarning(
                $"{GetPath()}: CarryKind {definition.CarryKind} has no animation config"
            );
        }

        CarryOperation.TakeOperation takeOperation = CarryOperation.Take(
            itemId,
            carriableItemObject,
            config
        );

        if (IsCarrying)
        {
            _pendingTakeOperation = takeOperation;
            if (TryStartDrop() is not null)
            {
                return takeOperation;
            }

            _pendingTakeOperation = null;
            takeOperation.Fail("TryStartDrop failed.");
            return null;
        }

        if (!TryActivateTakeOperation(takeOperation))
        {
            takeOperation.Fail("TryStartTake failed.");
            return null;
        }

        return takeOperation;
    }

    private bool TryActivateTakeOperation(CarryOperation.TakeOperation operation)
    {
        if (
            !IsAuthoritative
            || !IsInstanceValid(operation.CarriableItemObject)
            || !TryGetCarriableDefinition(operation.ItemId, out _)
            || Inventory is null
            || IsCarrying
        )
        {
            return false;
        }

        _currentCarryOperation = operation;

        if (operation.AnimationConfig is not null)
        {
            Rpc(nameof(PlayAnimation), operation.AnimationConfig.TakeAnimationName);
        }

        return true;
    }

    private bool TryCommitTake(StringName itemId, Node3D carriableObject)
    {
        if (
            !IsAuthoritative
            || carriableObject is null
            || !TryGetCarriableDefinition(itemId, out CarriableItemDefinition _)
            || Inventory is null
            || IsCarrying
        )
        {
            return false;
        }

        if (!Inventory.AddItem(itemId))
        {
            return false;
        }

        CarriedItemId = itemId;
        carriableObject.QueueFree();

        return true;
    }

    public CarryOperation? TryStartDrop()
    {
        if (_currentCarryOperation is not null)
        {
            return null;
        }

        if (
            !IsAuthoritative
            || Inventory is null
            || !IsCarrying
            || CarriedItemId is null
            || !TryGetCarriableDefinition(CarriedItemId, out CarriableItemDefinition definition)
        )
        {
            return null;
        }

        CarryKindAnimationConfig? config = AnimationConfig?.GetConfigForKind(definition.CarryKind);
        if (config is null)
        {
            GD.PushWarning(
                $"{GetPath()}: CarryKind {definition.CarryKind} has no animation config"
            );
        }
        else
        {
            Rpc(nameof(PlayAnimation), config.DropAnimationName);
        }

        CarryOperation.DropOperation dropOperation = CarryOperation.Drop(config);
        _currentCarryOperation = dropOperation;

        return dropOperation;
    }

    private bool TryCommitDrop()
    {
        if (
            !IsAuthoritative
            || CarriedItemId is null
            || Inventory is null
            || WorldSpawner is null
            || Carrier is null
            || !TryGetCarriableDefinition(CarriedItemId, out CarriableItemDefinition definition)
        )
        {
            return false;
        }

        if (Inventory.RemoveItem(CarriedItemId) != 1)
        {
            return false;
        }

        SpawnRequest request = new(
            Carrier.VisualTransform.Translated(Carrier.ForwardVector * 1.5f)
        );
        if (!WorldSpawner.TrySpawn(definition.SpawnDefinition!.Id, request, out _))
        {
            if (!Inventory.AddItem(CarriedItemId))
            {
                GD.PushError(
                    $"{GetPath()}: failed to restore '{CarriedItemId}' after a failed drop."
                );
            }

            return false;
        }

        CarriedItemId = null;
        return true;
    }

    public void CancelCurrentOperation()
    {
        CarryOperation? currentOperation = _currentCarryOperation;
        CarryOperation.TakeOperation? pendingTakeOperation = _pendingTakeOperation;

        _currentCarryOperation = null;
        _pendingTakeOperation = null;

        currentOperation?.Cancel();
        pendingTakeOperation?.Cancel();
    }

    public override void _Process(double delta)
    {
        CarryOperation? currentOperation = _currentCarryOperation;
        if (!IsAuthoritative || currentOperation is null)
        {
            return;
        }

        currentOperation.Elapsed += delta;

        if (
            !currentOperation.HasCommited
            && currentOperation.Elapsed >= currentOperation.CommitTimeSec
        )
        {
            switch (currentOperation)
            {
                case CarryOperation.TakeOperation takeOperation:
                    if (TryCommitTake(takeOperation.ItemId, takeOperation.CarriableItemObject))
                    {
                        currentOperation.Commit();
                    }
                    else
                    {
                        currentOperation.Fail("TryCommitTake failed.");
                        _currentCarryOperation = null;
                    }
                    break;
                case CarryOperation.DropOperation:
                    if (TryCommitDrop())
                    {
                        currentOperation.Commit();
                    }
                    else
                    {
                        currentOperation.Fail("TryCommitDrop failed.");
                        _currentCarryOperation = null;

                        _pendingTakeOperation?.Fail("TryStartTake failed after drop.");
                        _pendingTakeOperation = null;
                    }
                    break;
                default:
                    throw new Exception($"Unhandled carry operation {currentOperation.GetType()}");
            }
        }
        else if (
            currentOperation.HasCommited
            && currentOperation.Elapsed >= currentOperation.EndTimeSec
        )
        {
            switch (currentOperation)
            {
                case CarryOperation.TakeOperation:
                    _currentCarryOperation = null;
                    currentOperation.Finish();
                    break;
                case CarryOperation.DropOperation:
                    _currentCarryOperation = null;
                    currentOperation.Finish();

                    if (
                        _pendingTakeOperation is not null
                        && IsInstanceValid(_pendingTakeOperation.CarriableItemObject)
                    )
                    {
                        CarryOperation.TakeOperation pendingTakeOperation = _pendingTakeOperation;
                        _pendingTakeOperation = null;
                        if (!TryActivateTakeOperation(pendingTakeOperation))
                        {
                            pendingTakeOperation.Fail("TryStartTake failed after drop.");
                        }
                    }
                    else
                    {
                        _pendingTakeOperation?.Fail("Carriable item is no longer valid.");
                        _pendingTakeOperation = null;
                    }
                    break;
                default:
                    throw new Exception($"Unhandled carry operation {currentOperation.GetType()}");
            }
        }
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

        EmitSignal(SignalName.CarriedItemChanged);
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

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void PlayAnimation(StringName animationName)
    {
        AnimationController?.PlayOneShot(animationName);
    }
}
