# Structured config

Sub-objects, dictionaries and lists in a settings class — and what a player sees.

This is the part that used to send mod authors to a hand-written Dear ImGui panel. It
doesn't any more: annotate a plain C# class and ConfigKit builds the screen, the file, the
server sync and the reset behaviour from it.

Your settings class keeps **no reference to ConfigKit**. Every attribute below is stock
.NET, so the class compiles and runs unchanged with ConfigKit not installed.

If your config is a flat list of numbers and switches, you don't need this page —
[MIGRATING.md](MIGRATING.md) covers that in four lines.

---

## The short version

```csharp
public class TweaksConfig
{
    public bool Enabled = true;                              // a row

    public RainCollector RainCollector = new();              // a section

    [Category("Doors")]
    [DataType("blockcode")]
    public Dictionary<string, int> AutoCloseDelays = new();   // opens its own screen

    public Dictionary<string, DoorRule> CreaturesOpenDoors = new();
    public List<string> Blacklist { get; } = new();           // get-only is fine
}
```

```csharp
api.ModLoader.GetModSystem<ConfigKitModSystem>()
   .RegisterManagedConfig("danatweaks", config, "DanaTweaks.json");
```

That's the whole integration. No draw delegate, no `ControlButtons`, no widget ids.
[INTEGRATING.md](INTEGRATING.md) covers the API around it: the callbacks, when the values are
there, what the server does to them, and what to read when a field does not appear.

---

## What each shape becomes

### A plain field is a row

Fields that belong to no section sit at the top of the screen and are always visible.

### A nested class is a section

No attribute needed — the class is the group, and each of its fields becomes an ordinary
row with its own slider, switch, dropdown and **Reset**.

```csharp
public class RainCollector
{
    [Description("Collect rain at all.")]
    public bool Enabled = true;

    [Description("Litres gathered per hour of rain.")]
    [Range(0.1, 20.0)]
    public float LitresPerHour = 2.5f;
}
```

![A settings screen with one section open and the rest folded](screenshots/ck-1-sections.png)

**Sections fold, one at a time.** A mod with a dozen sub-configs is a dozen headings and one
body, rather than a screen to scroll through. If the whole config fits without scrolling
nothing is folded at all — there's no point hiding four settings behind three headings.

Folding is for sections that come from a **class**. A separator in a
`configlib-patches.json` is a divider its author placed between rows of one flat list, so
those are drawn as plain headings and nothing is hidden — see
[COMPATIBILITY.md](COMPATIBILITY.md).

**There is a filter box beside the mod dropdown**, and it cuts across the folding — a
search for "door" shows every match, not the matches that happen to be in whichever section
is open. A section's own name matches too, which brings out its whole body.

![The main list filtered to one word](screenshots/ck-2-filter.png)

Sections nest. A class inside a class gets a heading carrying both names, so a field three
classes down still says where it came from:

![A nested class as its own section](screenshots/ck-7-nested-object.png)

In the file it stays nested, exactly as `StoreModConfig` would have written it:

```json
{
  "Enabled": true,
  "RainCollector": { "Enabled": true, "LitresPerHour": 2.5 }
}
```

### A dictionary or list opens its own screen

One row on the main screen showing how many entries it holds; clicking it opens the
container, with a breadcrumb back out, a filter, per-row delete, and Add.

![A dictionary of block codes](screenshots/ck-3-codes.png)

Why a separate screen rather than expanding in place: these are unbounded. A dictionary
keyed by block code routinely runs to hundreds of entries in a real pack, and an entry row
needs a different set of columns — key, value, delete — than a settings row does.

### A dictionary of classes drills down again

The value's own fields, each with the right control:

![A dictionary of objects](screenshots/ck-4-dictionary.png)

