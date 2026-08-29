namespace QuestWorld.Tests;

using System;
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
    public void TimedStateTransitionRequiresAPositiveFiniteDuration()
    {
        TimedTransitionStateInteractionExecutor executor = new() { Duration = -1.0f };

        string[] warnings = InteractionValidator.Validate(executor).ToArray();

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
        AssertThat(typeof(InteractionAction).GetProperty("Priority") != null).IsTrue();
        AssertThat(typeof(InteractionAction).GetProperty("Automatic") != null).IsTrue();
    }

    [TestCase]
    public void ActionExecutionVisibilityDefaultsToRequesterOnly()
    {
        InteractionAction action = new();

        AssertThat(action.ExecutionVisibility)
            .IsEqual(InteractionExecutionVisibility.RequesterOnly);
    }

    [TestCase]
    public void ReplicatedActionRequiresAMatchingExecutionSynchronizer()
    {
        InteractiveComponent interactive = NewConfiguredInteractive();
        interactive.Actions[0].ExecutionVisibility = InteractionExecutionVisibility.Replicated;

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(
                warnings.Contains(
                    "Replicated actions require a child InteractionExecutionSynchronizer targeting this InteractiveComponent."
                )
            )
            .IsTrue();
    }

    [TestCase]
    public void ExecutionSynchronizerRequiresAnInteractiveTarget()
    {
        InteractionExecutionSynchronizer synchronizer = new();

        string[] warnings = InteractionValidator.Validate(synchronizer).ToArray();

        AssertThat(warnings.Contains("Interactive must be assigned.")).IsTrue();
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
        AssertThat(
                typeof(InteractionActionPresentation).GetProperty("HasTimed" + "Execution") == null
            )
            .IsTrue();
        AssertThat(typeof(InteractionActionPresentation).GetProperty("ExecutionProgress") == null)
            .IsTrue();

        System.Reflection.ParameterInfo[] parameters = typeof(IInteractionActionWidget)
            .GetMethod("Bind")!
            .GetParameters();
        AssertThat(parameters.Length).IsEqual(2);
        AssertThat(parameters[0].ParameterType)
            .IsEqual(typeof(InteractionActionPresentation).MakeByRefType());
        AssertThat(parameters[1].ParameterType).IsEqual(typeof(InteractionExecutionPresentation?));
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

    [TestCase]
    public void InteractiveResolvesItsDirectComposedChildrenWithoutOverrides()
    {
        Node3D owner = new();
        InteractiveComponent interactive = new() { Name = "Interactive" };
        InteractionArea3D interactionArea = new() { Name = "InteractionArea3D" };
        IndicationArea3D indicationArea = new() { Name = "IndicationArea3D" };
        InteractionAnchor3D anchor = new() { Name = "InteractionAnchor3D" };
        InteractionAction action = new()
        {
            Name = "OpenAction",
            Definition = new InteractionActionDefinition { Id = "open" },
        };
        NoopInteractionExecutor executor = new() { Name = "Executor" };

        try
        {
            owner.AddChild(interactive);
            interactive.AddChild(interactionArea);
            interactive.AddChild(indicationArea);
            interactive.AddChild(anchor);
            interactive.AddChild(action);
            action.AddChild(executor);

            AssertThat(interactive.ResolveInteractionArea() == interactionArea).IsTrue();
            AssertThat(interactive.ResolveIndicationArea() == indicationArea).IsTrue();
            AssertThat(interactive.ResolveInteractionAnchor() == anchor).IsTrue();
            AssertThat(interactive.ResolveActions().Count).IsEqual(1);
            AssertThat(action.ResolveExecutor() == executor).IsTrue();
        }
        finally
        {
            owner.Free();
        }
    }

    [TestCase]
    public void ExplicitReferencesOverrideComposedChildren()
    {
        Node3D owner = new();
        InteractiveComponent interactive = new() { Name = "Interactive" };
        InteractionArea3D composedArea = new();
        Area3D overrideArea = new();
        InteractionAnchor3D composedAnchor = new();
        Node3D overrideAnchor = new();
        InteractionAction composedAction = NewAction(
            new InteractionActionDefinition { Id = "composed" }
        );
        InteractionAction overrideAction = NewAction(
            new InteractionActionDefinition { Id = "override" }
        );
        InteractionActionExecutor composedExecutor = new NoopInteractionExecutor();
        InteractionActionExecutor overrideExecutor = new NoopInteractionExecutor();

        try
        {
            owner.AddChild(interactive);
            interactive.AddChild(composedArea);
            interactive.AddChild(composedAnchor);
            interactive.AddChild(composedAction);
            composedAction.AddChild(composedExecutor);
            interactive.InteractionArea = overrideArea;
            interactive.InteractionAnchor = overrideAnchor;
            interactive.Actions = new() { overrideAction };
            composedAction.Executor = overrideExecutor;

            AssertThat(interactive.ResolveInteractionArea() == overrideArea).IsTrue();
            AssertThat(interactive.ResolveInteractionAnchor() == overrideAnchor).IsTrue();
            AssertThat(interactive.ResolveActions()[0] == overrideAction).IsTrue();
            AssertThat(composedAction.ResolveExecutor() == overrideExecutor).IsTrue();
        }
        finally
        {
            owner.Free();
            overrideArea.Free();
            overrideAnchor.Free();
            overrideAction.Free();
            overrideExecutor.Free();
        }
    }

    [TestCase]
    public void CompositionRejectsRecursiveAndAmbiguousCandidates()
    {
        InteractiveComponent interactive = new();
        Node3D nested = new();
        InteractionArea3D nestedArea = new();
        InteractionArea3D firstArea = new();
        InteractionArea3D secondArea = new();

        try
        {
            interactive.AddChild(nested);
            nested.AddChild(nestedArea);

            AssertThat(interactive.ResolveInteractionArea() == null).IsTrue();

            interactive.AddChild(firstArea);
            interactive.AddChild(secondArea);

            AssertThat(interactive.ResolveInteractionArea() == null).IsTrue();
            string[] warnings = InteractionValidator.Validate(interactive).ToArray();
            AssertThat(warnings.Any(warning => warning.Contains("ambiguous"))).IsTrue();
        }
        finally
        {
            interactive.Free();
        }
    }

    [TestCase]
    public void InteractiveValidatorRecognizesComposedStatefulActions()
    {
        Node3D owner = new();
        Area3D area = new();
        StatefulComponent stateful = new()
        {
            Schema = new StateSchema
            {
                States = new() { "closed", "opened" },
            },
        };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionAnchor = owner,
        };
        StatefulTransitionAction action = new()
        {
            Definition = new InteractionActionDefinition { Id = "open" },
            From = new() { new StringName("closed") },
            To = new StringName("opened"),
            Automatic = true,
        };

        try
        {
            owner.AddChild(stateful);
            owner.AddChild(interactive);
            interactive.AddChild(action);

            string[] warnings = InteractionValidator.Validate(interactive).ToArray();

            AssertThat(warnings.Contains("Actions must declare at least one action.")).IsFalse();
            AssertThat(warnings.Contains("Actions[0] has no Executor.")).IsFalse();
            AssertThat(warnings.Contains("Stateful must be assigned.")).IsFalse();
        }
        finally
        {
            owner.Free();
            area.Free();
        }
    }

    [TestCase]
    public async Task StatefulIntegrationResolvesTheUniqueLocalComponent()
    {
        Node3D scope = new();
        StatefulComponent stateful = new()
        {
            Name = "StatefulComponent",
            InitialState = new StringName("closed"),
            Schema = new StateSchema
            {
                States = new() { "closed", "opened" },
            },
        };
        InteractiveComponent interactive = new() { Name = "Interactive" };
        InteractionArea3D interactionArea = new();
        InteractionAnchor3D anchor = new();
        InteractionAction action = new()
        {
            Name = "OpenAction",
            Definition = new InteractionActionDefinition { Id = "open" },
        };
        StatefulStateInteractionRule rule = new() { ExpectedStates = { new StringName("closed") } };
        SetStateInteractionExecutor executor = new() { TargetState = new StringName("opened") };
        InteractionInteractor interactor = new();

        scope.AddChild(stateful);
        scope.AddChild(interactive);
        interactive.AddChild(interactionArea);
        interactive.AddChild(anchor);
        interactive.AddChild(action);
        action.AddChild(executor);
        action.Rules.Add(rule);
        scope.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(scope);
        await runner.SimulateFrames(1);

        try
        {
            InteractionAvailability availability = interactive.EvaluateAvailability(
                interactor,
                action
            );
            AssertThat(availability is InteractionAllowed).IsTrue();
            AssertThat(
                    executor.Execute(
                        new InteractionExecutionContext(1, interactor, interactive, action)
                    ) is InteractionExecutionCompleted
                )
                .IsTrue();
            AssertThat(stateful.State).IsEqual(new StringName("opened"));
        }
        finally
        {
            scope.Free();
        }
    }

    [TestCase]
    public void StatefulTransitionActionComposesTheExistingPrimitives()
    {
        StatefulTransitionAction action = new()
        {
            From = new() { new StringName("closed") },
            To = new StringName("opened"),
        };

        try
        {
            AssertThat(action.ResolveExecutor() is SetStateInteractionExecutor).IsTrue();
            AssertThat(action.ResolveRules().Single() is StatefulStateInteractionRule).IsTrue();
            AssertThat(((SetStateInteractionExecutor)action.ResolveExecutor()!).TargetState)
                .IsEqual(new StringName("opened"));
            AssertThat(
                    ((StatefulStateInteractionRule)action.ResolveRules().Single()).ExpectedStates[0]
                )
                .IsEqual(new StringName("closed"));
        }
        finally
        {
            action.Free();
        }
    }

    [TestCase]
    public void StatefulRunningTransitionDefaultsToExternalCompletion()
    {
        StatefulRunningTransitionAction action = new()
        {
            From = new() { new StringName("closed") },
            Running = new StringName("opening"),
            Completed = new StringName("opened"),
            Cancelled = new StringName("closed"),
        };

        try
        {
            AssertThat(action.ResolveExecutor() is TransitionStateInteractionExecutor).IsTrue();
            AssertThat(action.ResolveExecutor() is TimedTransitionStateInteractionExecutor)
                .IsFalse();
            AssertThat(action.ResolveRules().Single() is StatefulStateInteractionRule).IsTrue();
        }
        finally
        {
            action.Free();
        }
    }

    private sealed partial class NoopInteractionExecutor : InteractionActionExecutor
    {
        public override InteractionExecutionResult Execute(
            in InteractionExecutionContext context
        ) => new InteractionExecutionCompleted();
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
