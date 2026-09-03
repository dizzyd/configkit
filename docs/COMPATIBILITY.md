# Which mods work with ConfigKit

A survey of the **2,000 most-downloaded mods that support Vintage Story 1.22**, taken from
mods.vintagestory.at on 3 September 2026, sorted by what a mod author would have to change to
move from configlib to ConfigKit.

**224 of the 2,000 use configlib** — 152 of them, the large majority, with no C# involved
at all. Two of the 224 are the libraries themselves.

**All of them keep loading and playing with ConfigKit in place of configlib.** Every mod that
binds the library guards the call — `IsModEnabled("configlib")`, a type-name lookup, or a
try/catch — so none of them throw when it is not there. What differs is how much of the config
experience survives.

| tier | mods | what it takes |
|---|---:|---|
| [A](#a--nothing-to-change) | 152 | nothing |
| [B](#b--one-or-two-strings) | 25 | one or two strings |
| [C](#c--swap-the-reference-and-rebuild) | 21 | swap the reference, rebuild |
| [D](#d--replace-the-imgui-screen) | 24 | replace the ImGui screen |

The four tiers match the four cases in [MIGRATING.md](MIGRATING.md). Mods are listed
alphabetically within each tier.

---

## A — nothing to change

**152 mods.** Content mods that ship a `configlib-patches.json` and no C#
that touches the library.

ConfigKit reads the same patch file, applies the same JSON-path writes to the same assets, and
writes the same `ModConfig/<domain>.yaml`. Players keep their existing settings. No author
action, no new dependency, no rebuild.

| mod | mod id |
|---|---|
| [[Legacy] Equus: Wild Horses](https://mods.vintagestory.at/equus) | `equus` |
| [Akari's Simple Tweaks](https://mods.vintagestory.at/akarisimpletweaks) | `akarisimpletweaks` |
| [Alchemy](https://mods.vintagestory.at/alchemy) | `alchemy` |
| [Aldi's Classes](https://mods.vintagestory.at/aldiclasses) | `aldiclasses` |
| [Amulet Bed Spawn](https://mods.vintagestory.at/amuletbedspawn) | `amuletbedspawn` |
| [Ancient Dungeons (Th3Dungeon)](https://mods.vintagestory.at/thedungeon) | `th3dungeon` |
| [Anthro Wolf Player Model](https://mods.vintagestory.at/anthrowolfrace) | `anthrowolfrace` |
| [Antique Harmony | Music Additions / Alternative Soundtrack](https://mods.vintagestory.at/antiqueharmony) | `antiqueharmony` |
| [Antique Tuning Cylinders](https://mods.vintagestory.at/antiquecylinders) | `antiquecylinders` |
| [Arctic Survival](https://mods.vintagestory.at/arcticsurvival) | `arcticsurvival` |
| [Armory Fork](https://mods.vintagestory.at/armoryfork) | `armoryfork` |
| [Arrow Barrels](https://mods.vintagestory.at/arrowbarrels) | `arrowbarrels` |
| [Bark Canoe](https://mods.vintagestory.at/show/mod/5976) | `barkcanoe` |
| [Better Moisture Redux](https://mods.vintagestory.at/show/mod/4871) | `bettermoistureredux` |
| [BetterRuins](https://mods.vintagestory.at/betterruins) | `betterruins` |
| [Bigger backpacks](https://mods.vintagestory.at/biggerbackpacks) | `biggerbackpacks` |
| [Birds of Prey Forked](https://mods.vintagestory.at/show/mod/10182) | `lopudromaeosauridae` |
| [Bixo](https://mods.vintagestory.at/bixo) | `bixo` |
| [Blackguard Additions CO fork](https://mods.vintagestory.at/blackguardadditionscofork) | `blackguardadditionscofork` |
| [Boat Speed Tweaks](https://mods.vintagestory.at/boatspeedtweaks) | `boatspeedtweaks` |
| [Buff Berries](https://mods.vintagestory.at/show/mod/8589) | `buffberries` |
| [Buffed Reed/Tule drops](https://mods.vintagestory.at/reedbuff) | `reedbuff` |
| [Buzzwords](https://mods.vintagestory.at/buzzwords) | `buzzwords` |
| [Caplock Firearms](https://mods.vintagestory.at/show/mod/7811) | `mannycaplockfirearms` |
| [Carnivorous Bull Forked](https://mods.vintagestory.at/show/mod/10181) | `lopuabelisauridae` |
| [Cartwright´s Caravan](https://mods.vintagestory.at/cartwrightscaravan) | `cartwrightscaravan` |
| [ChunkLOD](https://mods.vintagestory.at/chunklod) | `chunklod` |
| [Clayworks](https://mods.vintagestory.at/clayworks) | `clayworks` |
| [Combat Overhaul Armor Tweak](https://mods.vintagestory.at/show/mod/8708) | `zzcoarmorpenaltypatch` |
| [Combat Overhaul Fork](https://mods.vintagestory.at/combatoverhaulforked) | `combatoverhaulfork` |
| [CompostTweak](https://mods.vintagestory.at/show/mod/3682) | `rlldtco0001` |
| [Conquest Landform Overhaul](https://mods.vintagestory.at/conquestlandformoverhaul) | `landformoverhaul` |
| [Creature Footsteps](https://mods.vintagestory.at/show/mod/8515) | `creaturefootsteps` |
| [Crude Building Elements (v1.5.4)](https://mods.vintagestory.at/bradycrudebuilding) | `bradycrudebuilding` |
| [Cuniculture](https://mods.vintagestory.at/show/mod/3779) | `caffcuniculture` |
| [Cure Firewood (make aged firewood)](https://mods.vintagestory.at/show/mod/8043) | `curefirewood` |
| [Dampened Anvil & Pulverizer](https://mods.vintagestory.at/dampenedanvil) | `dampenedanvil` |
| [Dark Vision](https://mods.vintagestory.at/darkvision) | `darkvision` |
| [Domed Head Forked](https://mods.vintagestory.at/show/mod/10180) | `lopupachycephalosauria` |
| [Draconis](https://mods.vintagestory.at/draconis) | `draconis` |
| [Draconis: Rhinocroma](https://mods.vintagestory.at/draconisrhinocroma) | `draconisrhinocroma` |
| [Dressmakers](https://mods.vintagestory.at/dressmakers) | `dressmakers` |
| [Dummy Player - Combat Logout Protection](https://mods.vintagestory.at/show/mod/528) | `dummyplayer` |
| [Elk Jaunt Integration](https://mods.vintagestory.at/elkjaunt) | `elkjaunt` |
| [Elk Variants](https://mods.vintagestory.at/elkvariants) | `elkvariants` |
| [Equus: Medieval Horses](https://mods.vintagestory.at/equusdestrier) | `equusdestrier` |
| [Equus: Wild Horses 2.0](https://mods.vintagestory.at/equusferus) | `equusferus` |
| [Exoskeletons Fork](https://mods.vintagestory.at/exoskeletonsfork) | `exoskeletonsfork` |
| [Expanded Matter](https://mods.vintagestory.at/em) | `em` |
| [Extra Firearms [FORK]](https://mods.vintagestory.at/show/mod/5658) | `mannyextrafirearms` |
| [Farmland Drops Soil (Updated)](https://mods.vintagestory.at/soildropsupdated) | `farmlanddropssoilupdated` |
| [Farseer](https://mods.vintagestory.at/show/mod/3802) | `farseer` |
| [Fauna of the Stone Age: Rhinocerotidae](https://mods.vintagestory.at/rhinocerotidae) | `rhinocerotidae` |
| [Fendragon Server Mod Patch](https://mods.vintagestory.at/fendragonsmp) | `fendragonsmp` |
| [Ferguson rifle](https://mods.vintagestory.at/fergusonrifle) | `mannyfergusonrifle` |
| [Feverstone Wilds](https://mods.vintagestory.at/feverstonewilds) | `feverstonewilds` |
| [Fluffy Dreg](https://mods.vintagestory.at/fluffydreg) | `fluffydreg` |
| [Forlorn Additions CO fork](https://mods.vintagestory.at/forlornadditionscofork) | `forlornadditionscofork` |
| [Forlorn Hope armory](https://mods.vintagestory.at/heavyforlonarmor) | `heavyforlonarmor` |
| [Foxxo Player Model](https://mods.vintagestory.at/foxxoplymdl) | `foxxoplymdl` |
| [Fruit Refreshed](https://mods.vintagestory.at/show/mod/8575) | `fruitrefreshed` |
| [Fueled Wearable Lights Fork](https://mods.vintagestory.at/fueledwearablelightsfork) | `fueledwearablelightsfork` |
| [Fused Body Forked](https://mods.vintagestory.at/show/mod/10179) | `lopuankylosauria` |
| [Galeret](https://mods.vintagestory.at/galeret) | `galeret` |
| [Hazmat Suit](https://mods.vintagestory.at/show/mod/7157) | `hazmatsuit` |
| [Healing Springs](https://mods.vintagestory.at/healingsprings) | `healingsprings` |
| [Heatproof Bricks](https://mods.vintagestory.at/heatproofbricks) | `heatproofbricks` |
| [Horned Crown Forked](https://mods.vintagestory.at/show/mod/10178) | `lopuceratopsidae` |
| [Horrible Hands Forked](https://mods.vintagestory.at/show/mod/10177) | `lopuornithomimosauria` |
| [Humans](https://mods.vintagestory.at/humans) | `humans` |
| [Immersive Fibercraft](https://mods.vintagestory.at/show/mod/5814) | `spinningwheel` |
| [Improved Ladders (v1.2.2)](https://mods.vintagestory.at/bradyladders) | `bradyladder` |
| [Insectoid Player Model](https://mods.vintagestory.at/insectoid) | `insectoid` |
| [Ithania Backpacks](https://mods.vintagestory.at/ithaniabackpacks) | `ithaniabackpacks` |
| [Ithania Canned Goods](https://mods.vintagestory.at/ithaniacannedgoods) | `ithaniacannedgoods` |
| [Ithania Expanded Fishing](https://mods.vintagestory.at/ithaniaexpandedfishing) | `ithaniaexpandedfishing` |
| [Jazzberry's Cool Cooking Tweaks](https://mods.vintagestory.at/cookingtweaks) | `cookingtweaks` |
| [K's Cartography Table](https://mods.vintagestory.at/kscartographytable) | `kscartographytable` |
| [KCs Dragon Player!](https://mods.vintagestory.at/kcsdragons) | `kcsdragons` |
| [Kobold Packs](https://mods.vintagestory.at/show/mod/7143) | `koboldpack` |
| [Kobold Player Model Redux](https://mods.vintagestory.at/koboldrdx) | `koboldrdx` |
| [KRPG Enchantment](https://mods.vintagestory.at/krpgenchantment) | `krpgenchantment` |
| [Long Neck Forked](https://mods.vintagestory.at/show/mod/10176) | `lopumacronaria` |
| [Long term food](https://mods.vintagestory.at/longtermfood) | `longtermfood` |
| [Low Light Spawns](https://mods.vintagestory.at/lowlightspawns) | `lowlightspawns` |
| [Lupines](https://mods.vintagestory.at/lupines) | `lupines` |
| [Mad Crow Glider](https://mods.vintagestory.at/show/mod/7449) | `madcrowglider` |
| [Make Tea Forked](https://mods.vintagestory.at/maketeaforked) | `maketeaforked` |
| [Medieval Architecture](https://mods.vintagestory.at/show/mod/5310) | `medievalarchitecture` |
| [Metal Leaf](https://mods.vintagestory.at/show/mod/4045) | `metalleaf` |
| [Millwright](https://mods.vintagestory.at/millwright) | `millwright` |
| [Molds](https://mods.vintagestory.at/molds) | `molds` |
| [Monoceros: Ancient unicorns](https://mods.vintagestory.at/monoceros) | `monoceros` |
| [More Banners](https://mods.vintagestory.at/morebanners) | `morebanners` |
| [More Paintings](https://mods.vintagestory.at/morepaintings) | `morepaintings` |
| [MoreSaltPeter](https://mods.vintagestory.at/show/mod/3683) | `moresaltpeter` |
| [No Waste Bloomery](https://mods.vintagestory.at/show/mod/6265) | `nowastebloomery` |
| [Nymph race](https://mods.vintagestory.at/show/mod/10657) | `nymphrace` |
| [Ocean Tyrant Forked](https://mods.vintagestory.at/show/mod/10175) | `lopumosasauridae` |
| [Oddball Firearms](https://mods.vintagestory.at/show/mod/7542) | `mannyoddballfirearms` |
| [Orrukin](https://mods.vintagestory.at/orrukin) | `orrukin` |
| [P1nks Stack Sizes](https://mods.vintagestory.at/p1nksstacksizes) | `p1nkstacksizes` |
| [Panini Projection](https://mods.vintagestory.at/paniniprojection) | `paniniprojection` |
| [Patches for the Kaev server](https://mods.vintagestory.at/kaevserverpatches) | `kspatches` |
| [Pegasus](https://mods.vintagestory.at/pegasus) | `pegasus` |
| [Plated Back Forked](https://mods.vintagestory.at/show/mod/10174) | `lopustegosauria` |
| [Portcullis, Drawbridges and stuff](https://mods.vintagestory.at/show/mod/7736) | `portcullis` |
| [Primitive Survival](https://mods.vintagestory.at/primitivesurvival) | `primitivesurvival` |
| [Revolver Arquebus Wood Changer](https://mods.vintagestory.at/show/mod/6611) | `revolverarquebuswood` |
| [Role-Playables: Drifters](https://mods.vintagestory.at/rpdrifters) | `rpdrifters` |
| [RP Voice Chat](https://mods.vintagestory.at/rpvoicechat) | `rpvoicechat` |
| [Rust Girls - Player Model](https://mods.vintagestory.at/show/mod/10116) | `playablerustgirls` |
| [Rustbound Magic Easy Mode](https://mods.vintagestory.at/show/mod/3633) | `rustboundmagiceasy` |
| [Sailed Spine Forked](https://mods.vintagestory.at/show/mod/10173) | `lopuspinosauridae` |
| [Salty & Proto's Temporal Symphony](https://mods.vintagestory.at/temporalsymphony) | `temporalsymphony` |
| [Salty & Proto's Temporal Symphony [Fork 1.22][DEPRECATED]](https://mods.vintagestory.at/show/mod/8676) | `temporalsymphonyfork` |
| [Scythe Claws Forked](https://mods.vintagestory.at/show/mod/10172) | `loputherizinosauridae` |
| [Seraph Atelier - Player Model Lib](https://mods.vintagestory.at/seraphatelier) | `seraphatelier` |
| [Sharp Tooth Forked](https://mods.vintagestory.at/show/mod/10170) | `lopucarcharodontosauridae` |
| [Shipwright](https://mods.vintagestory.at/shipwright) | `shipwright` |
| [Shovel Mouth Forked](https://mods.vintagestory.at/show/mod/10169) | `lopuhadrosauroidea` |
| [Simulated Water Runoff](https://mods.vintagestory.at/simulatedwaterrunoff) | `runoffmod` |
| [Skaven/Rat Player Model](https://mods.vintagestory.at/skavenratplayermodel) | `vintageskavenrat` |
| [Skeletons](https://mods.vintagestory.at/skeletons) | `skeletons` |
| [Sleeves's Mega Patch](https://mods.vintagestory.at/slvmegapatch) | `slvmegapatch` |
| [Smithing Plus](https://mods.vintagestory.at/smithingplus) | `smithingplus` |
| [SOA Fantasy Patch](https://mods.vintagestory.at/show/mod/6622) | `aldiclassessoapatch` |
| [Solaria's Solace Server Tweaks](https://mods.vintagestory.at/show/mod/10706) | `solariatweaks` |
| [Specialized Classes](https://mods.vintagestory.at/specializedclasses) | `specializedclasses` |
| [String Sense](https://mods.vintagestory.at/stringsense) | `stringsense` |
| [Tabards Fork](https://mods.vintagestory.at/tabardsfork) | `tabardsfork` |
| [Tailor's Delight](https://mods.vintagestory.at/tailorsdelight) | `tailorsdelight` |
| [Threnkal](https://mods.vintagestory.at/threnkal) | `threnkal` |
| [Tiered Superiority](https://mods.vintagestory.at/tieredsuperiority) | `tieredsuperiority` |
| [TinkerTailor - Backpacks](https://mods.vintagestory.at/show/mod/9773) | `tinkertailorbackpacks` |
| [Toned Down Predators Fork](https://mods.vintagestory.at/show/mod/8612) | `toneddownpredatorsfork` |
| [Tool Animations Fork](https://mods.vintagestory.at/toolanimationsfork) | `toolsanimationsfork` |
| [Translocator Engineering - Redux](https://mods.vintagestory.at/translocatorengineeringredux) | `translocatorengineeringredux` |
| [Tree Shaker](https://mods.vintagestory.at/treeshaker) | `treeshaker` |
| [Tree Tap Redux](https://mods.vintagestory.at/treetapredux) | `treetapredux` |
| [Tyrant King Forked](https://mods.vintagestory.at/show/mod/10168) | `loputyrannosauridae` |
| [Valley of Ashes](https://mods.vintagestory.at/valleyofashes) | `ashes` |
| [Vintage Birbs (Avali Mod)](https://mods.vintagestory.at/show/mod/5275) | `vintagebirbs` |
| [Vintage Goat Player Model](https://mods.vintagestory.at/vintagegoatplayermodel) | `vintagegoat` |
| [Vintage Recipes](https://mods.vintagestory.at/vsrecipes) | `vsrecipes` |
| [Vintage Rift - Client](https://mods.vintagestory.at/vintageriftclient) | `vintagerift` |
| [Volley Pistol](https://mods.vintagestory.at/show/mod/7954) | `mannyvolleypistol` |
| [Water Weather Simulation Redux](https://mods.vintagestory.at/waterweathersimulationredux) | `waterweathersimulationredux` |
| [Wild Farming Fork](https://mods.vintagestory.at/show/mod/10010) | `wildfarmingfork` |
| [Wind Vanes & Randomized Winds](https://mods.vintagestory.at/windvanes) | `windvane` |
| [Wool 🙵 More](https://mods.vintagestory.at/wool) | `wool` |
| [WOO³](https://mods.vintagestory.at/show/mod/8477) | `wooo` |

---

## B — one or two strings

**25 mods.** These reach configlib by name only — a modid check, an event-bus topic, a
reflected type name — and never reference the assembly. It is the most common integration style
after plain content mods, and the cheapest to move.

**Unchanged:** the mod sees no config library and takes its fallback path, usually its own file
edited by hand. Asset patches still apply, because ConfigKit applies them.

**The fix:**

```
"configlib"                          ->  "configkit"
ConfigLib.ConfigLibModSystem         ->  ConfigKit.ConfigKitModSystem
configlib:{id}:setting-changed       ->  configkit:{id}:setting-changed
```

All four event-bus topics rename the same way — `setting-changed`, `setting-loaded`,
`config-saved` and `configlib:config-reload`. **Method names do not change at all**:
`GetConfig`, `GetSetting`, `SettingChanged`, `ConfigsLoaded` and `RegisterCustomManagedConfig`
all resolve, with configlib's signatures, so a reflection-based integration needs nothing
beyond the two strings above.

> `RegisterCustomManagedConfig` is an alias; ConfigKit's own name for it is
> `RegisterManagedConfig`. The alias exists because this survey found four mods — Ad Astra,
> Multi Signpost, Divine Ascension and Weapon Out — looking that method up by name and
> matching on its full parameter list `(string, object, string, Action, Action<string>,
> Action)`, and logging "ConfigLib found but RegisterCustomManagedConfig not available" when
> it is absent. Extra Overlays looks for a four-parameter version that configlib has never
> had, so its managed-config registration is already dead against configlib itself.

| mod | mod id | patches | what it does with configlib |
|---|---|:-:|---|
| [Ad Astra](https://mods.vintagestory.at/adastra) | `adastra` | — | reflection by type name, `RegisterCustomManagedConfig` |
| [Ancestral Bliss Shaders](https://mods.vintagestory.at/ancestralblissshaders) | `ancestralblissshaders` | yes | event bus `configlib:…`, `IsModEnabled("configlib")` |
| [BetterHandbook](https://mods.vintagestory.at/show/mod/8762) | `betterhandbook` | yes | event bus `configlib:…`, `IsModEnabled("configlib")` |
| [Collodion -- Classic Photography](https://mods.vintagestory.at/collodion) | `collodion` | yes | event bus `configlib:…` |
| [Crucible on Forge](https://mods.vintagestory.at/show/mod/10483) | `crucibleonforge` | yes | event bus `configlib:…` |
| [Dead](https://mods.vintagestory.at/dead) | `dead` | yes | event bus `configlib:…`, `IsModEnabled("configlib")` |
| [Dense Ground Storage](https://mods.vintagestory.at/densegroundstorage) | `densegroundstorage` | yes | event bus `configlib:…` |
| [Divine Ascension](https://mods.vintagestory.at/show/mod/6349) | `divineascension` | — | reflection by type name, `RegisterCustomManagedConfig`, `IsModEnabled("configlib")` |
| [Downed](https://mods.vintagestory.at/downed) | `downed` | yes | event bus `configlib:…`, `IsModEnabled("configlib")` |
| [Expanded Beekeeping - Fork](https://mods.vintagestory.at/show/mod/8221) | `expandedbeekeepingfork` | yes | `IsModEnabled("configlib")` |
| [Fast Map](https://mods.vintagestory.at/show/mod/8499) | `fastmap` | yes | event bus `configlib:…` |
| [HoR Patches](https://mods.vintagestory.at/horpatches) | `horpatches` | yes | `IsModEnabled("configlib")` |
| [Multi Signpost](https://mods.vintagestory.at/show/mod/9117) | `multisignpost` | — | reflection by type name, `RegisterCustomManagedConfig` |
| [OptiTime](https://mods.vintagestory.at/optitime) | `optitime` | yes | event bus `configlib:…`, `IsModEnabled("configlib")` — listens on the "configlib:{modid}:setting-changed" event bus |
| [Overhaul lib legacy compatibility](https://mods.vintagestory.at/overhaulliblegacycompat) | `overhaulliblegacycompat` | yes | reflection by type name, `IsModEnabled("configlib")` — reaches the mod system by type name and its members by reflection |
| [Plentiful Tree Felling](https://mods.vintagestory.at/plentifultreefelling) | `plentifultreefelling` | yes | event bus `configlib:…`, `IsModEnabled("configlib")` |
| [Resonator Overhaul ](https://mods.vintagestory.at/show/mod/10009) | `resonatoroverhaul` | yes | event bus `configlib:…` |
| [Seamarks -- Beacons & Lighthouses](https://mods.vintagestory.at/seamarks) | `seamarks` | yes | event bus `configlib:…` |
| [Temporal Convergence](https://mods.vintagestory.at/show/mod/9887) | `temporalconvergence` | yes | event bus `configlib:…` |
| [Temporal Relics](https://mods.vintagestory.at/show/mod/9886) | `temporalrelics` | yes | event bus `configlib:…` |
| [The Gravestones Mod](https://mods.vintagestory.at/gravestones) | `gravestones` | yes | `IsModEnabled("configlib")` |
| [The Hunter](https://mods.vintagestory.at/thehunter) | `thehunter` | yes | `IsModEnabled("configlib")` |
| [Translocator Locator Redux](https://mods.vintagestory.at/translocatorlocatorredux) | `translocatorlocatorredux` | yes | reflection by type name, `IsModEnabled("configlib")` |
| [Vigor](https://mods.vintagestory.at/show/mod/4425) | `vigor` | yes | event bus `configlib:…` |
| [Weapon Out](https://mods.vintagestory.at/show/mod/9710) | `weaponout` | — | reflection by type name, `RegisterCustomManagedConfig`, `IsModEnabled("configlib")` |

---

## C — swap the reference and rebuild

**21 mods.** These compile against `configlib.dll` and subscribe to `SettingChanged` to push
values onto their own config object. Every one of them also ships a `configlib-patches.json`.

**Unchanged:** the mod loads, and every setting that only drives an asset patch still works,
because ConfigKit does the patching. Settings the mod reads in C# stop tracking the yaml and
fall back to its own file — BetterEr Prospecting, for instance, keeps
`ModConfig/BetterErProspecting.json` via `LoadModConfig` and only uses configlib to push edits
onto it live.

**The fix:** point the reference at `ConfigKit.dll`, `using ConfigLib` → `using ConfigKit`,
`ConfigLibModSystem` → `ConfigKitModSystem`, rebuild. `GetConfig`, `GetSetting`,
`AssignSettingsValues` and the events keep their names and signatures.

| mod | mod id | patches | what it does with configlib |
|---|---|:-:|---|
| [Ambient Symphony](https://mods.vintagestory.at/ambientsymphony) | `ambientsymphony` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [BetterEr Prospecting](https://mods.vintagestory.at/bettererprospecting) | `bettererprospecting` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Cannibalism](https://mods.vintagestory.at/show/mod/4528) | `cannibalism` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Chest Preview](https://mods.vintagestory.at/chestpreview) | `chestpreview` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Crossbows Fork](https://mods.vintagestory.at/crossbowsfork) | `crossbowsfork` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Custom Player Model](https://mods.vintagestory.at/customplayermodel) | `customplayermodel` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Firearms fork](https://mods.vintagestory.at/firearmsfork) | `firearmsfork` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Fishing Config](https://mods.vintagestory.at/fishingconfig) | `fishingconfig` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Floating Damage Text!](https://mods.vintagestory.at/show/mod/6093) | `damagenumbers` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Glider Revamp (Drathon's Fork)](https://mods.vintagestory.at/gliderrevampdrathon) | `gliderrevampdrathon` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [HealthBar ](https://mods.vintagestory.at/healthbar) | `healthbar` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [OrekiWoof's Roaming Bees](https://mods.vintagestory.at/roamingbees) | `roamingbees` | yes | references `configlib.dll`, reflection by type name |
| [OrekiWoof's Simple Immersive Beehive](https://mods.vintagestory.at/orekiwoofsbeehives) | `orekiwoofsbeehives` | yes | references `configlib.dll`, reflection by type name, `IsModEnabled("configlib")` |
| [Player Model lib](https://mods.vintagestory.at/playermodellib) | `playermodellib` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Quivers and Sheaths Fork](https://mods.vintagestory.at/quiversfork) | `quiversfork` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Seafarer](https://mods.vintagestory.at/seafarer) | `seafarer` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Sound Physics Adapted](https://mods.vintagestory.at/show/mod/10996) | `soundphysicsadapted` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Storage Tweaks](https://mods.vintagestory.at/storagetweaks) | `storagetweaks` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Visored Helmets Fork](https://mods.vintagestory.at/visorhelmetsfork) | `visorhelmetsfork` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Wind Chimes](https://mods.vintagestory.at/windchimes) | `windchimes` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |
| [Wingworks](https://mods.vintagestory.at/wingworks) | `wingworks` | yes | references `configlib.dll`, `IsModEnabled("configlib")` |

---

## D — replace the ImGui screen

**24 mods.** These call `RegisterCustomConfig` and draw their own settings window with Dear
ImGui. Not one of them ships a patch file — configlib is purely their GUI toolkit.

**Unchanged:** the mod loads and its config still works by editing its own JSON; only the
in-game screen is gone.

**The fix:** `RegisterCustomConfig` has no ConfigKit equivalent, because it hands back an ImGui
drawing callback. Describe the settings instead — a POCO with `[Description]`, `[Range]`,
`[Category]` from `System.ComponentModel`, passed to `RegisterManagedConfig`. Those attributes
are stock .NET, so the settings class keeps no reference to ConfigKit and still compiles without
it. Forty sliders become forty `[Range]` attributes; it is usually less code than the window was.

Most of these are near-identical — the same `ConfigLibCompatibility` class, constructed from
`StartPre` behind an `IsModEnabled` guard — so it is largely one boilerplate fix repeated, not
24 separate problems. Extra Overlays is the one that differs: it reaches configlib entirely
by reflection and registers both a managed config and an ImGui panel.

| mod | mod id | patches | what it does with configlib |
|---|---|:-:|---|
| [AutoSort](https://mods.vintagestory.at/autosort) | `autosort` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [BarrelHud](https://mods.vintagestory.at/show/mod/7504) | `barrelhud` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [BetterProspecting](https://mods.vintagestory.at/betterprospecting) | `betterprospecting` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Blood Trail](https://mods.vintagestory.at/bloodtrail) | `bloodtrail` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Body Fat by Tasshroom](https://mods.vintagestory.at/tasshroombodyfat) | `tasshroombodyfat` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Closed Captions](https://mods.vintagestory.at/closedcaptions) | `closedcaptions` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Dana Tweaks](https://mods.vintagestory.at/danatweaks) | `danatweaks` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Durable Better Prospecting](https://mods.vintagestory.at/durablebetterprospecting) | `durablebetterprospecting` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Extra Info](https://mods.vintagestory.at/extrainfo) | `extrainfo` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Extra Overlays-1.22+](https://mods.vintagestory.at/show/mod/8381) | `extraoverlaysm4` | — | reflection by type name, `RegisterCustomConfig` (ImGui screen), `RegisterCustomManagedConfig`, `IsModEnabled("configlib")` |
| [Food Shelves](https://mods.vintagestory.at/foodshelves) | `foodshelves` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Footprints](https://mods.vintagestory.at/footprints) | `footprints` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Forager's Gamble](https://mods.vintagestory.at/foragersgamble) | `foragersgamble` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Harper's Immersive Tools (Fix For Patch 1.22.3)](https://mods.vintagestory.at/immersivetoolsfix) | `hitfork` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Hydrate or Diedrate](https://mods.vintagestory.at/hydrateordiedrate) | `hydrateordiedrate` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Insanity Lib](https://mods.vintagestory.at/insanitylib) | `insanitylib` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Item Pickup Highlighter](https://mods.vintagestory.at/show/mod/4287) | `itempickuphighlighter` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [minimal compass](https://mods.vintagestory.at/minimalcompass) | `minimalcompass` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Mobs Radar [Finished]](https://mods.vintagestory.at/mobsradar) | `mobsradar` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Projectile Trajectory](https://mods.vintagestory.at/speartrajectory) | `speartrajectory` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Tabletop Games](https://mods.vintagestory.at/tabletopgames) | `tabletopgames` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [TassHardcoreWinter](https://mods.vintagestory.at/tasshardcorewinter) | `tasshroomhardcorewinter` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [TassHunting](https://mods.vintagestory.at/tasshunting) | `tasshunting` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |
| [Thievery](https://mods.vintagestory.at/thievery) | `thievery` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen), `IsModEnabled("configlib")` |

---

## Not applicable

ConfigKit stands down and logs why if it sees either of these enabled, so installing it
alongside them breaks nothing — but only one library manages a player's configs. Auto Config Lib
is a configlib front-end and is the one entry here with no migration path short of its author
porting it.

| mod | mod id | patches | what it does with configlib |
|---|---|:-:|---|
| [Auto Config Lib](https://mods.vintagestory.at/autoconfiglib) | `autoconfiglib` | — | references `configlib.dll`, `RegisterCustomConfig` (ImGui screen) — a configlib front-end; ConfigKit stands down when it is installed |
| [Config lib](https://mods.vintagestory.at/configlib) | `configlib` | — | `RegisterCustomConfig` (ImGui screen), `RegisterCustomManagedConfig`, event bus `configlib:…` — the library itself |

---

## How this was measured

1. Took the 2,000 most-downloaded mods listing a 1.22 game version (4,252 do), via the ModDB JSON
   API. Downloads were only the sampling criterion — they say nothing about a mod's migration
   cost, so they are not reported per mod. **ModDB does not expose mod dependencies** — not in the API, not on the mod page — so
   configlib use has to be read out of the zips.
2. Read each zip's central directory over HTTP range requests, no full downloads, to list its
   entries; then pulled `modinfo.json` and every `.dll` as individual zip members. All 1,170 code
   mods among them were scanned; none failed.
3. Counted configlib tokens in each assembly in **both encodings** — ASCII for metadata (type
   and member names, assembly references) and UTF-16 for the `#US` heap, where C# string
   literals live. A modid check like `IsModEnabled("configlib")` appears *only* in UTF-16.
   Netted out `autoconfiglib`, which contains "configlib" as a substring and otherwise produces
   false positives such as Carry On.
4. Decompiled all 85 assemblies the token scan flagged, with `ilspycmd`, and tiered each mod
   from its actual call sites rather than from the counts. 13 turned out to be mentions
   rather than integrations: CompostTweak's only hit is a log message, Ancient Tools only ever
   names `autoconfiglib`, Solaria's Solace lists "configlib" among safe Harmony tokens, and
   P1nks Stack Sizes parses its own `configlib-patches.json` asset directly without the library.
   RP Voice Chat is the odd one — it detects configlib and then calls a
   `RegisterConfigLibIntegration()` whose body is empty. All are tier A.
5. Validated the classifier against a known answer — a 95-mod local pack whose tiers were
   established by hand during ConfigKit's build. It reproduced all of it: 14 patch-shipping
   mods, 6 assembly-binding, 4 of those on the ImGui callback.

### Scope and limits

- The top 2,000 by lifetime downloads, not all 4,252 1.22 mods. The tail below it will hold
  more configlib users at a similar ratio.
- 7 entries could not be inspected — no 1.22 release despite the index listing one, or a
  fetch that failed — so 1,993 of the 2,000 were actually read.
- Tier A means no configlib coupling was found in any shipped assembly. A mod that reaches the
  library through a dependency of its own would not show up.
