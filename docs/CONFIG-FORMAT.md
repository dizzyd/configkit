# Config file format

A reference for `configlib-patches.json` — the file that gives a mod settings without any
C#. ConfigKit reads the same format configlib does, so an existing file needs no changes.

Put it at:

```
assets/<yourmod>/config/configlib-patches.json
```

That's all. If ConfigKit isn't installed nothing happens and your mod uses its own
defaults; there is no dependency to declare.

---

## The shape of the file

```json
{
  "version": 1,
  "file": "yourmod.json",

  "settings":   [ ... ],
  "patches":    { ... },
  "formatting": [ ... ]
}
```

| Key | Meaning |
|---|---|
| `version` | Bump when you change what settings mean. A config file written against an older version is discarded rather than misapplied. |
| `file` | Optional. Write the config as this JSON file instead of `<yourmod>.yaml`. Use it when your mod reads the file itself. |
| `settings` | What the player can change. |
| `patches` | Where those values are written into game assets. Omit if your mod reads the settings itself. |
| `formatting` | Headings and notes in the settings screen. |

---

## Settings

Two shapes are accepted. **The array form** carries its own `type` and `code`, and orders
settings by position:

```json
"settings": [
  { "type": "boolean", "code": "enableThing", "nameInGui": "Enable thing",
    "default": true, "comment": "Turns the whole thing on and off." },

  { "type": "integer", "code": "radius", "nameInGui": "Search radius",
    "default": 12, "range": { "min": 4, "max": 40 } }
]
```

**The object form** groups by type and takes the code from the key. Only this form honours
`weight`:

```json
"settings": {
  "integer": {
    "radius":  { "default": 12, "weight": 1, "range": { "min": 4, "max": 40 } },
    "attempts":{ "default": 3,  "weight": 2 }
  },
  "boolean": {
    "enableThing": { "default": true, "weight": 3 }
  }
}
```

### Types

`boolean` · `integer` · `float` (or `number`) · `string` · `color` · `other`

`color` is a `"#rrggbb"` string shown with a swatch. `other` is an arbitrary JSON value,
edited as text.

### Fields

| Field | Applies to | Meaning |
|---|---|---|
| `default` | all | The value used until the player changes it. Required. |
| `code` | array form | The name patches and expressions refer to. |
| `nameInGui` / `ingui` / `name` | all | Label. A `domain:key` value is translated; otherwise shown as-is. |
| `comment` | all | Tooltip in the screen, and a comment in the written file. Translated the same way. |
| `weight` | object form | Sort order. Equal weights are fine. |
| `clientSide` | all | `true` means each player owns it and the server does not overwrite it. Anything else is server-authoritative and read-only for players without `controlserver`. |
| `hide` | all | Keep it in the file but out of the settings screen. |
| `link` | all | A URL written into the config file as a comment. |
| `logarithmic` | numeric | Slider scales logarithmically. |
| `range` | numeric | `{ "min": …, "max": …, "step": … }`. Gives a slider instead of a text box. |
| `values` | all | A fixed list of allowed values — a dropdown. |
| `mapping` | all | Named choices: `{ "Gentle": 0, "Brutal": 2 }`. The player picks a name, patches get the value. |

---

## Patches

Patches write setting values into game assets — yours, another mod's, or vanilla's. This
is how a content mod becomes configurable, and how a server admin retunes a pack from one
file without repacking anything.

```json
"patches": {
  "integer": {
    "game:itemtypes/tool/axe.json": { "durability": "AXE_DURABILITY" }
  },
  "float": {
    "@yourmod:patches/dye/*": { "-/value": "SEAL_HOURS * 0.77" }
  },
  "boolean": {
    "yourmod:recipes/barrel/curing.json": { "enabled": "ALLOW_CURING" }
  }
}
```

Structure is `type → asset → { json path: expression }`.

### Patch types

`boolean` · `integer` · `float` (or `number`) · `string` · `other` · `const`

Numeric types accept **expressions**, not just setting names.

### Targeting assets

