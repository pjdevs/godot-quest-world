namespace QuestWorld.Tests.GameplayActions;

using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Presentation.UI;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Rules;
using GameplayActionPlugin.Runtime.Runner;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class GameplayActionPresenterTest
{
    private const string ActionScenePath =
        "res://addons/gameplay_action_plugin/scenes/GameplayActionPrompt.tscn";

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
        world.Runner.BindAction(world.Owned, "owned", ownedAction, Config("owned"));
        world.Runner.BindAction(external, "external", source, Config("external"));

        await world.Scene.SimulateFrames(1);

        AssertThat(world.Actions.GetChildCount()).IsEqual(1);
        AssertThat(world.Actions.GetChild(0) is GameplayActionPromptWidget).IsTrue();
        AssertThat(
                world.Runner.TryGetBinding(
                    world.Owned,
                    ownedAction.Definition!.Id,
                    ownedAction,
                    out _
                )
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
        world.Runner.BindAction(world.Owned, "blocked", blocked, Config("blocked"));
        world.Runner.BindAction(world.Owned, "hidden", hidden, Config("hidden"));
        world.Runner.BindAction(
            world.Owned,
            "automatic",
            automatic,
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
        GameplayAction sharedAction = AddAction(world.Owned, "shared");
        world.Runner.BindAction(world.Owned, "shared", sharedAction, Config("first"));
        world.Runner.BindAction(world.Owned, "shared", sharedAction, Config("second"));

        await world.Scene.SimulateFrames(1);

        AssertThat(world.Actions.GetChildCount()).IsEqual(2);
    }

    [TestCase]
    public async Task PresenterRebindsHoldProgressWithoutRecreatingTheWidget()
    {
        PresentationWorld world = BuildWorld();
        GameplayAction chargeAction = AddAction(world.Owned, "charge");
        world.Runner.BindAction(
            world.Owned,
            "charge",
            chargeAction,
            Config("charge", GameplayActionActivationMode.Hold, holdDuration: 1.0f)
        );
        world.Runner.TryStartActionInput("charge");
        await world.Scene.SimulateFrames(1);
        ulong widgetId = world.Actions.GetChild(0).GetInstanceId();

        world.Runner.AdvanceGestures(0.25f);
        await world.Scene.SimulateFrames(1);

        GameplayActionPromptWidget widget = world.Actions.GetChild<GameplayActionPromptWidget>(0);
        AssertThat(widget.GetInstanceId()).IsEqual(widgetId);
        AssertThat(widget.ActionHoldProgress!.Value > 0.25f).IsTrue();
        AssertThat(widget.ActionHoldProgress.Value < 0.4f).IsTrue();
    }

    [TestCase]
    public async Task PresenterRemovesUnboundActionsAndClearsWhenRunnerIsNotLocal()
    {
        PresentationWorld world = BuildWorld();
        GameplayAction ownedAction = AddAction(world.Owned, "owned");
        GameplayActionBinding binding = world.Runner.BindAction(
            world.Owned,
            "owned",
            ownedAction,
            Config("owned")
        )!;
        await world.Scene.SimulateFrames(1);
        AssertThat(world.Actions.GetChildCount()).IsEqual(1);

        world.Runner.UnbindAction(binding.Id);
        await world.Scene.SimulateFrames(1);
        AssertThat(world.Actions.GetChildCount()).IsEqual(0);

        world.Runner.BindAction(world.Owned, "owned", ownedAction, Config("owned"));
        await world.Scene.SimulateFrames(1);
        world.Runner.OwnerPeerId = 2;
        await world.Scene.SimulateFrames(1);

        AssertThat(world.Actions.GetChildCount()).IsEqual(0);
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
