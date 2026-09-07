using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;

namespace MusicRandomizer.Tests
{
    internal static class Program
    {
        private static readonly (string Name, Action<string> Run)[] Cases =
        {
            (nameof(ConfigTests.ExactKeys), ConfigTests.ExactKeys),
            (nameof(ConfigTests.PersistenceAndMetadata), ConfigTests.PersistenceAndMetadata),
            (nameof(ConfigTests.BindFailureBeforeRegistration), ConfigTests.BindFailureBeforeRegistration),
            (nameof(ConfigTests.BindFailureAfterRegistration), ConfigTests.BindFailureAfterRegistration),
            (nameof(SelectorTests.CandidateBoundariesAndGroups), SelectorTests.CandidateBoundariesAndGroups),
            (nameof(SelectorTests.DiscoveryAndBackoff), SelectorTests.DiscoveryAndBackoff),
            (nameof(SelectorTests.DestroyedAndRecreatedClips), SelectorTests.DestroyedAndRecreatedClips),
            (nameof(SelectorTests.RequestModesAndGuards), SelectorTests.RequestModesAndGuards),
            (nameof(SelectorTests.PlaybackProtectionAndCooldown), SelectorTests.PlaybackProtectionAndCooldown),
            (nameof(SelectorTests.OriginalRegisteredBeforeObservation), SelectorTests.OriginalRegisteredBeforeObservation)
        };

        private static int Main(string[] args)
        {
            if (args.Length == 2)
            {
                try
                {
                    // Old MonoMod's DynamicMethod emitter targets Unity Mono, not this .NET 10 host.
                    Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE", "cecil");
                    // BepInEx's core config also stays inside this test's private fixture directory.
                    typeof(Paths).GetProperty(nameof(Paths.BepInExConfigPath))
                        .SetValue(null, Path.Combine(args[1], "BepInEx.cfg"));
                    Cases.Single(test => test.Name == args[0]).Run(args[1]);
                    Console.WriteLine("PASS " + args[0]);
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("FAIL " + args[0] + ": " + exception);
                    return 1;
                }
            }

            if (args.Length != 1 || !Directory.Exists(args[0]))
            {
                Console.Error.WriteLine("Supply an existing, approved temporary parent directory as the only argument.");
                return 2;
            }

            string root = Path.Combine(args[0], "MusicRandomizer-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            int failures = 0;
            try
            {
                // Fresh processes isolate production static state without adding reset hooks to the mod.
                foreach (var test in Cases)
                {
                    string directory = Path.Combine(root, test.Name);
                    Directory.CreateDirectory(directory);
                    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
                    start.ArgumentList.Add(typeof(Program).Assembly.Location);
                    start.ArgumentList.Add(test.Name);
                    start.ArgumentList.Add(directory);
                    using var process = Process.Start(start);
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        failures++;
                    }
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }

            Console.WriteLine($"{Cases.Length - failures}/{Cases.Length} managed checks passed. Unity/game APIs are test doubles; BepInEx config is real.");
            return failures == 0 ? 0 : 1;
        }

        internal static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected '{expected}', got '{actual}'");
        }

        internal static ConfigFile InitConfig(string directory)
        {
            var config = new ConfigFile(Path.Combine(directory, "selector.cfg"), false);
            ModConfig.Init(config);
            return config;
        }

        internal static ConfigEntry<bool> SongEntry(ConfigFile config, string name)
        {
            Check(config.TryGetEntry("Takeoff Songs", ModConfig.GetSongKey(name), out ConfigEntry<bool> entry), "Song setting must exist");
            return entry;
        }
    }
}
