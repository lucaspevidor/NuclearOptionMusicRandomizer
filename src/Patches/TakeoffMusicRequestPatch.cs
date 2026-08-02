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
            bool hasFadeSource = AccessTools.Field(typeof(MusicManager), "fadeSource") != null;
            bool hasIsFading = AccessTools.Field(typeof(MusicManager), "isFading") != null;

            if (!hasMethod || !hasFadeSource || !hasIsFading)
            {
                Plugin.Log?.LogError(
                    $"Incompatible game build: MusicManager.CrossFadeMusic found={hasMethod}, "
                    + $"field 'fadeSource' found={hasFadeSource}, field 'isFading' found={hasIsFading}. Patch skipped.");
                return false;
            }

            MusicPlaybackState.Initialize();
            return true;
        }

        private static bool Prefix(
            MusicManager __instance,
            ref AudioClip audioClip,
            ref bool allowReplay,
            ref bool replacePlaying)
        {
            if (!TakeoffMusicContext.IsActive || !ModConfig.Enabled.Value || audioClip == null)
            {
                return true;
            }

            MusicPlaybackState.Register(__instance);
            float now = Time.realtimeSinceStartup;
            if (MusicPlaybackState.Refresh(__instance, now))
            {
                Plugin.Log?.LogInfo("Takeoff music skipped because music is already playing.");
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

            allowReplay = true;
            replacePlaying = false;
            Plugin.Log?.LogInfo($"Playing takeoff music '{audioClip.name}'.");
            return true;
        }
    }
}
