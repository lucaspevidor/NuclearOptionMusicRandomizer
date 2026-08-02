using System.Collections.Generic;
using UnityEngine;

namespace MusicRandomizer
{
    internal static class TakeoffMusicPool
    {
        private static readonly List<AudioClip> Clips = new List<AudioClip>();
        private static readonly HashSet<AudioClip> ClipSet = new HashSet<AudioClip>();
        private static bool _loaded;

        public static AudioClip PickRandom(AudioClip fallback)
        {
            Load();
            return Clips.Count > 0 ? Clips[Random.Range(0, Clips.Count)] : fallback;
        }

        public static bool Contains(AudioClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            Load();
            return ClipSet.Contains(clip);
        }

        public static void Register(AudioClip clip)
        {
            if (clip != null && ClipSet.Add(clip))
            {
                Clips.Add(clip);
            }
        }

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null || encyclopedia.aircraft == null)
            {
                return;
            }

            foreach (AircraftDefinition definition in encyclopedia.aircraft)
            {
                AudioClip clip = definition?.aircraftParameters?.takeoffMusic;
                Register(clip);
            }

            _loaded = true;
        }
    }
}
