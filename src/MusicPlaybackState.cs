using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer
{
    internal static class MusicPlaybackState
    {
        internal readonly struct StartSnapshot
        {
            public readonly AudioSource Incoming;
            private readonly AudioClip _previousClip;
            private readonly bool _wasPlaying;
            private readonly bool _wasFading;

            public StartSnapshot(AudioSource incoming, bool wasFading)
            {
                Incoming = incoming;
                _previousClip = incoming != null ? incoming.clip : null;
                _wasPlaying = incoming != null && incoming.isPlaying;
                _wasFading = wasFading;
            }

            public bool HasStarted(AudioClip clip)
            {
                // Observe the original incoming object, not fields that the async fade can swap.
                return !_wasFading && clip != null && Incoming != null && Incoming.isPlaying
                    && Incoming.clip == clip && (!_wasPlaying || _previousClip != clip);
            }
        }

        private static AccessTools.FieldRef<MusicManager, AudioSource> _currentSource;
        private static AccessTools.FieldRef<MusicManager, AudioSource> _fadeSource;
        private static AccessTools.FieldRef<MusicManager, float> _currentClipPriority;
        private static AccessTools.FieldRef<MusicManager, bool> _isFading;
        // Binding-free recognition for takeoff clips absent from the discovered catalog in vanilla mode.
        private static readonly Dictionary<AudioSource, AudioClip> ConfirmedSources = new Dictionary<AudioSource, AudioClip>();
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
            _isFading = AccessTools.FieldRefAccess<MusicManager, bool>("isFading");
            IsInitialized = true;
        }

        public static void Register(MusicManager manager)
        {
            Manager = manager;
        }

        public static bool Refresh(MusicManager manager, float now)
        {
            PruneConfirmedSources();
            bool active = IsAircraftMusicActive(manager);
            if (_wasAircraftMusicActive && !active)
            {
                _hasEnded = true;
                _lastEndedAt = now;
            }

            _wasAircraftMusicActive = active;
            return active;
        }

        public static StartSnapshot CaptureStart(MusicManager manager)
        {
            return new StartSnapshot(_fadeSource(manager), _isFading(manager));
        }

        public static void ObserveStarted(MusicManager manager, StartSnapshot snapshot, AudioClip clip, float now)
        {
            ConfirmedSources[snapshot.Incoming] = clip;
            Register(manager);
            Refresh(manager, now);
        }

        private static void PruneConfirmedSources()
        {
            var expired = new List<AudioSource>();
            foreach (var source in ConfirmedSources)
            {
                if (!IsPlaying(source.Key) || source.Value == null || source.Key.clip != source.Value)
                {
                    expired.Add(source.Key);
                }
            }

            foreach (AudioSource source in expired)
            {
                ConfirmedSources.Remove(source);
            }
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
            return IsPlaying(source) && (TakeoffMusicPool.Contains(source.clip)
                || (ConfirmedSources.TryGetValue(source, out AudioClip clip) && clip != null && source.clip == clip));
        }

        private static bool IsPlaying(AudioSource source)
        {
            return source != null && source.isPlaying;
        }
    }
}
