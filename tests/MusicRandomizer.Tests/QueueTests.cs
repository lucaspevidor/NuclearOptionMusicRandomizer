using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using static MusicRandomizer.Tests.Program;

namespace MusicRandomizer.Tests
{
    internal static class QueueTests
    {
        public static void StableCycles(string directory)
        {
            InitConfig(directory);
            var clips = Register("A", "B", "C");
            Expect(clips[0]);
            Expect(clips[0]);
            Equal(2, Random.Calls, "Pending candidate neither consumes nor reshuffles");
            Start(clips[0]);
            Start(clips[1]);
            Start(clips[2]);
            Equal(2, Random.Calls, "One shuffle for a complete three-song cycle");

            // Fisher-Yates choices produce C-B-A; boundary correction must defer C, not discard it.
            Random.Choices.Enqueue(0);
            Random.Choices.Enqueue(1);
            Start(clips[1]);
            Start(clips[2]);
            Start(clips[0]);
            Equal(4, Random.Calls, "Second cycle contains each object once with only one new shuffle");
        }

        public static void DisabledHeadsExample(string directory)
        {
            var config = InitConfig(directory);
            var clips = Register("4", "2", "1", "5", "3");
            SongEntry(config, "4").Value = false;
            Start(clips[1]);
            Start(clips[2]);
            Start(clips[3]);
            Start(clips[4]);
            Equal(4, Random.Calls, "User example completes a single shuffled cycle");

            // Next order: 4-3-1-5-2. The first eligible entry, 3, must be deferred behind 1.
            foreach (int choice in new[] { 1, 3, 2, 1 }) Random.Choices.Enqueue(choice);
            Start(clips[2]);
            Start(clips[4]);
            Start(clips[3]);
            Start(clips[1]);
            Equal(8, Random.Calls, "Disabled heads do not cause extra shuffles");
        }

        public static void LiveTurnsAndDeferral(string directory)
        {
            var config = InitConfig(directory);
            var clips = Register("A", "B", "C", "D");
            SongEntry(config, "A").Value = false;
            SongEntry(config, "C").Value = false;
            Start(clips[1]); // A's turn is skipped.
            SongEntry(config, "A").Value = true;
            SongEntry(config, "C").Value = true;
            Start(clips[2]); // Enabled before its turn.
            Start(clips[3]);
            Equal(3, Random.Calls, "Enabling skipped A cannot bring it into the unfinished cycle");
            Expect(clips[0]); // Next cycle.
            SongEntry(config, "A").Value = false;
            Start(clips[1]); // Pending A became ineligible.

            TakeoffMusicPool.ConfirmStarted(clips[2], false);
            SongEntry(config, "D").Value = false;
            Start(clips[2]); // Only remaining eligible entry matches history: allowed.
            SongEntry(config, "A").Value = true;
            SongEntry(config, "D").Value = true;
            Start(clips[3]);
            Equal(6, Random.Calls, "Mode history cannot import consumed songs just to avoid repeats");
        }

        public static void LookaheadPreservesTurns(string directory)
        {
            var config = InitConfig(directory);
            var clips = Register("A", "B", "C");
            SongEntry(config, "B").Value = false;
            TakeoffMusicPool.ConfirmStarted(clips[0], false);
            Start(clips[2]); // A is swapped with C; looking past B must not skip B's turn.
            SongEntry(config, "B").Value = true;
            Start(clips[1]);
            Start(clips[0]);
            Equal(2, Random.Calls, "Deferred entry and newly enabled intervening turn both survive");
        }

        public static void BoundedEmptyCycles(string directory)
        {
            var config = InitConfig(directory);
            Check(!TakeoffMusicPool.TrySelectQueued(out _), "Empty catalog terminates");
            var clips = Register("A", "B", "C");
            foreach (var clip in clips) SongEntry(config, clip.name).Value = false;
            Check(!TakeoffMusicPool.TrySelectQueued(out _), "Cold all-disabled cycle terminates");
            Equal(2, Random.Calls, "Cold all-off selection builds at most one cycle");
            SongEntry(config, "A").Value = true;
            Expect(clips[0]);
            SongEntry(config, "A").Value = false;
            int before = Random.Calls;
            Check(!TakeoffMusicPool.TrySelectQueued(out _), "Exhaust old ineligible cycle and terminate after one fresh cycle");
            Equal(before + 2, Random.Calls, "Old-cycle exhaustion does not permit two fresh shuffles");
            SongEntry(config, "B").Value = true;
            Start(clips[1]);
            Start(clips[1]);
            Start(clips[1]);
        }

        public static void UnresolvedCycle(string directory)
        {
            var config = InitConfig(directory);
            using (var locked = new FileStream(config.ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var clips = Register("A", "B", "C");
                Check(!TakeoffMusicPool.TrySelectQueued(out _), "All-unresolved cycle terminates");
                Equal(2, Random.Calls, "Unresolved objects are included in exactly one shuffle");
                foreach (var clip in clips) Check(TakeoffMusicPool.Contains(clip), "Unresolved catalog membership survives");
            }
        }

        public static void QueueLifecycle(string directory)
        {
            var config = InitConfig(directory);
            var clips = Register("A", "B", "C");
            Start(clips[0]);
            var late = Register("Late")[0];
            Expect(clips[1]);
            clips[1].Destroyed = true;
            Start(clips[2]); // Destruction is safe before discovery prunes.
            clips[2].Destroyed = true; // Destroy history too.
            TakeoffMusicPool.Discover(1f);
            var remaining = (IList)typeof(TakeoffMusicPool).GetField("Remaining", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Equal(0, remaining.Count, "Destroyed queue references are pruned");
            Check(ReferenceEquals(null, typeof(TakeoffMusicPool).GetField("_lastStarted", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)), "Destroyed history reference is cleared");
            Start(clips[0]);
            Start(late);

            Expect(clips[0]);
            SongEntry(config, "B").Value = false;
            var recreated = Register("B")[0];
            Check(!TakeoffMusicPool.IsEnabled(recreated), "Recreated object retains its saved name-group exclusion");
            SongEntry(config, "B").Value = true;
            TakeoffMusicPool.ConfirmStarted(clips[0], true);
            Start(late);
            Start(clips[0]); // Recreated B joins this next cycle, not the preceding A-Late cycle.
            Start(recreated);
            Start(late);

            // Prune a destroyed pending entry during discovery while preserving the next live turn.
            Expect(clips[0]);
            clips[0].Destroyed = true;
            TakeoffMusicPool.Discover(2f);
            Check(!remaining.Contains(clips[0]), "Discovery removes pending destroyed references");
            Start(recreated);
        }

        internal static AudioClip[] Register(params string[] names)
        {
            var clips = new AudioClip[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                clips[index] = new AudioClip { name = names[index] };
                TakeoffMusicPool.Register(clips[index], 0f);
            }
            return clips;
        }

        internal static void Expect(AudioClip expected)
        {
            Check(TakeoffMusicPool.TrySelectQueued(out var clip) && clip == expected, "Expected pending queue clip " + expected.name);
        }

        internal static void Start(AudioClip expected)
        {
            Expect(expected);
            TakeoffMusicPool.ConfirmStarted(expected, true);
        }
    }
}
