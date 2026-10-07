# Lanternvale — Art Key Catalog

Every visual asset is addressed by a **key**. Data files (maps, creatures, classes, companions, items)
reference keys; `Resources/Art/art_manifest.json` maps each key to a PNG and its in-world size. The Unity
`ArtLibrary` loads the PNG, and falls back to a procedural placeholder if a key is missing — so the game
always runs, and real art can be dropped in later **with the same file name**.

## Manifest format (`Resources/Art/art_manifest.json`)

```jsonc
{
  "entries": [
    { "key": "prop_cottage_a", "path": "Art/Props/prop_cottage_a", "height": 6.0, "pivot": [0.5, 0.06],
      "category": "Prop", "loop": false, "shadow": true }
  ]
}
```

* `path`: Resources path without extension, including the `Art/` prefix (e.g. `Art/Props/prop_cottage_a`). PNG with transparency. Folders: `Art/{Backgrounds,Ground,Props,Foreground,Characters,Portraits,Creatures,Effects,Icons,UI}`.
* `height`: height in **world metres** the **whole image** is drawn at (width follows the image aspect
  ratio). For `loop` backgrounds, `height` is the layer height. Icons/UI ignore it. Units with a creature
  definition and no class (enemies, hunter pets, warlock demons, shaman totems and other summons) ignore it
  too: they are drawn with the whole image `size` metres tall, where `size` is the field of their creature
  entry (`Resources/Data/content/creatures.json`, or the `creatures` list in
  `Resources/Data/classes/<class>.json` for pets, demons, totems and other summons) — edit that, not the
  manifest `height`, to resize their art. Their `pivot` still comes from the manifest.
* `pivot`: normalised pivot; props/characters stand on their pivot (feet at y ≈ 0.04–0.08).
* `category`: Background, Ground, Prop, Foreground, Character, Portrait, Creature, Effect, Icon, UI.
* `loop`: background seamlessly tiles horizontally. Ground textures tile in both axes.
* `shadow`: draw a soft ground shadow under it.

Placeholder images should use power-of-two dimensions (Unity-safe); real art may be any size.
Scale reference: a human is 1.8 m tall; a cottage ~6 m; the camera shows ~12 m vertically.

## Environment — background layers (category Background, loop: true, 2048×1024 or 2048×512)

| key | description | height |
|---|---|---|
| `bg_clouds` | soft cumulus band, transparent below | 6 |
| `bg_mountains_far` | distant blue-violet mountains, atmospheric perspective | 9 |
| `bg_hills_far` | watercolour rolling hills, pale green | 7 |
| `bg_hills_near` | nearer hills with tree clumps, richer green | 6 |
| `bg_forest_far` | misty far forest silhouettes | 8 |
| `bg_forest_near` | darker forest edge, trunks and canopy | 8 |
| `bg_shrine_cliffs` | mossy cliffs with old stone stairs and ruins | 9 |
| `bg_village_far` | distant rooftops, windmill and smoke on hills | 6 |

## Ground (category Ground, tiling 1024×1024)

