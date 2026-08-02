using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer.Patches
{
    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.CrossFadeMusic))]
    internal static class TakeoffMusicRequestPatch
    {
        private static bool Prepare()
        {
            bool hasMethod = AccessTools.Method(typeof(MusicManager), nameof(MusicManager.CrossFadeMusic)) != null;
            bool hasCurrentSource = AccessTools.Field(typeof(MusicManager), "currentSource") != null;
            bool hasFadeSource = AccessTools.Field(typeof(MusicManager), "fadeSource") != null;
            bool hasCurrentPriority = AccessTools.Field(typeof(MusicManager), "currentClipPriority") != null;

            if (!hasMethod || !hasCurrentSource || !hasFadeSource || !hasCurrentPriority)
            {
                Plugin.Log?.LogError(
                    $"Incompatible game build: MusicManager.CrossFadeMusic found={hasMethod}, "
                    + $"field 'currentSource' found={hasCurrentSource}, field 'fadeSource' found={hasFadeSource}, "
                    + $"field 'currentClipPriority' found={hasCurrentPriority}. Patch skipped.");
                return false;
            }

            MusicPlaybackState.Initialize();
            return true;
        }

        private static bool Prefix(
            MusicManager __instance,
            ref AudioClip audioClip,
            ref bool allowReplay,
            ref bool replacePlaying,
            ref float priority)
        {
            if (!TakeoffMusicContext.IsActive || !ModConfig.Enabled.Value || audioClip == null)
            {
                return true;
            }

            MusicPlaybackState.Register(__instance);
            float now = Time.realtimeSinceStartup;
            if (MusicPlaybackState.Refresh(__instance, now))
            {
                Plugin.Log?.LogInfo("Takeoff music skipped because an aircraft song is already playing.");
                return false;
            }

            if (!MusicPlaybackState.IsCooldownComplete(now, ModConfig.CooldownSeconds.Value))
            {
                Plugin.Log?.LogInfo("Takeoff music skipped because the cooldown is still active.");
                return false;
            }

            if (ModConfig.RandomizeTakeoffMusic.Value)
            {
                audioClip = TakeoffMusicPool.PickRandom(audioClip);
            }

            TakeoffMusicPool.Register(audioClip);
            allowReplay = true;
            replacePlaying = true;
            if (MusicPlaybackState.IsAnyMusicActive(__instance))
            {
                priority = Mathf.Max(priority, MusicPlaybackState.GetCurrentPriority(__instance));
            }

            Plugin.Log?.LogInfo($"Playing takeoff music '{audioClip.name}'.");
            return true;
        }
    }
}
