namespace QuestWorld.Tests.Game;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using InventoryPlugin;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed class CarryComponentTest
{
    [TestCase]
    public void EmptyReplicatedItemIdIsNormalizedToNoCarriedItem()
    {
        CarryComponent carry = new() { CarriedItemId = new StringName() };

        AssertThat(carry.CarriedItemId is null).IsTrue();
        AssertThat(carry.IsCarrying).IsFalse();
        carry.Free();
    }

    [TestCase]
    public async Task CancellingTakeOperationStopsBeforeCommit()
    {
        StringName batteryId = new("battery");
        StringName cellId = new("cell");
        Node3D root = new();
        InventoryComponent inventory = new()
        {
            Name = "Inventory",
            Catalog = CreateCatalog(
                CreateDefinition(batteryId, "battery_spawn"),
                CreateDefinition(cellId, "cell_spawn")
            ),
        };
        RecordingWorldSpawner worldSpawner = new(root);
        CarryComponent carry = new()
        {
            Name = "Carry",
            Inventory = inventory,
            WorldSpawner = worldSpawner,
        };
        Node3D battery = new() { Name = "Battery" };
        Node3D cell = new() { Name = "Cell" };
        root.AddChild(inventory);
        root.AddChild(carry);
        root.AddChild(battery);
        root.AddChild(cell);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);

        CarryOperation? operation = carry.TryStartTake(batteryId, battery);
        AssertThat(operation is not null).IsTrue();
        AssertThat(carry.TryStartTake(cellId, cell) is null).IsTrue();

        bool committed = false;
        bool finished = false;
        bool failed = false;
        bool cancelled = false;
        operation!.Committed += () => committed = true;
        operation.Finished += () => finished = true;
        operation.Failed += _ => failed = true;
        operation.Cancelled += () => cancelled = true;

        carry.CancelCurrentOperation();
        await runner.SimulateFrames(2);

