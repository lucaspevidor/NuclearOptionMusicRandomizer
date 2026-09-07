using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using static MusicRandomizer.Tests.Program;

namespace MusicRandomizer.Tests
{
    internal static class ConfigTests
    {
        public static void ExactKeys(string directory)
        {
            var config = InitConfig(directory);
            string[] names = { "A", "a", " A", "A ", "#intro", "a=b", "[song]", "\"song\"", "a/b", "a\\b", "\u00e9", "e\u0301", "\u65e5\u672c", "\ud83c\udfb5", "\ud800", "\ufffd", "Unnamed", " ", "a\r\n[Injected]\nx=y" };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                string key = ModConfig.GetSongKey(name);
                Check(keys.Add(key), "Exact names must not collide");
                Check(key.StartsWith("Song_") && key.Substring(5).All(c => "0123456789ABCDEF".Contains(c)), "Key must be config-safe uppercase hex");
                var definition = new ConfigDefinition("Takeoff Songs", key);
                Check(config.Bind(definition, true).Value, "Real BepInEx must accept each key");
                Equal(key, ModConfig.GetSongKey(name), "Encoding must be deterministic");
            }

            Equal("Song_0041", ModConfig.GetSongKey("A"), "Fixed UTF-16 persistence vector");
            Equal("Song_00E9", ModConfig.GetSongKey("\u00e9"), "Non-ASCII persistence vector");
            Equal("Song_Unnamed", ModConfig.GetSongKey(null), "Reserved unnamed key");
            Equal(ModConfig.GetSongKey(null), ModConfig.GetSongKey(""), "Null and empty deliberately share a group");
            Check(!keys.Contains(ModConfig.GetSongKey("")), "Unnamed group must not collide with a named clip");
        }

        public static void PersistenceAndMetadata(string directory)
        {
            var config = InitConfig(directory);
            ModConfig.Enabled.Value = false;
            ModConfig.RandomizeTakeoffMusic.Value = false;
            ModConfig.CooldownSeconds.Value = 123f;
            string name = "#Flight [A]=\"\u00e9\"";
            var entry = ModConfig.TryBindSong(name, new[] { "Aircraft Alpha", "Aircraft Beta" }, 0f);
            Check(entry.Value, "New songs default enabled");
            entry.Value = false;
            Check(entry.Description.Description.Contains(name), "Readable title must be persisted");
            Check(entry.Description.Description.Contains("later-discovered"), "Grouping must be described from first binding");
            Check(entry.Description.Description.Contains("Aircraft Alpha, Aircraft Beta"), "Owners must be readable");
            Check(entry.Description.Tags.OfType<DisplayNameAttribute>().Single().DisplayName.Contains(name), "Native UI tag must be friendly");
            Check(File.ReadAllText(config.ConfigFilePath).Contains(name), "Config comments must include the title, not just encoded keys");

            var loaded = new ConfigFile(config.ConfigFilePath, false);
            ModConfig.Init(loaded);
            loaded.Save();
            // Another reconstruction after saving static entries proves that orphan song values survived.
            loaded = new ConfigFile(config.ConfigFilePath, false);
            ModConfig.Init(loaded);
            Check(!ModConfig.Enabled.Value && !ModConfig.RandomizeTakeoffMusic.Value, "Existing static values must survive");
            Equal(123f, ModConfig.CooldownSeconds.Value, "Cooldown persistence");
            var restored = ModConfig.TryBindSong(name, new[] { "Aircraft Beta", "Aircraft Alpha" }, 0f);
            Check(!restored.Value, "Late binding must recover a saved exclusion");
            var rebound = ModConfig.TryBindSong(name, new[] { "Late aircraft" }, 1f);
            Check(ReferenceEquals(restored, rebound), "Same identity must reuse the real entry");
            Check(!rebound.Description.Description.Contains("Late aircraft"), "Rebinding does not enrich immutable metadata");
            Check(ModConfig.TryBindSong("Brand new", Array.Empty<string>(), 1f).Value, "New names default enabled despite exclusions");

            var controls = ModConfig.TryBindSong("Title\r\n[Injected]\nx=y", new[] { "Owner\rInjected=true" }, 2f);
            Check(!controls.Description.Description.Contains('\r') && !controls.Description.Description.Contains('\n'), "Control characters must not inject config lines");
            var roundTrip = new ConfigFile(loaded.ConfigFilePath, false);
            Check(roundTrip.Bind("Takeoff Songs", controls.Definition.Key, false).Value, "Escaped descriptions must round-trip");
            Check(!roundTrip.Keys.Any(key => key.Section == "Injected"), "No injected section");
        }

