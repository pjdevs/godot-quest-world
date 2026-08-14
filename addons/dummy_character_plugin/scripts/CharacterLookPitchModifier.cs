using Godot;
using System.Collections.Generic;

namespace QuestWorld.Character;

public partial class CharacterLookPitchModifier : SkeletonModifier3D
{
    [ExportGroup("Look Pitch")]
    [Export]
    public float MaxLookUpDegrees { get; set; } = 32.0f;

    [Export]
    public float MaxLookDownDegrees { get; set; } = 18.0f;

    [Export]
    public float ThirdPersonInfluence { get; set; } = 0.45f;

    [Export]
    public float SmoothingSpeed { get; set; } = 14.0f;

    [Export]
    // The UAL visual is rotated 180 degrees around Y, so its local X pitch axis is opposite to CameraPitch.
    public float PitchRotationSign { get; set; } = -1.0f;

    [ExportGroup("Bone Names")]
    [Export]
    public string SpineBone { get; set; } = "spine_01";

    [Export]
    public string ChestBone { get; set; } = "spine_02";

    [Export]
    public string UpperChestBone { get; set; } = "spine_03";

    [Export]
    public string NeckBone { get; set; } = "neck_01";

    [Export]
    public string HeadBone { get; set; } = "head";

    private static readonly string[][] BoneAliases =
    {
        new[] { "spine_01", "Spine_01", "spine1", "Spine1", "spine" },
        new[] { "spine_02", "Spine_02", "spine2", "Spine2", "chest" },
        new[] { "spine_03", "Spine_03", "spine3", "Spine3", "upperchest", "upper_chest" },
        new[] { "neck_01", "Neck_01", "neck1", "Neck1", "neck" },
        new[] { "head", "Head" }
    };

    private static readonly float[] BoneWeights = { 0.10f, 0.20f, 0.25f, 0.20f, 0.25f };

    private Node3D _cameraPitch = null!;
    private Character _character = null!;
    private int[] _boneIndices = System.Array.Empty<int>();
    private int _resolvedBoneCount;
    private float _smoothedPitch;
    private bool _initialized;
    private bool _reportedMissingSetup;

    public override void _Ready()
    {
        ResolveCharacterAndCamera();
        ResolveBones();
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (!_initialized)
        {
            ResolveCharacterAndCamera();
            ResolveBones();
        }

        Skeleton3D skeleton = GetSkeleton();
        if (skeleton == null || _cameraPitch == null || _resolvedBoneCount == 0)
        {
            if (!_reportedMissingSetup)
            {
                GD.PushWarning($"{Name}: look-pitch modifier could not resolve its skeleton, camera pitch, or bones.");
                _reportedMissingSetup = true;
            }

            return;
        }

        Vector3 cameraForward = -_cameraPitch.GlobalBasis.Z;
        float cameraPitch = (float)Mathf.Asin(Mathf.Clamp(cameraForward.Y, -1.0f, 1.0f));
        float maxLookUp = Mathf.DegToRad(Mathf.Max(MaxLookUpDegrees, 0.0f));
        float maxLookDown = Mathf.DegToRad(Mathf.Max(MaxLookDownDegrees, 0.0f));
        float targetPitch = Mathf.Clamp(cameraPitch, -maxLookDown, maxLookUp);
        targetPitch *= PitchRotationSign;
        if (_character != null && _character.CurrentViewMode == Character.ViewMode.ThirdPerson)
        {
            targetPitch *= Mathf.Clamp(ThirdPersonInfluence, 0.0f, 1.0f);
        }

        float frameDelta = (float)delta;
        if (frameDelta <= 0.0f)
        {
            frameDelta = 1.0f / 60.0f;
        }

        float smoothingWeight = 1.0f - (float)Mathf.Exp(-Mathf.Max(SmoothingSpeed, 0.0f) * frameDelta);
        _smoothedPitch = Mathf.Lerp(_smoothedPitch, targetPitch, smoothingWeight);

        Vector3 rotationAxis = Vector3.Right;
        for (int i = 0; i < _boneIndices.Length; i++)
        {
            int boneIndex = _boneIndices[i];
            if (boneIndex < 0)
            {
                continue;
            }

            Quaternion poseRotation = skeleton.GetBonePoseRotation(boneIndex);
            Quaternion additiveRotation = new(rotationAxis, _smoothedPitch * BoneWeights[i]);
            skeleton.SetBonePoseRotation(boneIndex, poseRotation * additiveRotation);
        }
    }

    private void ResolveCharacterAndCamera()
    {
        Node current = this;
        while (current != null)
        {
            if (current is Character character)
            {
                _character = character;
                _cameraPitch = character.CameraPitchNode;
                return;
            }

            current = current.GetParent();
        }
    }

    private void ResolveBones()
    {
        Skeleton3D skeleton = GetSkeleton();
        if (skeleton == null)
        {
            return;
        }

        string[] requestedNames = { SpineBone, ChestBone, UpperChestBone, NeckBone, HeadBone };
        _boneIndices = new int[requestedNames.Length];
        List<string> missingBones = new();
        _resolvedBoneCount = 0;
        for (int i = 0; i < requestedNames.Length; i++)
        {
            _boneIndices[i] = FindBone(skeleton, requestedNames[i], BoneAliases[i]);
            if (_boneIndices[i] >= 0)
            {
                _resolvedBoneCount++;
            }
            else
            {
                missingBones.Add(requestedNames[i]);
            }
        }

        if (missingBones.Count > 0)
        {
            GD.PushWarning($"{Name}: look-pitch modifier could not resolve bones: {string.Join(", ", missingBones)}.");
        }

        _initialized = true;
    }

    private static int FindBone(Skeleton3D skeleton, string requestedName, string[] aliases)
    {
        int boneIndex = FindBoneByName(skeleton, requestedName);
        if (boneIndex >= 0)
        {
            return boneIndex;
        }

        foreach (string alias in aliases)
        {
            boneIndex = FindBoneByName(skeleton, alias);
            if (boneIndex >= 0)
            {
                return boneIndex;
            }
        }

        return -1;
    }

    private static int FindBoneByName(Skeleton3D skeleton, string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
        {
            return -1;
        }

        int exactIndex = skeleton.FindBone(boneName);
        if (exactIndex >= 0)
        {
            return exactIndex;
        }

        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            if (string.Equals(skeleton.GetBoneName(i), boneName, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
