# What changed from configlib

ConfigKit is derived from [configlib](https://github.com/maltiez2/vsmod_configlib) (CC0).
What was rebuilt, what was fixed, and which of those bugs are still present upstream.

Everything marked **inherited** was verified against configlib's own source. Everything
marked **ours** was introduced by this project, mostly by the GUI rewrite.

---

## Rebuilt

| | |
|---|---|
| **The settings window** | Dear ImGui replaced with the game's own Cairo GUI. `ConfigWindow.cs` + `GuiManager.cs` (926 lines) became a `GuiDialog`. `vsimgui` is no longer a dependency of any kind. |
| **Dependency packaging** | No ILRepack. YamlDotNet ships as its own unmodified file, resolved from NuGet at build time rather than checked in as a binary. |
| **Licence** | MIT, rather than CC0. |

## Deliberate differences

**`requiredOnClient: true`, `requiredOnServer: false`, and a pinned `networkVersion`.**

- `requiredOnClient: true` is not optional, however unfriendly it looks. ConfigKit registers
  a recipe registry (`configkit:configs`) to sync configs, and a server sends every registry
  it has to every client. The client resolves each by code with `GetRecipeRegistry`, which
  returns **null** for one it does not know, and then calls `FromBytes` on it. A client
  without ConfigKit joining a server with it therefore crashes to desktop with a
  NullReferenceException in `HandleServerAssets_Step11`. With the flag set, the client is
  turned away on the connect screen with a "mods missing" message and an offer to download,
  which is what configlib does and why.
- `requiredOnServer: false` keeps ConfigKit enabled on a client whose server does not have
  it. With `true`, the client disables the mod, so a player who installs ConfigKit to
  configure their own client-side mods would lose it the moment they joined a server without
  it. Nothing arrives from such a server, so there is nothing to crash on.
- `networkVersion` is pinned to `1.0.0` and deliberately not bumped per release. Clients are
  matched on `ModID@NetworkVersion`, and it defaults to the mod version, so without pinning
  every patch release would reject clients one version behind. Bump it only when the config
  sync format changes.

## Added

- **39 in-game tests** across three configurations: singleplayer, two-process
  multiplayer, and a run with configlib installed. Includes standing regression tests for
  every bug below.
- **A two-process multiplayer test tier** (contributed to
  [vstestkit](https://github.com/dizzyd/vstestkit)): a real server and a client joined
  over a socket, because a singleplayer session never takes the sync code's other branch,
  which is where the two worst bugs were hiding.
- **Build-time verification**: the assembly may declare only its own namespaces, every
  third-party dll must match its publisher's hash, and each must ship its licence.
- **Standing down** when configlib or autoconfiglib is installed, rather than two mods
  fighting over one config file.
- **Read-only server settings** for players without `controlserver`, and labels
  humanised from field names when a mod ships no translation.
- **A value beside every slider**, showing the setting's own number rather than the
  slider's: sliders are integer-only, so a float rides at 100x and vanilla's in-track text
  would read 250 for a value of 2.5.
- A colour swatch beside the hex field, panels sized to their content, and
  `ConfigsReceivedFromServer` so a mod can tell synced values from local defaults.

## Removed

- `ReflectionContext` and `StatsContext` from the vendored expression engine. Unreachable,
  but they invoked members by string name with `BindingFlags.NonPublic` on an arbitrary
  object, a primitive useful to nobody but an attacker.

---

## Bugs fixed

### Sync and configuration

**A managed config was emptied on a remote server** (*inherited*).
Three constructors assigned `_clientSideSettings` the *same dictionary object* as
`_settings`; `SyncFromServer`'s multiplayer branch clears it and then iterates `_settings`,
so the loop ran zero times. Every setting vanished, while the window still drew every row,
so a player could edit and save into nothing. Singleplayer takes the other branch, which is
why it went unnoticed.

**Values loaded from file never reached the mod's object** (*inherited*).
`AssignSettingsValues` was implemented, exposed on the API, and called from nowhere. A mod
ran on its compiled-in defaults while both the file and the settings screen showed the
player's edits.

**Patches compounded on a client** (*inherited*).
Patching runs twice there (once over local configs, again when the server's arrive) and
rewrites the asset in place, so `"value * 2"` became ×4. Patching now starts from the
asset's pristine bytes and is idempotent however often it runs.

**A config file from an older definition version** is rejected rather than applied to a
newer one.

### Crashes and data loss

**Duplicate sorting weights silently emptied the whole config** (*inherited*).
The de-duplication added a fixed `1E-10f`, which float32 loses entirely at any weight of 1
or more, so the insert threw and the mod was left with no settings at all.

**An unknown enum mapping key threw `KeyNotFoundException`** (*inherited*).
Reachable from the network: a server whose build renamed an enum member crashed the
client's join.

**Unboxing casts on config fields threw** (*inherited*).
`(float)value` on a boxed `double`, a `long`, an enum, or a `[DefaultValue(3)]` int over a
float field. Each took the mod's *entire* registration down, not just that setting. The
assign-back path had the mirror bug. `double`, `long` and enum fields now work.

**`JsonObjectPath.Set` counted a lazy sequence after mutating through it** (*inherited*).

**A config-reload event naming an unknown domain** threw out of the event bus (*inherited*).

### Lifecycle

**The shared file watcher was destroyed by the first config disposed** (*inherited*).
Configs in a directory share one `FileSystemWatcher` in a static dictionary; `Dispose`
disposed it outright and cleared the whole path registry, so whichever config went first
killed live reload for every other config in the process and left a disposed watcher
cached for anything created later. The only symptom was that editing a config file quietly
stopped working.

**`ReloadConfigs` orphaned the config it replaced** (*inherited*).
It stayed registered in those static tables holding a handler bound to a session that had
ended.

**Dispose re-patched the pause menu instead of unpatching it** (*inherited*).
`Patch()` was called from both `Start` and `Dispose` while `Unpatch()` sat unused, so every
world reload stacked another Harmony prefix on a method that runs for every button in the
pause menu.

**Harmony patches were registered under the id `"configlib"`** (*inherited*).
configlib's own `UnpatchAll` would have removed them.

**`Unpatch` removed every mod's prefix, not just ours** (*ours*).
Harmony's third parameter defaults to `"*"`, and constructing the instance with our id does
not scope the call. Introduced when Dispose was corrected to unpatch; it would have
stripped configlib's pause-menu button in exactly the case stand-down exists to avoid.

### Security

**A hostile server could make a client write a file anywhere** (*inherited*).
The domain and file name in a synced config come from the server and reached
`Path.Combine` unchecked, which honours `..`, absolute and rooted paths. Config paths are
now contained under `ModConfig` or refused.

**Server-authoritative settings were editable by unprivileged players** (*ours*).
configlib's ImGui window disabled them; the Cairo rewrite dropped the check, so a player
could drag a server slider and then run on a value the server never agreed to.

### Interface

**A client whose server does not run ConfigKit had no settings window** (*inherited*).
It was built only from the server's config registry, so the hotkey and the pause-menu
button did nothing while the client's own configs sat loaded and editable.

**Rows scrolled out of view were drawn over the buttons and the hotbar** (*ours*).
`GuiElementContainer` renders each child through its own `RenderInteractiveElements`, and
several stock elements ignore `InsideClipBounds` and paint at their own bounds. Only the
container's static texture was scissored, so a row below the fold lost its frame but kept
its label and value. The same bounds drive mouse handling, so a switch invisible under a
button could still be clicked.

**Every slider below a rangeless number setting vanished** (*engine*).
`GuiElementTextInput` overwrites the GL scissor rect with its own box to trim its text and
ends on `GlScissorFlag(false)` instead of restoring what it was handed;
`GuiElementHoverText` switches the flag back on without setting a rect. A settings row is a
label, a tooltip and a control, so after the first number input every following slider was
scissored into a stale text-box-sized rect. ConfigKit re-establishes the clip around each
child rather than trusting the previous one to have left it alone.

**A `values` list of numbers rendered as blank rows** (*ours*).
`JsonObject.AsString` returns the default for any non-string token, so
`[AllowedValues(1,2,3)]` produced empty dropdown entries that wrote `""` back, which then
threw on the next read.

**A fresh client had no `ModConfig` directory** and the first config write threw. Only
reachable on a client joined to a remote server.

---

## What was measured and left alone

- **Wildcard patching** enumerates all 32,433 asset locations per wildcard target. Measured
  at ~1ms for the lookup and ~4ms per sweep, so roughly 40ms once at load for a pack with
  eight wildcard targets. Not worth changing.
- **`ConfigSetting.Changed()`** is dead upstream, with its body commented out. Left as-is:
  re-enabling an event whose disabling isn't explained is how you introduce the next bug.

## Known and unfixed

- A malformed config file leaves that mod with **no settings** rather than falling back to
  defaults. A test documents the current behaviour.
- `ServerSideSettingChanged.IsSinglePlayer` is a bool the client chooses and the server
  trusts; a privileged client can suppress the server's persist.
- Orphan accounting in the static watcher tables is not directly tested across repeated
  world reloads.
