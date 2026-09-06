# Screenshots

Captured on a real client (vsclient.home, hardware GL) with actual mods loaded, not
mock-ups. Regenerate with:

```bash
VSTK_EXTRA_MODS=<configkit build>:<mods with configs> bash scripts/boot.sh --client
bash scripts/vstk cmd "/time set day"
bash scripts/vstk eval --side client "ConfigKit.Gui.ConfigGui.Show()"
bash scripts/vstk shot -o shot.png
```

The mod shown used to be whichever came first alphabetically, so photographing a
particular one meant dropping the others out of the mods directory. Scope a dialog to
one domain instead:

```bash
bash scripts/vstk eval --side client '
var ck = capi.ModLoader.GetModSystem<ConfigKit.ConfigKitModSystem>();
var cfg = ck.GetConfig("fornax") as ConfigKit.Config;
new ConfigKit.Gui.ConfigDialog(capi, new Dictionary<string, ConfigKit.Config> { ["fornax"] = cfg }).TryOpen();
return "ok";'
```

The `ck-*.png` set is different: those come from `tests/ShotsTest.cs`, which builds its own
scenes and writes into `<dataPath>/shots/`.

```bash
VSTK_SHOTS=1 bash scripts/run.sh <tests> --mod <configkit> --client --filter TakeDocumentationShots
```

**Fornax must be 1.6.0 or newer** (cairn pulls 1.7.0 into the demo pack). It hands its config over by reflection against
`ConfigKit.ConfigKitModSystem`, and only 1.6.0 onwards does — 1.5.x still looks for
configlib, so it loads fine and contributes no settings at all, and the window says "No
mods here have settings ConfigKit can edit". Take the zip from ModDB rather than from a
pack's mods directory, which may be pinned to an older release.

A build tree works too, passed the same way as ConfigKit's — its assets are unbuilt, so
they have to come in as an origin:

```bash
VSTK_EXTRA_MODS=<configkit>/bin/Debug/Mods:<fornax>/bin/Debug/Mods \
VSTK_EXTRA_ORIGINS=<fornax>/assets \
    bash scripts/boot.sh --client
```

A slider shows its value to the right of the bar, and every editable row ends in a Reset
button. Fornax is the useful one to check after a GUI change: it mixes sliders, floats and
rangeless number fields, and has more settings than fit, so it exercises the scrolling clip,
the readouts and the reset column at once. BetterRuins is the one that catches the column
spacing, because its four-digit values sit closest to the buttons.

| File | Shows |
|---|---|
| `settings-betterhandbook.png` | Boolean settings with headings, and the scrollbar |
| `settings-betterruins.png` | Sliders with their values, across grouped sections |
| `settings-fornax.png` | Float values beside sliders, and settings with no range as number fields |
