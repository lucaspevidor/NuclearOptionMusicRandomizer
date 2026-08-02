using System.Collections.Generic;
using UnityEngine;

namespace MusicRandomizer
{
    internal static class TakeoffMusicPool
    {
        public static AudioClip PickRandom(AudioClip fallback)
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null || encyclopedia.aircraft == null)
            {
                return fallback;
            }

            var clips = new List<AudioClip>();
            var seen = new HashSet<AudioClip>();

            foreach (AircraftDefinition definition in encyclopedia.aircraft)
            {
                AudioClip clip = definition?.aircraftParameters?.takeoffMusic;
                if (clip != null && seen.Add(clip))
                {
                    clips.Add(clip);
                }
            }

            return clips.Count > 0 ? clips[Random.Range(0, clips.Count)] : fallback;
        }
    }
}
