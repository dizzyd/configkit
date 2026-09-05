# Integrating

The API, and when things happen.

[STRUCTURED-CONFIG.md](STRUCTURED-CONFIG.md) is about *shapes* — what a dictionary or a
nested class becomes on screen. This page is about the other half: how you register a config,
when its values are actually there, and what to do when the server disagrees with the client.

---

## Registering

```csharp
public class MyModSystem : ModSystem
{
    public static MyConfig Config { get; private set; } = new();

    public override void StartPre(ICoreAPI api)
    {
        api.ModLoader.GetModSystem<ConfigKitModSystem>()
           .RegisterManagedConfig("mymod", Config, "mymod.json");
    }
}
```

```csharp
void RegisterManagedConfig(
    string domain,                      // your modid
    object configObject,                // the instance ConfigKit fills in
    string? path = null,                // file name; defaults to "<domain>.json"
    Action? onSyncedFromServer = null,
    Action<string>? onSettingChanged = null,
    Action? onConfigSaved = null);
```

`RegisterCustomManagedConfig` is the same method under configlib's name, kept so a mod that
looks it up by reflection finds it.

**Both signatures are frozen.** Adding even an optional parameter to them is source
compatible and binary incompatible: your mod, compiled against one release, emits a call
naming that exact signature, and a later release that added a parameter no longer has it.
The failure is a `MissingMethodException` at registration rather than a compile error.
Anything new therefore arrives as a method of its own, as `SetConfigDisplayName` did, and
`DocsAndFormatTests` fails if either signature drifts.

### Naming a config in the dropdown

The dropdown shows the name of the mod whose id is the domain. A mod registering **several**
configs needs a domain each, and none of them is its mod id — so those show the raw domain
until you say otherwise:

```csharp
system.RegisterManagedConfig("mymod-server", ServerConfig, "mymod/server.json");
system.SetConfigDisplayName("mymod-server", "My Mod: Server");
```

The list sorts by that name, so a mod's configs sit together.

That first snippet assumes ConfigKit is installed. If it might not be, see *Making it
optional* below — it is not as simple as a null check.

**Reference ConfigKit for this call and nothing else.** The settings class itself uses only
`System.ComponentModel` attributes, so it compiles and runs with ConfigKit absent — which is
the point.

### Making it optional

The snippet above assumes ConfigKit is installed. `?.` does **not** cover its absence: naming
`ConfigKitModSystem` means the runtime has to resolve that type, and it cannot if the assembly
is not there — so the method fails, null-conditional or not.

A hard dependency in `modinfo.json` is the simple answer and usually the wrong one. It is
resolved by mod id at load time, so the game refuses your mod outright to anyone who does not
have that exact library — [COMPATIBILITY.md](COMPATIBILITY.md) has six mods in that position.

Keep the call in a method of its own instead. Types are resolved when a method first runs, so
one that never runs never needs them:

```csharp
public override void StartPre(ICoreAPI api)
{
    if (api.ModLoader.IsModEnabled("configkit")) Register(api);
}

// Its own method on purpose: ConfigKit is only resolved when this runs, so a player without
// it loads the mod normally and simply gets no settings screen. NoInlining so the JIT cannot
// fold the reference back into the caller.
[MethodImpl(MethodImplOptions.NoInlining)]
private void Register(ICoreAPI api)
    => api.ModLoader.GetModSystem<ConfigKitModSystem>()
          .RegisterManagedConfig("mymod", Config, "mymod.json");
```

Your config still loads from its own file either way — a mod without a settings screen is not
a mod without settings.

### Where to call it

**`StartPre`.** ConfigKit reads every registered config in `AssetsLoaded`, and its own
`ExecuteOrder` is `0.01` so it gets there early.

Two things will refuse a registration, both of which log and return rather than throw:

- **ConfigKit has stood down.** If `configlib` or `autoconfiglib` is enabled, ConfigKit does
  nothing at all rather than fight over the same files. Your mod still runs; it just has no
  settings screen.
- **The registry has already been sent to clients.** Registering after that point would give
  a joining client a config the server never told it about, so it is refused. In practice
  this only happens if you register from something later than `AssetsLoaded`.

---

## When the values are there

**Immediately after `RegisterManagedConfig` returns.** The constructor reads the file and
assigns it onto your object before the call comes back, so this is safe:

```csharp
api.ModLoader.GetModSystem<ConfigKitModSystem>().RegisterManagedConfig("mymod", Config);
api.Logger.Notification($"[mymod] radius is {Config.SearchRadius}");   // already loaded
```

You do not need to wait for an event to read your own config.

### After that

| what happened | what runs |
|---|---|
| a player edits a setting | your object is updated, **then** `onSettingChanged(code)` |
| Save is pressed, or the file is written | `onConfigSaved()` |
| the file changes on disk | reloaded and assigned, as if edited |
| a client finishes syncing from a server | `onSyncedFromServer()` |

