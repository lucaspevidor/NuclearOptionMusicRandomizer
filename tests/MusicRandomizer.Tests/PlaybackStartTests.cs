using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using static MusicRandomizer.Tests.Program;
using static MusicRandomizer.Tests.QueueTests;
using static MusicRandomizer.Tests.SelectorTests;
using Random = UnityEngine.Random;

namespace MusicRandomizer.Tests
{
    internal static class PlaybackStartTests
    {
        public static void QueuedStartsAndRejections(string directory)
        {
            InitConfig(directory);
            MusicPlaybackState.Initialize();
            ModConfig.CooldownSeconds.Value = 0f;
            var clips = Register("A", "B", "C");
            var manager = new MusicManager();
            manager.currentSource.clip = new AudioClip { name = "Tactical" };
            manager.currentSource.isPlaying = true;
            manager.isFading = true;
            var rejected = Request(manager, clips[2], 1f);
            Check(rejected.Allowed && rejected.Clip == clips[0], "Active game fade rejects after the prefix selects A");
            Expect(clips[0]);
            Check(LastStarted() == null, "Rejected fade cannot update history");
            Equal(2, Random.Calls, "Rejected request retains its exact pending permutation");

            manager.isFading = false;
            Request(manager, clips[2], 2f, outcome: AssignOnly);
            Expect(clips[0]);
            Check(LastStarted() == null, "Clip assignment without Play is not a start");
            // Incoming source already holds A but is stopped. Play and a source swap must confirm it.
            var incoming = manager.fadeSource;
            Request(manager, clips[2], 3f, outcome: StartAndSwap);
            Check(LastStarted() == clips[0] && manager.currentSource == incoming && manager.isFading, "Confirm against original incoming source even after fields swap and fading becomes true");
            Expect(clips[1]);
            Equal(2, Random.Calls, "Confirmation consumes once without building a new cycle");

            // Confirmation must mark activity immediately, even if playback stops before the next poll.
            manager.currentSource.isPlaying = false;
            manager.fadeSource.isPlaying = false;
            MusicPlaybackState.Refresh(manager, 4f);
            Check(!MusicPlaybackState.IsCooldownComplete(63f, 60f), "Short confirmed playback starts end-based cooldown on its first stopped poll");
            Check(MusicPlaybackState.IsCooldownComplete(64f, 60f), "Cooldown boundary follows actual observed end");

            var replacement = new MusicManager();
            Request(replacement, clips[2], 5f, outcome: StartIncoming);
            Check(LastStarted() == clips[1], "Manager replacement preserves B's queued turn");
            Expect(clips[2]);
        }

        public static void SourceConfirmationBoundaries(string directory)
        {
            InitConfig(directory);
            MusicPlaybackState.Initialize();
            ModConfig.Enabled.Value = false;
            var clips = Register("A", "B");
            Expect(clips[0]);
            var previous = new AudioClip { name = "Previous history" };

            var cases = new (string Name, Action<MusicManager> Before, Action<MusicManager, AudioClip> Outcome, bool Started)[]
            {
                ("stopped incoming", _ => { }, StartIncoming, true),
                ("stopped matching incoming", m => m.fadeSource.clip = clips[0], StartIncoming, true),
                ("playing different incoming", m => { m.fadeSource.clip = clips[1]; m.fadeSource.isPlaying = true; }, StartIncoming, true),
                ("source swap after start", _ => { }, StartAndSwap, true),
                ("post-call fading", _ => { }, (m, c) => { StartIncoming(m, c); m.isFading = true; }, true),
                ("unchanged matching incoming", m => { m.fadeSource.clip = clips[0]; m.fadeSource.isPlaying = true; }, NoStart, false),
                ("unchanged matching outgoing", m => { m.currentSource.clip = clips[0]; m.currentSource.isPlaying = true; }, NoStart, false),
                ("field swap alone", m => { m.currentSource.clip = clips[0]; m.currentSource.isPlaying = true; }, (m, _) => Swap(m), false),
                ("clip assignment only", _ => { }, AssignOnly, false),
                ("pre-call fading", m => m.isFading = true, NoStart, false),
                ("pre-call fading despite source change", m => m.isFading = true, StartIncoming, false),
                ("destroyed incoming before snapshot", m => m.fadeSource.Destroyed = true, NoStart, false),
                ("destroyed incoming after snapshot", _ => { }, (m, _) => m.fadeSource.Destroyed = true, false),
                ("destroyed manager after snapshot", _ => { }, (m, c) => { StartIncoming(m, c); m.Destroyed = true; }, false),
                ("missing incoming", m => m.fadeSource = null, NoStart, false)
            };

            float now = 0f;
            foreach (var test in cases)
            {
                TakeoffMusicPool.ConfirmStarted(previous, false);
                var manager = new MusicManager();
                test.Before(manager);
                Request(manager, clips[0], ++now, outcome: test.Outcome, aircraftArguments: true);
                Equal(test.Started ? clips[0] : previous, LastStarted(), test.Name);
                Equal(2, RemainingCount(), "Vanilla observation cannot consume queue entries: " + test.Name);
            }

            TakeoffMusicPool.ConfirmStarted(previous, false);
            var destroyed = new AudioClip { name = "Destroyed during call" };
            Request(new MusicManager(), destroyed, ++now, outcome: (m, c) => { StartIncoming(m, c); c.Destroyed = true; }, aircraftArguments: true);
            Equal(previous, LastStarted(), "Destroyed requested clip cannot confirm");
        }

