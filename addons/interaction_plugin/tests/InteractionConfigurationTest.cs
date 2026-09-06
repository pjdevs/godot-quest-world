namespace QuestWorld.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using InteractionPlugin.Editor;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Editor;
using QuestWorld.GameplayActions.Integration.Stateful;
using QuestWorld.GameplayActions.Presentation.UI;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionConfigurationTest
{
    [TestCase]
    public void InteractiveComponentRequiresExplicitAreaAndAnchor()
    {
        InteractiveComponent interactive = new();

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Contains("InteractionArea must be assigned.")).IsTrue();
        AssertThat(warnings.Contains("InteractionAnchor must be assigned.")).IsTrue();
    }

    [TestCase]
    public void InteractiveComponentAcceptsAssignedAreaAndAnchor()
    {
        InteractiveComponent interactive = NewConfiguredInteractive();

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
    }

    [TestCase]
    public void InteractiveComponentAllowsOptionalReferencesToRemainUnset()
    {
        InteractiveComponent interactive = NewConfiguredInteractive();

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
    }

    [TestCase]
    public void InteractiveComponentDoesNotExposeAnOwnerReference()
    {
        AssertThat(typeof(InteractiveComponent).GetProperty("InteractionOwner") == null).IsTrue();
    }

    [TestCase]
    public void InteractiveComponentNoLongerInterpretsWorldStateItself()
    {
        AssertThat(typeof(InteractiveComponent).GetProperty("BusyReason") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetProperty("ActivatedReason") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetProperty("InteractionRules") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("EvaluateStatus") == null).IsTrue();
    }

    [TestCase]
    public void InteractorRequiresAnExplicitDetector()
    {
        InteractionInteractor interactor = new();

        string[] warnings = InteractionValidator.Validate(interactor).ToArray();

        AssertThat(warnings.Contains("Detector must be assigned.")).IsTrue();
    }

    [TestCase]
    public void DetectorRequiresAnExplicitViewOriginOnly()
    {
        AreaInteractionDetector detector = new();

        string[] warnings = InteractionValidator.Validate(detector).ToArray();

        AssertThat(warnings.Contains("ViewOrigin must be assigned.")).IsTrue();
        AssertThat(warnings.Any(warning => warning.Contains("InteractionOrigin"))).IsFalse();
    }

    [TestCase]
    public void DetectorReportsNegativeRangeAndScoreSettings()
    {
        AreaInteractionDetector detector = new()
        {
            MaxDistance = -1.0f,
            DistanceScoreCoefficient = -1.0f,
        };

        string[] warnings = InteractionValidator.Validate(detector).ToArray();

        AssertThat(warnings.Contains("MaxDistance must not be negative.")).IsTrue();
        AssertThat(warnings.Contains("DistanceScoreCoefficient must not be negative.")).IsTrue();
    }

    [TestCase]
    public void PresenterRequiresExplicitInteractorAndCamera()
    {
        InteractionPresenter presenter = new();

        string[] warnings = InteractionValidator.Validate(presenter).ToArray();

        AssertThat(warnings.Contains("Interactor must be assigned.")).IsTrue();
        AssertThat(warnings.Contains("Camera must be assigned.")).IsTrue();
    }

    [TestCase]
    public void LongActionExecutorRequiresTheStateComponentItDrives()
    {
        TransitionStateGameplayActionExecutor executor = new();

        string[] warnings = GameplayActionValidator.Validate(executor).ToArray();

        AssertThat(warnings.Contains("Stateful must be assigned.")).IsTrue();
    }

    [TestCase]
    public void TimedStateTransitionRequiresAPositiveFiniteDuration()
    {
        TimedTransitionStateGameplayActionExecutor executor = new() { Duration = -1.0f };

        string[] warnings = GameplayActionValidator.Validate(executor).ToArray();

        AssertThat(warnings.Contains("Duration must be finite and greater than zero.")).IsTrue();
        AssertThat(warnings.Contains("Stateful must be assigned.")).IsTrue();
    }

    [TestCase]
    public void MissingFocusIsRepresentedByAnAbsentPresentation()
    {
        InteractionInteractor interactor = new();

        InteractionTargetPresentation? presentation = interactor.GetInteractionPresentation();

        AssertThat(presentation == null).IsTrue();
    }

    [TestCase]
    public void InteractiveDoesNotOwnTargetWideInputOrAvailabilityPresentation()
    {
        AssertThat(typeof(InteractiveComponent).GetProperty("InteractionActionName") == null)
            .IsTrue();
        AssertThat(typeof(InteractiveComponent).GetProperty("PromptScene") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetProperty("ActionPromptScene") != null).IsTrue();
    }

    [TestCase]
    public void InteractorDoesNotExposeDetectionCollectionsOrConfigurationState()
    {
        AssertThat(typeof(InteractionInteractor).GetProperty("IndicatedInteractives") == null)
            .IsTrue();
        AssertThat(typeof(InteractionInteractor).GetProperty("InteractiveCandidates") == null)
            .IsTrue();
        AssertThat(typeof(InteractionInteractor).GetProperty("IsConfigurationValid") == null)
            .IsTrue();
    }

    [TestCase]
    public void InputAndAutomationBelongToTheActionNotToTheInteractorOrTarget()
    {
        AssertThat(typeof(InteractionInteractor).GetProperty("InteractionActionName") == null)
            .IsTrue();
        AssertThat(typeof(InteractiveComponent).GetProperty("AutomaticInteraction") == null)
            .IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("Priority") == null).IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("Automatic") == null).IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("DefaultBindingConfig") != null).IsTrue();
    }

    [TestCase]
    public void ActionExecutionVisibilityDefaultsToRequesterOnly()
    {
        InteractionAction action = new();

        AssertThat(action.ExecutionVisibility)
            .IsEqual(GameplayActionExecutionVisibility.RequesterOnly);
    }

    [TestCase]
    public void ReplicatedActionRequiresAMatchingExecutionSynchronizer()
    {
        InteractiveComponent interactive = NewConfiguredInteractive();
        interactive.ActionAt(0).ExecutionVisibility = GameplayActionExecutionVisibility.Replicated;

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(
                warnings.Contains(
                    "Replicated actions require a GameplayActionExecutionSynchronizer targeting the assigned ActionComponent."
                )
            )
            .IsTrue();
    }

    [TestCase]
    public void ExecutionSynchronizerRequiresAComponent()
    {
        GameplayActionExecutionSynchronizer synchronizer = new();

        string[] warnings = GameplayActionValidator.Validate(synchronizer).ToArray();

        AssertThat(warnings.Contains("Component must be assigned.")).IsTrue();
    }

    [TestCase]
    public void WorldStateAndInteractionStayIndependent()
    {
        AssertThat(typeof(StatefulComponent).GetProperty("ActiveInteractor") == null).IsTrue();
        AssertThat(typeof(StatefulComponent).GetMethod("ExecuteAction") == null).IsTrue();
        AssertThat(typeof(StatefulComponent).GetMethod("CompleteExecution") == null).IsTrue();
        AssertThat(typeof(StatefulComponent).GetMethod("CancelExecution") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetProperty("Stateful") == null).IsTrue();
        AssertThat(
                typeof(InteractiveComponent).Assembly.GetType(
                    "QuestWorld.Interaction.Runtime.State.InteractionStateful"
                ) == null
            )
            .IsTrue();
        AssertThat(
                typeof(InteractiveComponent).Assembly.GetType(
                    "QuestWorld.Interaction.InteractionState"
                ) == null
            )
            .IsTrue();
    }

    [TestCase]
    public void ActionPresentationContainsOnlyActionAndHoldData()
    {
        AssertThat(typeof(GameplayActionPresentation).GetProperty("HasTimed" + "Execution") == null)
            .IsTrue();
        AssertThat(typeof(GameplayActionPresentation).GetProperty("ExecutionProgress") == null)
            .IsTrue();

        System.Reflection.ParameterInfo[] parameters = typeof(IGameplayActionWidget)
            .GetMethod("Bind")!
            .GetParameters();
        AssertThat(parameters.Length).IsEqual(2);
        AssertThat(parameters[0].ParameterType)
            .IsEqual(typeof(GameplayActionPresentation).MakeByRefType());
        AssertThat(parameters[1].ParameterType)
            .IsEqual(typeof(GameplayActionExecutionPresentation?));
    }

    [TestCase]
    public void InteractionRulesUseTheGenericUnavailableKind()
    {
        AssertThat(
                typeof(StatefulStateInteractionRule)
                    .GetProperty("MismatchAvailability")!
                    .PropertyType
            )
            .IsEqual(typeof(GameplayActionUnavailableKind));
        AssertThat(
                typeof(InteractionRule)
                    .GetMethods()
                    .Single(method =>
                        method.Name == "Evaluate"
                        && method.ReturnType == typeof(GameplayActionAvailability)
                        && method.GetParameters()[0].ParameterType
                            == typeof(InteractionContext).MakeByRefType()
                    )
                    .ReturnType
            )
            .IsEqual(typeof(GameplayActionAvailability));
    }

    [TestCase]
    public void ExecutionBelongsToAnExecutorInsteadOfASignalSubscriber()
    {
        AssertThat(
                typeof(InteractionAction).GetProperty(
                    "Executor",
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly
                ) == null
            )
            .IsTrue();
        AssertThat(typeof(GameplayAction).GetProperty("Executor") != null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethods().Any(m => m.Name == "ExecuteAction"))
            .IsFalse();
        AssertThat(typeof(InteractiveComponent).GetMethod("CompleteExecution") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("StartInteraction") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("StartInteractionPhase") == null)
            .IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("EndInteractionPhase") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("ReleaseInteractionInput") == null)
            .IsTrue();
    }

    [TestCase]
    public void LevelWiresTheWallControlButtonToTheStateOfAnotherScene()
    {
        const string buttonPath = "./Level/Button/GameplayActions";
        SceneState level = GD.Load<PackedScene>("res://quest_world/levels/test_world.tscn")
            .GetState();

        AssertThat(
                Declared(level, $"{buttonPath}/RaiseAction/RaiseExecutor", "Stateful")
                    .AsNodePath()
                    .ToString()
            )
            .IsEqual("../../../../LeverWall/StatefulComponent");
        AssertThat(
                Declared(level, $"{buttonPath}/LowerAction/LowerExecutor", "Stateful")
                    .AsNodePath()
                    .ToString()
            )
            .IsEqual("../../../../LeverWall/StatefulComponent");

        Godot.Collections.Array rules = Declared(level, $"{buttonPath}/RaiseAction", "Rules")
            .AsGodotArray();
        StatefulStateInteractionRule phase = rules[0].As<StatefulStateInteractionRule>();
        StatefulStateInteractionRule ready = rules[1].As<StatefulStateInteractionRule>();

        AssertThat(rules.Count).IsEqual(2);
        AssertThat(phase.StatefulPath.ToString()).IsEqual("../../../LeverWall/StatefulComponent");
        AssertThat(phase.ExpectedStates.Count).IsEqual(2);
        AssertThat(phase.MismatchAvailability).IsEqual(GameplayActionUnavailableKind.Hidden);
        AssertThat(ready.ExpectedStates.Count).IsEqual(1);
        AssertThat(ready.MismatchAvailability).IsEqual(GameplayActionUnavailableKind.Blocked);
        AssertThat(ready.BlockReason).IsEqual("The wall is moving.");
    }

    private static Variant Declared(SceneState state, string nodePath, string property)
    {
        for (int node = 0; node < state.GetNodeCount(); node++)
        {
            if (state.GetNodePath(node).ToString() != nodePath)
            {
                continue;
            }

            for (int index = 0; index < state.GetNodePropertyCount(node); index++)
            {
                if (state.GetNodePropertyName(node, index).ToString() == property)
                {
                    return state.GetNodePropertyValue(node, index);
                }
            }
        }

        return new Variant();
    }

    [TestCase]
    public void InteractionInputSignalsNoLongerExistAsACommandPath()
    {
        InteractiveComponent interactive = new();

        try
        {
            string[] signals = interactive
                .GetSignalList()
                .Select(signal => signal["name"].AsString())
                .ToArray();

            AssertThat(signals.Contains("InteractionInputStarted")).IsFalse();
            AssertThat(signals.Contains("InteractionInputEnded")).IsFalse();
            AssertThat(signals.Contains("InteractionActionStarted")).IsTrue();
            AssertThat(signals.Contains("InteractionActionCompleted")).IsTrue();
            AssertThat(signals.Contains("InteractionActionCancelled")).IsTrue();
            AssertThat(signals.Contains("InteractionActionFailed")).IsTrue();
            AssertThat(signals.Contains("InteractionActionRejected")).IsTrue();
        }
        finally
        {
            interactive.Free();
        }
    }

    [TestCase]
    public void InteractiveReportsActionsWithoutDefinitionOrExecutor()
    {
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            ActionComponent = new() { Actions = { new InteractionAction() } },
        };

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Contains("Actions[0] has no Definition.")).IsTrue();
    }

    [TestCase]
    public async Task InteractiveValidatorResolvesActionStateRulesFromTheirAction()
    {
        Node3D root = new();
        Node3D owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        StatefulComponent stateful = new() { Name = "StatefulComponent" };
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = owner,
        };
        InteractionAction action = NewAction(new GameplayActionDefinition { Id = "open" });
        action.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = new NodePath("../../StatefulComponent"),
                ExpectedStates = { new StringName("closed") },
            }
        );
        owner.AddChild(area);
        owner.AddChild(stateful);
        owner.AddChild(interactive);
        interactive.AddAction(action);
        root.AddChild(owner);
        ISceneRunner runner = ISceneRunner.Load(root);
        await runner.SimulateFrames(1);

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Any(warning => warning.Contains("does not resolve"))).IsFalse();
    }

    [TestCase]
    public void InteractiveReportsDuplicateActionIds()
    {
        GameplayActionDefinition definition = new() { Id = "open" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            ActionComponent = new() { Actions = { NewAction(definition), NewAction(definition) } },
        };

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Contains("Actions declare the action id 'open' more than once."))
            .IsTrue();
    }

    [TestCase]
    public void InteractiveReportsTwoActionsSharingOneTrigger()
    {
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            ActionComponent = new()
            {
                Actions =
                {
                    NewAction(new GameplayActionDefinition { Id = "open" }),
                    NewAction(new GameplayActionDefinition { Id = "force" }),
                },
            },
        };

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Any(warning => warning.Contains("share the input 'interact'")))
            .IsTrue();
    }

    [TestCase]
    public void InteractiveAcceptsTwoActionsOnOneTriggerSeparatedByPriority()
    {
        // Sharing an input and a threshold is how "open" and "unlock" alternate on one key: the
        // resolver separates them by availability, then by priority. Only a tie the author did not
        // break is worth reporting, because below priority the identifier order decides.
        InteractionAction open = NewAction(new GameplayActionDefinition { Id = "open" });
        InteractionAction unlock = NewAction(new GameplayActionDefinition { Id = "unlock" });
        unlock.DefaultBindingConfig!.Priority = 10;
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            ActionComponent = new() { Actions = { open, unlock } },
        };

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Any(warning => warning.Contains("share the input 'interact'")))
            .IsFalse();
    }

    [TestCase]
    public void ActionRequiresADefinitionAndAnExecutor()
    {
        InteractionAction action = new();

        string[] warnings = InteractionValidator.Validate(action).ToArray();

        AssertThat(warnings.Contains("Definition must be assigned.")).IsTrue();
        AssertThat(warnings.Contains("Executor must be assigned.")).IsTrue();
    }

    [TestCase]
    public void ActionDefinitionRequiresAnId()
    {
        GameplayActionDefinition definition = new();

        string[] warnings = GameplayActionValidator.Validate(definition).ToArray();

        AssertThat(warnings.Contains("Id must be assigned.")).IsTrue();
    }

    [TestCase]
    public void ShortActionExecutorReportsAStateOutsideTheSchema()
    {
        SetStateGameplayActionExecutor executor = new()
        {
            Stateful = new StatefulComponent
            {
                Schema = new StateSchema { States = new() { "closed" } },
            },
            TargetState = "open",
        };

        string[] warnings = GameplayActionValidator.Validate(executor).ToArray();

        AssertThat(warnings.Contains("TargetState 'open' is absent from the assigned StateSchema."))
            .IsTrue();
    }

    private static InteractiveComponent NewConfiguredInteractive()
    {
        InteractionAction action = NewAction(new GameplayActionDefinition { Id = "open" });
        GameplayActionComponent component = new() { Actions = { action } };
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            ActionComponent = component,
        };
        action.PrepareForInteractive(interactive, interactive.TargetRules);
        component.AddAction(action);
        return interactive;
    }

    private static InteractionAction NewAction(GameplayActionDefinition definition)
    {
        return new()
        {
            Definition = definition,
            DefaultBindingConfig = new GameplayActionBindingConfig
            {
                InputActionName = new StringName("interact"),
                ActivationMode = GameplayActionActivationMode.Press,
            },
            Executor = new SetStateGameplayActionExecutor(),
        };
    }
}
