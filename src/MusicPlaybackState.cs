using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer
{
    internal static class MusicPlaybackState
    {
        private static AccessTools.FieldRef<MusicManager, AudioSource> _currentSource;
        private static AccessTools.FieldRef<MusicManager, AudioSource> _fadeSource;
        private static AccessTools.FieldRef<MusicManager, float> _currentClipPriority;
        private static bool _wasAircraftMusicActive;
        private static bool _hasEnded;
        private static float _lastEndedAt;

        public static bool IsInitialized { get; private set; }

        public static MusicManager Manager { get; private set; }

        public static void Initialize()
        {
            _currentSource = AccessTools.FieldRefAccess<MusicManager, AudioSource>("currentSource");
            _fadeSource = AccessTools.FieldRefAccess<MusicManager, AudioSource>("fadeSource");
            _currentClipPriority = AccessTools.FieldRefAccess<MusicManager, float>("currentClipPriority");
            IsInitialized = true;
        }

        public static void Register(MusicManager manager)
        {
            Manager = manager;
        }

        public static bool Refresh(MusicManager manager, float now)
        {
            bool active = IsAircraftMusicActive(manager);
            if (_wasAircraftMusicActive && !active)
            {
                _hasEnded = true;
                _lastEndedAt = now;
            }

            _wasAircraftMusicActive = active;
            return active;
        }

        public static bool IsAnyMusicActive(MusicManager manager)
        {
            return IsPlaying(_currentSource(manager)) || IsPlaying(_fadeSource(manager));
        }

        public static float GetCurrentPriority(MusicManager manager)
        {
            return _currentClipPriority(manager);
        }

        public static bool IsCooldownComplete(float now, float cooldownSeconds)
        {
            return !_hasEnded || now - _lastEndedAt >= Mathf.Max(0f, cooldownSeconds);
        }

        private static bool IsAircraftMusicActive(MusicManager manager)
        {
            return IsPlayingAircraftMusic(_currentSource(manager)) || IsPlayingAircraftMusic(_fadeSource(manager));
        }

        private static bool IsPlayingAircraftMusic(AudioSource source)
        {
            return IsPlaying(source) && TakeoffMusicPool.Contains(source.clip);
        }

        private static bool IsPlaying(AudioSource source)
        {
            return source != null && source.isPlaying;
        }
    }
}
