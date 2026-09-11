using System;
using System.Threading.Tasks;
using DummyCharacterPlugin;
using Godot;
using InventoryPlugin;

[GlobalClass]
public partial class CarryComponent : Node, ICarrier
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

    public IWorldSpawner? WorldSpawner { get; set; }

    public IOriented? Carrier { get; set; }

    private CarryOperationState? _currentCarryOperationState;

    private CarryOperationState? CurrentCarryOperationState
    {
        get => _currentCarryOperationState;
        set
        {
            _currentCarryOperationState = value;
            ReplicatedCurrentCarryOperationState = _currentCarryOperationState?.Serialize();
        }
    }

    [ExportGroup("Replication")]
    [Export]
    public Godot.Collections.Dictionary<string, Variant>? ReplicatedCurrentCarryOperationState
    {
        get => _currentCarryOperationState?.Serialize();
        set
        {
            CarryOperationState? newCarryOperationState = null;

            if (value is not null)
            {
                if (!CarryOperationState.Deserialize(value, out newCarryOperationState))
                {
                    GD.PushWarning($"{GetPath()}: invalid CarryOperationState variant");
                    _currentCarryOperationState = null;
                }
            }

            CarryOperationState? lastOperation = _currentCarryOperationState;
            _currentCarryOperationState = newCarryOperationState;

            OnCarryOperationChanged(lastOperation, _currentCarryOperationState);
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
    public bool IsInCarryOperation => CurrentCarryOperationState is not null;

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

    public async Task<bool> TryTakeAsync(
        StringName itemId,
        Node3D carriableObject,
        Action? onCommited = null
    )
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

        if (IsCarrying && !await TryDropAsync())
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

        CurrentCarryOperationState = new CarryOperationState(
            CarryOperationKind.Take,
            itemId,
            Time.GetTicksMsec()
        );

        await ToSignal(
            GetTree().CreateTimer(config?.TakeAnimationCommitTimeSec ?? 0f, processAlways: false),
            SceneTreeTimer.SignalName.Timeout
        );

        if (!TryCommitTake(itemId, carriableObject))
        {
            CurrentCarryOperationState = null;
            return false;
        }

        onCommited?.Invoke();

        await ToSignal(
            GetTree().CreateTimer(config?.TakeAnimationEndTimeSec ?? 0f, processAlways: false),
            SceneTreeTimer.SignalName.Timeout
        );

        CurrentCarryOperationState = null;

        return true;
    }

    private bool TryCommitTake(StringName itemId, Node3D carriableObject)
    {
        if (
            !IsAuthoritative
            || carriableObject is null
            || !TryGetCarriableDefinition(itemId, out CarriableItemDefinition definition)
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

    public async Task<bool> TryDropAsync(Action? onCommited = null)
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

        CurrentCarryOperationState = new CarryOperationState(
            CarryOperationKind.Drop,
            CarriedItemId,
            Time.GetTicksMsec()
        );

        await ToSignal(
            GetTree().CreateTimer(config?.DropAnimationCommitTimeSec ?? 0f, processAlways: false),
            SceneTreeTimer.SignalName.Timeout
        );

        if (!TryCommitDrop())
        {
            CurrentCarryOperationState = null;
            return false;
        }

        onCommited?.Invoke();

        await ToSignal(
            GetTree().CreateTimer(config?.DropAnimationEndTimeSec ?? 0f, processAlways: false),
            SceneTreeTimer.SignalName.Timeout
        );

        CurrentCarryOperationState = null;

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
            if (Inventory.AddItem(CarriedItemId))
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

    private void OnCarryOperationChanged(
        CarryOperationState? lastOperation,
        CarryOperationState? currentCarryOperation
    )
    {
        // Started carrying
        if (
            currentCarryOperation is null
            || !TryGetCarriableDefinition(
                currentCarryOperation.Value.ItemId,
                out CarriableItemDefinition definition
            )
        )
        {
            return;
        }

        CarryKindAnimationConfig? config = AnimationConfig?.GetConfigForKind(definition.CarryKind);
        if (config is not null)
        {
            switch (currentCarryOperation?.OperationKind)
            {
                case CarryOperationKind.Take:
                    AnimationController?.PlayOneShot(config.TakeAnimationName);
                    break;

                case CarryOperationKind.Drop:
                    AnimationController?.PlayOneShot(config.DropAnimationName);
                    break;
            }
        }
        else
        {
            GD.PushWarning(
                $"{GetPath()}: CarryKind {definition.CarryKind} has no animation config"
            );
        }
    }
}
