using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerSoundProfile", menuName = "Audio/Player Sound Profile")]
public sealed class PlayerSoundProfile : ScriptableObject
{
    [Header("Weapon swapping")]
    [SerializeField] private AudioClip equipSound;
    [SerializeField] private AudioClip unEquipSound;

    [Header("Footsteps")]
    [SerializeField] private List<AudioClip> walkSounds = new();
    [SerializeField] private List<AudioClip> sprintSounds = new();
    [Range(0f, 1f)]
    [SerializeField] private float walkVolume = 0.4f;
    [Range(0f, 1f)]
    [SerializeField] private float sprintVolume = 0.35f;
    [SerializeField] private float walkDelay = 0.45f;
    [SerializeField] private float sprintDelay = 0.30f;
    [Range(0f, 1f)]
    [SerializeField] private float sprintClipThreshold = 0.55f;

    [Header("Jumping")]
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip landSound;
    [Range(0f, 1f)]
    [SerializeField] private float jumpVolume = 0.4f;
    [Range(0f, 1f)]
    [SerializeField] private float landVolume = 0.25f;

    [Header("Aiming")]
    [SerializeField] private AudioClip aimInSound;
    [SerializeField] private AudioClip aimOutSound;

    [Header("Volume")]
    [Range(0f, 2f)]
    [SerializeField] private float oneShotVolume = 1f;

    public AudioClip EquipSound => equipSound;
    public AudioClip UnEquipSound => unEquipSound;
    public IReadOnlyList<AudioClip> WalkSounds => walkSounds;
    public IReadOnlyList<AudioClip> SprintSounds => sprintSounds;
    public float WalkVolume => walkVolume;
    public float SprintVolume => sprintVolume;
    public float WalkDelay => walkDelay;
    public float SprintDelay => sprintDelay;
    public float SprintClipThreshold => sprintClipThreshold;
    public AudioClip JumpSound => jumpSound;
    public AudioClip LandSound => landSound;
    public float JumpVolume => jumpVolume;
    public float LandVolume => landVolume;
    public AudioClip AimInSound => aimInSound;
    public AudioClip AimOutSound => aimOutSound;
    public float OneShotVolume => oneShotVolume;
}
