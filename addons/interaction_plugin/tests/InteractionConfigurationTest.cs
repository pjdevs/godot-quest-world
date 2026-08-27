namespace QuestWorld.Tests;

using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using InteractionPlugin.Editor;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
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
        TransitionStateInteractionExecutor executor = new();

        string[] warnings = InteractionValidator.Validate(executor).ToArray();

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
        AssertThat(typeof(InteractionAction).GetProperty("Priority") != null).IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("Automatic") != null).IsTrue();
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
    public void ExecutionBelongsToAnExecutorInsteadOfASignalSubscriber()
    {
        AssertThat(typeof(InteractionAction).GetProperty("Executor") != null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethods().Any(m => m.Name == "ExecuteAction"))
            .IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("CompleteExecution") != null).IsTrue();
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
        const string buttonPath = "./Level/Button/InteractiveComponent";
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
        AssertThat(phase.MismatchAvailability).IsEqual(InteractionUnavailableKind.Hidden);
        AssertThat(ready.ExpectedStates.Count).IsEqual(1);
        AssertThat(ready.MismatchAvailability).IsEqual(InteractionUnavailableKind.Blocked);
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
            Actions = new() { new InteractionAction() },
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
        InteractionAction action = NewAction(new InteractionActionDefinition { Id = "open" });
        action.Rules.Add(
            new StatefulStateInteractionRule
            {
                StatefulPath = new NodePath("../../StatefulComponent"),
                ExpectedStates = { new StringName("closed") },
            }
        );
        interactive.Actions.Add(action);
        owner.AddChild(area);
        owner.AddChild(stateful);
        owner.AddChild(interactive);
        interactive.AddChild(action);
        root.AddChild(owner);
        ISceneRunner runner = ISceneRunner.Load(root);
        await runner.SimulateFrames(1);

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Any(warning => warning.Contains("does not resolve"))).IsFalse();
    }

    [TestCase]
    public void InteractiveReportsDuplicateActionIds()
    {
        InteractionActionDefinition definition = new() { Id = "open" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            Actions = new() { NewAction(definition), NewAction(definition) },
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
            Actions = new()
            {
                NewAction(new InteractionActionDefinition { Id = "open" }),
                NewAction(new InteractionActionDefinition { Id = "force" }),
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
        InteractionAction open = NewAction(new InteractionActionDefinition { Id = "open" });
        InteractionAction unlock = NewAction(new InteractionActionDefinition { Id = "unlock" });
        unlock.Priority = 10;
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            Actions = new() { open, unlock },
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
        InteractionActionDefinition definition = new();

        string[] warnings = InteractionValidator.Validate(definition).ToArray();

        AssertThat(warnings.Contains("Id must be assigned.")).IsTrue();
    }

    [TestCase]
    public void ShortActionExecutorReportsAStateOutsideTheSchema()
    {
        SetStateInteractionExecutor executor = new()
        {
            Stateful = new StatefulComponent
            {
                Schema = new StateSchema { States = new() { "closed" } },
            },
            TargetState = "open",
        };

        string[] warnings = InteractionValidator.Validate(executor).ToArray();

        AssertThat(warnings.Contains("TargetState 'open' is absent from the assigned StateSchema."))
            .IsTrue();
    }

    private static InteractiveComponent NewConfiguredInteractive()
    {
        return new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
            Actions = new() { NewAction(new InteractionActionDefinition { Id = "open" }) },
        };
    }

    private static InteractionAction NewAction(InteractionActionDefinition definition)
    {
        return new() { Definition = definition, Executor = new SetStateInteractionExecutor() };
    }
}
