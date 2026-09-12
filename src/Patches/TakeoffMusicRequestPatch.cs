using HarmonyLib;
using UnityEngine;

namespace MusicRandomizer.Patches
{
    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.CrossFadeMusic))]
    internal static class TakeoffMusicRequestPatch
    {
        private struct RequestState
        {
            public bool Observe;
            public AudioClip QueueCandidate;
            public MusicPlaybackState.StartSnapshot Snapshot;
        }

        private static bool Prepare()
        {
            bool hasMethod = AccessTools.Method(typeof(MusicManager), nameof(MusicManager.CrossFadeMusic)) != null;
            bool hasCurrentSource = AccessTools.Field(typeof(MusicManager), "currentSource") != null;
            bool hasFadeSource = AccessTools.Field(typeof(MusicManager), "fadeSource") != null;
            bool hasCurrentPriority = AccessTools.Field(typeof(MusicManager), "currentClipPriority") != null;
            bool hasIsFading = AccessTools.Field(typeof(MusicManager), "isFading") != null;

            if (!hasMethod || !hasCurrentSource || !hasFadeSource || !hasCurrentPriority || !hasIsFading)
            {
                Plugin.Log?.LogError(
                    $"Incompatible game build: MusicManager.CrossFadeMusic found={hasMethod}, "
                    + $"field 'currentSource' found={hasCurrentSource}, field 'fadeSource' found={hasFadeSource}, "
                    + $"field 'currentClipPriority' found={hasCurrentPriority}, field 'isFading' found={hasIsFading}. Patch skipped.");
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
            ref float priority,
            out RequestState __state)
        {
            __state = default;
            if (!TakeoffMusicContext.IsActive || audioClip == null || GameManager.IsHeadless || __instance == null)
            {
                return true;
            }

            if (!ModConfig.Enabled.Value)
            {
                __state.Observe = true;
                __state.Snapshot = MusicPlaybackState.CaptureStart(__instance);
                return true;
            }

            float now = Time.realtimeSinceStartup;
            TakeoffMusicPool.Discover(now);
            TakeoffMusicPool.Register(audioClip, now);
            MusicPlaybackState.Register(__instance);
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
                if (!TakeoffMusicPool.TrySelectQueued(out AudioClip selected))
                {
                    Plugin.Log?.LogInfo("Takeoff music skipped because no enabled, resolved songs are available.");
                    return false;
                }

                audioClip = selected;
                __state.QueueCandidate = selected;
            }
            else if (!TakeoffMusicPool.IsEnabled(audioClip))
            {
                Plugin.Log?.LogInfo("Takeoff music skipped because the assigned song is disabled or its setting is unresolved.");
                return false;
            }

            allowReplay = true;
            replacePlaying = true;
            if (MusicPlaybackState.IsAnyMusicActive(__instance))
            {
                priority = Mathf.Max(priority, MusicPlaybackState.GetCurrentPriority(__instance));
            }

            Plugin.Log?.LogInfo($"Requesting takeoff music '{ModConfig.GetSongLabel(audioClip.name)}'.");
            __state.Observe = true;
            __state.Snapshot = MusicPlaybackState.CaptureStart(__instance);
            return true;
        }

        private static void Postfix(MusicManager __instance, AudioClip audioClip, RequestState __state)
        {
            if (!__state.Observe || __instance == null || !__state.Snapshot.HasStarted(audioClip))
            {
                return;
            }

            TakeoffMusicPool.ConfirmStarted(audioClip, __state.QueueCandidate != null && __state.QueueCandidate == audioClip);
            MusicPlaybackState.ObserveStarted(__instance, __state.Snapshot, audioClip, Time.realtimeSinceStartup);
        }
    }
}
