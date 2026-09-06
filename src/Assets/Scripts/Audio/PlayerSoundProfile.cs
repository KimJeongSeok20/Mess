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
    [SerializeField] private float walkDelay = 0.45f;
    [SerializeField] private float sprintDelay = 0.30f;
    [Range(0f, 1f)]
    [SerializeField] private float sprintClipThreshold = 0.55f;

    [Header("Jumping")]
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip landSound;

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
    public float WalkDelay => walkDelay;
    public float SprintDelay => sprintDelay;
    public float SprintClipThreshold => sprintClipThreshold;
    public AudioClip JumpSound => jumpSound;
    public AudioClip LandSound => landSound;
    public AudioClip AimInSound => aimInSound;
    public AudioClip AimOutSound => aimOutSound;
    public float OneShotVolume => oneShotVolume;
}