        public static void BindFailureBeforeRegistration(string directory)
        {
            string path = Path.Combine(directory, "selector.cfg");
            var seed = new ConfigFile(path, false);
            seed.Bind("Takeoff Songs", ModConfig.GetSongKey("Excluded later"), true).Value = false;
            var config = InitConfig(directory);
            ModConfig.RandomizeTakeoffMusic.Value = false;
            ModConfig.CooldownSeconds.Value = 0f;
            var current = new AudioClip { name = "Current" };
            var unresolved = new AudioClip { name = "Excluded later" };
            TakeoffMusicPool.Register(current, 0f);
            MusicPlaybackState.Initialize();
            var manager = new MusicManager();
            manager.currentSource.clip = current;
            manager.currentSource.isPlaying = true;
            Check(MusicPlaybackState.Refresh(manager, 0f), "Initial aircraft observation");

            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                manager.currentSource.isPlaying = false;
                Check(!SelectorTests.Request(manager, unresolved, 1f).Allowed, "Failed assigned binding must skip even with a zero configured cooldown");
                Check(TakeoffMusicPool.Contains(unresolved), "Unresolved clips still belong to playback catalog");
                Check(!TakeoffMusicPool.IsEnabled(unresolved), "Failed binding is not default-enabled");
                Check(!config.TryGetEntry<bool>("Takeoff Songs", ModConfig.GetSongKey(unresolved.name), out _), "Failure occurred before entry registration");
                Check(!MusicPlaybackState.IsCooldownComplete(60f, 60f), "Request still observed the song ending at t=1");
                Equal(1, Plugin.Warnings.Count, "First bind error warning");

                for (int now = 2; now < 31; now++)
                {
                    TakeoffMusicPool.Register(new AudioClip { name = "Late " + now }, now);
                    TakeoffMusicPool.Discover(now);
                    MusicPlaybackState.Refresh(manager, now);
                    Check(TakeoffMusicPool.Contains(unresolved), "Polling must not lose membership");
                }

                Equal(1, Plugin.Warnings.Count, "New names and scans cannot bypass global bind backoff");
                Check(TakeoffMusicPool.IsEnabled(current), "Already-resolved entries remain usable during I/O failures");
                Check(TakeoffMusicPool.TryPickRandom(out var candidate) && candidate == current, "Random selection must exclude all unresolved clips");
                Equal(1, UnityEngine.Random.LastMaximum, "Exactly one resolved candidate during partial initialization");
                TakeoffMusicPool.Register(unresolved, 31f);
                Equal(2, Plugin.Warnings.Count, "One bounded retry while still locked");
            }

            TakeoffMusicPool.Register(unresolved, 60f);
            Check(!config.TryGetEntry<bool>("Takeoff Songs", ModConfig.GetSongKey(unresolved.name), out _), "Unlocking must not bypass retry deadline");
            TakeoffMusicPool.Register(unresolved, 61f);
            Check(!SongEntry(config, unresolved.name).Value, "Successful retry must honor saved exclusion");
            Check(!TakeoffMusicPool.IsEnabled(unresolved), "Saved false remains excluded after recovery");
            Check(MusicPlaybackState.IsCooldownComplete(61f, 60f), "Recovery does not reset observed cooldown");
        }

        public static void BindFailureAfterRegistration(string directory)
        {
            var config = InitConfig(directory);
            var clip = new AudioClip { name = "Registered before save failed" };
            FileStream locked = null;
            config.SettingChanged += (_, args) =>
            {
                if (args.ChangedSetting.Definition.Section == "Takeoff Songs" && locked == null)
                {
                    // The default-value notification is after the constructor's save but before Bind's final save.
                    locked = new FileStream(config.ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
            };

            try
            {
                TakeoffMusicPool.Register(clip, 0f);
                Check(SongEntry(config, clip.name).Value, "BepInEx retained the partially bound entry");
                Check(TakeoffMusicPool.Contains(clip) && !TakeoffMusicPool.IsEnabled(clip), "Partially bound true is still unresolved");
                Check(!TakeoffMusicPool.TryPickRandom(out _), "No fallback to partially bound true");
                TakeoffMusicPool.Register(clip, 30f);
                Equal(2, Plugin.Warnings.Count, "Retry must attempt the failed save, not just return the cached entry");
                Check(!TakeoffMusicPool.IsEnabled(clip), "Still ineligible after another failed save");
            }
            finally
            {
                locked?.Dispose();
            }

            TakeoffMusicPool.Register(clip, 60f);
            Check(TakeoffMusicPool.IsEnabled(clip), "Explicit save retry resolves existing true entry");
            using var readLock = new FileStream(config.ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            TakeoffMusicPool.Register(clip, 61f);
            TakeoffMusicPool.Discover(61f);
            Equal(2, Plugin.Warnings.Count, "Known settings must not be saved again in steady state");
        }
    }
}
