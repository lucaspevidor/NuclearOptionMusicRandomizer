using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer.Patches
{
    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.CrossFadeMusic))]
    internal static class TakeoffMusicRequestPatch
    {
        private struct RequestState
        {
            public AudioClip Clip;
            public bool Repeat;
            public float FadeInTime;
        }

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
            float fadeInTime,
            bool repeat,
            ref bool allowReplay,
            ref bool replacePlaying,
            out RequestState __state)
        {
            __state = default;
            float now = Time.realtimeSinceStartup;
            if (!TakeoffMusicContext.IsActive)
            {
                MusicPlaybackState.Register(__instance);
                __state = new RequestState
                {
                    Clip = audioClip,
                    Repeat = repeat,
                    FadeInTime = fadeInTime
                };
                return true;
            }

            if (!ModConfig.Enabled.Value || audioClip == null)
            {
                return true;
            }

            MusicPlaybackState.Register(__instance);
            if (MusicPlaybackState.Refresh(__instance, now) || MusicPlaybackState.IsReserved(now))
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
            MusicPlaybackState.Reserve(audioClip, repeat, now, fadeInTime);
            Plugin.Log?.LogInfo($"Playing takeoff music '{audioClip.name}'.");
            return true;
        }

        private static void Postfix(MusicManager __instance, RequestState __state)
        {
            if (__state.Clip != null && MusicPlaybackState.DidCrossFadeStart(__instance, __state.Clip))
            {
                MusicPlaybackState.Reserve(
                    __state.Clip,
                    __state.Repeat,
                    Time.realtimeSinceStartup,
                    __state.FadeInTime);
            }
        }
    }
}
