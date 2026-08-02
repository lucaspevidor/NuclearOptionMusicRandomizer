using HarmonyLib;

namespace MusicRandomizer.Patches
{
    [HarmonyPatch(typeof(MusicManager), "Awake")]
    internal static class MusicManagerAwakePatch
    {
        private static bool Prepare()
        {
            bool compatible = AccessTools.Method(typeof(MusicManager), "Awake") != null;
            if (!compatible)
            {
                Plugin.Log?.LogError("Incompatible game build: MusicManager.Awake was not found. Patch skipped.");
            }

            return compatible;
        }

        private static void Postfix(MusicManager __instance)
        {
            MusicPlaybackState.Register(__instance);
        }
    }
}
