using Godot;

namespace QuestWorld.Inventory;

/// <summary>Static presentation data for one inventory item.</summary>
/// <remarks>
/// Definitions stay local on every peer. Runtime replication transports only the stable
/// <see cref="Id"/> and quantity, never this resource.
/// </remarks>
[GlobalClass]
public partial class InventoryItemDefinition : Resource
{
    /// <summary>Gets or sets the stable identifier stored in inventories, saves, and network state.</summary>
    [Export]
    public StringName Id { get; set; } = new(string.Empty);

    /// <summary>Gets or sets the player-facing item name.</summary>
    [Export]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional player-facing icon.</summary>
    [Export]
    public Texture2D? Icon { get; set; }
}
