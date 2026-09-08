namespace QuestWorld.Tests.Game;

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
        CarryComponent carry = new();

        carry.CarriedItemId = new StringName();

        AssertThat(carry.CarriedItemId is null).IsTrue();
        AssertThat(carry.IsCarrying).IsFalse();
        carry.Free();
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

        AssertThat(carry.TryTake(batteryId, battery)).IsTrue();
        AssertThat(battery.IsQueuedForDeletion()).IsTrue();
        AssertThat(inventory.GetItemCount(batteryId)).IsEqual(1);
        AssertThat(carry.CarriedItemId == batteryId).IsTrue();

        AssertThat(carry.TryTake(cellId, cell)).IsTrue();
        AssertThat(cell.IsQueuedForDeletion()).IsTrue();
        AssertThat(inventory.GetItemCount(batteryId)).IsEqual(0);
        AssertThat(inventory.GetItemCount(cellId)).IsEqual(1);
        AssertThat(carry.CarriedItemId == cellId).IsTrue();
        AssertThat(worldSpawner.SuccessfulSpawnIds).ContainsExactly(new[] { "battery_spawn" });
        AssertThat(worldSpawner.LastRequest.Transform.Origin)
            .IsEqual(new Vector3(2.0f, 3.0f, 2.5f));

        worldSpawner.ShouldSucceed = false;
        AssertThat(carry.TryDrop(cellId)).IsFalse();
        AssertThat(inventory.GetItemCount(cellId)).IsEqual(1);
        AssertThat(carry.CarriedItemId == cellId).IsTrue();

        worldSpawner.ShouldSucceed = true;
        AssertThat(carry.TryDrop(cellId)).IsTrue();
        AssertThat(inventory.GetItemCount(cellId)).IsEqual(0);
        AssertThat(carry.CarriedItemId is null).IsTrue();

        AssertThat(carry.TryTake(cellId, replacementCell)).IsTrue();
        carry.QueueFree();
        await runner.SimulateFrames(1);

        AssertThat(inventory.GetItemCount(cellId)).IsEqual(0);
        AssertThat(worldSpawner.SuccessfulSpawnIds)
            .ContainsExactly(new[] { "battery_spawn", "cell_spawn", "cell_spawn" });
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
