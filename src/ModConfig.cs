using BepInEx.Configuration;

namespace MusicRandomizer
{
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled { get; private set; }

        public static ConfigEntry<bool> RandomizeTakeoffMusic { get; private set; }

        public static ConfigEntry<float> CooldownSeconds { get; private set; }

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Replay aircraft music on eligible takeoffs.");

            RandomizeTakeoffMusic = config.Bind(
                "General",
                "RandomizeTakeoffMusic",
                true,
                "Choose each takeoff track from all aircraft music. Disable this to use the current aircraft's track.");

            CooldownSeconds = config.Bind(
                "Timing",
                "CooldownSeconds",
                60f,
                new ConfigDescription(
                    "Minimum time after any music ends before takeoff music may start.",
                    new AcceptableValueRange<float>(0f, 600f)));
        }
    }
}
