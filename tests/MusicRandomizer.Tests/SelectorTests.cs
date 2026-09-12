using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using MusicRandomizer.Patches;
using UnityEngine;
using static MusicRandomizer.Tests.Program;
using Random = UnityEngine.Random;

namespace MusicRandomizer.Tests
{
    internal static class SelectorTests
    {
        public static void CandidateBoundariesAndGroups(string directory)
        {
            var config = InitConfig(directory);
            Check(!TakeoffMusicPool.TrySelectQueued(out var empty) && empty == null, "Empty catalog must fail explicitly");
            Equal(0, Random.Calls, "Empty selection must not draw a random index");
            var shared = new AudioClip { name = "Shared" };
            var duplicate = new AudioClip { name = "Shared" };
            var other = new AudioClip { name = "Other" };
            SetCatalog(Definition(shared, "Beta"), Definition(shared, "Alpha"), Definition(duplicate, "Gamma"), Definition(other, "Delta"));
            TakeoffMusicPool.Discover(0f);
            Equal(5, config.Count, "Three static settings plus two song-name checkboxes");
            Equal(1, Plugin.Warnings.Count, "One warning for distinct same-name objects");
            string label = SongEntry(config, "Shared").Description.Tags.OfType<DisplayNameAttribute>().Single().DisplayName;
            Equal("Shared (Alpha, Beta, Gamma)", label, "Gather and sort all initial aircraft labels before binding");
            var actual = new HashSet<AudioClip>();
            for (int index = 0; index < 3; index++)
            {
                Check(TakeoffMusicPool.TrySelectQueued(out var selected), "All-enabled selection");
                actual.Add(selected);
                TakeoffMusicPool.ConfirmStarted(selected, true);
            }
            Check(actual.SetEquals(new[] { shared, duplicate, other }), "Enumerate every candidate deterministically");
            Equal(2, Random.Calls, "Three unique objects require two Fisher-Yates draws per cycle");

            SongEntry(config, "Shared").Value = false;
            Check(!TakeoffMusicPool.IsEnabled(shared) && !TakeoffMusicPool.IsEnabled(duplicate), "Group exclusion covers every member");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Check(TakeoffMusicPool.TrySelectQueued(out var selected) && selected == other, "One enabled clip may replay");
                TakeoffMusicPool.ConfirmStarted(selected, true);
            }
            Check(TakeoffMusicPool.Contains(shared), "Disabled song remains recognizable");
            SongEntry(config, "Other").Value = false;
            Check(!TakeoffMusicPool.TrySelectQueued(out _), "All-off must not fall back");

            var late = new AudioClip { name = "Shared" };
            TakeoffMusicPool.Register(late, 1f);
            Check(!TakeoffMusicPool.IsEnabled(late), "Late same-name member inherits exclusion");
            Equal(1, Plugin.Warnings.Count, "Ambiguity warning is once per identity");
            var unnamed = new AudioClip();
            var emptyName = new AudioClip { name = "" };
            TakeoffMusicPool.Register(unnamed, 1f);
            TakeoffMusicPool.Register(emptyName, 1f);
            Equal(2, Plugin.Warnings.Count, "One warning for unnamed group");
            Check(SongEntry(config, null).Value, "New unnamed group defaults enabled even when prior songs are all off");
            SongEntry(config, "").Value = false;
            Check(!TakeoffMusicPool.IsEnabled(unnamed) && !TakeoffMusicPool.IsEnabled(emptyName), "Unnamed group exclusion");
            TakeoffMusicPool.Register(new AudioClip { name = "Unnamed" }, 2f);
            Check(SongEntry(config, "Unnamed").Value, "Literal Unnamed is a separate identity");
        }