        AssertThat(cancelled).IsTrue();
        AssertThat(committed).IsFalse();
        AssertThat(finished).IsFalse();
        AssertThat(failed).IsFalse();
        AssertThat(inventory.GetItemCount(batteryId)).IsEqual(0);
        AssertThat(carry.CarriedItemId is null).IsTrue();
        AssertThat(battery.IsQueuedForDeletion()).IsFalse();
        AssertThat(worldSpawner.SuccessfulSpawnIds).IsEmpty();
    }

    [TestCase]
    public void StartingAnotherOperationDoesNotReplaceActiveDrop()
    {
        StringName batteryId = new("battery");
        InventoryComponent inventory = new()
        {
            Catalog = CreateCatalog(CreateDefinition(batteryId, "battery_spawn")),
        };
        CarryComponent carry = new() { Inventory = inventory, CarriedItemId = batteryId };

        CarryOperation? drop = carry.TryStartDrop();
        AssertThat(drop is not null).IsTrue();

        bool cancelled = false;
        drop!.Cancelled += () => cancelled = true;
        Node3D replacement = new();

        AssertThat(carry.TryStartDrop() is null).IsTrue();
        AssertThat(carry.TryStartTake(batteryId, replacement) is null).IsTrue();

        carry.CancelCurrentOperation();

        AssertThat(cancelled).IsTrue();
        carry.Free();
        inventory.Free();
        replacement.Free();
    }

    [TestCase]
    public async Task CarryFlowKeepsInventoryWorldAndLifecycleConsistent()
    {
        StringName batteryId = new("battery");
        StringName cellId = new("cell");
        Node3D root = new();
        InventoryComponent inventory = new()
        {
            Name = "Inventory",
            Catalog = CreateCatalog(
                CreateDefinition(batteryId, "battery_spawn"),
                CreateDefinition(cellId, "cell_spawn")
            ),
        };
        RecordingWorldSpawner worldSpawner = new(root);
        CarryComponent carry = new()
        {
            Name = "Carry",
            Inventory = inventory,
            WorldSpawner = worldSpawner,
            Carrier = new FixedOrientation(
                new Transform3D(Basis.Identity, new Vector3(2.0f, 3.0f, 4.0f)),
                Vector3.Forward
            ),
        };
        Node3D battery = new() { Name = "Battery" };
        Node3D cell = new() { Name = "Cell" };
        Node3D replacementCell = new() { Name = "ReplacementCell" };
        root.AddChild(inventory);
        root.AddChild(carry);
        root.AddChild(battery);
        root.AddChild(cell);
        root.AddChild(replacementCell);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);

        bool batteryQueuedForDeletion = false;
        CarryOperation? batteryTake = carry.TryStartTake(batteryId, battery);
        AssertThat(batteryTake is not null).IsTrue();
        (bool batteryCommitted, bool batteryFinished, string? batteryFailure, _) =
            await AwaitOperation(
                batteryTake!,
                runner,
                () => batteryQueuedForDeletion = battery.IsQueuedForDeletion()
            );
        AssertThat(batteryCommitted).IsTrue();
        AssertThat(batteryFinished).IsTrue();
        AssertThat(batteryFailure is null).IsTrue();
        AssertThat(batteryQueuedForDeletion).IsTrue();
        AssertThat(inventory.GetItemCount(batteryId)).IsEqual(1);
        AssertThat(carry.CarriedItemId == batteryId).IsTrue();

        bool cellQueuedForDeletion = false;
        CarryOperation? cellTake = carry.TryStartTake(cellId, cell);
        AssertThat(cellTake is not null).IsTrue();
        (bool cellCommitted, bool cellFinished, string? cellFailure, _) = await AwaitOperation(
            cellTake!,
            runner,
            () => cellQueuedForDeletion = cell.IsQueuedForDeletion()
        );
        AssertThat(cellCommitted).IsTrue();
        AssertThat(cellFinished).IsTrue();
        AssertThat(cellFailure is null).IsTrue();
        AssertThat(cellQueuedForDeletion).IsTrue();
        AssertThat(inventory.GetItemCount(batteryId)).IsEqual(0);
        AssertThat(inventory.GetItemCount(cellId)).IsEqual(1);
        AssertThat(carry.CarriedItemId == cellId).IsTrue();
        AssertThat(worldSpawner.SuccessfulSpawnIds).ContainsExactly(new[] { "battery_spawn" });
        AssertThat(worldSpawner.LastRequest.Transform.Origin)
            .IsEqual(new Vector3(2.0f, 3.0f, 2.5f));

        worldSpawner.ShouldSucceed = false;
        CarryOperation? failedDrop = carry.TryStartDrop();
        AssertThat(failedDrop is not null).IsTrue();
        (bool failedDropCommitted, bool failedDropFinished, string? failedDropFailure, _) =
            await AwaitOperation(failedDrop!, runner);
        AssertThat(failedDropCommitted).IsFalse();
        AssertThat(failedDropFinished).IsFalse();
        AssertThat(failedDropFailure is not null).IsTrue();
        AssertThat(inventory.GetItemCount(cellId)).IsEqual(1);
        AssertThat(carry.CarriedItemId == cellId).IsTrue();

        worldSpawner.ShouldSucceed = true;
        CarryOperation? drop = carry.TryStartDrop();
        AssertThat(drop is not null).IsTrue();
        (bool dropCommitted, bool dropFinished, string? dropFailure, _) = await AwaitOperation(
            drop!,
            runner
        );
        AssertThat(dropCommitted).IsTrue();
        AssertThat(dropFinished).IsTrue();
        AssertThat(dropFailure is null).IsTrue();
        AssertThat(inventory.GetItemCount(cellId)).IsEqual(0);
        AssertThat(carry.CarriedItemId is null).IsTrue();

        CarryOperation? replacementTake = carry.TryStartTake(cellId, replacementCell);
        AssertThat(replacementTake is not null).IsTrue();
        (bool replacementCommitted, bool replacementFinished, string? replacementFailure, _) =
            await AwaitOperation(replacementTake!, runner);
        AssertThat(replacementCommitted).IsTrue();
        AssertThat(replacementFinished).IsTrue();
        AssertThat(replacementFailure is null).IsTrue();
        carry.QueueFree();
        await runner.SimulateFrames(1);

        AssertThat(inventory.GetItemCount(cellId)).IsEqual(0);
        AssertThat(worldSpawner.SuccessfulSpawnIds)
            .ContainsExactly(new[] { "battery_spawn", "cell_spawn", "cell_spawn" });
    }

    private static async Task<(
        bool Committed,
        bool Finished,
        string? FailureReason,
        bool Cancelled
    )> AwaitOperation(CarryOperation operation, ISceneRunner runner, Action? onCommitted = null)
    {
        bool committed = false;
        bool finished = false;
        bool cancelled = false;
        string? failureReason = null;

        operation.Committed += () =>
        {
            committed = true;
            onCommitted?.Invoke();
        };
        operation.Finished += () => finished = true;
        operation.Failed += reason => failureReason = reason;
        operation.Cancelled += () => cancelled = true;

        for (int frame = 0; frame < 8 && !finished && failureReason is null && !cancelled; frame++)
        {
            await runner.SimulateFrames(1);
        }

        return (committed, finished, failureReason, cancelled);
    }

    private static InventoryCatalog CreateCatalog(params CarriableItemDefinition[] definitions)
    {
        InventoryCatalog catalog = new();
        foreach (CarriableItemDefinition definition in definitions)
        {
            catalog.Items.Add(definition);
        }

        return catalog;
    }

    private static CarriableItemDefinition CreateDefinition(StringName itemId, string spawnId) =>
        new()
        {
            Id = itemId,
            DisplayName = itemId,
            SpawnDefinition = new SpawnDefinition { Id = new StringName(spawnId) },
        };

    private sealed class FixedOrientation(Transform3D visualTransform, Vector3 forwardVector)
        : IOriented
    {
        public Transform3D VisualTransform { get; } = visualTransform;
        public Vector3 ForwardVector { get; } = forwardVector;
    }

    private sealed class RecordingWorldSpawner(Node spawnRoot) : IWorldSpawner
    {
        public bool ShouldSucceed { get; set; } = true;
        public List<string> SuccessfulSpawnIds { get; } = new();
        public SpawnRequest LastRequest { get; private set; }

        public bool TrySpawn(StringName definitionId, in SpawnRequest request, out Node3D? spawned)
        {
            LastRequest = request;
            if (!ShouldSucceed)
            {
                spawned = null;
                return false;
            }

            SuccessfulSpawnIds.Add(definitionId);
            spawned = spawnRoot as Node3D;
            return true;
        }
    }
}
