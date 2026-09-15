using System;
using System.Threading.Tasks;
using Godot;

public interface ICarrier
{
    public StringName? CarriedItemId { get; }
    public bool IsCarrying { get; }
    public bool IsInCarryOperation { get; }
    public Task<bool> TryTakeAsync(
        StringName itemId,
        Node3D carriableObject,
        Action? onCommited = null
    );
    public Task<bool> TryDropAsync(Action? onCommited = null);
}
