using System;
using System.Collections.Generic;
using Godot;

namespace QuestWorld.Inventory;

/// <summary>Owns one authoritative collection of catalog-backed item quantities.</summary>
/// <remarks>
/// Gameplay mutates the server copy through <see cref="AddItem"/> and <see cref="RemoveItem"/>.
/// An optional <see cref="InventoryReplicationSynchronizer"/> observes quantity signals and transports
/// the current spawn snapshot plus on-change batches without changing this gameplay API.
/// </remarks>
[GlobalClass]
public partial class InventoryComponent : Node
{
    /// <summary>Current serialization version used by <see cref="InventorySavedState"/>.</summary>
    public const int CurrentSaveVersion = 1;

    /// <summary>Emitted whenever one local item quantity changes.</summary>
    /// <param name="itemId">Stable item identifier.</param>
    /// <param name="oldQuantity">Quantity before the change.</param>
    /// <param name="newQuantity">Quantity after the change.</param>
    /// <param name="isSynchronization">Whether this peer is catching up to existing state.</param>
    [Signal]
    public delegate void ItemQuantityChangedEventHandler(
        StringName itemId,
        int oldQuantity,
        int newQuantity,
        bool isSynchronization
    );

    /// <summary>Emitted after a complete replicated or restored snapshot has been applied.</summary>
    /// <param name="isInitialSynchronization">Whether this is the first replicated snapshot.</param>
    [Signal]
    public delegate void InventorySynchronizedEventHandler(bool isInitialSynchronization);

    /// <summary>Gets or sets the catalog used to validate authoritative mutations.</summary>
    [Export]
    public InventoryCatalog? Catalog { get; set; }

    private Godot.Collections.Dictionary<StringName, int> _items = new();

    /// <summary>Gets whether this peer owns authoritative inventory state.</summary>
    public bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    /// <summary>Gets the quantity currently held for an item.</summary>
    public int GetItemCount(StringName itemId) =>
        _items.TryGetValue(itemId, out int count) ? count : 0;

    /// <summary>Gets a detached, deterministically ordered snapshot of every non-empty stack.</summary>
    public InventoryEntry[] GetEntries()
    {
        List<InventoryEntry> entries = new(_items.Count);
        foreach ((StringName itemId, int quantity) in _items)
        {
            if (quantity > 0)
            {
                entries.Add(new InventoryEntry(itemId, quantity));
            }
        }

        entries.Sort(
            (left, right) => string.CompareOrdinal(left.ItemId.ToString(), right.ItemId.ToString())
        );
        return entries.ToArray();
    }

    /// <summary>Adds a positive quantity on the authority.</summary>
    /// <returns><see langword="true"/> when the inventory changed.</returns>
    public bool AddItem(StringName itemId, int quantity = 1)
    {
        if (!CanMutate(itemId, quantity))
        {
            return false;
        }

        int oldQuantity = GetItemCount(itemId);
        int newQuantity;
        try
        {
            newQuantity = checked(oldQuantity + quantity);
        }
        catch (OverflowException)
        {
            GD.PushWarning(
                $"{GetPath()}: adding {quantity} '{itemId}' would overflow its quantity."
            );
            return false;
        }

        Godot.Collections.Dictionary<StringName, int> next = CloneItems();
        next[itemId] = newQuantity;
        ApplyAuthoritativeMutation(next, itemId, oldQuantity, newQuantity);
        return true;
    }

    /// <summary>Removes up to a positive quantity on the authority.</summary>
    /// <returns>The quantity actually removed.</returns>
    public int RemoveItem(StringName itemId, int quantity = 1)
    {
        if (!CanMutate(itemId, quantity))
        {
            return 0;
        }

        int oldQuantity = GetItemCount(itemId);
        int removedQuantity = Math.Min(oldQuantity, quantity);
        if (removedQuantity == 0)
        {
            return 0;
        }

        int newQuantity = oldQuantity - removedQuantity;
        Godot.Collections.Dictionary<StringName, int> next = CloneItems();
        if (newQuantity == 0)
        {
            next.Remove(itemId);
        }
        else
        {
            next[itemId] = newQuantity;
        }

        ApplyAuthoritativeMutation(next, itemId, oldQuantity, newQuantity);
        return removedQuantity;
    }

    /// <summary>Creates a versioned snapshot for the project persistence system.</summary>
    public InventorySavedState SaveState() => new(CurrentSaveVersion, GetEntries());

