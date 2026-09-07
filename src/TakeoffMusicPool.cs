using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace MusicRandomizer
{
    internal static class TakeoffMusicPool
    {
        private sealed class Song
        {
            public readonly string Name;
            public readonly SortedSet<string> AircraftNames = new SortedSet<string>(StringComparer.Ordinal);
            public ConfigEntry<bool> Entry;
            public bool AmbiguityLogged;

            public Song(string name)
            {
                Name = name;
            }
        }

        private static readonly Dictionary<AudioClip, Song> Clips = new Dictionary<AudioClip, Song>();
        private static readonly Dictionary<string, Song> Songs = new Dictionary<string, Song>(StringComparer.Ordinal);
        private static float _nextDiscoveryAt;
        private static float _missingDataRetrySeconds = 1f;
        private static bool _missingDataReported;

        public static bool TryPickRandom(out AudioClip clip)
        {
            var candidates = new List<AudioClip>();
            foreach (var candidate in Clips)
            {
                if (candidate.Key != null && candidate.Value.Entry != null && candidate.Value.Entry.Value)
                {
                    candidates.Add(candidate.Key);
                }
            }

            clip = candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
            return clip != null;
        }

        public static bool Contains(AudioClip clip)
        {
            return clip != null && Clips.ContainsKey(clip);
        }

        public static bool IsEnabled(AudioClip clip)
        {
            return clip != null && Clips.TryGetValue(clip, out Song song)
                && song.Entry != null && song.Entry.Value;
        }

        public static void Register(AudioClip clip, float now)
        {
            Song song = AddClip(clip, null);
            if (song != null && song.Entry == null)
            {
                song.Entry = ModConfig.TryBindSong(song.Name, song.AircraftNames, now);
            }
        }

        public static void Discover(float now)
        {
            if (now < _nextDiscoveryAt)
            {
                return;
            }

            // Update and takeoff requests share this deadline, including empty or failed scans.
            _nextDiscoveryAt = now + 1f;
            var destroyed = new List<AudioClip>();
            foreach (AudioClip clip in Clips.Keys)
            {
                if (clip == null)
                {
                    destroyed.Add(clip);
                }
            }

            foreach (AudioClip clip in destroyed)
            {
                Clips.Remove(clip);
            }

            if (MainMenu.State == MainMenu.LoadingState.Loaded)
            {
                bool foundClip = false;
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (encyclopedia != null && encyclopedia.aircraft != null)
                {
                    foreach (AircraftDefinition definition in encyclopedia.aircraft)
                    {
                        if (definition == null || definition.aircraftParameters == null)
                        {
                            continue;
                        }

                        if (AddClip(definition.aircraftParameters.takeoffMusic, definition.unitName) != null)
                        {
                            foundClip = true;
                        }
                    }
                }

                if (foundClip)
                {
                    _missingDataRetrySeconds = 1f;
                    _missingDataReported = false;
                }
                else
                {
                    _missingDataRetrySeconds = Mathf.Min(_missingDataRetrySeconds * 2f, 30f);
                    _nextDiscoveryAt = now + _missingDataRetrySeconds;
                    if (!_missingDataReported)
                    {
                        _missingDataReported = true;
                        Plugin.Log?.LogWarning("Takeoff catalog unavailable after menu load; retrying with backoff (up to 30 seconds).");
                    }
                }
            }

            // Gather the whole discovery batch's aircraft labels before the first Bind freezes metadata.
            foreach (Song song in Songs.Values)
            {
                if (song.Entry == null)
                {
                    song.Entry = ModConfig.TryBindSong(song.Name, song.AircraftNames, now);
                }
            }
        }

        private static Song AddClip(AudioClip clip, string aircraftName)
        {
            if (clip == null)
            {
                return null;
            }

            if (!Clips.TryGetValue(clip, out Song song))
            {
                string name = clip.name ?? "";
                if (!Songs.TryGetValue(name, out song))
                {
                    song = new Song(name);
                    Songs.Add(name, song);
                }

                bool ambiguous = name.Length == 0;
                if (!song.AmbiguityLogged && !ambiguous)
                {
                    foreach (var known in Clips)
                    {
                        if (known.Key != null && known.Value == song)
                        {
                            ambiguous = true;
                            break;
                        }
                    }
                }

                Clips.Add(clip, song);
                if (ambiguous && !song.AmbiguityLogged)
                {
                    song.AmbiguityLogged = true;
                    Plugin.Log?.LogWarning(
                        $"Ambiguous takeoff song identity '{ModConfig.GetSongLabel(name)}'. "
                        + "One checkbox controls all clips in this exact-name group; they may be different recordings.");
                }

                Plugin.Log?.LogDebug($"Registered takeoff clip '{ModConfig.GetSongLabel(name)}' ({ModConfig.GetSongKey(name)}).");
            }

            if (song.Entry == null && !string.IsNullOrEmpty(aircraftName))
            {
                song.AircraftNames.Add(aircraftName);
            }

            return song;
        }
    }
}
