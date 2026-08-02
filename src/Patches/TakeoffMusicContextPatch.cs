using System;
using HarmonyLib;

namespace MusicRandomizer.Patches
{
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.CheckRadarAlt))]
    internal static class TakeoffMusicContextPatch
    {
        private static bool Prepare()
        {
            bool compatible = AccessTools.Method(typeof(Aircraft), nameof(Aircraft.CheckRadarAlt)) != null;
            if (!compatible)
            {
                Plugin.Log?.LogError("Incompatible game build: Aircraft.CheckRadarAlt was not found. Patch skipped.");
            }

            return compatible;
        }

        private static void Prefix()
        {
            TakeoffMusicContext.Enter();
        }

        private static void Postfix()
        {
            TakeoffMusicContext.Exit();
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                TakeoffMusicContext.Exit();
            }

            return __exception;
        }
    }
}
