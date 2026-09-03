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

A slider shows its value to the right of the bar. Fornax is the useful one to check after
a GUI change: it mixes sliders, floats and rangeless number fields, and has more settings
than fit, so it exercises both the scrolling clip and the readouts.

| File | Shows |
|---|---|
| `settings-betterhandbook.png` | Boolean settings with headings, and the scrollbar |
| `settings-betterruins.png` | Sliders with their values, across grouped sections |
| `settings-fornax.png` | Float values beside sliders, and settings with no range as number fields |