`ground_meadow` (grass with tiny flowers), `ground_forest` (moss, leaf litter), `ground_shrine` (mossy
flagstones), `ground_village` (packed earth and grass). Expansion biomes (`MapDef.biome`, `Game/World/Biomes.cs`; a
biome's ground replaces a placeholder `ground` key): `ground_highlands` (golden grass, poppies and cornflowers),
`ground_fen` (peaty mud, moss cushions, little sky-lit pools, flattened reeds), `ground_snow` (wind-combed snow, blue
shadows, rocks and dry grass poking through), `ground_cave` (packed earth, worn bedrock slabs, grit, damp patches),
`ground_ice` (pale blue ice plates, white crazing, frost), `ground_crypt` (flagstones in running courses, dust, cracks),
`ground_hollow` (dark soil, violet moss, pale roots, petals, glowing spores), `ground_roost` (ash-grey rock, soot
scorch marks, ash drifts, ember flecks).

Terrain authoring notes for the expansion maps (MapTerrain, `Game/World/MapTerrain.*.cs`):
* **Biome looks.** highlands: rolling golden downs, wheat and poppies, round golden trees on the hills; fen: flat
  hummocks with peat pools in the surroundings, reeds and cattails, willows and dead snags, will-o'-wisp motes; peaks:
  snow, snow-capped stones and drifts, ridges of bare rock climbing to snowy pines; roost: ash-grey summit, broken crags,
  soot scorches with a faint ember glow, the land falling away in front. Indoors (cave, ice_cave, crypt, hollow_heart,
  or any map with `environment` cave/crypt): the back wall is two tiers of outlined rock columns (about 4-6 m, then
  9-13 m), the sides are lower rows the camera looks over at yaw ±45°, the front edge drops into a dark pit past a low
  rubble lip; a crypt stands dressed masonry (7.6 m back wall with pilasters and alcoves, 2.4 m side walls) in front of
  the rock. Keep content at least 0.5 m inside the walkable rect at the back and sides of an indoor map.
* **Fill** (`MapDef.fill`): 0.3-0.5 outdoors, 0.4-0.6 indoors (rubble, stalagmites, crystals, bones, roots and glowing
  fungus); cover keeps clear of paths, water, props, exits, spawns, NPCs and chests on its own. Stub maps default to 0.
* **Paths** (`MapDef.paths`): smoothed through their points; a path that ends within 3 m of an edge runs on over the
  margin (3.4 m at an exit), and fades out into a ford's shallows. Width 2-2.6 for roads, 1.4-1.8 for trails.
* **Water** (`MapDef.water`): rivers are smoothed through their points and run on out of the map when they reach an
  edge; ponds are closed polygons with soft shores. A crossing with a bridge prop over it is a bridge (deep bed);
  any other crossing is a ford (a gravel bar with stepping stones). Make a ford's rect cover the river's full width
  plus 1 m on each bank. Banks get reeds (fen) or tall grass (highlands/meadow) from the fill.
* **Exits**: side exits get roads through the hills; back- and front-edge exits (within 3.5 m of y = D or y = 0) get a
  road or, indoors, a passage through the wall (back/sides) or a ramp across the pit (front). Hidden transitions get none.
* **Ambient** (`ambient`): `snow`, `ash` (with a few glowing flecks), `dust` (motes hanging in the air), `drips` (drops
  from the roof with ripples); fen maps get wisp motes automatically; `mist` now scales with the map's depth.

Decals (Prop, flat on ground, no collider):
`decal_path_dirt` (curved dirt path segment 8 m), `decal_path_stone` (flagstone path segment 8 m),
`decal_flowers` (flower patch 3 m), `decal_blight` (grey creeping blight stain 4 m).

## Midground props (category Prop)

| key | description | height |
|---|---|---|
| `prop_cottage_a` | whimsical cottage, thatched roof, round window | 6 |
| `prop_cottage_b` | tall crooked cottage, chimney, flower boxes | 7 |
| `prop_cottage_c` | cottage with blue tile roof and lantern | 6 |
| `prop_inn` | larger two-storey inn "The Sleepy Lantern" | 8 |
| `prop_shop_stall` | market stall with striped awning and goods | 3.2 |
| `prop_smithy` | open smithy with anvil and forge glow | 4.5 |
| `prop_windmill` | small windmill | 9 |
| `prop_well` | stone well with roof | 2.6 |
| `prop_fence` | wooden fence segment 3 m wide | 1.1 |
| `prop_lamp_post` | wooden post with hanging paper lantern | 2.8 |
| `prop_spirit_lantern` | stone shrine lantern (tōrō style), glowing | 2.4 |
| `prop_spirit_lantern_dark` | same, dark and blighted | 2.4 |
| `prop_tree_oak` | round fluffy oak | 7 |
| `prop_tree_pine` | tall pine | 9 |
| `prop_tree_birch` | slender birch | 7 |
| `prop_tree_great` | enormous camphor tree with shimenawa rope | 12 |
| `prop_tree_dead` | blighted grey tree | 6 |
| `prop_bush_a`, `prop_bush_b` | round bushes | 1.4 |
| `prop_rock_large` | mossy boulder | 2.2 |
| `prop_rock_small` | small rocks | 0.8 |
| `prop_stump` | tree stump with mushrooms | 0.9 |
| `prop_log` | fallen mossy log | 1.0 |
| `prop_cart` | wooden cart | 2.0 |
| `prop_barrel`, `prop_crate`, `prop_hay` | village clutter | 1.0–1.3 |
| `prop_signpost` | wooden signpost | 2.0 |
| `prop_noticeboard` | quest notice board | 2.4 |
| `prop_bench` | wooden bench | 0.9 |
| `prop_campfire` | campfire with stones | 1.0 |
| `prop_tent` | travel tent | 2.4 |
| `prop_chest` / `prop_chest_open` | treasure chest closed/open | 0.9 |
| `prop_mushrooms` | cluster of glowing mushrooms | 0.7 |
| `prop_ruin_pillar` | broken mossy pillar | 3.5 |
| `prop_ruin_arch` | ruined stone arch | 5 |
| `prop_shrine_gate` | vermilion shrine gate (torii-like) | 6 |
| `prop_spirit_statue` | small fox/deer spirit statue | 1.8 |
| `prop_blight_crystal` | grey-violet blight crystal growth | 2.0 |
| `prop_banner` | cloth banner on a pole | 3.0 |
| `prop_bridge` | small wooden footbridge | 2.0 |

## Foreground (category Foreground, drawn in front with extra parallax)

`fg_stones_a`, `fg_stones_b` (mossy stones, 1.2 m), `fg_grass_a`, `fg_grass_b` (soft grass tufts, 1.0 m),
`fg_flowers_a`, `fg_flowers_b` (vibrant wildflowers, 1.0 m), `fg_ferns` (1.3 m).

## Characters (category Character, height 1.8, pivot [0.5, 0.05]; facing the camera, slight 3/4)

Final Fantasy X–inspired designs (layered fabrics, belts, asymmetry, ornate trims, expressive hair).

| key | design |
|---|---|
| `char_warrior` | heavy red coat over plate, greatsword on shoulder |
| `char_hunter` | islander athlete, leather and fur, bow, beaded hair |
| `char_paladin` | bright tabard over mail/plate, warhammer and kite shield, sun motif |
| `char_mage` | long dark dress of belts, fur-trimmed, staff with orb |
| `char_priest` | summoner-like kimono and hakama, staff with rings |
| `char_rogue` | goggles, scarf, short shorts, twin daggers, playful |
| `char_warlock` | elegant long coat, high collar, violet runes, grimoire |
| `char_shaman` | tall beast-folk (ram horns, fur), spear, totem charms |

Companions (same specs): `comp_kael` (Warrior), `comp_lys` (Mage), `comp_seren` (Priest), `comp_rook`
(Hunter), `comp_pip` (Rogue), `comp_torvan` (Shaman), `comp_aldric` (Paladin), `comp_morwen` (Warlock).

Portraits (category Portrait, 256×256, bust): `portrait_<same suffix>` for every char_/comp_/npc_ key,
e.g. `portrait_warrior`, `portrait_kael`, `portrait_elder`.

NPCs: `npc_elder`, `npc_innkeeper`, `npc_merchant`, `npc_smith`, `npc_villager_a`, `npc_villager_b`,
`npc_child`, `npc_guard`, `npc_trainer` (generic robed trainer), `npc_spirit` (friendly forest spirit, kodama-like).

The four class trainers have their own 3D models (see "3D-only model keys" at the end); their 2D portrait stays
`portrait_trainer`.

## Creatures (category Creature)

The heights below are the `size` values (whole-image height in metres; see `height` above) of the
creature entries with the same id in `creatures.json`. They, not the manifest `height`, set how tall these
sprites are drawn as units. Entries that share a sprite can differ (the creature *cr_wolf_packmate* draws
`cr_wolf` at 1.0 m). The creature *cr_greymane* has its own 3D model (see "3D-only model keys" at the end), drawn at
1.5 m; its portrait key is `cr_wolf_blighted`, the 2D art the UI shows for it.

| key | description | height |
|---|---|---|
| `cr_wolf` | grey wolf | 1.1 |
| `cr_wolf_blighted` | wolf with grey blight patches, violet eyes | 1.2 |
| `cr_boar` | bristly boar | 1.0 |
| `cr_spider` | big forest spider | 0.9 |
| `cr_mossling` | small mischievous moss creature with a leaf hat | 0.9 |
| `cr_mossling_shaman` | mossling with a twig staff and bead necklace | 1.0 |
| `cr_bandit` | bandit cutthroat with scarf mask | 1.8 |
| `cr_bandit_archer` | bandit with shortbow | 1.8 |
| `cr_bandit_hexer` | bandit spellcaster with charms | 1.8 |
| `cr_bandit_chief` | burly bandit leader | 2.0 |
| `cr_hollow_wisp` | floating grey-violet wisp | 1.0 |
| `cr_hollow_spirit` | humanoid hollowed spirit in tattered robes | 1.9 |
| `cr_hollow_treant` | small blighted treant | 2.6 |
| `cr_hollow_warden` | boss: enormous corrupted stag spirit with lantern antlers | 4.5 |
| `cr_training_dummy` | straw training dummy | 1.6 |

Pets/demons/totems: `pet_wolf`, `pet_cat`, `pet_boar`, `pet_bear`, `pet_owl`, `demon_imp`, `demon_voidwalker`,
`demon_succubus`, `demon_felhunter`, `demon_infernal` (hulking burning stone golem, 2.6 m), `totem_earth`, `totem_fire`, `totem_water`, `totem_air` (totems 1.2 m).
These are drawn at the `size` of their entries in the class files (`hunter.json`, `warlock.json`, `shaman.json`).

## Effects (category Effect)

`fx_glow` (soft radial white), `fx_spark`, `fx_ring` (thin ring), `fx_bolt` (generic glowing projectile),
`fx_arrow`, `fx_slash` (arc), `fx_heal` (rising plus sparkles), `fx_smoke`, `fx_leaf`, `fx_firefly`,
`fx_shadow` (soft ellipse ground shadow), `fx_target_ring` (selection ellipse), `fx_rune_circle`.
Effects are white/greyscale so they can be tinted by school colour.

## Icons (category Icon, 128×128, white silhouette on transparent)

`glyph_<name>` for every glyph in Docs/DataSchema.md → "Icon glyphs". The UI composes the glyph on a
school-coloured frame. Class crests: `crest_<class>` (warrior, hunter, …).

## UI (category UI)

`ui_parchment` (tileable warm paper texture 512×512), `ui_vignette`, `logo_lanternvale` (title logo).

## 3D-only model keys (no 2D art)

Sprite keys in the data that only name a 3D model (`UnitRecipes`, see `Docs/ThreeD.md` §5): they have no entry in
the manifest and no painter, so the UI shows the portrait key instead. `Tools/artgen` stops reading this file at this
heading; the content checks (`Tools/datagen/world_content/wn_check.py`) accept them as sprite keys.

| key | model | used by |
|---|---|---|
| `npc_trainer_warrior` | Sir Odo: an old knight in plate and a crimson tabard, a grand handlebar moustache, sword in hand, kite shield on his back (also `npc_trainer_paladin`) | trainer_odo |
| `npc_trainer_hunter` | Fennel: a green-clad ranger with a feathered cap, a braided beard, a fur collar, bow and quiver (also `npc_trainer_shaman`) | trainer_fennel |
| `npc_trainer_mage` | Magister Quillon: a tall scholar in starry indigo with a tall bent hat, spectacles, a long white beard and an orb staff (also `npc_trainer_warlock`) | trainer_quillon |
| `npc_trainer_priest` | Brother Wick: a round, tonsured friar in undyed wool with a gold stole, a rosary and a lit lantern (also `npc_trainer_rogue`) | trainer_wick |
| `cr_hollow_keeper` | Keeper Ishiro, hollowed: the Old Shrine's last keeper (portrait key `cr_hollow_spirit`) | cr_hollow_keeper, keeper_ishiro |
| `cr_wolf_greymane` | Greymane, the elite alpha: bigger and heavier, charcoal coat, shaggy silver mane, glowing violet eyes, scars, torn ear, big glowing spine crystals (also the creature id `cr_greymane`) | cr_greymane |
| `cr_hawk` | *models-c.* Amberfield hawk: chestnut bird of prey soaring at head height (flier, no legs on the ground), barred cream chest, yellow cere, hooked beak, fingered wings, rufous fan tail. Natural 0.85 m. **size 1.0**; portrait `pet_owl` | amberfield hawks |
| `cr_harpy` | *models-c.* Skyreach harpy: hovering bird-woman, storm-blue plumage, big feathered back wings, coral feather crest, white war paint, taloned hands, dangling bird legs. Natural 1.7 m. **size 1.8**; portrait `demon_succubus` | skyreach harpies |
| `cr_crocolisk` | *models-c.* Mirefen crocolisk: low mossy-green croc with scutes, a long toothy snout and a lily pad (pink flower) on its head; long flat 3-bone tail. Natural 0.72 m tall, ≈ 3.4 m long at size 1. **size 0.9** (elite 1.1); portrait `cr_boar` | mirefen crocolisks |
| `cr_wolf_frost` | *models-c.* Frost wolf: snow-white wolf, blue-grey saddle, frosty ruff, tufted ears, pale glowing eyes, ice crystals on shoulders and spine. Natural 1.05 m. **size 1.2** (pack alpha 1.5); portrait `cr_wolf` | skyreach wolves |
| `cr_yeti` | *models-c.* Yeti: huge shaggy white ape, blue face, curled ram horns, tusks, long arms with clawed hands (Slam, Throw = snowball). Natural 2.4 m. **size 2.8**; portrait `pet_bear` | skyreach yetis |
| `cr_rootling` | *models-c.* Rootling: little walking root tuber, glowing amber eyes, a leaf sprout with a glowing bud, forked root limbs (Headbutt). Natural 0.8 m. **size 0.9**; portrait `cr_mossling` | Root Hollows, Hollow Heart trash |
| `cr_blight_hound` | *models-c.* Blight hound: gaunt violet-charcoal hound, bone skull-mask with glowing eye holes, exposed ribs, ragged mane, blight crystals on the shoulders and tail tip. Natural 1.2 m. **size 1.4** (elite 1.6); portrait `cr_wolf_blighted` | Hollow Heart trash, mirefen |
| `cr_spider_giant` | *models-c.* Mossdeep giant spider: teal-black, moss on the back, glowing sea-green spots, banded bristly legs, pale fangs. Natural 1.25 m, leg span ≈ 3.6 m. **size 1.5**; portrait `cr_spider` | Mossdeep |
| `cr_rootwarden` | *models-c.* The Rootwarden: hunched 3 m root guardian, honey-gold lantern heart in a cage of root ribs, pale shrine mask with vermilion marks and glowing eye slits, root antlers with two paper lanterns, mossy mushroom burls, long root-claw arms (Slam). **size 3.4**; portrait `cr_hollow_treant` (also key `cr_dg1_rootwarden`) | cr_dg1_rootwarden |
| `cr_drake_whelp` | *models-c.* Drake whelp: chubby big-eyed red-orange baby dragon that **hovers** (FloatHeight 0.55 m) on quick wing beats, nub horns, cream belly (Bite, Howl = breath). Natural 1.0 m. **size 1.2**; portrait `demon_imp` | skyreach, roost trash |
| `cr_frost_drake` | *models-c.* Frost drake: ice-blue winged drake, crystal horns and spines, glowing eyes; wings folded at rest, thrown open in bites and roars. Natural 2.4 m (≈ 5.5 m long). **size 3.0** (elite 3.4); portrait `demon_imp` | skyreach drakes |
| `cr_r1_thornmaw` | *models-c.* Thornmaw (raid 1, boss 1): giant beast of tangled root wood, root bands, a bramble of black thorns and blight crystals, corrupted mushrooms, a huge glowing thorn-toothed maw, four violet eyes, trunk legs splaying into roots (Bite). Natural 2.8 m. **size 6.0**; portrait `cr_hollow_treant` | cr_r1_thornmaw |
| `cr_r1_hollow_heart` | *models-c.* The Hollow Heart (raid 1, final): a huge beating rose-violet heart (golden lantern core, glowing veins) in a cage of root ribs under the ring of an old lantern frame with paper charms, rooted by six thick root tendrils plus two slender grasping arms that lash in attacks. Spider rig, **static** (faces the camera, never walks), heart pulses (`HeartBeat`). Natural 4.2 m. **size 7.5–8**; portrait `cr_hollow_wisp` | cr_r1_hollow_heart |
| `cr_r2_frostclaw` | *models-c.* Frostclaw, the yeti matriarch (raid 2, boss 1): broader yeti, frost-blue fur tips, ice-crystal crown, gold-beaded braids, necklace of ice charms, glowing ice eyes, icicle claws. Natural 2.8 m. **size 5.0**; portrait `pet_bear` | cr_r2_frostclaw |
| `cr_r2_cinder_drake` | *models-c.* Cinder drake (raid 2, boss 2 base): charcoal winged drake split by ember seams, smoking ember nostrils, horn crown, torn wings (Swipe: rears with wings thrown open). Natural 2.4 m. **size 4.2**. Tinted twins: **`cr_r2_cinder_drake_emberjaw`** (= `cr_r2_emberjaw`, red-orange, molten jaw) and **`cr_r2_cinder_drake_ashtongue`** (= `cr_r2_ashtongue`, ash-violet, pale violet fire); portrait `demon_imp` | cr_r2_emberjaw, cr_r2_ashtongue |
| `cr_r2_vyrmathra` | *models-c.* Vyrmathra, the Ashwyrm (raid 2, final): great ash-black dragon, molten seams on flanks and throat, a furnace glowing between the belly plates, crown of bone-gold horns, molten eyes, ember-tipped double dorsal plates, a long tail with a glowing spade blade, vast tattered 5-finger wings with ember undersides (Stomp: rears and slams with wings open; Howl = breath with wings spread). Natural 4.3 m (≈ 9 m nose to tail). **size 7.5** (fits CameraRig max zoom 10.4 with margin); portrait `demon_infernal` | cr_r2_vyrmathra |

All four trainers share the trainers' gold sash with a lantern medallion.

*models-c* keys live in `Units/UnitRecipes.CreaturesC.cs`. The drakes and the Ashwyrm use the winged-quad rig
extension (`QB.WingL/R`, `WingL2/R2`, `Tail3`; `QuadKit.Wings`, `QuadKit.LongTail`; `UnitModel.WingFold`, `WingBeat`,
`LongTail`): wings fold over the back at rest, spread in roars, casts, rearing attacks and dodges, and beat while a
quad hovers (`FloatHeight` > 0). The data `size` is the height to the top of the head/horns; the Core collision
radius is `clamp(size × 0.22, 0.3, 1.6)`, so the 7.5 m bosses get 1.6 m although their bodies are ≈ 4 m long.

3D-only props (`PropModels`, `World/Props/PropGarden.cs`; the village garden by the cottages): `prop_stone_wall`
(dry-stone wall), `prop_veg_patch` (vegetable bed with a watering can), `prop_washing_line` (laundry on a line, sways).
