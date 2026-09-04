# Demo

A settings class holding one of every shape ConfigKit can render, in a Cairn pack you can
launch and poke at.

```bash
demo/run.sh
```

That builds ConfigKit and the demo mods, puts them in a pack called `configkitdemo`, and
launches it. Press **P** in game, or use **Mod settings** in the pause menu.

```bash
demo/run.sh --no-launch    # build and sync the pack, don't start the game
demo/run.sh --reset        # throw away the pack's saved config and world first
```

Cairn owns the game install and gives the pack its own data directory, so none of this
touches the game you actually play or the mods in it. Re-run `run.sh` after any change; it
rebuilds and replaces the zips in place.

---

## What's in it

Three mods, and the point is the contrast between the first two.

| mod | what it is |
|---|---|
| `configkit` | the library, built from this repo |
| `configkitdemo` | a **C# settings class**, one section per idea |
| `configkitdemodef` | the same screen from a **`configlib-patches.json`**, no code at all |

Both appear in the settings screen's dropdown, so you can switch between the two paths.

## Things worth trying

**The accordion.** `ConfigKit demo` has eight sections and opens with them folded — one at a
time, so it stays a list of headings rather than a wall. Type in the filter box beside the
dropdown and it cuts across the folding: searching `door` finds matches wherever they live.

**A nested class.** *Rain collector* is a section with a class inside it, so you get
`Rain collector › Overflow` as a heading of its own. Each field keeps its own slider and its
own Reset — that per-field granularity is why nested classes flatten instead of opening a
screen.

**A dictionary.** *5 Dictionaries → Auto close delays* opens its own screen. Three keys:
`game:door-*` and `game:door-crude` are real, and `game:door-oak` is flagged, because it
looks exactly like a real block code and isn't one. That's `[DataType("blockcode")]`.

- **Rename** a key onto one that already exists — refused, and nothing is lost.
- **Add** — it invents a free key rather than landing on one in use.
- Try adding a fourth entry to *Per difficulty*, keyed by a three-member enum. The button is
  replaced by a line saying why.

**Two levels down.** *Chute flow rates* is a dictionary of dictionaries. The second screen is
the first screen again.

**A class for a value.** *Creatures open doors* → any row → its fields, each with its own
control and its own **Reset** back to what the class declares. A class has a fixed shape, so
there is nothing to rename, delete or add here.

**Lists.** *Loot pool* rows are labelled by the field marked `[Key]`, not `#0 #1`.

**Nothing vanishes.** *3 Not editable* is the rule under test: `[ReadOnly]` and a `readonly`
field are shown but not editable, `[Browsable(false)]` is off screen but still in the file,
`[JsonIgnore]` is in neither, and `Impossible` — a type nothing can construct — gets a line
saying so rather than disappearing. Compare the screen with
`~/.cairn/packs/configkitdemo/data/ModConfig/configkitdemo.json`.

**Raw JSON, honestly.** *7 Awkward → Legacy data* is a `Dictionary<string, JToken>`. Its
values have no schema by definition, so it is **not** a dictionary screen — it is the raw
JSON control, and the registration log says so rather than pretending otherwise. It still
round-trips untouched, which is the whole reason a mod keeps a field like it.

**The file.** That JSON is nested exactly as the class is, which is what lets a mod move off
`LoadModConfig<T>` without orphaning its players' files. `7 Awkward → Renamed in the file` is
stored under `storedUnderThisName`, and the old name still reads.

**It reaches the object, not just the file.** `/ckdemo` in chat prints what the config
actually holds. Change something, run it again — no Save needed; Save is for the file.

**The definition mod.** Switch the dropdown to `ConfigKit demo (definition)`. Same controls,
no C#. Its separators carry an explanatory line as well as a title, and it is long enough
that its sections fold too.

## Editing the file by hand

`~/.cairn/packs/configkitdemo/data/ModConfig/` holds both configs. Edit one while the game
is running and it reloads on the spot.

---

Not for release. There is no CakeBuild target and the zips are named `*_dev.zip` so they are
never mistaken for something publishable.
