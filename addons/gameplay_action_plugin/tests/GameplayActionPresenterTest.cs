namespace QuestWorld.Tests.GameplayActions;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Presentation.UI;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Rules;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Inventory;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class GameplayActionPresenterTest
{
    private const string ActionScenePath =
        "res://addons/gameplay_action_plugin/scenes/GameplayActionPrompt.tscn";
    private const string BatteryScenePath = "res://quest_world/interactibles/battery/Battery.tscn";
    private const string CharacterScenePath = "res://quest_world/character/Character.tscn";

    [TestCase]
    public async Task PresenterShowsOwnedActionsAndIgnoresExternalBindings()
    {
        PresentationWorld world = BuildWorld();
        GameplayAction ownedAction = AddAction(world.Owned, "owned");
        GameplayActionComponent external = new() { Name = "ExternalActions" };
        world.Root.AddChild(external);
        AddAction(external, "external");
        Node source = new();
        world.Root.AddChild(source);
        world.Runner.BindAction(world.Owned, "owned", source, Config("owned"));
        world.Runner.BindAction(external, "external", source, Config("external"));

        await world.Scene.SimulateFrames(1);

        AssertThat(world.Actions.GetChildCount()).IsEqual(1);
        AssertThat(world.Actions.GetChild(0) is GameplayActionPromptWidget).IsTrue();
        AssertThat(
                world.Runner.TryGetBinding(world.Owned, ownedAction.Definition!.Id, source, out _)
            )
            .IsTrue();
    }

    [TestCase]
    public async Task PresenterKeepsBlockedActionsVisibleAndOmitsHiddenAndAutomaticActions()
    {
        PresentationWorld world = BuildWorld();
        GameplayAction blocked = AddAction(world.Owned, "blocked");
        blocked.Rules.Add(new FixedAvailabilityRule(new GameplayActionBlocked("Locked")));
        GameplayAction hidden = AddAction(world.Owned, "hidden");
        hidden.Rules.Add(new FixedAvailabilityRule(new GameplayActionHidden()));
        GameplayAction automatic = AddAction(world.Owned, "automatic");
        Node source = new();
        world.Root.AddChild(source);
        world.Runner.BindAction(world.Owned, "blocked", source, Config("blocked"));
        world.Runner.BindAction(world.Owned, "hidden", source, Config("hidden"));
        world.Runner.BindAction(
            world.Owned,
            "automatic",
            source,
            Config(string.Empty, GameplayActionActivationMode.Automatic)
        );

        await world.Scene.SimulateFrames(1);

        AssertThat(world.Actions.GetChildCount()).IsEqual(1);
        GameplayActionPromptWidget widget = world.Actions.GetChild<GameplayActionPromptWidget>(0);
        AssertThat(widget.ActionNameLabel!.Text).IsEqual("blocked: Locked");
        AssertThat(automatic.IsInsideTree()).IsTrue();
    }

    [TestCase]
    public async Task PresenterUsesBindingIdsForTwoBindingsOfTheSameAction()
    {
        PresentationWorld world = BuildWorld();
        AddAction(world.Owned, "shared");
        Node firstSource = new();
        Node secondSource = new();
        world.Root.AddChild(firstSource);
        world.Root.AddChild(secondSource);
        world.Runner.BindAction(world.Owned, "shared", firstSource, Config("first"));
        world.Runner.BindAction(world.Owned, "shared", secondSource, Config("second"));

        await world.Scene.SimulateFrames(1);

        AssertThat(world.Actions.GetChildCount()).IsEqual(2);
    }

    [TestCase]
    public async Task PresenterRebindsHoldProgressWithoutRecreatingTheWidget()
    {
        PresentationWorld world = BuildWorld();
        AddAction(world.Owned, "charge");
        Node source = new();
        world.Root.AddChild(source);
        world.Runner.BindAction(
            world.Owned,
            "charge",
            source,
            Config("charge", GameplayActionActivationMode.Hold, holdDuration: 1.0f)
        );
        world.Runner.TryStartActionInput("charge");
        await world.Scene.SimulateFrames(1);
        ulong widgetId = world.Actions.GetChild(0).GetInstanceId();

        world.Runner.AdvanceGestures(0.25f);
        await world.Scene.SimulateFrames(1);

        GameplayActionPromptWidget widget = world.Actions.GetChild<GameplayActionPromptWidget>(0);
        AssertThat(widget.GetInstanceId()).IsEqual(widgetId);
        AssertThat(widget.ActionProgress!.Value > 0.25f).IsTrue();
        AssertThat(widget.ActionProgress.Value < 0.4f).IsTrue();
    }

    [TestCase]
    public async Task PresenterRemovesUnboundActionsAndClearsWhenRunnerIsNotLocal()
    {
        PresentationWorld world = BuildWorld();
        AddAction(world.Owned, "owned");
        Node source = new();
        world.Root.AddChild(source);
        GameplayActionBinding binding = world.Runner.BindAction(
            world.Owned,
            "owned",
            source,
            Config("owned")
        )!;
        await world.Scene.SimulateFrames(1);
        AssertThat(world.Actions.GetChildCount()).IsEqual(1);

        world.Runner.UnbindAction(binding.Id);
        await world.Scene.SimulateFrames(1);
        AssertThat(world.Actions.GetChildCount()).IsEqual(0);

        world.Runner.BindAction(world.Owned, "owned", source, Config("owned"));
        await world.Scene.SimulateFrames(1);
        world.Runner.OwnerPeerId = 2;
        await world.Scene.SimulateFrames(1);

        AssertThat(world.Actions.GetChildCount()).IsEqual(0);
    }

    [TestCase]
    public async Task CharacterPresentsAnInventoryGrantedActionThroughTheGenericPresenter()
    {
        global::Character character = GD.Load<PackedScene>(CharacterScenePath)
            .Instantiate<global::Character>();
        Node3D battery = GD.Load<PackedScene>(BatteryScenePath).Instantiate<Node3D>();
        Node3D root = new() { Name = "World" };
        root.AddChild(character);
        root.AddChild(battery);
        ISceneRunner scene = ISceneRunner.Load(root, autoFree: true);
        await scene.SimulateFrames(1);

        GameplayActionRunner runner = character.GetNode<GameplayActionRunner>(
            "GameplayActionRunner"
        );
        GameplayActionComponent actions = character.GetNode<GameplayActionComponent>(
            "GameplayActions"
        );
        VBoxContainer actionContainer = character.GetNode<VBoxContainer>(
            "GameplayActionPresenter/ActionContainer/ActionList"
        );
        InventoryComponent inventory = character.GetNode<InventoryComponent>("InventoryComponent");
        InteractiveComponent batteryInteraction = battery.GetNode<InteractiveComponent>(
            "InteractiveComponent"
        );

        AssertThat(actionContainer.GetChildCount()).IsEqual(0);
        AssertThat(runner.GetBindings().Count).IsEqual(1);
        AssertThat(runner.GetBindings()[0].Component == actions).IsFalse();
        AssertThat(batteryInteraction.ActionPromptScene!.ResourcePath).IsEqual(ActionScenePath);
        GameplayActionComponent batteryActions = battery.GetNode<GameplayActionComponent>(
            "ActionComponent"
        );
        GameplayActionExecutionResult takeResult = batteryActions.ExecuteAction(
            "take",
            out _,
            character
        );
        AssertThat(takeResult is GameplayActionExecutionCompleted).IsTrue();
        AssertThat(inventory.GetItemCount("battery")).IsEqual(1);
        await scene.SimulateFrames(1);

        AssertThat(actions.ResolveAction("drop_battery") is InputGameplayAction).IsTrue();
        AssertThat(runner.GetBindings().Count).IsEqual(1);
        AssertThat(runner.GetBindings()[0].Component == actions).IsTrue();
        AssertThat(actionContainer.GetChildCount()).IsEqual(1);
        AssertThat(actionContainer.GetChild<GameplayActionPromptWidget>(0).ActionNameLabel!.Text)
            .IsEqual("Drop Battery");

        AssertThat(inventory.RemoveItem("battery")).IsEqual(1);
        await scene.SimulateFrames(1);

        AssertThat(actions.ResolveAction("drop_battery") is null).IsTrue();
        AssertThat(actionContainer.GetChildCount()).IsEqual(0);
    }

    private static PresentationWorld BuildWorld()
    {
        Node root = new() { Name = "World" };
        GameplayActionComponent owned = new() { Name = "OwnedActions" };
        root.AddChild(owned);
        GameplayActionRunner runner = new() { Name = "Runner", OwnedActionComponent = owned };
        root.AddChild(runner);
        VBoxContainer actions = new() { Name = "Actions" };
        root.AddChild(actions);
        GameplayActionPresenter presenter = new()
        {
            Name = "Presenter",
            ActionRunner = runner,
            ActionContainer = actions,
            ActionScene = GD.Load<PackedScene>(ActionScenePath),
        };
        root.AddChild(presenter);
        return new PresentationWorld(
            root,
            runner,
            owned,
            actions,
            ISceneRunner.Load(root, autoFree: true)
        );
    }

    private static GameplayAction AddAction(GameplayActionComponent component, string id)
    {
        TestGameplayActionExecutor executor = new();
        GameplayAction action = new()
        {
            Name = id,
            Definition = new GameplayActionDefinition { Id = new StringName(id), Label = id },
            Executor = executor,
        };
        action.AddChild(executor);
        component.AddAction(action);
        return action;
    }

    private static GameplayActionBindingConfig Config(
        string input,
        GameplayActionActivationMode mode = GameplayActionActivationMode.Press,
        float holdDuration = 0.0f
    ) =>
        new()
        {
            InputActionName = new StringName(input),
            ActivationMode = mode,
            HoldDuration = holdDuration,
        };

    private sealed partial class FixedAvailabilityRule(GameplayActionAvailability availability)
        : GameplayActionRule
    {
        public override GameplayActionAvailability Evaluate(in GameplayActionContext context) =>
            availability;
    }

    private sealed record PresentationWorld(
        Node Root,
        GameplayActionRunner Runner,
        GameplayActionComponent Owned,
        VBoxContainer Actions,
        ISceneRunner Scene
    );
}
