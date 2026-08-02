using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer.Patches
{
    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.StopMusic))]
    internal static class MusicStopPatch
    {
        private static bool Prepare()
        {
            bool compatible = AccessTools.Method(typeof(MusicManager), nameof(MusicManager.StopMusic)) != null;
            if (!compatible)
            {
                Plugin.Log?.LogError("Incompatible game build: MusicManager.StopMusic was not found. Patch skipped.");
            }

            return compatible;
        }

        private static void Postfix()
        {
            MusicPlaybackState.MarkEnded(Time.realtimeSinceStartup);
        }
    }
}