        public static void DiscoveryAndBackoff(string directory)
        {
            var config = InitConfig(directory);
            ModConfig.Enabled.Value = false;
            ModConfig.RandomizeTakeoffMusic.Value = false;
            var original = new AudioClip { name = "Request-only" };
            for (int i = 0; i < 10; i++) TakeoffMusicPool.Discover(i / 10f);
            Equal(0, Encyclopedia.Reads, "Do not read logging singleton before menu readiness");
            Check(!MusicPlaybackState.IsInitialized, "Discovery test starts without playback initialization");
            TakeoffMusicPool.Register(original, 0.5f);
            Check(SongEntry(config, original.name).Value, "Known originals register before broad readiness");
            using (var locked = new FileStream(config.ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                for (int i = 0; i < 100; i++) Check(TakeoffMusicPool.Contains(original), "Contains remains pure membership");
                Equal(0, Encyclopedia.Reads, "Contains never discovers");
                Equal(0, Plugin.Warnings.Count, "Contains never binds/saves");
            }

            MainMenu.State = MainMenu.LoadingState.Loaded;
            TakeoffMusicPool.Discover(1f);
            Equal(1, Encyclopedia.Reads, "First ready scan");
            MusicPlaybackState.Initialize();
            ModConfig.Enabled.Value = true;
            for (int i = 0; i < 10; i++) Request(new MusicManager(), original, 1.1f + i / 10f);
            Equal(1, Encyclopedia.Reads, "Takeoff prefix shares failed broad-scan deadline");
            TakeoffMusicPool.Discover(3f);
            Equal(2, Encyclopedia.Reads, "First backoff retry");
            TakeoffMusicPool.Discover(6.9f);
            Equal(2, Encyclopedia.Reads, "Exponential backoff is not bypassed");
            Encyclopedia.Instance = new Encyclopedia { aircraft = new List<AircraftDefinition>() };
            TakeoffMusicPool.Discover(7f);
            Equal(3, Encyclopedia.Reads, "Empty catalog is retryable");
            var late = new AudioClip { name = "Late catalog" };
            Encyclopedia.Instance.aircraft.Add(Definition(late, "Late owner"));
            ModConfig.Enabled.Value = false;
            TakeoffMusicPool.Discover(14.9f);
            Check(!TakeoffMusicPool.Contains(late), "Late data respects existing deadline");
            TakeoffMusicPool.Discover(15f);
            Check(TakeoffMusicPool.IsEnabled(late), "Discovery works with feature and randomization disabled");
            var next = new AudioClip { name = "Next" };
            Encyclopedia.Instance.aircraft.Add(Definition(next, "Next owner"));
            TakeoffMusicPool.Discover(16f);
            Check(TakeoffMusicPool.Contains(next), "Successful scan restores one-second interval");
            Equal(1, Plugin.Warnings.Count, "Missing-data warning does not repeat through backoff");
        }

        public static void DestroyedAndRecreatedClips(string directory)
        {
            var config = InitConfig(directory);
            var removed = new AudioClip { name = "Returning" };
            var stillLive = new AudioClip { name = "Still playing" };
            SetCatalog(Definition(removed, "Old owner"), Definition(stillLive, "Live owner"));
            TakeoffMusicPool.Discover(0f);
            SongEntry(config, "Returning").Value = false;
            removed.Destroyed = true;
            Check(!TakeoffMusicPool.IsEnabled(removed) && !TakeoffMusicPool.Contains(removed), "Destroyed clips are never eligible or active members");
            var deadDefinition = Definition(new AudioClip { name = "Invalid definition" }, "Invalid");
            deadDefinition.Destroyed = true;
            var deadParameters = Definition(new AudioClip { name = "Invalid parameters" }, "Invalid");
            deadParameters.aircraftParameters.Destroyed = true;
            SetCatalog(null, deadDefinition, deadParameters, Definition(removed, "Destroyed clip"));
            TakeoffMusicPool.Discover(1f);
            var clips = (IDictionary)typeof(TakeoffMusicPool).GetField("Clips", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Equal(1, clips.Count, "Destroyed references are actually pruned, not merely filtered");
            Check(TakeoffMusicPool.Contains(stillLive), "Live playback membership survives disappearance from encyclopedia");
            var recreated = new AudioClip { name = "Returning" };
            TakeoffMusicPool.Register(recreated, 2f);
            Check(!TakeoffMusicPool.IsEnabled(recreated), "Recreated identity reuses saved entry");
            Equal(5, config.Count, "No new entry for recreation or invalid objects");
            Encyclopedia.Instance.Destroyed = true;
            TakeoffMusicPool.Discover(3f);
            Check(TakeoffMusicPool.Contains(stillLive), "Destroyed encyclopedia does not clear live catalog");
        }

        public static void RequestModesAndGuards(string directory)
        {
            var config = InitConfig(directory);
            MusicPlaybackState.Initialize();
            ModConfig.CooldownSeconds.Value = 0f;
            ModConfig.RandomizeTakeoffMusic.Value = false;
            var assigned = new AudioClip { name = "Assigned" };
            var other = new AudioClip { name = "Other" };
            TakeoffMusicPool.Register(assigned, 0f);
            TakeoffMusicPool.Register(other, 0f);
            SongEntry(config, "Assigned").Value = false;
            var manager = new MusicManager { currentClipPriority = 9f };
            manager.currentSource.clip = new AudioClip { name = "Tactical" };
            manager.currentSource.isPlaying = true;
            var skipped = Request(manager, assigned, 1f);
            Check(!skipped.Allowed && skipped.Clip == assigned && !skipped.Replay && !skipped.Replace && skipped.Priority == 2f, "Assigned exclusion skips without argument mutation or substitution");
            Check(manager.currentSource.isPlaying && manager.currentSource.clip.name == "Tactical", "Skip leaves unrelated source alone");

            ModConfig.RandomizeTakeoffMusic.Value = true;
            var selected = Request(manager, assigned, 2f);
            Check(selected.Allowed && selected.Clip == other && selected.Replay && selected.Replace, "Random mode selects only eligible clips with replay/replacement enabled");
            Equal(9f, selected.Priority, "Existing priority override is retained");
            SongEntry(config, "Other").Value = false;
            skipped = Request(manager, assigned, 3f);
            Check(!skipped.Allowed && skipped.Clip == assigned && skipped.Priority == 2f && !skipped.Replay && !skipped.Replace, "All-off has no original fallback or argument mutation");
            Check(MusicPlaybackState.IsCooldownComplete(3f, 60f), "Rejected requests do not manufacture cooldown state");

            ModConfig.Enabled.Value = false;
            var vanilla = Request(manager, assigned, 4f);
            Check(vanilla.Allowed && vanilla.Clip == assigned && !vanilla.Replay && !vanilla.Replace && vanilla.Priority == 2f, "Global disable is vanilla passthrough");
            ModConfig.Enabled.Value = true;
            var outside = Request(manager, assigned, 5f, false);
            Check(outside.Allowed && !outside.Replay, "Same disabled clip outside takeoff context is unaffected");
            var noClip = Request(manager, null, 6f);
            Check(noClip.Allowed && noClip.Clip == null && !noClip.Replay, "Null originals remain no-op passthrough");
            GameManager.IsHeadless = true;
            var headlessClip = new AudioClip { name = "Headless" };
            Check(Request(manager, headlessClip, 7f).Allowed && !TakeoffMusicPool.Contains(headlessClip), "Headless request does not discover or register");
            GameManager.IsHeadless = false;
            ModConfig.RandomizeTakeoffMusic.Value = false;
            var late = new AudioClip { name = "Request-only enabled" };
            var lateRequest = Request(manager, late, 8f);
            Check(lateRequest.Allowed && lateRequest.Clip == late && SongEntry(config, late.name).Value, "Enabled assigned original absent from encyclopedia is registered and used");
        }

        public static void PlaybackProtectionAndCooldown(string directory)
        {
            var config = InitConfig(directory);
            MusicPlaybackState.Initialize();
            var playing = new AudioClip { name = "Playing" };
            var requested = new AudioClip { name = "Requested" };
            TakeoffMusicPool.Register(playing, 0f);
            TakeoffMusicPool.Register(requested, 0f);
            SongEntry(config, "Playing").Value = false;
            var manager = new MusicManager();
            manager.currentSource.clip = playing;
            manager.currentSource.isPlaying = true;
            Check(!Request(manager, requested, 1f).Allowed, "Disabled current song still protects playback");
            manager.currentSource.isPlaying = false;
            manager.fadeSource.clip = playing;
            manager.fadeSource.isPlaying = true;
            Check(!Request(manager, requested, 2f).Allowed, "Disabled fading song still protects playback");
            manager.fadeSource.isPlaying = false;
            Check(!Request(manager, requested, 3f).Allowed, "Cooldown starts on observed end");
            SongEntry(config, "Requested").Value = false;
            SongEntry(config, "Requested").Value = true;
            Check(!Request(manager, requested, 62f).Allowed, "Checkbox changes cannot reset or bypass cooldown");
            Check(Request(manager, requested, 63f).Allowed, "Realtime boundary is inclusive");
            Check(Request(manager, requested, 64f).Allowed, "Submitted request alone is not proof of playback and creates no cooldown");
        }

        public static void OriginalRegisteredBeforeObservation(string directory)
        {
            InitConfig(directory);
            MusicPlaybackState.Initialize();
            var original = new AudioClip { name = "Unlisted original" };
            var random = new AudioClip { name = "Random" };
            TakeoffMusicPool.Register(random, 0f);
            var manager = new MusicManager();
            manager.fadeSource.clip = original;
            manager.fadeSource.isPlaying = true;
            Check(!Request(manager, original, 1f).Allowed, "Registration must precede source classification, not follow randomization");
            Check(TakeoffMusicPool.Contains(original), "Random mode cannot hide original from catalog");
            Equal(0, Random.Calls, "Active-source guard must run before random draw");
        }

        private static AircraftDefinition Definition(AudioClip clip, string owner)
        {
            return new AircraftDefinition { unitName = owner, aircraftParameters = new AircraftParameters { takeoffMusic = clip } };
        }

        private static void SetCatalog(params AircraftDefinition[] definitions)
        {
            MainMenu.State = MainMenu.LoadingState.Loaded;
            Encyclopedia.Instance = new Encyclopedia { aircraft = definitions.ToList() };
        }

        internal static (bool Allowed, AudioClip Clip, bool Replay, bool Replace, float Priority) Request(
            MusicManager manager, AudioClip original, float now, bool takeoff = true,
            Action<MusicManager, AudioClip> outcome = null, bool aircraftArguments = false)
        {
            Time.realtimeSinceStartup = now;
            if (takeoff) TakeoffMusicContext.Enter();
            try
            {
                object[] arguments = { manager, original, false, aircraftArguments, aircraftArguments ? 0f : 2f, null };
                bool allowed = (bool)typeof(TakeoffMusicRequestPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, arguments);
                // Outcomes model the game's result independently of production confirmation logic.
                // No outcome means a rejected/no-start original call. Postfix also runs on suppression.
                if (allowed) outcome?.Invoke(manager, (AudioClip)arguments[1]);
                typeof(TakeoffMusicRequestPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new[] { manager, arguments[1], arguments[5] });
                return (allowed, (AudioClip)arguments[1], (bool)arguments[2], (bool)arguments[3], (float)arguments[4]);
            }
            finally
            {
                if (takeoff) TakeoffMusicContext.Exit();
            }
        }
    }
}
