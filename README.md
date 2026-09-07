# MusicRandomizer

A BepInEx 5 mod for Nuclear Option that allows aircraft takeoff music to play more than once per
match. An aircraft song will not interrupt another aircraft song, and a configurable cooldown
starts when an aircraft song ends.

By Lucas Pevidor.

## Behavior

The base game assigns one track to each aircraft and records that track as played after its first
takeoff. This mod changes takeoff requests so they can replay. By default, it also chooses a random
track from the enabled aircraft takeoff songs discovered in the game's encyclopedia or received
in takeoff requests. Each unique clip has equal selection weight, even if several aircraft share it.

The mod only changes music requested by `Aircraft.CheckRadarAlt`. A takeoff song can replace
tactical, strategic, mission-result, death, kill-song, or menu music that is currently playing.
Those events still use their normal game behavior when they start.

A takeoff request is skipped when:

- Another aircraft song is playing or crossfading.
- Less than the configured cooldown has passed since an aircraft song stopped.
- The aircraft has no takeoff track.
- Randomization is enabled but no valid, enabled songs are available.
- Randomization is disabled and the aircraft's assigned song is unchecked or its setting could not be resolved.

The cooldown uses Unity's real time, so time spent in a pause menu counts toward it.
An empty selection leaves existing music alone: there is no fallback to disabled songs, forced
silence, or substitution in assigned-song mode. A single enabled song can replay on every
otherwise-eligible takeoff.

## Configuration

The config file is `BepInEx/config/com.lucaspevidor.musicrandomizer.cfg`.

| Section / Key | Default | Effect |
|---------------|---------|--------|
| `General / Enabled` | `true` | Enable repeatable takeoff music. Disabling it restores the game's original takeoff behavior. |
| `General / RandomizeTakeoffMusic` | `true` | Pick from enabled, unique aircraft takeoff clips. When false, use the current aircraft's track only if its song is enabled. |
| `Timing / CooldownSeconds` | `60` | Minimum time after an aircraft song ends before another one can start. Range: 0 to 600 seconds. |
| `Takeoff Songs / Song_<encoded name>` | `true` | Allow this exact song-name group on future takeoffs. Configuration Manager shows a readable song name and initially known aircraft names. |
| `Takeoff Songs / Song_Unnamed` | `true` | Allow the shared `Unnamed takeoff songs` group, if any unnamed clips are discovered. |

Config values are read live. They can be changed through BepInEx Configuration Manager without
restarting the game. Unchecking the playing or fading song does not stop it, allow another aircraft
song to interrupt it, or reset cooldown. Changes affect the next eligible takeoff; a skipped
takeoff is not queued or retried. Global `Enabled = false` restores vanilla takeoff behavior and
deliberately ignores all song checkboxes. Non-takeoff music requests are never filtered.

### Song Discovery

Song settings are discovered on the main thread approximately once per second after the main menu
reports `Loaded`, even with the mod or randomization disabled and before any flight. Missing data
is retried with backoff up to 30 seconds. The request path shares that scan deadline and also
registers its original clip independently, so partial catalogs never cause an unfiltered fallback.

If Configuration Manager was already open before discovery, close and reopen its window to see
new checkboxes. Existing checkbox values take effect live. Newly introduced songs default to
enabled unless a matching saved setting exists, even when every previously known song is off.
Configuration Manager is optional; with the game closed, the same settings can be edited in the
config file for the next launch.

### Identity And Persistence

Settings use the full, case-sensitive `AudioClip.name`, not an aircraft ID or a guessed song title.
Keys start with `Song_` followed by four uppercase hexadecimal digits for each UTF-16 code unit
(for example, `A` becomes `Song_0041`). This preserves punctuation, whitespace, Unicode, and even
malformed surrogate sequences without key collisions. Null and empty names share the reserved,
non-hex `Song_Unnamed` key; a clip literally named `Unnamed` has a different encoded key.

Readable descriptions are also saved as config comments, so encoded keys remain usable without
the UI. Labels include aircraft names known at first binding; this discovery-time information may
be incomplete, especially for request-only or late content. Reopening the UI reveals new entries
but does not enrich existing labels. Control characters in labels are escaped.

Distinct clip objects with the same exact name share one checkbox and generate one ambiguity
warning per name. Unnamed clips likewise share one explicitly labeled group. These clips may be
different recordings: grouped controls are a conservative fallback for ambiguous content, not
per-recording identification. Shared clip references never gain extra random-selection weight.
Recreated clips reuse the same setting, missing content keeps its saved values, and a renamed
asset is treated as a new song. No hardcoded stock-song list or game assets are modified.

Recoverable dynamic binding/save errors produce at most one mod warning and one failed retry
attempt per 30 seconds across unresolved songs. Already-resolved settings remain usable, and
unresolved clips stay excluded but recognizable for playback protection. Binding recovery does
not reload the config, overwrite saved exclusions with defaults, or interrupt playback observation.

## Building

