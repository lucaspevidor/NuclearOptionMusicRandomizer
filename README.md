# MusicRandomizer

A BepInEx 5 mod for Nuclear Option that allows aircraft takeoff music to play more than once per
match. An aircraft song will not interrupt another aircraft song, and a configurable cooldown
starts when an aircraft song ends.

By Lucas Pevidor.

## Behavior

The base game assigns one track to each aircraft and records that track as played after its first
takeoff. This mod changes takeoff requests so they can replay. By default, it plays enabled aircraft
takeoff songs in shuffled cycles, using clips discovered in the game's encyclopedia or received
in takeoff requests. Each unique clip has one turn per cycle, even if several aircraft share it.

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

### Shuffled Cycles

With a stable catalog and settings, every enabled song starts once before the next cycle begins.
Each cycle snapshots all known live clip objects, including disabled or unresolved songs, and
shuffles them with Fisher-Yates. Eligibility is checked live as each turn is reached:

- Disabled, unresolved, or destroyed entries lose their turn and are skipped.
- Enable a song before its remaining turn and it can play in this cycle. Enable it after its turn
  was skipped and it waits for the next cycle. That cycle can begin on the same takeoff if the old
  one is exhausted.
- New or recreated clip objects join the next cycle, keeping the existing name-based checkbox rules.
- A selected song keeps its turn until the game actually starts its incoming source. If the game
  rejects the crossfade, that song remains pending for the next otherwise-eligible takeoff. Its
  eligibility is checked again then. There is no automatic playback retry.
- Selection scans at most one fresh cycle per request. All-off safely returns without starting music;
  one eligible song is allowed to repeat.

The last successfully started **takeoff** clip is deferred when a different eligible clip remains
in the current cycle. For example, queue `4-2-1-5-3` with 4 disabled plays `2-1-5-3`; the next cycle's
first eligible song will not be 3 if another is eligible. The deferred song retains its turn.
If only the last-played clip remains eligible, it can play again: consumed, skipped, and newly
discovered clips are not pulled into an unfinished cycle just to avoid a repeat.

Assigned-song mode and global disable preserve queue progress. A successful assigned or vanilla
takeoff updates last-played history without consuming a queued turn, so resuming shuffle can defer
that clip too. Failed calls and non-takeoff event playback do not update this history or consume turns.
Unknown vanilla takeoff clips are recognized for active-playback protection and end-based cooldown
without binding settings through the vanilla request path.

Queue identity and repeat comparisons use unique `AudioClip` objects, not names: distinct same-name
objects have separate turns but share their existing checkbox. Queue progress and history survive
mode changes, menu/mission transitions, and music-manager replacement while their clips remain live.
Destroyed references are pruned. Restarting the game begins a fresh queue with no last-played
reference; saved song exclusions remain intact. Queue/history state is never written to disk.

## Configuration

The config file is `BepInEx/config/com.lucaspevidor.musicrandomizer.cfg`.

| Section / Key | Default | Effect |
|---------------|---------|--------|
| `General / Enabled` | `true` | Enable repeatable takeoff music. Disabling it restores the game's original takeoff behavior. |
| `General / RandomizeTakeoffMusic` | `true` | Play enabled takeoff clips in shuffled cycles. When false, use the current aircraft's track only if its song is enabled, preserving queue progress. |
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
$deployment = Join-Path $verificationRoot "MusicRandomizer-song-queue"
dotnet build -c Release -p:PluginDeploymentDirectory="$deployment"
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }
dotnet build "tests\MusicRandomizer.Tests\MusicRandomizer.Tests.csproj" -c Release -p:PluginDeploymentDirectory="$deployment"
if ($LASTEXITCODE -ne 0) { throw "Managed test build failed." }
dotnet run --project "tests\MusicRandomizer.Tests" -c Release --no-build --no-restore -- "$verificationRoot"
if ($LASTEXITCODE -ne 0) { throw "Managed checks failed." }
dotnet build -c Release -p:PluginDeploymentDirectory="$deployment"
if ($LASTEXITCODE -ne 0) { throw "Post-test plugin build failed." }
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
the actual config, pool, request patch, context, and playback-state code. It uses real BepInEx
5 configuration/persistence and Harmony field-reference access, with managed Unity/game API doubles
and deterministic Fisher-Yates choices. Requests run through the real prefix, an independently
specified simulated game outcome, and the real postfix. Each check runs in a fresh process to
isolate static state.
The test-only BepInEx package build target is excluded so dependencies are copied to the test host;
the older MonoMod dependency uses its Cecil emitter there for .NET 10 compatibility.

The 23 managed checks cover exact keys, persisted labels/exclusions, discovery throttling, grouping,
zero/one/multiple candidates, full cycles and boundary repeat prevention, live turns, late/recreated
clips, rejected pending requests, assigned/vanilla history, and uncatalogued vanilla playback recognition.
They also cover source swaps, stopped-source restarts, pre/post-call fade state, unconfirmed source
activity, exceptions, destroyed objects, guard rejections, file-lock failures before/after binding,
bounded recovery, membership cleanup, and playback/cooldown policy.

## Runtime Checks

The checklist below covers regression and compatibility scenarios.
When validating game or content updates, check exact clip names, shared references, aircraft labels,
and duplicate/empty-name cases. If genuinely different stock songs share a name or are unnamed,
the grouping policy must be revised with a verified stable mapping before claiming independent
controls for each stock song.

Use `BepInEx/LogOutput.log` together with actual listening/source observation for these checks.
`Requesting takeoff music` means a request was submitted, not that the game's crossfade guards
accepted it.

The postfix checks the pre-call incoming source object even if the manager's fields swap: it must
now play the requested clip and previously have been stopped or held another clip. A pre-call active
fade, unchanged already-playing matching incoming source, outgoing matching audio, field swap alone,
or clip assignment without playback does not confirm a start. Post-call fading can be a successful
start. An unchanged matching incoming source is conservatively treated as unconfirmed playback.

After an explicitly authorized deployment with the game stopped, validate the queue:

1. Listen/observe incoming source activity over at least two full cycles with several enabled songs;
   verify complete coverage and no avoidable boundary repeat. Repeat with disabled heads, all-off,
   and one enabled song.
2. Take off during an unrelated active crossfade. Verify that rejection retains the candidate for the
   next successful takeoff and does not start cooldown.
3. Successfully play the pending clip in assigned mode, then resume shuffle. Verify an eligible
   alternative goes first and the deferred clip still gets its turn. Repeat across global disable,
   and confirm rejected vanilla calls do not affect order.
4. Toggle songs before/after their turns and while pending/playing. Check late discovery, mission/menu
   transitions, manager replacement, destroyed/recreated clips, and a fresh queue after game restart.
5. Confirm synchronous incoming-start detection and end-based cooldown in the real game, including
   source swaps and re-enabling the mod during previously uncatalogued vanilla takeoff playback.

Selector and gameplay regressions:

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