        public static void VanillaGameRejections(string directory)
        {
            InitConfig(directory);
            MusicPlaybackState.Initialize();
            ModConfig.Enabled.Value = false;
            var clips = Register("A", "B");
            Expect(clips[0]);
            var previous = new AudioClip { name = "Previous" };
            TakeoffMusicPool.ConfirmStarted(previous, false);

            // Outcomes are explicit game rejections, independent of production's observer.
            // Replacement uses a generic caller; the aircraft itself always supplies replacePlaying=true.
            var replacement = new MusicManager();
            replacement.currentSource.isPlaying = true;
            replacement.currentSource.clip = new AudioClip { name = "Other music" };
            var result = Request(replacement, clips[0], 1f, outcome: NoStart);
            Check(!result.Replace && !result.Replay && result.Priority == 2f, "Vanilla replacement rejection retains original generic arguments");

            var priority = new MusicManager { currentClipPriority = 10f };
            priority.currentSource.isPlaying = true;
            priority.currentSource.clip = replacement.currentSource.clip;
            result = Request(priority, clips[0], 2f, outcome: NoStart, aircraftArguments: true);
            Check(result.Replace && !result.Replay && result.Priority == 0f && result.Clip == clips[0], "Actual aircraft arguments stay vanilla on priority rejection");
            Request(new MusicManager(), clips[0], 3f, outcome: NoStart, aircraftArguments: true); // Replay-history rejection.
            Request(new MusicManager { isFading = true }, clips[0], 4f, outcome: NoStart, aircraftArguments: true);
            Equal(previous, LastStarted(), "Replacement, priority, replay, and active-fade rejection leave history unchanged");
            Expect(clips[0]);
            Equal(1, Random.Calls, "Vanilla rejections do not consume or reshuffle");

            ModConfig.Enabled.Value = true;
            bool threw = false;
            try
            {
                Request(new MusicManager(), clips[0], 5f, outcome: (m, c) =>
                {
                    StartIncoming(m, c);
                    throw new InvalidOperationException("Simulated game exception after source start");
                });
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Check(threw && !TakeoffMusicContext.IsActive, "Game exceptions propagate; helper unwinds context without invoking postfix");
            Equal(previous, LastStarted(), "Exception does not commit");
            Expect(clips[0]);
        }

        public static void ModeHistoryAndQueueProgress(string directory)
        {
            InitConfig(directory);
            MusicPlaybackState.Initialize();
            ModConfig.CooldownSeconds.Value = 0f;
            var clips = Register("A", "B", "C");
            Expect(clips[0]);
            ModConfig.RandomizeTakeoffMusic.Value = false;
            Request(new MusicManager(), clips[0], 0f, outcome: NoStart, aircraftArguments: true);
            Check(LastStarted() == null, "Rejected assigned takeoff leaves history unchanged");
            Expect(clips[0]);
            Request(new MusicManager(), clips[0], 1f, outcome: StartIncoming, aircraftArguments: true);
            Equal(clips[0], LastStarted(), "Successful assigned takeoff updates history");
            Equal(3, RemainingCount(), "Assigned start does not consume its matching queue entry");
            ModConfig.RandomizeTakeoffMusic.Value = true;
            Expect(clips[1]);
            Request(new MusicManager(), clips[0], 2f, outcome: StartIncoming);
            Equal(clips[1], LastStarted(), "Resumed shuffle plays B before deferred A");
            Expect(clips[0]);

            ModConfig.Enabled.Value = false;
            Request(new MusicManager(), clips[0], 3f, outcome: StartIncoming, aircraftArguments: true);
            Equal(clips[0], LastStarted(), "Successful vanilla takeoff updates history");
            Equal(2, RemainingCount(), "Vanilla start leaves matching queued A available");
            Request(new MusicManager(), clips[2], 4f, outcome: NoStart, aircraftArguments: true);
            Equal(clips[0], LastStarted(), "Rejected vanilla start cannot influence resumed ordering");
            ModConfig.Enabled.Value = true;
            Expect(clips[2]);
            Request(new MusicManager(), clips[0], 5f, outcome: StartIncoming);
            Expect(clips[0]);

            ModConfig.RandomizeTakeoffMusic.Value = false;
            Request(new MusicManager(), clips[0], 6f, outcome: StartIncoming);
            ModConfig.RandomizeTakeoffMusic.Value = true;
            Expect(clips[0]); // Only remaining eligible turn: allow the repeat, don't import B or C.
            Request(new MusicManager(), clips[1], 7f, outcome: StartIncoming);
            Equal(0, RemainingCount(), "Deferred A eventually consumes its original turn");
            Equal(2, Random.Calls, "Settings toggles and manager replacements never reset this cycle");
        }

        public static void UncataloguedVanillaRecognition(string directory)
        {
            var config = InitConfig(directory);
            MusicPlaybackState.Initialize();
            ModConfig.Enabled.Value = false;
            var unknown = new AudioClip { name = "Unknown vanilla" };
            var manager = new MusicManager();
            var result = Request(manager, unknown, 1f, outcome: StartAndSwap, aircraftArguments: true);
            Check(result.Allowed && result.Clip == unknown && !result.Replay && result.Replace && result.Priority == 0f, "Vanilla arguments are untouched");
            Equal((2f, 0f, false, false, true, 0f), manager.LastRequest, "Outcome receives the complete vanilla aircraft arguments");
            Check(!TakeoffMusicPool.Contains(unknown) && config.Count == 3, "Vanilla observation performs no catalog registration or binding");
            Check(MusicPlaybackState.Refresh(manager, 2f), "Uncatalogued confirmed playback stays recognizable on later polls");
            ModConfig.Enabled.Value = true;
            var requested = Register("Requested")[0];
            Check(!Request(manager, requested, 3f).Allowed, "Re-enable protects uncatalogued vanilla takeoff while active");
            manager.currentSource.isPlaying = false;
            MusicPlaybackState.Refresh(manager, 4f);
            Check(!Request(manager, requested, 63f).Allowed, "Cooldown begins on unknown playback's observed end");
            Check(Request(manager, requested, 64f).Allowed, "End-based cooldown completes without discovering the vanilla clip");
            Check(!TakeoffMusicPool.Contains(unknown), "No request accidentally registers the unknown clip");

            // Recognition is attached to the observed source/clip pair and expires on reuse or destruction.
            ModConfig.Enabled.Value = false;
            var other = new AudioClip { name = "Another unknown" };
            var reused = new MusicManager();
            Request(reused, other, 65f, outcome: StartIncoming, aircraftArguments: true);
            reused.fadeSource.clip = new AudioClip { name = "Menu" };
            Check(!MusicPlaybackState.Refresh(reused, 66f), "Source reuse for unrelated music expires takeoff recognition");
            reused.fadeSource.clip = other;
            Check(!MusicPlaybackState.Refresh(reused, 67f), "Unobserved later event playback is not permanently classified as takeoff");
            Request(new MusicManager(), other, 68f, outcome: StartIncoming, aircraftArguments: true);
            var destroyedManager = MusicPlaybackState.Manager;
            destroyedManager.fadeSource.Destroyed = true;
            Check(!MusicPlaybackState.Refresh(destroyedManager, 69f), "Destroyed observed source is safely pruned");
        }

        public static void GuardsDoNotAdvanceQueue(string directory)
        {
            var config = InitConfig(directory);
            MusicPlaybackState.Initialize();
            var clips = Register("A", "B");
            Expect(clips[0]);
            var manager = new MusicManager();
            Request(manager, null, 1f, outcome: NoStart);
            GameManager.IsHeadless = true;
            Request(manager, clips[0], 2f, outcome: NoStart);
            GameManager.IsHeadless = false;
            Request(manager, clips[1], 3f, takeoff: false, outcome: StartIncoming);
            Check(LastStarted() == null, "Non-takeoff event start is outside takeoff history");
            manager.fadeSource.isPlaying = false;
            SongEntry(config, "B").Value = false;
            manager.currentSource.clip = clips[1];
            manager.currentSource.isPlaying = true;
            Check(!Request(manager, clips[0], 4f, outcome: StartIncoming).Allowed, "Disabled aircraft source still suppresses request before selection");
            manager.currentSource.isPlaying = false;
            Check(!Request(manager, clips[0], 5f, outcome: StartIncoming).Allowed, "Cooldown guard does not select");
            Expect(clips[0]);
            Equal(1, Random.Calls, "All guards preserve pending order and shuffle count");
            Check(LastStarted() == null, "Postfix on suppressed calls cannot commit");
        }

        private static AudioClip LastStarted()
        {
            return (AudioClip)typeof(TakeoffMusicPool).GetField("_lastStarted", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        }

        private static int RemainingCount()
        {
            return ((IList)typeof(TakeoffMusicPool).GetField("Remaining", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count;
        }

        private static void NoStart(MusicManager manager, AudioClip clip) { }

        private static void AssignOnly(MusicManager manager, AudioClip clip)
        {
            manager.fadeSource.clip = clip;
        }

        private static void StartIncoming(MusicManager manager, AudioClip clip)
        {
            manager.fadeSource.clip = clip;
            manager.fadeSource.isPlaying = true;
        }

        private static void StartAndSwap(MusicManager manager, AudioClip clip)
        {
            StartIncoming(manager, clip);
            Swap(manager);
            manager.isFading = true;
        }

        private static void Swap(MusicManager manager)
        {
            (manager.currentSource, manager.fadeSource) = (manager.fadeSource, manager.currentSource);
        }
    }
}