![One entry's fields](screenshots/ck-5-entry.png)

Each field of an entry has its own **Reset**, back to the value its class declares. An entry
you added to a dictionary has no default of its own, so that is offered here and not on the
list screens.

The row label comes from the field marked `[Key]`, falling back to the first string field,
then `#0`. So you usually annotate nothing — but reach for `[Key]` when the first string
field is not the identifying one.

### Nesting costs nothing

`Dictionary<string, Dictionary<string, float>>` is the shape nothing else handles
generically. Here the second level is the first level again:

![A nested dictionary, two levels down](screenshots/ck-6-nested.png)

---

## Attributes

All stock .NET. `System.ComponentModel`, `System.ComponentModel.DataAnnotations`, and
`Newtonsoft.Json` — which your mod already references if it uses `LoadModConfig<T>`.

| Attribute | Effect |
|---|---|
| `[Description("…")]` | Hover text on the label |
| `[Range(min, max)]` | Slider instead of a text box — and a real constraint, see *Validation* |
| `[DefaultValue(x)]` | The value Reset goes back to |
| `[AllowedValues(…)]` | Dropdown of fixed choices |
| `[Category("Doors")]` | Puts the setting in a section called Doors |
| `[Category("Doors, clientside")]` | Section, plus the flag |
| `[Display(Name = "…")]` | Label, instead of the tidied-up field name |
| `[DisplayName("…")]` | The same, but **only on a property** — see below |
| `[Display(GroupName = "…")]` | Section |
| `[Display(Order = n)]` | Where the row sits; lower is earlier |
| `[Key]` | On a field of a list element: which field labels its row |
| `[DataType("blockcode")]` | Keys are block codes — see below |
| `[DisplayFormat(DataFormatString = "P")]` | How the number reads — see *Formats* below |
| `[JsonProperty("…")]` | The key to write in the file |
| `[Browsable(false)]` | Hidden from the screen, **still saved** |
| `[JsonIgnore]` | **Not saved** and not shown |
| `[ReadOnly(true)]` | Shown, not editable |

A field with no way to assign it — a `readonly` field, or a get-only property that is not a
collection — is shown read-only too, without the attribute. Offering a control for one would
be theatre: the value would be written to the file and never reach your object.

`[Browsable(false)]` and `[JsonIgnore]` are deliberately different. The first is about
display and keeps the key in the file; the second is about serialisation and removes it.
Reach for `Browsable` unless you actually want the value gone.

Labels come from the field name, tidied up: `LitresPerHour` reads "Litres per hour". Ship a
translation to override it properly — the key is the setting's path with the prefix, so
`<yourmod>:setting-RainCollector-LitresPerHour` — or `[Display(Name = "…")]` for a quick fix.

**Section headings translate the same way.** A heading's key is
`<yourmod>:section-<its identity>`: `section-Doors` for `[Category("Doors")]`, and
`section-RainCollector-Overflow` for a class nested inside a class. Headings are compared by
that identity and only *drawn* by their caption, so translating one does not change what the
screen remembers is open — and a `[Category("Doors")]` sitting beside a nested class also
called `Doors` stays two sections rather than merging into one.

**`[DisplayName]` does not work on a field.** Its `AttributeUsage` allows class, method,
property, indexer and event, and a config class is nearly always public fields, so reach for
`[Display(Name = "…")]` instead — it allows fields and does the same job. `[DisplayName]` is
there for the properties case.

Rows sit in the order they are declared, and so do sections — a section sits where its
earliest member does. `[Display(Order = n)]` moves either; anything without it sorts after
everything that has it, which is the convention `DisplayAttribute` documents for itself.

Two details worth knowing. **Fields come before properties**: reflection cannot give true
source order across the two kinds, so fields are listed in the order you wrote them and
properties follow. Use `[Display(Order)]` if you need them interleaved. And a **section name
may contain a comma** — `[Category("Yours, not the server's, clientside")]` is a section
called "Yours, not the server's" with the `clientside` flag, not two sections.

### Validation

Every `System.ComponentModel.DataAnnotations` validation attribute is enforced, with the
message its author wrote:

```csharp
[Range(1, 10, ErrorMessage = "Pick a number of doors between 1 and 10")]
public int Doors = 4;
```

A value that fails is **kept on screen so it can be corrected, and not assigned to your
object** — your mod keeps the last value its own attributes agreed to, and the message
appears at the bottom of the window. `config.Errors` has the same thing in code.

This is what makes an open bound mean something: `[Range(0, double.PositiveInfinity)]` takes
the number input rather than a slider, and typing `-5` into it is now refused rather than
stored.

**Your own validators work too** — anything deriving from `ValidationAttribute`:

```csharp
public class EvenAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(object? value, ValidationContext context)
        => value is int n && n % 2 != 0
            ? new ValidationResult($"{context.DisplayName} must be even")
            : ValidationResult.Success!;
}
```

Each is asked through `GetValidationResult` with a real `ValidationContext`, so a validator
can read the rest of the object it lives on. One that throws is reported against its own
setting rather than escaping into the GUI.

### Open bounds

`[Range]` gives you a slider, but only where a slider makes sense. These are all idiomatic
and none of them becomes one:

```csharp
[Range(0, double.PositiveInfinity)] public float FreezingDamage = 1f;
[Range(1, int.MaxValue)]            public int  PollIntervalMs  = 500;
```

They say "no upper limit", not "a slider two billion units wide", so the setting keeps the
plain number input. The bound that *is* real still binds — the first of those refuses a
negative, as *Validation* above describes. A bound is treated as open when it is
infinite, or when the span is wider than a million steps at the scale the slider would use —
past that a pixel is thousands of units and no particular value can be chosen anyway.

Write the bound you mean. An honest `[Range(0, 10)]` gets the slider; reaching for
`int.MaxValue` to mean "any positive number" gets the text box, which is the better control
for it.

### Your doc comments are the tooltips

A member with no `[Description]` falls back to its own `///` summary:

```csharp
/// <summary>The lowest durability objects should ever drop to.</summary>
[Range(0, 1)]
public float MinDurability = 0;
```

That text becomes the hover tooltip, with the source indentation collapsed and any
`<see cref="..."/>` reduced to the name it points at. `[Description]` still wins where a
member has both — it was written to be shown, and the doc comment was written for a reader
of the source.

It costs one line in your csproj, plus shipping the `.xml` the compiler emits alongside
your `.dll` in the zip:

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

Without that file nothing happens and nothing breaks — the member simply has no tooltip.

### Formats

`[DisplayFormat(DataFormatString = "P")]` changes how a number **reads**, never what is
stored:

```csharp
[Range(0, 1)]
[DisplayFormat(DataFormatString = "P")]
public float DurabilityLeeway = 0.95f;      // reads 95.00 %, stores 0.95
```

Any .NET numeric format works — `P`, `N2`, `0.##`. It applies to the readout beside a slider
and to the tooltip that follows the handle, and deliberately **not** to a box you type into:
formatting an editable field means parsing the format back out, and a half-typed value has
to parse too. `ApplyFormatInEditMode` is ignored for that reason.

A format string that does not work out costs the formatting and nothing else — the raw
number is shown instead.

### `[DataType]`, and why it earns its place

A dictionary keyed by block code is the commonest structured setting there is, and a typo
in one of those keys **does nothing at all** — the entry sits in the file looking perfectly
correct and simply never matches. Nothing in the game tells you.

```csharp
[DataType("blockcode")]                 // or "itemcode", or "entitycode"
public Dictionary<string, int> AutoCloseDelays = new();
```

The screen then marks any key that names nothing loaded, and puts an example in the empty
field. In the screenshot above, `game:door-*` and `game:door-crude` are fine;
`game:door-oak` is flagged, because it looks exactly like a real code and isn't one.

**Wildcards count as valid.** `game:door-*` matches a great many blocks and is exactly as
correct as naming one.

It only ever marks a definite no. A field with no `[DataType]`, an unrecognised one, or a
registry that hasn't finished loading gets no opinion — flagging every key on an
unannotated dictionary would be far worse than saying nothing.

---

## What you get for free

**Keys can't collide.** Renaming an entry onto a name already in use is refused with a
message; the entry that was there is not replaced. Add invents a key that is free rather
than landing on one in use — and where there is no free key to invent, such as a dictionary
keyed by a three-member enum that already holds three entries, the button is replaced by a
line saying why.

**Keys commit when you leave the field**, or press Enter — never per keystroke. Typing
`abc` passes through `a` and `ab`, either of which might collide with something.

**Save, Reload and Restore defaults act on the whole config**, from however deep you are.
Restore asks once, because three levels down it looks local.

**Every public field is accounted for.** A field ConfigKit genuinely can't store — an
abstract type, a class that holds itself, something nested past five levels — gets no key in
the file, because a key that can't round-trip is worse than none. But it does get a line on
the settings screen saying so, next to where it would have been. Reporting it only in the log
would not count; players don't read the log.

You get the summary there too:

```
[ConfigKit] Registered 'danatweaks': 6 settings, 5 sections, 4 containers.
```

followed by a line for anything it couldn't make editable, with the type and the reason.

---

## Types that work

**Scalars** — `bool`, `string`, `int`, `long`, `short`, `byte`, `float`, `double`,
`decimal`, and enums. An enum becomes a dropdown of its member names, and is **stored by
name**, so renaming a member fails loudly instead of silently landing on whatever now holds
its old number.

**Containers** — `Dictionary<,>`, `List<>`, `HashSet<>`, arrays, and anything implementing
`IDictionary<,>`, `IList<>`, `ICollection<>` or `IReadOnlyCollection<>`.

**Dictionary keys** — `string`, enums, and any type with a `TypeConverter`, which includes
`AssetLocation`.

**Nesting** — classes inside classes, dictionaries of dictionaries, lists of classes
holding lists. Capped at five levels deep, and a class that holds itself is detected and
skipped rather than recursed into.

**`Dictionary<string, JToken>`** round-trips untouched, so the usual "read the old config
and migrate it" field keeps working.

**Get-only collections** — `public List<string> X { get; } = new();` is filled in place
rather than replaced, so a reference your mod took to that list stays live.

**Nullable members** — `string?`, `int?`, a class or a container left unset. A stored null
reaches your object as null, not as `""` or `0`, so a mod that reads null as "not
configured" keeps that distinction. A non-nullable value type cannot take one and keeps the
converted value instead.

A **nullable value type** — `int?`, `float?`, `bool?` — also gets a control that can *say*
null, because for these null is a value and not merely an absence:

```csharp
/// <summary>How much durability can be repaired. Unset means no limit.</summary>
[Range(0, double.PositiveInfinity)]
public float? MaintenanceLimit { get; set; }        // null is not 0
```

- a number reads **empty** when it is null, and clearing the box sets it back to null
- it takes the number input rather than a slider, because a slider has no position for unset
- a `bool?` is a three-way dropdown — `(unset)`, `true`, `false` — rather than a switch
- an enum or `[AllowedValues]` dropdown gains an `(unset)` entry

Without that, null showed as `0` and could never be typed back — which for the member above
means the screen said "no repair allowed" about a config that said "no limit", and the first
edit made it so.

Anything else Newtonsoft can round-trip is shown as raw JSON, which is honest and editable.
Anything it can't is reported and left alone.

---

## Server and client

A setting is server-owned unless it says otherwise. On a server that runs ConfigKit, a
client without `controlserver` sees those read-only; the privilege is checked and audited on
the server before any change is applied, so the read-only rendering is a courtesy, not the
enforcement.

Mark the ones that are genuinely per-player:

```csharp
[Category("Waypoints, clientside")]
public List<string> ExtraWaypointColors = new();
```

Because sub-objects flatten, this works **per field** — you can have one class where some
fields are server truth and others are client preference, which a per-object split cannot
express.

---

## Migrating an existing config

If your mod already uses `api.LoadModConfig<T>()`, the shape on disk is the same, so your
players' existing files keep loading. Two things to check:

1. **`[JsonProperty]` names are honoured**, and the field name is still read. A rename
   never orphans a stored value. If a file holds both keys, the field name wins - it is the
   one earlier ConfigKit versions read and wrote, so it is the value the player last had in
   effect - and the next save drops it, leaving only the `[JsonProperty]` name.
2. **Fields that were previously invisible now appear** in the file and on screen. That is
   the point of the change; `[Browsable(false)]` is the opt-out if you want one hidden.

What you delete: the `RegisterCustomConfig` delegate, the four-flag `ControlButtons`
handling, the `##label-{i}-{id}` suffixes, and your copy of `DictionaryEditor<T>`.

---

## Regenerating these screenshots

They come from a fixture rather than a drawing, so they can't drift from the real screen:

```bash
cd ~/src/anego-1.22/vstestkit
bash scripts/sync-linux.sh dizzyd@vsclient.home --mod ../configkit/configkit
ssh dizzyd@vsclient.home 'cd vstestkit-configkit && \
  bash scripts/run.sh ~/mods/configkit/tests --mod ~/mods/configkit/configkit \
       --client --filter TakeDocumentationShots'
```

The fixture is `tests/ShotsTest.cs`; the crop is 766px wide from the centre of a 960x600
frame.
