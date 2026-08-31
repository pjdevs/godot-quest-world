using Godot;

namespace QuestWorld.Inventory;

/// <summary>Read-only snapshot of one item stack.</summary>
/// <param name="ItemId">Stable item identifier.</param>
/// <param name="Quantity">Positive quantity in the stack.</param>
public readonly record struct InventoryEntry(StringName ItemId, int Quantity);

/// <summary>Versioned snapshot handed to the project's persistence layer.</summary>
/// <param name="Version">Serialization contract version.</param>
/// <param name="Entries">Complete inventory contents.</param>
public readonly record struct InventorySavedState(int Version, InventoryEntry[] Entries);
