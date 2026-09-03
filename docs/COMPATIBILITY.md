# Which mods work with ConfigKit

A survey of the **500 most-downloaded mods that support Vintage Story 1.22**, taken from
mods.vintagestory.at on 2026-09-03, sorted by what a mod author would have to change to
move from configlib to ConfigKit.

**89 of the 500 use configlib.** Two of those are the libraries themselves, leaving
12,825,451 downloads of ordinary mods riding on it.

**All of them keep loading and playing with ConfigKit in place of configlib.** Every mod that
binds the library guards the call — `IsModEnabled("configlib")`, a type-name lookup, or a
try/catch — so none of them throw when it is not there. What differs is how much of the config
experience survives.

| tier | mods | downloads | what it takes |
|---|---:|---:|---|
| [A](#a--nothing-to-change) | 61 | 9,317,406 | nothing |
| [B](#b--one-or-two-strings) | 2 | 205,618 | one or two strings |
| [C](#c--swap-the-reference-and-rebuild) | 12 | 1,155,977 | swap the reference, rebuild |
| [D](#d--replace-the-imgui-screen) | 12 | 2,146,450 | replace the ImGui screen |

73% of those downloads need no author involvement at all. 26% sit with mods that
need a rebuild to get their in-game settings back.

The four tiers match the four cases in [MIGRATING.md](MIGRATING.md).

---

## A — nothing to change

**61 mods, 9,317,406 downloads.** Content mods that ship a `configlib-patches.json` and no C#
that touches the library.

ConfigKit reads the same patch file, applies the same JSON-path writes to the same assets, and
writes the same `ModConfig/<domain>.yaml`. Players keep their existing settings. No author
action, no new dependency, no rebuild.

| mod | mod id | downloads |
|---|---|---:|
| [BetterRuins](https://mods.vintagestory.at/betterruins) | `betterruins` | 1,134,414 |
| [Primitive Survival](https://mods.vintagestory.at/primitivesurvival) | `primitivesurvival` | 960,027 |
| [Expanded Matter](https://mods.vintagestory.at/em) | `em` | 538,250 |
| [Alchemy](https://mods.vintagestory.at/alchemy) | `alchemy` | 508,021 |
| [Millwright](https://mods.vintagestory.at/millwright) | `millwright` | 501,826 |
| [RP Voice Chat](https://mods.vintagestory.at/rpvoicechat) | `rpvoicechat` | 373,683 |
| [Smithing Plus](https://mods.vintagestory.at/smithingplus) | `smithingplus` | 338,723 |
| [Tailor's Delight](https://mods.vintagestory.at/tailorsdelight) | `tailorsdelight` | 325,551 |
| [Ancient Dungeons (Th3Dungeon)](https://mods.vintagestory.at/thedungeon) | `th3dungeon` | 274,940 |
| [Cartwright´s Caravan](https://mods.vintagestory.at/cartwrightscaravan) | `cartwrightscaravan` | 245,754 |
| [Farseer](https://mods.vintagestory.at/show/mod/3802) | `farseer` | 222,132 |
| [Wool 🙵 More](https://mods.vintagestory.at/wool) | `wool` | 208,849 |
| [Dressmakers](https://mods.vintagestory.at/dressmakers) | `dressmakers` | 197,978 |
| [Translocator Engineering - Redux](https://mods.vintagestory.at/translocatorengineeringredux) | `translocatorengineeringredux` | 186,028 |
| [Fauna of the Stone Age: Rhinocerotidae](https://mods.vintagestory.at/rhinocerotidae) | `rhinocerotidae` | 167,632 |
| [[Legacy] Equus: Wild Horses](https://mods.vintagestory.at/equus) | `equus` | 164,742 |
| [Molds](https://mods.vintagestory.at/molds) | `molds` | 155,326 |
| [Salty & Proto's Temporal Symphony](https://mods.vintagestory.at/temporalsymphony) | `temporalsymphony` | 153,613 |
| [Aldi's Classes](https://mods.vintagestory.at/aldiclasses) | `aldiclasses` | 149,236 |
| [Kobold Player Model Redux](https://mods.vintagestory.at/koboldrdx) | `koboldrdx` | 144,615 |
| [Feverstone Wilds](https://mods.vintagestory.at/feverstonewilds) | `feverstonewilds` | 139,338 |
| [Immersive Fibercraft](https://mods.vintagestory.at/show/mod/5814) | `spinningwheel` | 137,495 |
| [Draconis](https://mods.vintagestory.at/draconis) | `draconis` | 133,153 |
| [Long term food](https://mods.vintagestory.at/longtermfood) | `longtermfood` | 116,877 |
| [KRPG Enchantment](https://mods.vintagestory.at/krpgenchantment) | `krpgenchantment` | 116,212 |
| [Extra Firearms [FORK]](https://mods.vintagestory.at/show/mod/5658) | `mannyextrafirearms` | 115,815 |
| [Buzzwords](https://mods.vintagestory.at/buzzwords) | `buzzwords` | 111,545 |
| [Combat Overhaul Fork](https://mods.vintagestory.at/combatoverhaulforked) | `combatoverhaulfork` | 103,679 |
| [Shipwright](https://mods.vintagestory.at/shipwright) | `shipwright` | 102,830 |
| [Armory Fork](https://mods.vintagestory.at/armoryfork) | `armoryfork` | 83,340 |
| [Skeletons](https://mods.vintagestory.at/skeletons) | `skeletons` | 82,357 |
| [Lupines](https://mods.vintagestory.at/lupines) | `lupines` | 80,129 |
| [Conquest Landform Overhaul](https://mods.vintagestory.at/conquestlandformoverhaul) | `landformoverhaul` | 65,287 |
| [Medieval Architecture](https://mods.vintagestory.at/show/mod/5310) | `medievalarchitecture` | 63,301 |
| [Elk Variants](https://mods.vintagestory.at/elkvariants) | `elkvariants` | 58,219 |
| [Make Tea Forked](https://mods.vintagestory.at/maketeaforked) | `maketeaforked` | 56,917 |
| [Skaven/Rat Player Model](https://mods.vintagestory.at/skavenratplayermodel) | `vintageskavenrat` | 51,524 |
| [Portcullis, Drawbridges and stuff](https://mods.vintagestory.at/show/mod/7736) | `portcullis` | 47,558 |
| [More Banners](https://mods.vintagestory.at/morebanners) | `morebanners` | 45,125 |
| [Ithania Canned Goods](https://mods.vintagestory.at/ithaniacannedgoods) | `ithaniacannedgoods` | 42,279 |
| [Elk Jaunt Integration](https://mods.vintagestory.at/elkjaunt) | `elkjaunt` | 40,685 |
| [Ithania Backpacks](https://mods.vintagestory.at/ithaniabackpacks) | `ithaniabackpacks` | 38,897 |
| [Fluffy Dreg](https://mods.vintagestory.at/fluffydreg) | `fluffydreg` | 37,439 |
| [Arrow Barrels](https://mods.vintagestory.at/arrowbarrels) | `arrowbarrels` | 37,216 |
| [KCs Dragon Player!](https://mods.vintagestory.at/kcsdragons) | `kcsdragons` | 35,380 |
| [Dark Vision](https://mods.vintagestory.at/darkvision) | `darkvision` | 33,462 |
| [K's Cartography Table](https://mods.vintagestory.at/kscartographytable) | `kscartographytable` | 33,233 |
| [Insectoid Player Model](https://mods.vintagestory.at/insectoid) | `insectoid` | 32,471 |
| [Clayworks](https://mods.vintagestory.at/clayworks) | `clayworks` | 31,614 |
| [CompostTweak](https://mods.vintagestory.at/show/mod/3682) | `rlldtco0001` | 31,188 |
| [Metal Leaf](https://mods.vintagestory.at/show/mod/4045) | `metalleaf` | 29,691 |
| [Pegasus](https://mods.vintagestory.at/pegasus) | `pegasus` | 28,034 |
| [Humans](https://mods.vintagestory.at/humans) | `humans` | 27,976 |
| [Orrukin](https://mods.vintagestory.at/orrukin) | `orrukin` | 24,484 |
| [Bark Canoe](https://mods.vintagestory.at/show/mod/5976) | `barkcanoe` | 24,322 |
| [Tree Tap Redux](https://mods.vintagestory.at/treetapredux) | `treetapredux` | 23,006 |
| [Specialized Classes](https://mods.vintagestory.at/specializedclasses) | `specializedclasses` | 22,810 |
| [Crude Building Elements (v1.5.4)](https://mods.vintagestory.at/bradycrudebuilding) | `bradycrudebuilding` | 21,485 |
| [Bixo](https://mods.vintagestory.at/bixo) | `bixo` | 20,936 |
| [Equus: Wild Horses 2.0](https://mods.vintagestory.at/equusferus) | `equusferus` | 19,427 |
| [Monoceros: Ancient unicorns](https://mods.vintagestory.at/monoceros) | `monoceros` | 19,330 |

---

## B — one or two strings

**2 mods, 205,618 downloads.** These reach configlib by name only: a modid check, an
event-bus topic, a reflected type name. No assembly reference.

**Unchanged:** the mod sees no config library and takes its fallback path, usually its own file
edited by hand. Asset patches still apply, because ConfigKit applies them.

**The fix:**

```
"configlib"                          ->  "configkit"
ConfigLib.ConfigLibModSystem         ->  ConfigKit.ConfigKitModSystem
configlib:{id}:setting-changed       ->  configkit:{id}:setting-changed
```

All four event-bus topics are renamed the same way — `setting-changed`, `setting-loaded`,
`config-saved` and `configlib:config-reload`. Member names (`GetConfig`, `SettingChanged`,
`ConfigsLoaded`) are unchanged, so a reflection-based integration needs no other edit.

| mod | mod id | downloads | patches | what it does with configlib |
|---|---|---:|:-:|---|
| [Overhaul lib legacy compatibility](https://mods.vintagestory.at/overhaulliblegacycompat) | `overhaulliblegacycompat` | 162,909 | yes | `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue` — reflection by type name ("ConfigLib.ConfigLibModSystem"); no assembly reference |
| [OptiTime](https://mods.vintagestory.at/optitime) | `optitime` | 42,709 | yes | `IsModEnabled("configlib")`, event bus `configlib:…`, `SettingChanged` — event-bus listener on "configlib:{modid}:setting-changed" |

---

## C — swap the reference and rebuild

**12 mods, 1,155,977 downloads.** These compile against `configlib.dll` and subscribe to
`SettingChanged` to push values onto their own config object.

**Unchanged:** the mod loads, and every setting that only drives an asset patch still works,
because ConfigKit does the patching. Settings the mod reads in C# stop tracking the yaml and
fall back to its own file — BetterEr Prospecting, for instance, keeps
`ModConfig/BetterErProspecting.json` via `LoadModConfig` and only uses configlib to push edits
onto it live.

**The fix:** point the reference at `ConfigKit.dll`, `using ConfigLib` → `using ConfigKit`,
`ConfigLibModSystem` → `ConfigKitModSystem`, rebuild. `GetConfig`, `GetSetting`,
`AssignSettingsValues` and the events keep their names and signatures.

| mod | mod id | downloads | patches | what it does with configlib |
|---|---|---:|:-:|---|
| [Player Model lib](https://mods.vintagestory.at/playermodellib) | `playermodellib` | 470,412 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [BetterEr Prospecting](https://mods.vintagestory.at/bettererprospecting) | `bettererprospecting` | 140,338 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [Wind Chimes](https://mods.vintagestory.at/windchimes) | `windchimes` | 138,839 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [Firearms fork](https://mods.vintagestory.at/firearmsfork) | `firearmsfork` | 96,630 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [Quivers and Sheaths Fork](https://mods.vintagestory.at/quiversfork) | `quiversfork` | 72,519 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [Crossbows Fork](https://mods.vintagestory.at/crossbowsfork) | `crossbowsfork` | 69,853 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [Storage Tweaks](https://mods.vintagestory.at/storagetweaks) | `storagetweaks` | 38,240 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [OrekiWoof's Simple Immersive Beehive](https://mods.vintagestory.at/orekiwoofsbeehives) | `orekiwoofsbeehives` | 35,426 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [Seafarer](https://mods.vintagestory.at/seafarer) | `seafarer` | 26,735 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, references `configlib.dll` |
| [HealthBar ](https://mods.vintagestory.at/healthbar) | `healthbar` | 24,293 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `AssignSettingValue`, references `configlib.dll` |
| [OrekiWoof's Roaming Bees](https://mods.vintagestory.at/roamingbees) | `roamingbees` | 23,236 | yes | `GetModSystem<ConfigLibModSystem>`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |
| [Cannibalism](https://mods.vintagestory.at/show/mod/4528) | `cannibalism` | 19,456 | yes | `GetModSystem<ConfigLibModSystem>`, `IsModEnabled("configlib")`, `SettingChanged`, `GetConfig`, `AssignSettingValue`, references `configlib.dll` |

---

## D — replace the ImGui screen

**12 mods, 2,146,450 downloads.** These call `RegisterCustomConfig` and draw their own settings
window with Dear ImGui.

**Unchanged:** the mod loads and its config still works by editing its own JSON; only the
in-game screen is gone. None of these ship a patch file — configlib is purely their GUI.

**The fix:** `RegisterCustomConfig` has no ConfigKit equivalent, because it hands back an ImGui
drawing callback. Describe the settings instead — a POCO with `[Description]`, `[Range]`,
`[Category]` from `System.ComponentModel`, passed to `RegisterManagedConfig`. Those attributes
are stock .NET, so the settings class keeps no reference to ConfigKit and still compiles without
it. Forty sliders become forty `[Range]` attributes; it is usually less code than the window was.

These twelve are near-identical — the same `ConfigLibCompatibility` class, constructed from
`StartPre` behind an `IsModEnabled` guard. It is one boilerplate fix twelve times, not twelve
problems.

| mod | mod id | downloads | patches | what it does with configlib |
|---|---|---:|:-:|---|
| [Food Shelves](https://mods.vintagestory.at/foodshelves) | `foodshelves` | 600,607 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Blood Trail](https://mods.vintagestory.at/bloodtrail) | `bloodtrail` | 290,900 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Hydrate or Diedrate](https://mods.vintagestory.at/hydrateordiedrate) | `hydrateordiedrate` | 281,346 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Tabletop Games](https://mods.vintagestory.at/tabletopgames) | `tabletopgames` | 230,263 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Dana Tweaks](https://mods.vintagestory.at/danatweaks) | `danatweaks` | 197,589 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Extra Info](https://mods.vintagestory.at/extrainfo) | `extrainfo` | 180,098 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Footprints](https://mods.vintagestory.at/footprints) | `footprints` | 171,486 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Mobs Radar [Finished]](https://mods.vintagestory.at/mobsradar) | `mobsradar` | 59,816 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [BetterProspecting](https://mods.vintagestory.at/betterprospecting) | `betterprospecting` | 50,964 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Forager's Gamble](https://mods.vintagestory.at/foragersgamble) | `foragersgamble` | 33,553 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |
| [Insanity Lib](https://mods.vintagestory.at/insanitylib) | `insanitylib` | 26,834 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, `GetConfig`, references `configlib.dll`, draws ImGui |
| [Thievery](https://mods.vintagestory.at/thievery) | `thievery` | 22,994 | — | `GetModSystem<ConfigLibModSystem>`, `RegisterCustomConfig`, `IsModEnabled("configlib")`, references `configlib.dll`, draws ImGui |

---

## Not applicable

ConfigKit stands down and logs why if it sees either of these enabled, so installing it
alongside them breaks nothing — but only one library manages a player's configs. Auto Config Lib
is a configlib front-end and is the one entry here with no migration path short of its author
porting it.

| mod | mod id | downloads | patches | what it does with configlib |
|---|---|---:|:-:|---|
| [Config lib](https://mods.vintagestory.at/configlib) | `configlib` | 661,266 | — | the library itself |
| [Auto Config Lib](https://mods.vintagestory.at/autoconfiglib) | `autoconfiglib` | 87,188 | — | alternative configlib front-end; ConfigKit stands down when it is installed |

---

## How this was measured

1. Took the 500 most-downloaded mods listing a 1.22 game version (4,252 do), via the ModDB JSON
   API. **ModDB does not expose mod dependencies** — not in the API, not on the mod page — so
   configlib use has to be read out of the zips.
2. Read each zip's central directory over HTTP range requests, no full downloads, to list its
   entries; then pulled `modinfo.json` and every `.dll` as individual zip members. All 323 code
   mods among the 500 were scanned; none failed.
3. Counted configlib tokens in each assembly in **both encodings** — ASCII for metadata (type
   and member names, assembly references) and UTF-16 for the `#US` heap, where C# string
   literals live. A modid check like `IsModEnabled("configlib")` appears *only* in UTF-16.
   Netted out `autoconfiglib`, which contains "configlib" as a substring and otherwise produces
   false positives such as Carry On.
4. Decompiled all 30 flagged assemblies with `ilspycmd` and read the call sites. That is what
   separates a real integration from a mention: CompostTweak's only hit is a log message, and
   RP Voice Chat detects configlib and then calls a `RegisterConfigLibIntegration()` whose body
   is empty. Both are tier A.
5. Validated the classifier against a known answer — a 95-mod local pack whose tiers were
   established by hand during ConfigKit's build. It reproduced all of it: 14 patch-shipping
   mods, 6 assembly-binding, 4 of those on the ImGui callback.

### Scope and limits

- The top 500 by **lifetime** downloads, not all 4,252 1.22 mods. The long tail will hold more
  configlib users at a similar ratio.
- Four entries had no 1.22 release despite the index listing one, and one timed out, so 495 were
  actually inspected.
- Tier A means no configlib coupling was found in any shipped assembly. A mod that reaches the
  library through a dependency of its own would not show up.
- Download counts are lifetime totals across all game versions, not 1.22-only.