The order matters in the first row: **the object is assigned before the callback fires**, so
`onSettingChanged` can read the new value straight off your own class rather than being handed
it. The `code` it receives is the setting's code — for a nested member that is its path,
`RainCollector/LitresPerHour`.

```csharp
.RegisterManagedConfig("mymod", Config, "mymod.json",
    onSettingChanged: code =>
    {
        if (code.StartsWith("RainCollector/")) RebuildRainCollectors();
    });
```

Do not do expensive work in there unconditionally — it fires per edit, which for a text field
is per keystroke.

---

## Client and server

A setting is **server-owned** unless it says `clientside`. On a server running ConfigKit:

- the server's values are sent to every client on join, and overwrite the client's own
- a client without the `controlserver` privilege sees them read-only
- a change from a client is checked against that privilege **on the server** before it is
  applied, and a refusal is written to the audit log

So the read-only rendering is a courtesy; the privilege check is the enforcement. Mark the
settings that are genuinely per-player:

```csharp
[Category("Display, clientside")]
public bool ShowOverlay = true;
```

Because nested classes flatten into individual settings, this works **per field** — one class
can hold some server truth and some client preference.

`onSyncedFromServer` fires on the client once that has happened. Use it if you cache anything
derived from config: at that moment the values may have changed under you.

> **A server running ConfigKit requires its clients to have it.** Anyone without it is turned
> away at the connect screen, because the server sends config data a client without ConfigKit
> has nothing to receive with. Joining a server that does *not* run ConfigKit is fine — you
> keep your own settings screen for client-side mods.

---

## Reading someone else's config

`ConfigKitModSystem` implements `IConfigProvider`:

```csharp
IEnumerable<string> Domains { get; }
IConfig? GetConfig(string domain);
ISetting? GetSetting(string domain, string code);

event Action<string, IConfig, ISetting> SettingChanged;   // domain, config, setting
event Action ConfigsLoaded;                               // all configs are up
event Action ConfigWindowOpened;
event Action ConfigWindowClosed;
```

`ConfigsLoaded` fires on the server once every config is read, and on the client once they
have arrived from the server — so it is the right place to do anything that needs *another*
mod's settings.

`IConfig` gives you `GetSetting`, `WriteToFile`, `ReadFromFile`, `RestoreToDefaults` and
`AssignSettingsValues(object)`. `ISetting` carries `Value`, `DefaultValue`, `MappingKey`,
`SettingType` and `Validation`.

`Config.Errors` is every setting currently failing its own validation attributes, keyed by
code — empty for a sound config. A `ConfigSetting` also carries `Error`, `Nullable`,
`IsNull` and `Format`.

Editing another mod's config through this is possible and rarely a good idea; reading it to
stay consistent with it is the normal use.

---

## When something does not appear

ConfigKit says what it made of your class at registration:

```
[ConfigKit] Registered 'mymod': 23 settings, 2 sections, 10 containers, 1 as raw JSON, 1 hidden, 1 not editable
```

followed by a line for each member it could not make editable, naming the type and the reason.
**Nothing is dropped silently** — if a field is missing from the screen, that log says why.
The same counts are on the config as `SchemaSummary` and `SchemaNotices`, and `Definition`
returns what ConfigKit built from your class, which is the first thing to look at when a
setting is not where you expected.

Common answers:

| symptom | cause |
|---|---|
| no settings screen at all | `configlib` or `autoconfiglib` is installed; ConfigKit stood down |
| one field missing, no log line | it is `[JsonIgnore]`, or `[Browsable(false)]` — the latter is still in the file |
| a field shows as raw JSON | nothing can edit that type; it still round-trips |
| an edit does not reach your object | it failed a validation attribute — the message is at the bottom of the window, and in `config.Errors` |
| a number shows a text box, not a slider | its range has an open bound, or it is nullable — neither has a slider position |
| a setting has no tooltip | no `[Description]`, and no `.xml` doc file shipped beside the dll |
| a field says *not editable* | nothing can construct it — an interface, an abstract class, or a cycle |
| a key marked with `!` | `[DataType("blockcode")]` and the key names nothing loaded |
| values reverted on upgrade | file a bug — see [regression-review-2026-09.md](regression-review-2026-09.md) for the shape of these |

---

## Testing your integration

ConfigKit's own suite runs in a live game through
[vstestkit](https://github.com/dizzyd/vstestkit), and the useful pattern for a mod is the
same: register the config, edit a setting, assert your object changed.

```csharp
MyConfig settings = new();
Config config = new(Capi, "mymod", "My Mod", settings, "mymod.json");
config.AssignSettingsValues(settings);

config.GetSetting("SearchRadius")!.Value = new JsonObject(new JValue(30));
Assert.Equal(30, settings.SearchRadius);
```

There is a demo mod in [`demo/`](../demo) holding one of every shape, with a Cairn pack that
launches it — `demo/run.sh`. It is the fastest way to see what your own class will look like
before you write it.
