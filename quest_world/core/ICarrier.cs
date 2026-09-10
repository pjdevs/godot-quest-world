using System;
using System.Threading.Tasks;
using Godot;

public interface ICarrier
{
    public StringName? CarriedItemId { get; }
    public bool IsCarrying { get; }
    public Task<bool> TryTakeAsync(
        StringName itemId,
        Node3D carriableObject,
        Action? onCommited = null
    );
    public bool TryTake(StringName itemId, Node3D carriableObject);
    public bool TryDrop();
}
