namespace QuestWorld.Tests;

using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Character;
using static GdUnit4.Assertions;
using Character = QuestWorld.Character.Character;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed class CharacterBehaviorTest
{
    private const string CharacterScenePath = "res://addons/dummy_character_plugin/Character.tscn";
    private const string CameraEffectsPath =
        "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects";
    private const string CameraPath = $"{CameraEffectsPath}/Camera3D";

    [TestCase]
    public async Task InitialFloorContactDoesNotTriggerLanding()
    {
        Character character = InstantiateCharacter(Vector3.Zero);
        ISceneRunner runner = BuildWorld(character);
        bool landingObserved = false;
        bool initialContactEstablished = false;

        for (int sample = 0; sample < 30 && !initialContactEstablished; sample++)
        {
            await WaitForNextPhysicsFrame(runner, character);
            landingObserved |= character.LatestFrame.Landed;
            initialContactEstablished = character.LatestFrame.IsGrounded;
        }

        AssertThat(initialContactEstablished).IsTrue();
        AssertThat(landingObserved).IsFalse();
        await runner.SimulateFrames(1);
        Node3D cameraEffects = character.GetNode<Node3D>(CameraEffectsPath);
        AssertThat(cameraEffects.Position.Y).IsEqualApprox(0.0f, 0.0001f);
    }

    [TestCase]
    public async Task LandingStrengthIncreasesWithImpact()
    {
        Character lowCharacter = InstantiateCharacter(new Vector3(-2.0f, 1.0f, 0.0f));
        Character highCharacter = InstantiateCharacter(new Vector3(2.0f, 5.0f, 0.0f));
        ISceneRunner runner = BuildWorld(lowCharacter, highCharacter);
        float lowOffset = 0.0f;
        float highOffset = 0.0f;
        int lowGroundedFrames = 0;
        int highGroundedFrames = 0;

        for (int frame = 0; frame < 240; frame++)
        {
            await WaitForNextPhysicsFrame(runner, lowCharacter);
            CaptureCameraBouncePeak(lowCharacter, ref lowOffset);
            CaptureCameraBouncePeak(highCharacter, ref highOffset);
            lowGroundedFrames = lowCharacter.LatestFrame.IsGrounded ? lowGroundedFrames + 1 : 0;
            highGroundedFrames = highCharacter.LatestFrame.IsGrounded ? highGroundedFrames + 1 : 0;
            if (lowGroundedFrames >= 12 && highGroundedFrames >= 12)
            {
                break;
            }
        }

        AssertThat(lowGroundedFrames).IsGreater(0);
        AssertThat(highGroundedFrames).IsGreater(0);
        AssertThat(lowOffset).IsGreater(0.0f);
        AssertThat(highOffset).IsGreater(lowOffset + 0.002f);
        AssertThat(highOffset).IsGreater(lowOffset + 0.002f);
    }

    [TestCase]
    public async Task RotatedInstanceKeepsVisualAlignedWithCamera()
    {
        Character character = InstantiateCharacter(Vector3.Zero);
        character.Rotation = new Vector3(0.0f, Mathf.Pi / 2.0f, 0.0f);
        character.RotationSpeed = 1000.0f;
        ISceneRunner runner = BuildWorld(character);
        await WaitForNextPhysicsFrame(runner, character);

        character.CurrentViewMode = Character.ViewMode.FirstPerson;
        SpringArm3D springArm = character.GetNode<SpringArm3D>("CameraYaw/CameraPitch/SpringArm3D");
        AssertThat(springArm.SpringLength).IsEqualApprox(0.0f, 0.0001f);
        await WaitForNextPhysicsFrame(runner, character);

        Node3D visual = character.GetNode<Node3D>("Visual");
        Node3D cameraYaw = character.GetNode<Node3D>("CameraYaw");
        float yawError = Math.Abs(
            Mathf.Wrap(visual.GlobalRotation.Y - cameraYaw.GlobalRotation.Y, -Mathf.Pi, Mathf.Pi)
        );
        AssertThat(yawError).IsLess(0.001f);
    }

    [TestCase]
    public async Task SimulationUsesExplicitViewYawWithoutCameraRig()
    {
        Character character = InstantiateCharacter(Vector3.Zero);
        ISceneRunner runner = BuildWorld(character);
        await WaitForNextPhysicsFrame(runner, character);

        CharacterCameraRig cameraRig = character.GetNode<CharacterCameraRig>("CameraYaw");
        cameraRig.Rotation = new Vector3(0.0f, Mathf.Pi / 2.0f, 0.0f);
        cameraRig.SetActive(false);

        character.Simulate(
            new CharacterSimulationInput(new Vector2(0.0f, -1.0f), 0.0f, 0.0f, false, false),
            1.0 / 60.0
        );

        AssertThat(character.LatestFrame.MoveDirection.X).IsEqualApprox(0.0f, 0.001f);
        AssertThat(character.LatestFrame.MoveDirection.Z).IsEqualApprox(-1.0f, 0.001f);
    }

    [TestCase]
    public async Task PossessionTransfersInputAuthorityAndActiveCamera()
    {
        Character firstCharacter = InstantiateCharacter(new Vector3(-2.0f, 0.0f, 0.0f));
        Character secondCharacter = InstantiateCharacter(new Vector3(2.0f, 0.0f, 0.0f));
        CharacterPlayerController player = new();
        CharacterPlayerController replacementPlayer = new();
        ISceneRunner runner = BuildWorldWithPlayers(
            new[] { player, replacementPlayer },
            firstCharacter,
            secondCharacter
        );
        await WaitForNextPhysicsFrame(runner, firstCharacter);

        player.Possess(firstCharacter);
        AssertThat(firstCharacter.IsPossessed).IsTrue();
        AssertThat(secondCharacter.IsPossessed).IsFalse();
        AssertThat(firstCharacter.GetNode<Camera3D>(CameraPath).Current).IsTrue();

        player.Possess(secondCharacter);
        AssertThat(firstCharacter.IsPossessed).IsFalse();
        AssertThat(secondCharacter.IsPossessed).IsTrue();
        AssertThat(firstCharacter.GetNode<Camera3D>(CameraPath).Current).IsFalse();
        AssertThat(secondCharacter.GetNode<Camera3D>(CameraPath).Current).IsTrue();

        replacementPlayer.Possess(secondCharacter);
        AssertThat(player.ControlledCharacter == null).IsTrue();
        AssertThat(secondCharacter.PossessingController == replacementPlayer).IsTrue();
        AssertThat(secondCharacter.IsPossessed).IsTrue();
        AssertThat(secondCharacter.GetNode<Camera3D>(CameraPath).Current).IsTrue();
    }

    [TestCase]
    public async Task AirborneStateCancelsTurnInPlaceOverride()
    {
        Character character = InstantiateCharacter(Vector3.Zero);
        character.CurrentViewMode = Character.ViewMode.FirstPerson;
        ISceneRunner runner = BuildWorld(character);
        for (int sample = 0; sample < 30 && !character.LatestFrame.IsGrounded; sample++)
        {
            await WaitForNextPhysicsFrame(runner, character);
        }

        character.SubmitInputFrame(
            new CharacterInputFrame(Vector2.Zero, new Vector2(-250.0f, 0.0f), false, false)
        );
        for (int frame = 0; frame < 10 && !character.IsTurnInPlaceActive; frame++)
        {
            await runner.SimulateFrames(1);
        }
        AssertThat(character.IsTurnInPlaceActive).IsTrue();

        CharacterAnimationController animationController =
            character.GetNode<CharacterAnimationController>("AnimationController");
        animationController.TurnInPlaceEnabled = false;
        await WaitForNextPhysicsFrame(runner, character);
        AssertThat(character.IsTurnInPlaceActive).IsFalse();

        animationController.TurnInPlaceEnabled = true;
        character.SubmitInputFrame(
            new CharacterInputFrame(Vector2.Zero, new Vector2(-250.0f, 0.0f), false, false)
        );
        for (int frame = 0; frame < 10 && !character.IsTurnInPlaceActive; frame++)
        {
            await runner.SimulateFrames(1);
        }
        AssertThat(character.IsTurnInPlaceActive).IsTrue();

        character.SubmitInputFrame(
            new CharacterInputFrame(Vector2.Zero, Vector2.Zero, true, false)
        );
        await WaitForNextPhysicsFrame(runner, character);
        AssertThat(character.LatestFrame.IsGrounded).IsFalse();
        AssertThat(character.IsTurnInPlaceActive).IsFalse();
    }

    private static ISceneRunner BuildWorld(params Character[] characters)
    {
        Node3D world = new();
        world.AddChild(CreateFloor());
        foreach (Character character in characters)
        {
            world.AddChild(character);
        }

        return ISceneRunner.Load(world, autoFree: true);
    }

    private static ISceneRunner BuildWorldWithPlayers(
        CharacterPlayerController[] players,
        params Character[] characters
    )
    {
        Node3D world = new();
        world.AddChild(CreateFloor());
        foreach (Character character in characters)
        {
            world.AddChild(character);
        }

        foreach (CharacterPlayerController player in players)
        {
            world.AddChild(player);
        }

        return ISceneRunner.Load(world, autoFree: true);
    }

    private static StaticBody3D CreateFloor()
    {
        BoxShape3D shape = new() { Size = new Vector3(20.0f, 1.0f, 20.0f) };
        CollisionShape3D collision = new()
        {
            Shape = shape,
            Position = new Vector3(0.0f, -0.5f, 0.0f),
        };
        StaticBody3D floor = new();
        floor.AddChild(collision);
        return floor;
    }

    private static Character InstantiateCharacter(Vector3 position)
    {
        PackedScene scene = GD.Load<PackedScene>(CharacterScenePath);
        Character character = scene.Instantiate<Character>();
        character.Position = position;
        return character;
    }

    private static void CaptureCameraBouncePeak(Character character, ref float peakOffset)
    {
        Node3D cameraEffects = character.GetNode<Node3D>(CameraEffectsPath);
        peakOffset = Math.Max(peakOffset, Math.Abs(cameraEffects.Position.Y));
    }

    private static async Task WaitForNextPhysicsFrame(
        ISceneRunner runner,
        Character character,
        int maximumRenderFrames = 30
    )
    {
        ulong initialFrame = character.LatestFrame.FrameNumber;
        for (
            int renderFrame = 0;
            renderFrame < maximumRenderFrames && character.LatestFrame.FrameNumber == initialFrame;
            renderFrame++
        )
        {
            await ISceneRunner.SyncPhysicsFrame;
            await ISceneRunner.SyncProcessFrame;
        }

        if (character.LatestFrame.FrameNumber == initialFrame)
        {
            throw new TimeoutException(
                "Character physics did not advance within the render-frame budget."
            );
        }
    }
}
