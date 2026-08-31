using Godot;

namespace QuestWorld.Inventory;

/// <summary>Declares every item identifier accepted by an inventory.</summary>
[GlobalClass]
public partial class InventoryCatalog : Resource
{
    /// <summary>Gets or sets the item definitions available to gameplay and presentation.</summary>
    [Export]
    public Godot.Collections.Array<InventoryItemDefinition> Items { get; set; } = new();

    /// <summary>Gets the definition associated with an identifier.</summary>
    /// <param name="itemId">Stable item identifier.</param>
    /// <returns>The matching definition, or <see langword="null"/> when none is declared.</returns>
    public InventoryItemDefinition? GetItem(StringName itemId)
    {
        foreach (InventoryItemDefinition item in Items)
        {
            if (item is not null && item.Id == itemId)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Checks whether this catalog declares an identifier.</summary>
    public bool Contains(StringName itemId) => GetItem(itemId) is not null;
}
