# Migrating from configlib to ConfigKit

ConfigKit does what configlib does, without the Dear ImGui dependency. It reads the same
`configlib-patches.json` files and writes the same `ModConfig/<yourmod>.yaml`, so your
players keep their settings.

Most mods need **no changes at all**. Find your case below.

---

## Which case am I?

| If your mod… | You need to… |
|---|---|
| has no C#, just a `configlib-patches.json` | **do nothing** |
| checks `GetMod("configlib")` in C# | **change one line** |
| calls `GetModSystem<ConfigLibModSystem>()` | **change a reference and rebuild** |
| draws its own settings screen with ImGui | **replace it with a settings class** |

---

## Case 1: content mods, nothing to do

ConfigKit reads `assets/<yourmod>/config/configlib-patches.json` exactly as configlib
does, and writes the same config file. Your mod works under either library with no
changes and no new dependency.

Nothing below applies to you. You're done.

---

## Case 2: you check whether configlib is installed

Change the mod id:

```csharp
// before
bool configAvailable = api.ModLoader.GetMod("configlib") != null;

// after
bool configAvailable = api.ModLoader.GetMod("configkit") != null;
```

That's the whole change. You still need no reference to the library at all.

---

## Case 3: you call into the library from C#

Three edits.

**1. Point the reference at ConfigKit** (in your `.csproj`):

```xml
<Reference Include="ConfigKit">
    <HintPath>$(VINTAGE_STORY)/Mods/configkit/ConfigKit.dll</HintPath>
    <Private>false</Private>
</Reference>
```

**2. Rename the namespace and mod system** in your C#:

```csharp
using ConfigLib;                                  // → using ConfigKit;
GetModSystem<ConfigLibModSystem>()                // → GetModSystem<ConfigKitModSystem>()
```

**3. Update your `modinfo.json`** if you declared a dependency:

```json
"dependencies": { "configkit": "" }
```

Everything else is the same. `GetConfig`, `GetSetting`, `AssignSettingsValues`,
`RegisterCustomManagedConfig` and the `SettingChanged` events all keep their names and
signatures.

`RegisterCustomManagedConfig` is kept as an alias of `RegisterManagedConfig`, which is
what it is called here. Both work; new code should use the shorter one.

### If you find configlib by reflection rather than by reference

Some mods look the mod system up by type name instead of referencing the assembly:

```csharp
// before
ModSystem? system = api.ModLoader.GetModSystem("ConfigLib.ConfigLibModSystem");
MethodInfo? register = system?.GetType().GetMethod("RegisterCustomManagedConfig");

// after - the type name is all that changes
ModSystem? system = api.ModLoader.GetModSystem("ConfigKit.ConfigKitModSystem");
MethodInfo? register = system?.GetType().GetMethod("RegisterCustomManagedConfig");
```

That is deliberate: the member names and signatures are configlib's, so a reflection-based
integration only needs the mod id and the type name changed. If you match on the full
parameter list, it is unchanged too — `(string, object, string, Action, Action<string>,
Action)`.

---

## Case 4: you draw your own settings screen with ImGui

`RegisterCustomConfig` is gone, because it handed you an ImGui drawing callback and
ConfigKit has no ImGui. Instead, describe your settings and let ConfigKit draw them.

```csharp
// before: you draw the widgets
api.ModLoader.GetModSystem<ConfigLibModSystem>()
    .RegisterCustomConfig("mymod", (id, buttons) =>
    {
        ImGui.Checkbox("Enable thing" + id, ref MyConfig.EnableThing);
        ImGui.SliderFloat("Speed" + id, ref MyConfig.Speed, 0.1f, 5f);
    });
```

```csharp
// after: you describe the settings
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

public class MyConfig
{
    [Description("Enable thing")]
    public bool EnableThing = true;

    [Description("How fast the thing goes")]
    [Range(0.1, 5.0)]
    public float Speed = 1f;
}

// once, in StartPre or Start:
api.ModLoader.GetModSystem<ConfigKitModSystem>()
    .RegisterManagedConfig("mymod", myConfig);
```

ConfigKit writes `ModConfig/mymod.json`, draws the settings screen, and assigns values
straight onto your object: both what it reads from the file at startup and anything the
player edits afterwards.

This is usually **less** code than you had. A screen with forty sliders becomes forty
`[Range]` attributes.

Those attributes are plain .NET (`System.ComponentModel`), not ConfigKit types, so your
settings class has no reference to ConfigKit and still compiles and runs if ConfigKit
isn't installed.

### Attributes you can use

