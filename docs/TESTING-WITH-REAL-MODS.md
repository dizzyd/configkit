# Testing against other authors' mods

The fixture mods in `tests/fixtures/Mods` are written to exercise ConfigKit. Real mods are
not, and that is where the defects have been. `tests/CompatibilityTests.cs` holds the library
against a recorded baseline of ten real configs; the suites below go further and load the
mods themselves, so what is asserted is what the game actually built out of the assets.

A pack is just a directory of zips handed to `run.sh --mods`. The path must be **absolute** —
a relative one resolves against the game install, the mods silently do not load, and the tests
that need them fail for the wrong reason.

```bash
ssh dizzyd@vsclient.home 'cd ~/vstestkit-configkit && bash scripts/run.sh \
    mods/configkit/tests \
    --mod  mods/configkit/configkit \
    --mods $HOME/vstestkit-configkit/mods/configkit/tests/fixtures/Mods \
    --mods $HOME/mods/<pack> \
    --client'
```

Pass the fixtures through as well, or a third of the suite fails for want of them. Remove
`configlib` and `autoconfiglib` from any pack first — ConfigKit stands down when either is
installed, and then every test fails for that one reason.

Each suite skips itself, with the reason, when its pack is not in the run.

---

## `~/mods/skaven` — PlayerModelPatchTests

A server owner reported that a json patch moving the Skaven player model out of its own
character-selection group was silently reverted when ConfigKit was installed. The Skaven mod
declares a ConfigKit config whose patches target
`config/customplayermodels/skaven.json` — the same file PlayerModelLib reads the group from —
so ConfigKit's second pass over that file restored bytes it had snapshotted before the json
patch loader ran. Fixed by rebasing the baseline onto another writer's version rather than
restoring over it.

| zip | why |
|---|---|
| `skaven_1.6.95.zip` | Skaven/Rat Player Model, the mod in the report |
| `playermodellib_1.23.8.zip` | reads the group; the Skaven mod requires it |
| `jsonpatcheslib_1.5.9.zip` | PlayerModelLib requires it |
| `overhaullib_2.0.10.zip` | PlayerModelLib requires it |
| `ckgroupswap.zip` | stands in for the server owner: replaces `/skaven/Group` with `beast` |

`ckgroupswap` is this repository's, at `tests/fixtures/packs/ckgroupswap` — copy the folder
into the pack directory as it is, since a mods directory takes unzipped mods as readily as
zips. It sits under `packs/` rather than `fixtures/Mods` because it is opt-in: it declares a
hard dependency on `vintageskavenrat`, so a run without the Skaven mod disables it and the
suite skips rather than failing.

The four mod zips come from mods.vintagestory.at. Check the versions still resolve before
blaming a failure on ConfigKit — PlayerModelLib's dependencies move.

## `~/mods/ckcompat` — the InsanityLib pack

Thirteen of TheInsanityGod's mods plus the InsanityLib-on-ConfigKit port. Their reports name
config *shapes* — nullable numbers, enum dictionary keys, `[Range]` inside dictionary values —
which the demo pack covers but the real mods exercise differently. A cairn pack of these
turned up an unbounded `[Range]` that killed the client outright with nothing in the log.
