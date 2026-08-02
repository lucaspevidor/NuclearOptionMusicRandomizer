using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

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
            _harmony.PatchAll();

            int patched = _harmony.GetPatchedMethods().Count();
            Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded. Applied {patched} patch target(s).");
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }
    }
}