Requires the .NET SDK and a local Nuclear Option install.
The plugin targets `netstandard2.1`; the managed checks below require the .NET 10 SDK/runtime.

**Build safety:** the project's post-build target normally copies the DLL/PDB into
`<GameDir>/BepInEx/plugins/MusicRandomizer/`. Do not run an ordinary build as a read-only check.
Redirect deployment on every build-capable verification command, including any future test project
that references the plugin. The following commands do not install the mod or launch the game:

```powershell
$verificationRoot = Join-Path $env:TEMP "opencode"
if (!(Test-Path -LiteralPath $verificationRoot)) { throw "Verify/create an approved temporary parent first." }
$deployment = Join-Path $verificationRoot "MusicRandomizer-song-selector"
dotnet build -c Release -p:PluginDeploymentDirectory="$deployment"
dotnet run --project "tests\MusicRandomizer.Tests" -c Release -p:PluginDeploymentDirectory="$deployment" -- "$verificationRoot"
dotnet build -c Release -p:PluginDeploymentDirectory="$deployment"
git diff --check
```

The second root build checks that generated test sources under `tests/**/obj/` remain excluded from
the plugin. Normal ignored `bin/` and `obj/` artifacts stay in the repository. Add the game-directory
override to a safely redirected build if your install differs:

```powershell
dotnet build -c Release -p:GameDirectory="D:\SteamLibrary\steamapps\common\Nuclear Option" -p:PluginDeploymentDirectory="$deployment"
```

The small executable test harness exits nonzero on failure and creates/deletes its own config
fixtures under the supplied temporary parent, never using installed game configs. It source-links
the actual config, pool, request-prefix, context, and playback-state code. It uses real BepInEx
5 configuration/persistence and Harmony field-reference access, with managed Unity/game API doubles
and deterministic random indices. Each check runs in a fresh process to isolate static state.
The test-only BepInEx package build target is excluded so dependencies are copied to the test host;
the older MonoMod dependency uses its Cecil emitter there for .NET 10 compatibility.

Managed coverage includes exact keys, persisted labels/exclusions, discovery throttling, grouping,
zero/one/multiple candidate sets, both selection modes, original registration, file-lock failures
before/after binding, bounded recovery, membership cleanup, and playback/cooldown policy. These
checks do **not** execute native Unity, apply Harmony detours, instantiate the real plugin lifecycle,
validate Configuration Manager rendering, or prove audio playback/async readiness.

## Runtime Checks

The song selector has been tested successfully in game. The checklist below covers additional
regression and compatibility scenarios; not every scenario has been verified. Serialized stock
clips/assignments and actual asynchronous readiness cannot be inferred from the C# decompile.
When validating game or content updates, check exact clip names, shared references, aircraft labels,
and duplicate/empty-name cases. If genuinely different stock songs share a name or are unnamed,
the grouping policy must be revised with a verified stable mapping before claiming independent
controls for each stock song.

Use `BepInEx/LogOutput.log` together with actual listening/source observation for these checks.
`Requesting takeoff music` means a request was submitted, not that the game's crossfade guards
accepted it.

1. At the main menu before any takeoff, confirm that discovery yields readable settings without unloaded-singleton log spam, including with global enable or randomization off. Reopening Configuration Manager should reveal late entries.
2. Restart with exclusions saved. Existing static values and song exclusions should survive regardless of discovery order; newly introduced songs should default enabled.
3. Exclude one song, then all songs, then enable exactly one in random mode. Only enabled clips should be requested; all-off should leave unrelated music alone; a single song should replay after landing/rearming when otherwise eligible.
4. Disable randomization. An unchecked assigned track should skip without substitution; enabling it should restore that track on the next eligible takeoff.
5. Uncheck the current or fading aircraft song. It should finish normally and remain protected against interruption. Cooldown should start on its actual end, include pause-menu time, and not reset or bypass when checkboxes change.
6. Take off during tactical/strategic/other non-aircraft music. An enabled track should retain replacement/priority behavior; all-off should not replace it. Non-takeoff requests, including coincidentally matching clips, should remain vanilla.
7. Disable the mod globally and confirm vanilla once-per-track behavior and song selection return.
8. Validate late/recreated/shared/ambiguous content, empty/null data, destroyed objects, and scene transitions without stale selection, permanently empty catalogs, repeated ambiguity warnings, or loss of playback protection.
9. Check operation without Configuration Manager, on a multiplayer client, and on a headless instance. No required UI dependency, network/gameplay mutation, or headless discovery work should be introduced.

## Compatibility

- Nuclear Option on Unity 2022.3.x with the Mono backend.
- BepInEx 5.
- Client-side only. The mod does not change networked gameplay state.
- Aircraft content mods are included when their definitions appear in `Encyclopedia.i.aircraft` or their clips reach the existing local takeoff request path.
- If a game update changes the patched methods or private music fields, the affected patch is
  skipped and an error is written to the BepInEx log.
