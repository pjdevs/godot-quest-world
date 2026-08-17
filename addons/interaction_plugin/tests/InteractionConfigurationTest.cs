namespace QuestWorld.Tests;

using System.Linq;
using GdUnit4;
using Godot;
using InteractionPlugin.Editor;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Examples.Interactive;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionConfigurationTest
{
    [TestCase]
    public void InteractiveComponentRequiresExplicitAreaAndHandler()
    {
        InteractiveComponent interactive = new();

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Contains("InteractionArea must be assigned.")).IsTrue();
        AssertThat(warnings.Contains("InteractionOwner must be assigned.")).IsTrue();
    }

    [TestCase]
    public void InteractiveComponentRejectsAnOwnerWithoutInteractionHandler()
    {
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionOwner = new Node3D(),
        };

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Contains("InteractionOwner must implement IInteractionHandler."))
            .IsTrue();
    }

    [TestCase]
    public void InteractiveComponentAllowsOptionalReferencesToRemainUnset()
    {
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionOwner = new TestInteractionOwner(),
        };

        string[] warnings = InteractionValidator.Validate(interactive).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
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
    public void StatefulAllowsAnAbsentStateOwner()
    {
        InteractionStateful stateful = new();

        string[] warnings = InteractionValidator.Validate(stateful).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
    }

    [TestCase]
    public void StatefulRejectsAStateOwnerWithoutStateHandler()
    {
        InteractionStateful stateful = new() { StateOwner = new Node3D() };

        string[] warnings = InteractionValidator.Validate(stateful).ToArray();

        AssertThat(warnings.Contains("StateOwner must implement IInteractionStateHandler."))
            .IsTrue();
    }

    [TestCase]
    public void StatefulAcceptsAStateOwnerWithStateHandler()
    {
        InteractionStateful stateful = new() { StateOwner = new TestStateOwner() };

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

        InteractionPresentation? presentation = interactor.GetInteractionPresentation();

        AssertThat(presentation == null).IsTrue();
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
    public void StatefulDoesNotOwnInteractionLifecycleApi()
    {
        AssertThat(typeof(InteractionStateful).GetProperty("ActiveInteractor") == null).IsTrue();
        AssertThat(typeof(InteractionStateful).GetMethod("StartInteractionPhase") == null).IsTrue();
        AssertThat(typeof(InteractionStateful).GetMethod("EndInteractionPhase") == null).IsTrue();
        AssertThat(typeof(InteractionStateful).GetMethod("ReleaseInteractionInput") == null)
            .IsTrue();
    }

    private sealed partial class TestInteractionOwner : Node3D, IInteractionHandler
    {
        public InteractionStatus EvaluateCustomInteractionStatus(in InteractionContext context) =>
            new InteractionAllowed();

        public void OnStartInteractionInput(in InteractionContext context) { }

        public void OnEndInteractionInput(in InteractionContext context) { }
    }

    private sealed partial class TestStateOwner : Node, IInteractionStateHandler
    {
        public void OnInteractionStateChangedAuthority(
            InteractionState oldState,
            InteractionState newState
        ) { }

        public void OnInteractionStateChangedPresentation(
            InteractionState oldState,
            InteractionState newState
        ) { }
    }
}
