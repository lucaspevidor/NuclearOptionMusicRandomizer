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

        public static bool IsInitialized { get; private set; }

        public static void Initialize()
        {
            _fadeSource = AccessTools.FieldRefAccess<MusicManager, AudioSource>("fadeSource");
            _isFading = AccessTools.FieldRefAccess<MusicManager, bool>("isFading");
            IsInitialized = true;
        }

        public static bool Refresh(MusicManager manager, float now)
        {
            bool active = IsActive(manager);
            if (_wasActive && !active)
            {
                _hasEnded = true;
                _lastEndedAt = now;
            }

            _wasActive = active;
            return active;
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
    }
}
