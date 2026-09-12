using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using BepInEx.Configuration;

namespace MusicRandomizer
{
    internal static class ModConfig
    {
        private static ConfigFile _config;
        private static float _nextSongBindAttempt;
        private static bool _songSavePending;

        public static ConfigEntry<bool> Enabled { get; private set; }

        public static ConfigEntry<bool> RandomizeTakeoffMusic { get; private set; }

        public static ConfigEntry<float> CooldownSeconds { get; private set; }

        public static void Init(ConfigFile config)
        {
            _config = config;
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Replay enabled aircraft songs on eligible takeoffs. Disabling the mod restores vanilla selection, ignoring song checkboxes.");

            RandomizeTakeoffMusic = config.Bind(
                "General",
                "RandomizeTakeoffMusic",
                true,
                "Play enabled aircraft songs in shuffled cycles, consuming a turn only when playback starts. "
                + "Avoid repeating the last started takeoff song when a different eligible turn remains. "
                + "Disable this to use the current aircraft's track, or skip it if unchecked; queue progress is retained.");

            CooldownSeconds = config.Bind(
                "Timing",
                "CooldownSeconds",
                60f,
                new ConfigDescription(
                    "Minimum time after an aircraft song ends before another one may start.",
                    new AcceptableValueRange<float>(0f, 600f)));
        }

        public static string GetSongKey(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Song_Unnamed";
            }

            // Encode UTF-16 code units directly: even unpaired surrogates retain exact identity.
            var key = new StringBuilder("Song_");
            foreach (char character in name)
            {
                key.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }

            return key.ToString();
        }

        public static string GetSongLabel(string name)
        {
            return string.IsNullOrEmpty(name) ? "Unnamed takeoff songs" : ReadableName(name);
        }

        public static ConfigEntry<bool> TryBindSong(string name, IEnumerable<string> aircraftNames, float now)
        {
            if (_config == null || now < _nextSongBindAttempt)
            {
                return null;
            }

            try
            {
                // Bind can register an entry before Save throws. Rebinding alone would not retry that save.
                if (_songSavePending)
                {
                    _config.Save();
                    _songSavePending = false;
                }

                string label = GetSongLabel(name);
                var aircraft = new List<string>();
                foreach (string aircraftName in aircraftNames)
                {
                    aircraft.Add(ReadableName(aircraftName));
                }

                string owners = aircraft.Count == 0 ? "" : " (" + string.Join(", ", aircraft) + ")";
                string scope = string.IsNullOrEmpty(name)
                    ? "Applies to all takeoff clips with null or empty names, including later-discovered clips."
                    : "Applies to all takeoff clips with this exact, case-sensitive name, including later-discovered clips.";
                return _config.Bind(
                    "Takeoff Songs",
                    GetSongKey(name),
                    true,
                    new ConfigDescription(
                        label + owners + ". Enable for future takeoffs while the mod is enabled. " + scope
                        + " Aircraft labels are discovery-time information and may be incomplete."
                        + " Unchecking does not stop current playback or reset cooldown.",
                        null,
                        new DisplayNameAttribute(label + owners)));
            }
            catch (Exception exception) when (exception is IOException
                                              || exception is UnauthorizedAccessException
                                              || exception is SecurityException)
            {
                _songSavePending = true;
                _nextSongBindAttempt = now + 30f;
                Plugin.Log?.LogWarning(
                    $"Could not resolve takeoff song settings ({exception.GetType().Name}: {exception.Message}). "
                    + "Unresolved songs remain excluded; retrying in 30 seconds.");
                return null;
            }
        }

        private static string ReadableName(string name)
        {
            var readable = new StringBuilder();
            foreach (char character in name)
            {
                // Keep asset names from injecting lines into persisted config descriptions or the log.
                if (char.IsControl(character))
                {
                    readable.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    readable.Append(character);
                }
            }

            return readable.ToString();
        }
    }
}
