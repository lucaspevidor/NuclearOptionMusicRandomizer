using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer.Patches
{
    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.PlayMusic))]
    internal static class MusicPlaybackPatch
    {
        private static bool Prepare()
        {
            bool compatible = AccessTools.Method(typeof(MusicManager), nameof(MusicManager.PlayMusic)) != null;
            if (!compatible)
            {
                Plugin.Log?.LogError("Incompatible game build: MusicManager.PlayMusic was not found. Patch skipped.");
            }

            return compatible;
        }

        private static void Postfix(MusicManager __instance, AudioClip audioClip, bool repeat)
        {
            MusicPlaybackState.Register(__instance);
            if (__instance.IsPlaying())
            {
                MusicPlaybackState.Reserve(audioClip, repeat, Time.realtimeSinceStartup);
            }
        }
    }
}
