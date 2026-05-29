using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAudioController : MonoBehaviour
    {
        [Header("Clips")]
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField] private AudioClip[] sprintFootstepClips;
        [SerializeField] private AudioClip[] remoteFootstepClips;
        [SerializeField] private AudioClip jumpClip;
        [SerializeField] private AudioClip shotClip;
        [SerializeField] private AudioClip reloadPullClip;
        [SerializeField] private AudioClip reloadInsertClip;
        [SerializeField] private AudioClip remoteReloadClip;
        [SerializeField] private AudioClip hitPlayerClip;

        [Header("Volume")]
        [Range(0f, 1f)]
        [SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)]
        [SerializeField] private float footstepVolume = 0.35f;
        [Range(0f, 1f)]
        [SerializeField] private float sprintFootstepVolumeMultiplier = 1.12f;
        [Range(0f, 1f)]
        [SerializeField] private float jumpVolume = 0.5f;
        [Range(0f, 1f)]
        [SerializeField] private float shotVolume = 0.8f;
        [Range(0f, 1f)]
        [SerializeField] private float reloadPullVolume = 0.55f;
        [Range(0f, 1f)]
        [SerializeField] private float reloadInsertVolume = 0.62f;
        [Range(0f, 1f)]
        [SerializeField] private float reloadInsertNormalizedTime = 0.78f;
        [Range(0f, 1f)]
        [SerializeField] private float hitPlayerVolume = 0.65f;
        [Range(0f, 1f)]
        [SerializeField] private float remoteShotVolumeMultiplier = 0.65f;
        [Range(0f, 1f)]
        [SerializeField] private float remoteReloadVolumeMultiplier = 0.6f;

        [Header("Distance")]
        [SerializeField] private float defaultMinDistance = 1.5f;
        [SerializeField] private float defaultMaxDistance = 16f;
        [SerializeField] private float shotMaxDistance = 38f;

        private AudioSource nearSource;
        private AudioSource shotSource;
        private int footstepIndex;
        private int sprintFootstepIndex;
        private Coroutine reloadAudioRoutine;

        private void Awake()
        {
            nearSource = CreateSource("AudioNear", defaultMaxDistance);
            shotSource = CreateSource("AudioShot", shotMaxDistance);
        }

        public void PlayFootstep(bool isLocal, bool isSprinting = false)
        {
            var clip = GetNextFootstepClip(isLocal, isSprinting);
            if (clip == null)
            {
                return;
            }

            var volume = footstepVolume;
            if (isLocal && isSprinting)
            {
                volume *= sprintFootstepVolumeMultiplier;
            }

            PlayClip(nearSource, clip, volume, isLocal, defaultMaxDistance);
        }

        public void PlayJump(bool isLocal)
        {
            PlayClip(nearSource, jumpClip, jumpVolume, isLocal, defaultMaxDistance);
        }

        public void PlayShot(bool isLocal)
        {
            var volume = shotVolume * (isLocal ? 1f : Mathf.Clamp01(remoteShotVolumeMultiplier));
            PlayClip(shotSource, shotClip, volume, isLocal, shotMaxDistance);
        }

        public void PlayReload(bool isLocal)
        {
            PlayReloadSequence(isLocal, 1f);
        }

        public void PlayReloadSequence(bool isLocal, float durationSeconds)
        {
            if (reloadAudioRoutine != null)
            {
                StopCoroutine(reloadAudioRoutine);
                reloadAudioRoutine = null;
            }

            reloadAudioRoutine = StartCoroutine(ReloadAudioRoutine(isLocal, durationSeconds));
        }

        public void PlayHitPlayer(bool isLocal)
        {
            PlayClip(nearSource, hitPlayerClip, hitPlayerVolume, isLocal, defaultMaxDistance);
        }

        public void InheritFrom(PlayerAudioController source)
        {
            if (source == null || ReferenceEquals(source, this))
            {
                return;
            }

            footstepClips = source.footstepClips;
            sprintFootstepClips = source.sprintFootstepClips;
            remoteFootstepClips = source.remoteFootstepClips;
            jumpClip = source.jumpClip;
            shotClip = source.shotClip;
            reloadPullClip = source.reloadPullClip;
            reloadInsertClip = source.reloadInsertClip;
            remoteReloadClip = source.remoteReloadClip;
            hitPlayerClip = source.hitPlayerClip;
            masterVolume = source.masterVolume;
            footstepVolume = source.footstepVolume;
            sprintFootstepVolumeMultiplier = source.sprintFootstepVolumeMultiplier;
            jumpVolume = source.jumpVolume;
            shotVolume = source.shotVolume;
            reloadPullVolume = source.reloadPullVolume;
            reloadInsertVolume = source.reloadInsertVolume;
            reloadInsertNormalizedTime = source.reloadInsertNormalizedTime;
            hitPlayerVolume = source.hitPlayerVolume;
            remoteShotVolumeMultiplier = source.remoteShotVolumeMultiplier;
            remoteReloadVolumeMultiplier = source.remoteReloadVolumeMultiplier;
            defaultMinDistance = source.defaultMinDistance;
            defaultMaxDistance = source.defaultMaxDistance;
            shotMaxDistance = source.shotMaxDistance;
        }

        private AudioSource CreateSource(string name, float maxDistance)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = Mathf.Max(0.1f, defaultMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance);
            source.dopplerLevel = 0f;
            return source;
        }

        private void PlayClip(AudioSource source, AudioClip clip, float volume, bool isLocal, float maxDistance)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.spatialBlend = isLocal ? 0f : 1f;
            source.minDistance = Mathf.Max(0.1f, defaultMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance);
            source.PlayOneShot(clip, Mathf.Clamp01(volume) * Mathf.Clamp01(masterVolume));
        }

        private AudioClip GetNextFootstepClip(bool isLocal, bool isSprinting)
        {
            AudioClip[] clips;
            if (isLocal)
            {
                clips = isSprinting ? sprintFootstepClips : footstepClips;
                if (clips == null || clips.Length == 0)
                {
                    clips = footstepClips;
                }
            }
            else
            {
                // Remote sprint uses the same clips as remote walk; cadence comes from network footstepSeq.
                clips = remoteFootstepClips;
                if (clips == null || clips.Length == 0)
                {
                    clips = footstepClips;
                }
            }

            if (clips == null || clips.Length == 0)
            {
                return null;
            }

            int idx;
            if (isLocal && isSprinting)
            {
                idx = Mathf.Abs(sprintFootstepIndex) % clips.Length;
                sprintFootstepIndex++;
            }
            else
            {
                idx = Mathf.Abs(footstepIndex) % clips.Length;
                footstepIndex++;
            }

            return clips[idx];
        }

        private System.Collections.IEnumerator ReloadAudioRoutine(bool isLocal, float durationSeconds)
        {
            var totalDuration = Mathf.Max(0.1f, durationSeconds);
            var insertAt = totalDuration * Mathf.Clamp01(reloadInsertNormalizedTime);
            if (!isLocal)
            {
                var remoteClip = remoteReloadClip != null ? remoteReloadClip : reloadPullClip;
                var remoteVolume = reloadPullVolume * Mathf.Clamp01(remoteReloadVolumeMultiplier);
                PlayClip(nearSource, remoteClip, remoteVolume, false, defaultMaxDistance);
                reloadAudioRoutine = null;
                yield break;
            }

            var pullClip = reloadPullClip;
            var insertClip = reloadInsertClip;
            PlayClip(nearSource, pullClip, reloadPullVolume, isLocal, defaultMaxDistance);
            if (insertClip != null)
            {
                yield return new WaitForSeconds(Mathf.Max(0.01f, insertAt));
                PlayClip(nearSource, insertClip, reloadInsertVolume, isLocal, defaultMaxDistance);
            }

            reloadAudioRoutine = null;
        }
    }
}
