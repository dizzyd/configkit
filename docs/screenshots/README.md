# Screenshots

Captured on a real client (vsclient.home, hardware GL) with actual mods loaded, not
mock-ups. Regenerate with:

```bash
VSTK_EXTRA_MODS=<configkit build>:<mods with configs> bash scripts/boot.sh --client
bash scripts/vstk cmd "/time set day"
bash scripts/vstk eval --side client "ConfigKit.Gui.ConfigGui.Show()"
bash scripts/vstk shot -o shot.png
```

The mod shown is whichever comes first alphabetically by display name; drop that mod
from the mods directory to photograph the next one.

**Fornax has to come from a build, not from its ModDB zip.** It hands its config over by
reflection against `ConfigKit.ConfigKitModSystem`, which only releases from 1.6.0 do — an
older zip loads fine and contributes no settings at all, and the window then says "No mods
here have settings ConfigKit can edit". Pass it the same way as ConfigKit:

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