| Form | Matches |
|---|---|
| `domain:path/file.json` | exactly that asset |
| `@domain:path/*` | every asset matching the wildcard |

On a **client**, patches to `itemtypes`, `blocktypes`, `entities` and `recipes` are
skipped — the server owns those and syncs the result, so patching them locally would
desync the two.

### JSON paths

Paths are `/`-separated. Each element is one of:

| Element | Selects |
|---|---|
| `name` | that key |
| `3` | that array index |
| `-` | **every** element of an array |
| `1..4` | a range of indexes |
| `@@wild*` | keys matching a wildcard |
| `key=value` | array elements whose `key` equals `value` |

So `behaviors/2/properties/damage` walks into an array, and `attacks/-/damage` writes to
every attack.

### Expressions

Numeric patches evaluate an expression. Setting codes are variables, and `value` is the
asset's own current value:

```
"damage":     "DAMAGE_MULTIPLIER * 2"
"durability": "value * DURABILITY_SCALE"
"chance":     "clamp(BASE_CHANCE, 0, 1)"
```

Available: `pi`, `e`, `sin`, `cos`, `abs`, `sqrt`, `ceiling`, `floor`, `exp`, `log`,
`round`, `sign`, `clamp(v, min, max)`, `max`, `min`, `greater(l, r, then, else)`,
`lesser(…)`, `equal(…)`, `notequal(…)`.

Boolean patches take a boolean expression over boolean settings.

> Patching is **idempotent** in ConfigKit: each application starts from the asset's
> original bytes, so `"value * 2"` stays ×2 however many times patches are applied. Under
> configlib the same patch compounds to ×4 on a client, which applies them twice.

---

## Formatting

Headings and notes, sorted among the settings by `weight`:

```json
"formatting": [
  { "type": "separator", "weight": 0, "title": "Behaviour",
    "text": "How the thing behaves.", "collapsible": true },

  { "type": "separator", "weight": 10, "title": "Advanced",
    "link": "https://example.com/docs", "linkText": "Read the docs" }
]
```

| Field | Meaning |
|---|---|
| `title` | Heading text |
| `text` | A line of explanation beneath it |
| `link` / `linkText` | A URL, and its label |
| `collapsible` | Settings after it can be folded away |
| `weight` | Where it sits among the settings |

Separators may also appear inline in the array form of `settings`, with
`"type": "separator"`.

---

## A complete example

```json
{
  "version": 1,
  "settings": [
    { "type": "separator", "title": "Gravestones",
      "text": "Who may recover a grave, and how long it lasts." },

    { "type": "boolean", "code": "ownerOnly", "nameInGui": "Owner only",
      "default": true, "clientSide": false,
      "comment": "When on, only the grave's owner can recover it." },

    { "type": "integer", "code": "decayHours", "nameInGui": "Hours before decay",
      "default": 48, "range": { "min": 1, "max": 240 } },

    { "type": "color", "code": "markerColor", "nameInGui": "Marker colour",
      "default": "#4FBFA8" }
  ],

  "patches": {
    "integer": {
      "yourmod:blocktypes/gravestone.json": {
        "attributes/decayHours": "decayHours"
      }
    },
    "boolean": {
      "yourmod:blocktypes/gravestone.json": {
        "attributes/ownerOnly": "ownerOnly"
      }
    }
  }
}
```

---

## Notes

- Settings are written to `ModConfig/<yourmod>.yaml` (or the `file` you named). Players can
  edit it by hand, and ConfigKit picks the change up without a restart.
- Keep `code` values stable. They key the config file, so renaming one loses that setting's
  saved value.
- Two settings may share a `weight`; ConfigKit orders them predictably rather than failing.

## Credit

The format is configlib's, and this reference draws on its
[JSON API documentation](https://github.com/maltiez2/vsmod_configlib/wiki) written by
**Somnium** — including the list of expression functions. Both are CC0. Every claim here
was checked against ConfigKit's own parser, so where the two differ this describes
ConfigKit.
