using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MusicRandomizer.Patches;
using UnityEngine;

namespace MusicRandomizer
{
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }

        private readonly Harmony _harmony = new Harmony(PluginInfo.Guid);

        private void Awake()
        {
            Log = Logger;
            ModConfig.Init(Config);
            _harmony.PatchAll(typeof(MusicManagerAwakePatch));
            _harmony.PatchAll(typeof(TakeoffMusicContextPatch));
            _harmony.PatchAll(typeof(TakeoffMusicRequestPatch));

            int patched = _harmony.GetPatchedMethods().Count();
            if (patched == 3)
            {
                Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded. Takeoff music patch active.");
            }
            else
            {
                Log.LogWarning(
                    $"{PluginInfo.Name} v{PluginInfo.Version} loaded, but only {patched} of 3 methods "
                    + "were patched. See errors above.");
            }
        }

        private void Update()
        {
            if (GameManager.IsHeadless)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            TakeoffMusicPool.Discover(now);
            if (!MusicPlaybackState.IsInitialized)
            {
                return;
            }

            MusicManager manager = MusicPlaybackState.Manager;
            if (manager != null)
            {
                MusicPlaybackState.Refresh(manager, now);
            }
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }
    }
}