    /// <summary>Restores a complete snapshot on the authority.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The version, item, or quantity is invalid.</exception>
    /// <exception cref="InvalidOperationException">The current peer is not authoritative.</exception>
    public void LoadState(InventorySavedState savedState)
    {
        if (savedState.Version != CurrentSaveVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedState),
                savedState.Version,
                $"Unsupported inventory save version {savedState.Version}; expected {CurrentSaveVersion}."
            );
        }

        if (!IsAuthoritative)
        {
            throw new InvalidOperationException(
                $"{GetPath()}: inventory restoration requires authority."
            );
        }

        Godot.Collections.Dictionary<StringName, int> restored = new();
        foreach (InventoryEntry entry in savedState.Entries ?? Array.Empty<InventoryEntry>())
        {
            if (entry.Quantity <= 0 || Catalog?.Contains(entry.ItemId) != true)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(savedState),
                    entry,
                    $"Restored item '{entry.ItemId}' must be catalogued with a positive quantity."
                );
            }

            if (!restored.TryAdd(entry.ItemId, entry.Quantity))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(savedState),
                    entry.ItemId,
                    $"Restored item '{entry.ItemId}' is declared more than once."
                );
            }
        }

        ApplySnapshot(restored, isSynchronization: true);
        EmitSignal(SignalName.InventorySynchronized, true);
    }

    /// <summary>Replaces local state with a complete snapshot received during network spawn.</summary>
    internal void ApplyReplicatedSnapshot(Godot.Collections.Dictionary<StringName, int> snapshot)
    {
        ApplySnapshot(snapshot, isSynchronization: true);
        EmitSignal(SignalName.InventorySynchronized, true);
    }

    /// <summary>Creates the complete transient wire snapshot read during peer synchronization.</summary>
    internal Godot.Collections.Dictionary<StringName, int> CaptureReplicationSnapshot() =>
        CloneItems();

    /// <summary>Applies a consolidated batch of authoritative quantities received from the network.</summary>
    internal void ApplyReplicatedChanges(Godot.Collections.Dictionary<StringName, int> changes)
    {
        Godot.Collections.Dictionary<StringName, int> next = CloneItems();
        foreach ((StringName itemId, int quantity) in changes)
        {
            if (quantity <= 0)
            {
                next.Remove(itemId);
            }
            else if (!itemId.IsEmpty)
            {
                next[itemId] = quantity;
            }
        }

        ApplySnapshot(next, isSynchronization: false);
    }

    private bool CanMutate(StringName itemId, int quantity)
    {
        if (!IsAuthoritative)
        {
            GD.PushWarning($"{GetPath()}: only the server may change an InventoryComponent.");
            return false;
        }

        if (quantity <= 0)
        {
            GD.PushWarning($"{GetPath()}: an inventory mutation requires a positive quantity.");
            return false;
        }

        if (Catalog?.Contains(itemId) != true)
        {
            GD.PushWarning(
                $"{GetPath()}: item '{itemId}' is not declared by the assigned Catalog."
            );
            return false;
        }

        return true;
    }

    private Godot.Collections.Dictionary<StringName, int> CloneItems()
    {
        Godot.Collections.Dictionary<StringName, int> clone = new();
        foreach ((StringName itemId, int quantity) in _items)
        {
            clone[itemId] = quantity;
        }

        return clone;
    }

    private void ApplyAuthoritativeMutation(
        Godot.Collections.Dictionary<StringName, int> next,
        StringName itemId,
        int oldQuantity,
        int newQuantity
    )
    {
        _items = next;
        EmitSignal(SignalName.ItemQuantityChanged, itemId, oldQuantity, newQuantity, false);
    }

    private void ApplySnapshot(
        Godot.Collections.Dictionary<StringName, int> snapshot,
        bool isSynchronization
    )
    {
        Godot.Collections.Dictionary<StringName, int> sanitized = new();
        foreach ((StringName itemId, int quantity) in snapshot)
        {
            if (!itemId.IsEmpty && quantity > 0)
            {
                sanitized[itemId] = quantity;
            }
        }

        HashSet<StringName> itemIds = new(_items.Keys);
        itemIds.UnionWith(sanitized.Keys);

        Godot.Collections.Dictionary<StringName, int> previous = _items;
        _items = sanitized;

        foreach (StringName itemId in itemIds)
        {
            int oldQuantity = previous.TryGetValue(itemId, out int previousQuantity)
                ? previousQuantity
                : 0;
            int newQuantity = sanitized.TryGetValue(itemId, out int currentQuantity)
                ? currentQuantity
                : 0;
            if (oldQuantity != newQuantity)
            {
                EmitSignal(
                    SignalName.ItemQuantityChanged,
                    itemId,
                    oldQuantity,
                    newQuantity,
                    isSynchronization
                );
            }
        }
    }
}
