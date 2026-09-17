using System;
using DummyCharacterPlugin;
using Godot;
using InventoryPlugin;

[GlobalClass]
public partial class CarryComponent : Node
{
    [Signal]
    public delegate void CarriedItemChangedEventHandler();

    [Signal]
    public delegate void TakeOperationCommittedEventHandler();

    [Signal]
    public delegate void DropOperationCommittedEventHandler();

    [Signal]
    public delegate void TakeOperationFailedEventHandler(string reason);

    [Signal]
    public delegate void DropOperationFailedEventHandler(string reason);

    [Signal]
    public delegate void TakeOperationFinishedEventHandler();

    [Signal]
    public delegate void DropOperationFinishedEventHandler();

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
    private (StringName ItemId, Node3D CarriableItemObject)? _pendingTakeOperation;
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

    public bool TryStartTake(StringName itemId, Node3D carriableItemObject)
    {
        if (
            !IsAuthoritative
            || carriableItemObject is null
            || !TryGetCarriableDefinition(itemId, out CarriableItemDefinition definition)
            || Inventory is null
        )
        {
            return false;
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
            _pendingTakeOperation = (itemId, carriableItemObject);
            return TryStartDrop();
        }

        _currentCarryOperation = takeOperation;

        if (config is not null)
        {
            Rpc(nameof(PlayAnimation), config.TakeAnimationName);
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

    public bool TryStartDrop()
    {
        if (
            !IsAuthoritative
            || Inventory is null
            || !IsCarrying
            || CarriedItemId is null
            || !TryGetCarriableDefinition(CarriedItemId, out CarriableItemDefinition definition)
        )
        {
            return false;
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

        _currentCarryOperation = CarryOperation.Drop(config);

        return true;
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
        _currentCarryOperation = null;
        _pendingTakeOperation = null;
    }

    public override void _Process(double delta)
    {
        if (!IsAuthoritative || _currentCarryOperation is null)
        {
            return;
        }

        _currentCarryOperation.Elapsed += delta;

        if (
            !_currentCarryOperation.HasCommited
            && _currentCarryOperation.Elapsed >= _currentCarryOperation.CommitTimeSec
        )
        {
            switch (_currentCarryOperation)
            {
                case CarryOperation.TakeOperation takeOperation:
                    if (TryCommitTake(
                        takeOperation.ItemId,
                        takeOperation.CarriableItemObject
                    ))
                    {
                        EmitSignal(SignalName.TakeOperationCommitted);
                        _currentCarryOperation.HasCommited = true;
                    }
                    else
                    {
                        EmitSignal(SignalName.TakeOperationFailed, "TryCommitTake failed.");
                        _currentCarryOperation = null;
                    }
                    break;
                case CarryOperation.DropOperation:
                    if (TryCommitDrop())
                    {
                        EmitSignal(SignalName.DropOperationCommitted);
                        _currentCarryOperation.HasCommited = true;
                    }
                    else
                    {
                        EmitSignal(SignalName.DropOperationFailed);
                        _currentCarryOperation = null;
                        _pendingTakeOperation = null;
                    }
                    break;
                default:
                    throw new Exception(
                        $"Unhandled carry operation {_currentCarryOperation.GetType()}"
                    );
            }
        }
        else if (
            _currentCarryOperation.HasCommited
            && _currentCarryOperation.Elapsed >= _currentCarryOperation.EndTimeSec
        )
        {
            switch (_currentCarryOperation)
            {
                case CarryOperation.TakeOperation:
                    EmitSignal(SignalName.TakeOperationFinished);
                    _currentCarryOperation = null;
                    break;
                case CarryOperation.DropOperation:
                    EmitSignal(SignalName.DropOperationFinished);

                    if (
                        _pendingTakeOperation is not null
                        && IsInstanceValid(_pendingTakeOperation.Value.CarriableItemObject)
                    )
                    {
                        if (!TryStartTake(
                            _pendingTakeOperation.Value.ItemId,
                            _pendingTakeOperation.Value.CarriableItemObject
                        ))
                        {
                            EmitSignal(SignalName.TakeOperationFailed, "TryStartTake failed after drop.");
                            _currentCarryOperation = null;
                        }

                        _pendingTakeOperation = null;
                    }
                    else
                    {
                        _currentCarryOperation = null;
                        _pendingTakeOperation = null;
                    }
                    break;
                default:
                    throw new Exception(
                        $"Unhandled carry operation {_currentCarryOperation.GetType()}"
                    );
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
