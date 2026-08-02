# MusicRandomizer

A BepInEx 5 mod for Nuclear Option that allows aircraft takeoff music to play more than once per
match. Takeoff music waits while another track is playing and observes a configurable cooldown
after that track ends.

By Lucas Pevidor.

## Behavior

The base game assigns one track to each aircraft and records that track as played after its first
takeoff. This mod changes takeoff requests so they can replay. By default, it also chooses a random
track from the aircraft in the game's encyclopedia.

The mod only changes music requested by `Aircraft.CheckRadarAlt`. Tactical and strategic phase
music, mission results, death music, kill songs, and menu music keep their normal behavior.

A takeoff request is skipped when:

- Any music source is playing or crossfading.
- Less than the configured cooldown has passed since music stopped.
- The aircraft has no takeoff track.

The cooldown uses Unity's real time, so time spent in a pause menu counts toward it.
The mod tracks both the music sources and the requested clip duration. The duration acts as a
fallback while the game's asynchronous crossfade code moves a track between audio sources.

## Configuration

The config file is `BepInEx/config/com.lucaspevidor.musicrandomizer.cfg`.

| Section / Key | Default | Effect |
|---------------|---------|--------|
| `General / Enabled` | `true` | Enable repeatable takeoff music. Disabling it restores the game's original takeoff behavior. |
| `General / RandomizeTakeoffMusic` | `true` | Pick from all unique aircraft takeoff tracks. When false, use the current aircraft's track. |
| `Timing / CooldownSeconds` | `60` | Minimum time after any music ends before takeoff music can start. Range: 0 to 600 seconds. |

Config values are read live. They can be changed through BepInEx Configuration Manager without
restarting the game.

## Building

Requires the .NET SDK and a local Nuclear Option install.

```powershell
dotnet build -c Release
```

To use a different game location:

```powershell
dotnet build -c Release -p:GameDirectory="D:\SteamLibrary\steamapps\common\Nuclear Option"
```

The build copies `MusicRandomizer.dll` to
`<GameDir>/BepInEx/plugins/MusicRandomizer/`.

## Runtime Checks

Use `BepInEx/LogOutput.log` to confirm these cases in game:

1. Take off with no music playing. A takeoff track should start.
2. Land, wait for the track to end and take off before the cooldown expires. No track should start.
3. Take off after the cooldown. Another track should start.
4. Take off while tactical or strategic music is playing. That track should continue uninterrupted.
5. Disable randomization and confirm the aircraft's assigned track is used.
6. Disable the mod and confirm the game's original once-per-track behavior returns.

## Compatibility

- Nuclear Option on Unity 2022.3.x with the Mono backend.
- BepInEx 5.
- Client-side only. The mod does not change networked gameplay state.
- Aircraft content mods are included when their definitions appear in `Encyclopedia.i.aircraft`.
- If a game update changes the patched methods or private music fields, the affected patch is
  skipped and an error is written to the BepInEx log.
