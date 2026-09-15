using System;
using Godot;

public enum CarryOperationKind
{
    Take,
    Drop,
}

public readonly record struct CarryOperationState(
    CarryOperationKind OperationKind,
    StringName ItemId,
    ulong StartedAtServerTimeMsec
)
{
    public Godot.Collections.Dictionary<string, Variant> Serialize()
    {
        return new Godot.Collections.Dictionary<string, Variant>()
        {
            { "operation_type", Enum.GetName(OperationKind) ?? "Take" },
            { "item_id", ItemId },
            { "started_at_server_time_msec", StartedAtServerTimeMsec },
        };
    }

    public static bool Deserialize(
        Godot.Collections.Dictionary<string, Variant> payload,
        out CarryOperationState? carryOperationState
    )
    {
        if (
            !payload.TryGetValue("operation_type", out Variant operationTypeVariant)
            || !payload.TryGetValue("item_id", out Variant itemIdVariant)
            || !payload.TryGetValue(
                "started_at_server_time_msec",
                out Variant startedAtServerTimeMsecVariant
            )
        )
        {
            carryOperationState = null;
            return false;
        }

        CarryOperationKind operationType;
        StringName itemId;
        ulong startedAtServerTimeMsec;
        try
        {
            operationType = Enum.Parse<CarryOperationKind>((string)operationTypeVariant);
            itemId = (string)itemIdVariant;
            startedAtServerTimeMsec = (ulong)startedAtServerTimeMsecVariant;
        }
        catch (InvalidOperationException)
        {
            carryOperationState = null;
            return false;
        }
        catch (InvalidCastException)
        {
            carryOperationState = null;
            return false;
        }

        carryOperationState = new CarryOperationState(
            operationType,
            itemId,
            startedAtServerTimeMsec
        );
        return true;
    }
}