All of them are stock .NET. Your settings class keeps no reference to ConfigKit.

| Attribute | Effect |
|---|---|
| `[Description("…")]` | Tooltip in the settings screen |
| `[Range(min, max)]` | Slider instead of a text box |
| `[DefaultValue(x)]` | Value used by "restore defaults" |
| `[Category("…")]` | Groups settings under a foldable heading |
| `[AllowedValues(…)]` | Dropdown of fixed choices |
| `[DisplayName("…")]` | Label, instead of the tidied-up field name |
| `[JsonProperty("…")]` | The key to write in the file, when it differs from the field name |
| `[Browsable(false)]` | Hidden from the screen, still saved |
| `[JsonIgnore]` | Not saved at all |
| `[Key]` | On a member of a list element: which field labels its row |
| `[DataType("blockcode")]` | Keys are block codes, so a typo gets flagged |

The label comes from the field name, tidied up, so `SearchRadius` shows as "Search radius",
unless you set `[DisplayName]` or your mod ships a translation for
`<yourmod>:setting-SearchRadius`.

`[Category]` takes a section name. It also still understands the two words configlib used —
`clientside` and `logarithmic` — and you can write both at once:
`[Category("Waypoints, clientside")]`.

### Field types

`bool`, `string`, `int`, `float`, `double`, `long`, `decimal`, `byte` and enums all work. An
enum becomes a dropdown of its member names.

**Nested classes and containers work too**, which is the part that used to send authors to a
hand-written ImGui panel:

```csharp
public class ConfigServer
{
    public bool Enabled = true;                          // a plain row

    public RainCollector RainCollector = new();          // a section, one row per field

    [Category("Doors")]
    [DataType("blockcode")]
    public Dictionary<string, int> AutoCloseDelays = new();   // opens its own screen

    public Dictionary<string, CreatureOpenDoors> Creatures = new();
    public List<string> Blacklist { get; } = new();      // get-only is fine
}
```

- **A nested class** becomes a foldable section, and each of its fields an ordinary row with
  its own slider, dropdown and Reset. In the file it stays nested, exactly as
  `StoreModConfig` would have written it.
- **A dictionary, list, set or array** becomes one row that opens its own screen: one entry
  per line, an editable key, a filter box, and Add and delete. Nesting works — a
  `Dictionary<string, Dictionary<string, float>>` is that screen twice.
- Dictionary keys can be `string`, an enum, or anything with a `TypeConverter` such as
  `AssetLocation`. Add picks a key that is free rather than one already in use, and renaming
  onto an existing key is refused instead of quietly replacing it.
- `Dictionary<string, JToken>` round-trips untouched, so the usual "read the old config and
  migrate it" field keeps working.
- A `[DataType("blockcode")]`, `"itemcode"` or `"entitycode"` on a dictionary describes its
  keys: the screen then marks any key that names nothing loaded. Wildcards like
  `game:door-*` count as valid.

Anything ConfigKit cannot render is **reported at registration**, never silently dropped —
look for the `[ConfigKit] Registered '<yourmod>'` line in the log, which says how many
settings, sections and containers it found and names anything it could not make editable.

> Under configlib, a `double`, a `long`, an enum, or a `[DefaultValue(3)]` on a `float`
> field throws while reading your defaults and takes the **whole** registration down with
> it, which is why some mods carry a comment about using `float` rather than `double`.
> That is fixed here; you can drop the workaround.

---

## Things to know

- **Settings carry over.** ConfigKit writes the same `ModConfig/<yourmod>.yaml` files, so
  players keep their existing values.
- **Don't ship both.** If configlib or autoconfiglib is installed, ConfigKit stands down
  and logs why, so nothing breaks, but only one of them manages configs. Migrate to one or
  the other rather than trying to support both.
- **Multiplayer needs it on both sides.** A server running ConfigKit needs its clients to
  have ConfigKit, the same as configlib.
- **Server settings are read-only for ordinary players.** On a multiplayer client, a
  setting that is not `clientSide` is shown but not editable without the `controlserver`
  privilege. An edit would never be sent or saved, so offering it would only mislead.
  Mark anything a player should own with `"clientSide": true`.
- **No vsimgui dependency.** You can drop it if it was only there for your config screen.

## If something behaves differently

ConfigKit fixes a number of bugs that are still present upstream, including two that stop
managed configs working properly on a real multiplayer server. If your mod behaved oddly
under configlib and behaves differently here, [what changed](CHANGES-FROM-CONFIGLIB.md)
lists them.

## Getting help

Open an issue at <https://github.com/dizzyd/configkit/issues>.
