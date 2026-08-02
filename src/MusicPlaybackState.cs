using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer
{
    internal static class MusicPlaybackState
    {
        private static AccessTools.FieldRef<MusicManager, AudioSource> _fadeSource;
        private static AccessTools.FieldRef<MusicManager, bool> _isFading;
        private static bool _wasActive;
        private static bool _hasEnded;
        private static float _lastEndedAt;
        private static float _reservedUntil;

        public static bool IsInitialized { get; private set; }

        public static MusicManager Manager { get; private set; }

        public static void Initialize()
        {
            _fadeSource = AccessTools.FieldRefAccess<MusicManager, AudioSource>("fadeSource");
            _isFading = AccessTools.FieldRefAccess<MusicManager, bool>("isFading");
            IsInitialized = true;
        }

        public static void Register(MusicManager manager)
        {
            Manager = manager;
        }

        public static bool Refresh(MusicManager manager, float now)
        {
            ExpireReservation(now);
            bool active = IsActive(manager);
            if (_wasActive && !active && _reservedUntil <= 0f)
            {
                MarkEnded(now);
            }

            _wasActive = active;
            return active;
        }

        public static void Reserve(AudioClip clip, bool repeat, float now, float startDelay = 0f)
        {
            if (clip == null)
            {
                return;
            }

            float until = repeat
                ? float.PositiveInfinity
                : now + Mathf.Max(0f, startDelay) + clip.length;
            _reservedUntil = until;
        }

        public static bool DidCrossFadeStart(MusicManager manager, AudioClip clip)
        {
            AudioSource fadeSource = _fadeSource(manager);
            return fadeSource != null
                && fadeSource.clip == clip
                && (fadeSource.isPlaying || _isFading(manager));
        }

        public static bool IsReserved(float now)
        {
            ExpireReservation(now);
            return now < _reservedUntil;
        }

        public static void MarkEnded(float now)
        {
            _reservedUntil = 0f;
            _hasEnded = true;
            _lastEndedAt = now;
            _wasActive = false;
        }

        public static bool IsCooldownComplete(float now, float cooldownSeconds)
        {
            return !_hasEnded || now - _lastEndedAt >= Mathf.Max(0f, cooldownSeconds);
        }

        private static bool IsActive(MusicManager manager)
        {
            AudioSource fadeSource = _fadeSource(manager);
            return manager.IsPlaying() || _isFading(manager) || (fadeSource != null && fadeSource.isPlaying);
        }

        private static void ExpireReservation(float now)
        {
            if (_reservedUntil > 0f && !float.IsPositiveInfinity(_reservedUntil) && now >= _reservedUntil)
            {
                float endedAt = _reservedUntil;
                _reservedUntil = 0f;
                _hasEnded = true;
                _lastEndedAt = endedAt;
            }
        }
    }
}
