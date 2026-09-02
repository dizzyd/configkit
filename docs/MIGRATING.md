# Migrating from ConfigLib to ConfigKit

ConfigKit does what ConfigLib does, without the Dear ImGui dependency. It reads the same
`configlib-patches.json` files and writes the same `ModConfig/<yourmod>.yaml`, so your
players keep their settings.

Most mods need **no changes at all**. Find your case below.

---

## Which case am I?

| If your mod… | You need to… |
|---|---|
| has no C# — just a `configlib-patches.json` | **do nothing** |
| checks `GetMod("configlib")` in C# | **change one line** |
| calls `GetModSystem<ConfigLibModSystem>()` | **change a reference and rebuild** |
| draws its own settings screen with ImGui | **replace it with a settings class** |

---

## Case 1 — content mods: nothing to do

ConfigKit reads `assets/<yourmod>/config/configlib-patches.json` exactly as ConfigLib
does, and writes the same config file. Your mod works under either library with no
changes and no new dependency.

Nothing below applies to you. You're done.

---

## Case 2 — you check whether ConfigLib is installed

Accept either library:

```csharp
// before
bool configAvailable = api.ModLoader.GetMod("configlib") != null;

// after
bool configAvailable = api.ModLoader.GetMod("configkit") != null
                    || api.ModLoader.GetMod("configlib") != null;
```

That's the whole change. You still need no reference to either library, and your mod
keeps working for players who have ConfigLib.

---

## Case 3 — you call into the library from C#

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

Everything else is the same — `GetConfig`, `GetSetting`, `AssignSettingsValues` and the
`SettingChanged` events all keep their names and signatures.

---

## Case 4 — you draw your own settings screen with ImGui

`RegisterCustomConfig` is gone, because it handed you an ImGui drawing callback and
ConfigKit has no ImGui. Instead, describe your settings and let ConfigKit draw them.

```csharp
// before — you draw the widgets
api.ModLoader.GetModSystem<ConfigLibModSystem>()
    .RegisterCustomConfig("mymod", (id, buttons) =>
    {
        ImGui.Checkbox("Enable thing" + id, ref MyConfig.EnableThing);
        ImGui.SliderFloat("Speed" + id, ref MyConfig.Speed, 0.1f, 5f);
    });
```

```csharp
// after — you describe the settings
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

ConfigKit writes `ModConfig/mymod.json`, draws the settings screen, and assigns edited
values straight back onto your object.

This is usually **less** code than you had. A screen with forty sliders becomes forty
`[Range]` attributes.

Those attributes are plain .NET (`System.ComponentModel`), not ConfigKit types — so your
settings class has no reference to ConfigKit and still compiles and runs if ConfigKit
isn't installed.

### Attributes you can use

| Attribute | Effect |
|---|---|
| `[Description("…")]` | Label and tooltip in the settings screen |
| `[Range(min, max)]` | Slider instead of a text box |
| `[DefaultValue(x)]` | Value used by "restore defaults" |
| `[Category("…")]` | Groups settings under a heading |
| `[AllowedValues(…)]` | Dropdown of fixed choices |

---

## Things to know

- **Settings carry over.** ConfigKit writes the same `ModConfig/<yourmod>.yaml` files, so
  players keep their existing values.
- **Don't ship both.** If ConfigLib or AutoConfigLib is installed, ConfigKit stands down
  and logs why, so nothing breaks — but only one of them manages configs.
- **Multiplayer needs it on both sides.** A server running ConfigKit needs its clients to
  have ConfigKit, the same as ConfigLib.
- **No vsimgui dependency.** You can drop it if it was only there for your config screen.

## Getting help

Open an issue at <https://github.com/dizzyd/configkit/issues>.
