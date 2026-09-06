namespace QuestWorld.Tests.GameplayActions;

using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Rules;
using QuestWorld.GameplayActions.Runtime.Runner;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class GameplayActionBindingTest
{
    [TestCase]
    public void BindingKeepsLocalInputAndPresentationContextWithoutTakingActionOwnership()
    {
        GameplayActionComponent component = CreateComponentWithAction("heal");
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        Node source = AutoFree(new Node());
        Node presentation = AutoFree(new Node());
        GameplayActionBindingConfig config = new()
        {
            InputActionName = new StringName("heal"),
            ActivationMode = GameplayActionActivationMode.Press,
            InputRequirement = GameplayActionInputRequirement.None,
            Priority = 12,
        };

        GameplayActionBinding? binding = runner.BindAction(
            component,
            new StringName("heal"),
            source,
            config,
            Variant.From(presentation)
        );

        AssertThat(binding is not null).IsTrue();
        AssertThat(binding!.Component == component).IsTrue();
        AssertThat(binding.ActionId).IsEqual(new StringName("heal"));
        AssertThat(binding.Source == source).IsTrue();
        AssertThat(binding.InputActionName).IsEqual(new StringName("heal"));
        AssertThat(binding.Priority).IsEqual(12);
        AssertThat(binding.PresentationContext.AsGodotObject() == presentation).IsTrue();
        AssertThat(component.ResolveAction(new StringName("heal"))!.Component == component)
            .IsTrue();
    }

    [TestCase]
    public void BindingCanBeRemovedAloneOrBySourceWithoutDisturbingOtherSources()
    {
        GameplayActionComponent component = CreateComponentWithAction("heal");
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        Node firstSource = AutoFree(new Node());
        Node secondSource = AutoFree(new Node());
        GameplayActionBinding first = runner.BindAction(
            component,
            new StringName("heal"),
            firstSource,
            Press("heal")
        )!;
        runner.BindAction(component, new StringName("heal"), secondSource, Press("heal"));

        AssertThat(runner.UnbindAction(first.Id)).IsTrue();
        AssertThat(runner.UnbindAction(first.Id)).IsFalse();
        AssertThat(runner.GetBindings()).HasSize(1);

        AssertThat(runner.UnbindSource(secondSource)).IsEqual(1);
        AssertThat(runner.GetBindings()).IsEmpty();
        AssertThat(component.ResolveAction(new StringName("heal")) is not null).IsTrue();
    }

    [TestCase]
    public void InvalidBindingInputContractsAreRejected()
    {
        GameplayActionComponent component = CreateComponentWithAction("heal");
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        Node source = AutoFree(new Node());

        GameplayActionBinding? emptyPress = runner.BindAction(
            component,
            new StringName("heal"),
            source,
            Press(string.Empty)
        );
        GameplayActionBinding? zeroHold = runner.BindAction(
            component,
            new StringName("heal"),
            source,
            new GameplayActionBindingConfig
            {
                InputActionName = new StringName("heal"),
                ActivationMode = GameplayActionActivationMode.Hold,
                HoldDuration = 0.0f,
            }
        );
        GameplayActionBinding? sustainedAutomatic = runner.BindAction(
            component,
            new StringName("heal"),
            source,
            new GameplayActionBindingConfig
            {
                ActivationMode = GameplayActionActivationMode.Automatic,
                InputRequirement = GameplayActionInputRequirement.Pressed,
            }
        );
        GameplayActionBinding? irrelevantHoldDuration = runner.BindAction(
            component,
            "heal",
            source,
            new GameplayActionBindingConfig
            {
                InputActionName = "heal",
                ActivationMode = GameplayActionActivationMode.Press,
                HoldDuration = 0.5f,
            }
        );
        GameplayActionBinding? releasedButStillPressed = runner.BindAction(
            component,
            "heal",
            source,
            new GameplayActionBindingConfig
            {
                InputActionName = "heal",
                ActivationMode = GameplayActionActivationMode.Release,
                InputRequirement = GameplayActionInputRequirement.Pressed,
            }
        );

        AssertThat(emptyPress is null).IsTrue();
        AssertThat(zeroHold is null).IsTrue();
        AssertThat(sustainedAutomatic is null).IsTrue();
        AssertThat(irrelevantHoldDuration is null).IsTrue();
        AssertThat(releasedButStillPressed is null).IsTrue();
        AssertThat(runner.GetBindings()).IsEmpty();
    }

    [TestCase]
    public void AutomaticBindingRequestsOncePerExplicitEligibilityWindow()
    {
        MutableRule rule = new(new GameplayActionBlocked("Not yet."));
        TestGameplayActionExecutor executor = new();
        GameplayActionComponent component = CreateComponentWithAction("heal", executor, rule);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        Node source = AutoFree(new Node());
        GameplayActionBinding binding = runner.BindAction(
            component,
            new StringName("heal"),
            source,
            new GameplayActionBindingConfig
            {
                ActivationMode = GameplayActionActivationMode.Automatic,
            }
        )!;

        rule.Result = new GameplayActionAllowed();
        runner.InvalidateBinding(binding.Id);
        runner.InvalidateBinding(binding.Id);

        AssertThat(executor.ExecuteCount).IsEqual(1);

        rule.Result = new GameplayActionBlocked("Spent.");
        runner.InvalidateSource(source);
        rule.Result = new GameplayActionAllowed();
        runner.InvalidateAction(component, new StringName("heal"));

        AssertThat(executor.ExecuteCount).IsEqual(2);
    }

    [TestCase]
    public void CompetingAutomaticEdgesConsumeOneSharedEligibilityWindow()
    {
        MutableRule rule = new(new GameplayActionBlocked("Not yet."));
        TestGameplayActionExecutor firstExecutor = new();
        TestGameplayActionExecutor secondExecutor = new();
        GameplayActionComponent component = CreateComponentWithAction("first", firstExecutor, rule);
        GameplayAction secondAction = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "second" },
                Executor = secondExecutor,
            }
        );
        secondAction.AddChild(secondExecutor);
        secondAction.Rules.Add(rule);
        component.AddAction(secondAction);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        Node source = AutoFree(new Node());
        GameplayActionBindingConfig automatic = new()
        {
            ActivationMode = GameplayActionActivationMode.Automatic,
        };
        runner.BindAction(component, "first", source, automatic);
        runner.BindAction(component, "second", source, automatic);

        rule.Result = new GameplayActionAllowed();
        runner.InvalidateSource(source);
        runner.InvalidateSource(source);

        AssertThat(firstExecutor.ExecuteCount).IsEqual(1);
        AssertThat(secondExecutor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public void InvalidationNotifiesOnlyTheAffectedLocalBindings()
    {
        GameplayActionComponent component = CreateComponentWithAction("heal");
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        Node firstSource = AutoFree(new Node());
        Node secondSource = AutoFree(new Node());
        GameplayActionBinding first = runner.BindAction(
            component,
            "heal",
            firstSource,
            Press("first")
        )!;
        GameplayActionBinding second = runner.BindAction(
            component,
            "heal",
            secondSource,
            Press("second")
        )!;
        int notifications = 0;
        long lastBindingId = 0;
        runner.GameplayActionBindingInvalidated += bindingId =>
        {
            notifications++;
            lastBindingId = bindingId;
        };

        runner.InvalidateBinding(first.Id);
        AssertThat(notifications).IsEqual(1);
        AssertThat(lastBindingId).IsEqual((long)first.Id);

        runner.InvalidateSource(secondSource);
        AssertThat(notifications).IsEqual(2);
        AssertThat(lastBindingId).IsEqual((long)second.Id);

        runner.InvalidateAction(component, "heal");
        AssertThat(notifications).IsEqual(4);

        runner.InvalidateBinding(999UL);
        AssertThat(notifications).IsEqual(4);
    }

    private static GameplayActionBindingConfig Press(string input) =>
        new()
        {
            InputActionName = new StringName(input),
            ActivationMode = GameplayActionActivationMode.Press,
        };

    private static GameplayActionComponent CreateComponentWithAction(
        string id,
        TestGameplayActionExecutor? executor = null,
        GameplayActionRule? rule = null
    )
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        executor ??= new TestGameplayActionExecutor();
        GameplayAction action = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = new StringName(id) },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        if (rule is not null)
        {
            action.Rules.Add(rule);
        }

        component.AddAction(action);
        return component;
    }

    private sealed partial class MutableRule(GameplayActionAvailability result) : GameplayActionRule
    {
        public GameplayActionAvailability Result { get; set; } = result;

        public override GameplayActionAvailability Evaluate(in GameplayActionContext context) =>
            Result;
    }
}
