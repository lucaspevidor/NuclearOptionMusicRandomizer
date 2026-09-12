using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

// Small managed API doubles, not a Unity emulator. The real plugin is compiled separately against game assemblies.
namespace UnityEngine
{
    internal class Object
    {
        private string _name;
        public bool Destroyed;

        public string name
        {
            get { RequireAlive(); return _name; }
            set { RequireAlive(); _name = value; }
        }

        protected void RequireAlive()
        {
            if (Destroyed) throw new InvalidOperationException("Accessed a destroyed test object");
        }

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null);
            bool rightNull = ReferenceEquals(right, null);
            if (leftNull) return rightNull || right.Destroyed;
            if (rightNull) return left.Destroyed;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    internal sealed class AudioClip : Object { }

    internal sealed class AudioSource : Object
    {
        private AudioClip _clip;
        private bool _isPlaying;
        public AudioClip clip
        {
            get { RequireAlive(); return _clip; }
            set { RequireAlive(); _clip = value; }
        }
        public bool isPlaying
        {
            get { RequireAlive(); return _isPlaying; }
            set { RequireAlive(); _isPlaying = value; }
        }
    }

    internal static class Time
    {
        public static float realtimeSinceStartup;
    }

    internal static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
    }

    internal static class Random
    {
        public static readonly Queue<int> Choices = new Queue<int>();
        public static int Calls;
        public static int LastMaximum;

        public static int Range(int minimum, int maximum)
        {
            Calls++;
            LastMaximum = maximum;
            int index = Choices.Count == 0 ? maximum - 1 : Choices.Dequeue();
            if (index < minimum || index >= maximum) throw new InvalidOperationException("Invalid test random index");
            return index;
        }
    }
}

internal sealed class AircraftParameters : UnityEngine.Object
{
    private UnityEngine.AudioClip _takeoffMusic;
    public UnityEngine.AudioClip takeoffMusic
    {
        get { RequireAlive(); return _takeoffMusic; }
        set { RequireAlive(); _takeoffMusic = value; }
    }
}

internal sealed class AircraftDefinition : UnityEngine.Object
{
    private AircraftParameters _parameters;
    public string unitName;
    public AircraftParameters aircraftParameters
    {
        get { RequireAlive(); return _parameters; }
        set { RequireAlive(); _parameters = value; }
    }
}

internal sealed class Encyclopedia : UnityEngine.Object
{
    public static Encyclopedia Instance;
    public static int Reads;
    public static Encyclopedia i { get { Reads++; return Instance; } }
    public List<AircraftDefinition> aircraft;
}

internal static class MainMenu
{
    public enum LoadingState { None, Loaded }
    public static LoadingState State;
}

internal static class GameManager
{
    public static bool IsHeadless;
}

internal sealed class MusicManager : UnityEngine.Object
{
    public UnityEngine.AudioSource currentSource = new UnityEngine.AudioSource();
    public UnityEngine.AudioSource fadeSource = new UnityEngine.AudioSource();
    public float currentClipPriority;
    public bool isFading;
    public Action<MusicManager, UnityEngine.AudioClip> Outcome;
    public (float FadeOut, float FadeIn, bool Repeat, bool Replay, bool Replace, float Priority) LastRequest;

    public void CrossFadeMusic(UnityEngine.AudioClip clip, float fadeOutTime, float fadeInTime,
        bool repeat, bool allowReplay, bool replacePlaying, float priority)
    {
        LastRequest = (fadeOutTime, fadeInTime, repeat, allowReplay, replacePlaying, priority);
        Outcome?.Invoke(this, clip);
    }
}

namespace MusicRandomizer
{
    internal static class Plugin
    {
        public static readonly ManualLogSource Log = new ManualLogSource("MusicRandomizer.Tests");
        public static readonly List<string> Warnings = new List<string>();

        static Plugin()
        {
            Log.LogEvent += (_, args) =>
            {
                if (args.Level == LogLevel.Warning) Warnings.Add(args.Data.ToString());
            };
        }
    }
}
