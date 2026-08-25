namespace QuestWorld.Tests;

using System.Linq;
using GdUnit4;
using Godot;
using InteractionPlugin.Editor;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Examples.Interactive;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;
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
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
        };

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
    }

    [TestCase]
    public void InteractiveComponentAllowsOptionalReferencesToRemainUnset()
    {
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionAnchor = new Node3D(),
        };

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
    public void InteractorRequiresAnExplicitViewOriginOnly()
    {
        InteractionInteractor interactor = new();

        string[] warnings = InteractionValidator.Validate(interactor).ToArray();

        AssertThat(warnings.Contains("ViewOrigin must be assigned.")).IsTrue();
        AssertThat(warnings.Any(warning => warning.Contains("InteractionOrigin"))).IsFalse();
    }

    [TestCase]
    public void StatefulRequiresNoOwnerConfiguration()
    {
        InteractionStateful stateful = new();

        string[] warnings = InteractionValidator.Validate(stateful).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
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
    public void InteractiveActorRequiresExplicitInteractiveAndStatefulReferences()
    {
        InteractiveActor actor = new();

        string[] warnings = InteractionValidator.Validate(actor).ToArray();

        AssertThat(warnings.Contains("Interactive must be assigned.")).IsTrue();
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
    public void StatefulDoesNotOwnInteractionLifecycleApi()
    {
        AssertThat(typeof(InteractionStateful).GetProperty("ActiveInteractor") == null).IsTrue();
        AssertThat(typeof(InteractionStateful).GetMethod("ExecuteAction") == null).IsTrue();
        AssertThat(typeof(InteractionStateful).GetMethod("CompleteExecution") == null).IsTrue();
        AssertThat(typeof(InteractionStateful).GetMethod("CancelExecution") == null).IsTrue();
    }

    [TestCase]
    public void ExecutionBelongsToAnExecutorInsteadOfASignalSubscriber()
    {
        AssertThat(typeof(InteractionAction).GetProperty("Executor") != null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("ExecuteAction") != null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("CompleteExecution") != null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("StartInteraction") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("StartInteractionPhase") == null)
            .IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("EndInteractionPhase") == null).IsTrue();
        AssertThat(typeof(InteractiveComponent).GetMethod("ReleaseInteractionInput") == null)
            .IsTrue();
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
}
