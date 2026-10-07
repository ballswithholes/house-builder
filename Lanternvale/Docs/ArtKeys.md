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
| `cr_gnoll` | *models-a.* Gnoll brute: hunched hyena-folk leaning forward from the hips, taupe fur with chocolate spots, long dark muzzle with a toothy grin, big round dark-rimmed ears, bristly dark mane with rust tips down the humped back, bushy tail, brick-red rags, notched cleaver (Mace strike, Throw). Natural 1.95 m. **size 2.0** (collision r 0.44); portrait `cr_bandit` | amberfield gnolls |
| `cr_gnoll_archer` | *models-a.* Gnoll archer: as the brute with green rags, a green bandana with tails and feathers, a strung shortbow (Bow), quiver; claws in melee. Natural 1.95 m. **size 1.95** (r 0.43); portrait `cr_bandit_archer` | amberfield gnolls |
| `cr_gnoll_mystic` | *models-a.* Gnoll mystic: leaner, purple rags over a fringed hide skirt, white face paint, feathers, a fang necklace, bone fetishes on the belt, a horned-skull staff hung with fetishes (teal glow at the eyes; cast anchor) and a teal flame in the off hand. Natural 1.9 m. **size 1.9** (r 0.42); portrait `cr_bandit_hexer` | amberfield gnolls |
| `cr_gnoll_chief` | *models-a.* Gnoll chief (elite): bigger and heavier, darker fur, a bone crown of fangs with a glowing red stone, golden glowing eyes, a dark fur mantle, bone-spiked pauldrons, a skull on the belt, a war banner on his back (red hide with a white jaw emblem; top ≈ 0.4 m above the head) and a great cleaver carried on the shoulder (two-handed Mace). Natural 2.35 m. **size 2.5** (r 0.55); portrait `cr_bandit_chief` | amberfield warcamp boss / elite |
| `cr_tunneler` | *models-a.* Mudpaw tunneler: stout mole-folk, velvet cocoa fur, a pink star nose on a long snout, bead eyes, whiskers, big pink digging paws with cream claws, patched denim-blue overalls, boots, a dented brass miner's helmet with a lit candle (emissive flame, top 1.5 m at size 1.3), a pick (Mace strike, Throw). Natural 1.3 m. **size 1.3** (r 0.3) | amberfield quarry |
| `cr_tunneler_geomancer` | *models-a.* Tunneler geomancer: the mole in a moss-green robe and moss shawl, a red knitted cap with a pompom, round brass spectacles, a root staff crowned with glowing amber crystals (cast anchor), pebbles floating over an amber spark in the off paw. Natural 1.35 m. **size 1.35** (r 0.3); portrait `cr_mossling_shaman` | amberfield quarry |
| `cr_mireling` | *models-a.* Mireling: round frog-folk, head and body one ball, pond-teal skin with darker spots, cream belly, wide grin, big golden bulging eyes with bar pupils, a coral fin crest from brow to back, cheek fins, webbed hands and feet, kelp loincloth; hops (Small gait). Shell pauldron and a barnacled driftwood club (Mace). Natural 1.1 m. **size 1.1** (r 0.3); portrait `cr_mossling` | mirefen |
| `cr_mireling_hunter` | *models-a.* Mireling hunter: reed headband with a feather, a creel on the back, a bone-tipped spear (Spear, Throw). Natural 1.15 m. **size 1.15** (r 0.3); portrait `cr_mossling` | mirefen |
| `cr_mireling_oracle` | *models-a.* Mireling oracle: violet fin crest, kelp shawl, pearl necklace, a staff topped by a big conch cradling a glowing pearl (cast anchor). Natural 1.2 m. **size 1.2** (r 0.3); portrait `cr_mossling_shaman` | mirefen |
| `cr_mire_hag` | *models-a.* Mire hag (Mother Mire's coven): crooked bog witch bent far forward, grey-green skin, long hooked nose with a wart, pointed chin, stringy moss-green hair, droopy wide-brimmed hat with a little frog on the brim, a cloak of dry reeds over a ragged plum dress, a gnarled crook (Staff) and a lantern of soft lime marsh-light in the other hand (Carry; the cast anchor; emissive, no light of its own). Natural 1.85 m (hat top ≈ 2.1 m). **size 1.9** (r 0.42; elite 2.1); portrait `cr_bandit_hexer` | mirefen coven |
| `cr_ogre` | *models-a.* Skyreach ogre: 3 m, huge round belly and shoulders, lilac-blue skin, heavy brow, big nose, underbite with tusks, black topknot, a brown bear pelt with the bear's head on one shoulder, hide loincloth, rope wraps, a big studded club (Mace, Throw); Heavy gait. Natural 3.0 m. **size 3.0** (r 0.66); portrait `cr_bandit_chief` | skyreach ogres |
| `cr_ogre_mage` | *models-a.* Ogre mage: the ogre in plum robes with gold trim and a gold-edged front panel, a gold sash, wide sleeves, a cream turban with a ruby and a blue plume, gold earrings, a staff with a big glowing ice-blue orb (cast anchor) and a blue flame in the off hand. Natural 3.0 m. **size 3.0** (r 0.66); portrait `cr_bandit_hexer` | skyreach ogres |
| `cr_dragonsworn` | *models-a.* Dragonsworn cultist: crimson-and-bronze scale mail in rows, a horned bronze helm with a dark visor and glowing ember eye slits and a crest of scales, a big dragon-scale pauldron, an ash-black cape with an ember hem, a black tabard with a burning eye, a greatsword with a glowing coal-red fuller (Greatsword, two-handed, Shoulder hold; Point cast). Natural 1.95 m. **size 1.95** (r 0.43; elite 2.2); portrait `cr_bandit` | skyreach, the roost |
| `cr_mossling_king` | *models-a.* The Mossling King (boss): a giant mossling, round mossy body with tiny flowers, big shiny eyes under frowning brows, a toothy grin, a beard of hanging grey moss, a crown of red spotted mushrooms around one big softly glowing golden cap (emission 0.35, reads in dark caves), a cape of autumn leaves, a twig sceptre with a glowing seed bud (cast anchor); Heavy gait. Natural 2.4 m. **size 2.6–3.0** (r 0.57–0.66); portrait `cr_mossling_shaman` | Mossdeep boss |
| `prop_cave_mouth` | *props-dungeon.* Cave entrance in a rock outcrop: mossy shouldering boulders, a ring of paler frame stones round a dark arched mouth (its own unoutlined "Opening" part), ivy and roots over the brow, a straw rope with paper charms (buckets 0, 2), a sapling on top (1, 3). Pivot = threshold, the hill reaches ≈ 4 m back. **Collider 6.0 × 1.8**; no lights. Also the scene's `cave` transition marker | dungeon entrances |
| `prop_cave_mouth_snow` | *props-dungeon.* The same outcrop for Skyreach: snow caps instead of moss, icicles instead of ivy, dry grass. **Collider 6.0 × 1.8** | skyreach cave marker (scene request) |
| `prop_stalagmite` | *props-dungeon.* Banded flowstone stalagmite (≈ 2 m) with two or three sister spires and glow-moss specks at the foot. **Collider 1.2 × 0.8** | caves |
| `prop_crystal_cluster` | *props-dungeon.* Cluster of eight glowing crystals on a dark rock, pale aqua-white so the data `tint` colours it (violet `#d8c4ff`, rose `#ffc0e0`, ice `#cfefff`). **Collider 1.4 × 0.9**; light anchor (0, 0.9): e.g. `{"color":"#9ff3e4","radius":4.5,"intensity":0.9,"offset":[0,0.9]}` | caves, Hollow Heart, Frozen Sanctum |
| `prop_glow_mushroom` | *props-dungeon.* Four or five glowing sea-green toadstools (1.1 m tallest) with lit gills on a mossy hump; tintable. **Collider 1.4 × 0.9**; light (0, 0.95), `#9ff3e4`, radius 4, intensity 0.8 | caves |
| `prop_root_column` | *props-dungeon.* Stone pillar (≈ 5 m) with a broken mossy crown, strangled by four of Old Kusu's great roots that spill over the crown, hug the shaft and splay across the floor; hanging rootlets, a straw rope with paper charms (not bucket 3), glow mushrooms in the root crooks. **Collider 2.0 × 1.3** (the crown overhangs) | Root Hollows, Hollow Heart |
| `prop_cave_wall` | *props-dungeon.* Free-standing rock rib: five big weathered boulders shouldering each other (ends lower so pieces overlap) over a solid back, mossy tops, rubble and glow-moss specks at the foot. ≈ 3.4 m. **Collider 4.4 × 1.3** | cave partitions |
| `prop_bones` | *props-dungeon.* Skull, ribcage and long bones in a dust patch, a rusted sword (buckets 0, 2) or a cracked shield. **No collider** (walkable clutter) | dungeons |
| `prop_rubble` | *props-dungeon.* Heap of cut crypt blocks, a fallen column drum and chips. **Collider 1.8 × 1.0** | crypts, vaults |
| `prop_brazier` | *props-dungeon.* Iron tripod brazier with a brass rim and lantern emblems, glowing coals and flames (`SetLit`); its key gets AmbientParticles embers. **Collider 0.9 × 0.7**; light (0, 1.35): `{"color":"#ffb062","radius":5,"intensity":1.0,"offset":[0,1.35],"flicker":true}` (no `nightOnly`: always burns) | crypts, roost |
| `prop_torch_sconce` | *props-dungeon.* Torch in an iron bracket on a short square stone post (2.4 m) with a carved lantern emblem; lit wrap and flame (`SetLit`). **Collider 0.6 × 0.5**; light (0, 2.3) (anchor 0.36 m in front of the post), `#ffb46c`, radius 4, intensity 0.9, flicker | crypts, caves |
| `prop_coffin` | *props-dungeon.* Six-sided wooden coffin along X with iron bands and a brass plaque, lid shut or pushed ajar (buckets 1, 3), a dried posy and a candle stub. **Collider 2.3 × 0.9** | crypts |
| `prop_sarcophagus` | *props-dungeon.* Stone sarcophagus with three carved lantern arches and a knight effigy (cushion, folded hands on a sword, hound at his feet), candles and flowers. **Collider 2.8 × 1.4** | crypts, Barrow |
| `prop_crypt_pillar` | *props-dungeon.* Octagonal crypt column (4.4 m): plinth, drums with a crack, a lantern niche with a candle, carved capital and abacus. **Collider 1.1 × 0.9** | crypts, Drowned Vault |
| `prop_crypt_door` | *props-dungeon.* Free-standing crypt doorway set into a grassy barrow mound: pilasters, voussoir arch, pediment with a lantern emblem, iron-bound doors ajar over the dark (own "Opening" part), steps, two little lamps lit at night. Pivot = threshold, mound ≈ 5 m back. **Collider 4.4 × 2.4**; lamps at (±1.25, 1.05), nightOnly, `#ffb46c`, radius 3. Also the scene's `door` marker | Barrow entrance |
| `prop_crypt_door_golden` | *props-dungeon.* The same doorway with the barrow in golden downland turf (Amberfield). **Collider 4.4 × 2.4** | amberfield door marker (scene request) |
| `prop_stairs_down` | *props-dungeon.* Stair going down into the dark through a raised stone platform, a small gabled hood over the bottom, two lantern posts lit at night. **Collider 4.0 × 2.6** (or none as a walk-in); lamps (±1.04, 1.16), nightOnly. Also the scene's `stairs` marker | shrine, mirefen |
| `prop_ice_pillar` | *props-dungeon.* Tall hexagonal ice crystal (≈ 4.4 m) with frost bands, leaning sister crystals and a snow drift. **Collider 1.8 × 1.2**; light (0, 1.6), `#bfe8ff`, radius 4, intensity 0.7 | Frozen Sanctum |
| `prop_frozen_statue` | *props-dungeon.* Stone knight on a pedestal, hands on a great sword, caught in ice shards with snow on helm and shoulders. **Collider 1.4 × 1.0** | Frozen Sanctum |
| `prop_drowned_arch` | *props-dungeon.* Wet stone arch (5.2 m) of block piers and voussoirs, algae at the waterline, barnacles and shells, swaying kelp, glassy drips. **No collider** (walk-through; the feet stand at x = ±1.75) | Drowned Vault |
| `prop_treasure_pile` | *props-dungeon.* Heap of gold coins (coins laid over its surface) with an open casket, goblet, crown, gems and a sword, two soft glints. **Collider 2.0 × 1.1**; light (0, 0.6), `#ffd88a`, radius 3.5, intensity 0.7 | vaults, roost |
| `prop_raid_portal` | *props-dungeon.* Spirit moon gate: a ring of carved stones with glowing runes and gold-capped keystones on a stepped plinth, a straw rope with charms, two lantern pillars; a soft violet-teal veil turns inside (own two-sided "Swirl" part, `PropMotion` spin). **Collider 6.4 × 1.3**; light (0, 2.25), `#c9a8ff`, radius 6, intensity 1.1. Also the scene's `portal` marker | mirefen → Hollow Heart |
| `prop_raid_portal_ember` | *props-dungeon.* The roost's gate: soot-dark basalt stones, ember runes and lanterns, a fire-gold veil, ash instead of moss. **Collider 6.4 × 1.3**; light `#ff9a4a` | skyreach → Ashwyrm's Roost (scene request) |
| `prop_hollow_heart_core` | *props-dungeon.* The Hollow Heart's set piece: a knot of the dead spirit tree's roots with rose veins, the old heart lantern (a 1.6× stone tōrō, tilted, its fire-box glowing rose round a veined heart) swallowed by thorn-roots, blight crystals. ≈ 5.3 m. **Collider 5.4 × 3.4** (mound set back); light (0, 3.2), `#ff7ab0`, radius 8, intensity 1.2 | raid_hollow_heart |
| `prop_thorn_wall` | *props-dungeon.* Tangled black bramble arcs with thorns, violet buds and blight crystals on a dark earth bank. **Collider 4.4 × 1.2** | Hollow Heart |
| `prop_root_arch` | *props-dungeon.* Arch of three braided great roots (4.2 m) with moss, hanging rootlets with two paper lanterns, glow mushrooms; blighted with crystals in buckets 1, 3. **No collider** (walk-through; feet at x = ±2.1); light (0, 2.7), `#ffd28a`, radius 3, intensity 0.6 | Hollow Heart, Root Hollows |
| `prop_dragon_skull` | *props-dungeon.* Huge bleached dragon skull half sunk in an ash drift, snout to the front-left, great swept horns, open jaw, soot and a crack, scattered bones. ≈ 4.2 m. **Collider 6.0 × 2.8** | Ashwyrm's Roost, Skyreach |
| `prop_roost_nest` | *props-dungeon.* Great nest of charred branches with ember-tipped twigs, woven ribs and a horned skull, an ash-and-straw lining with down feathers, three dark eggs cracked with ember light. **Collider 6.2 × 3.4** (set back); light (0, 0.7), `#ff9a4a`, radius 5, intensity 0.9 | Ashwyrm's Roost |
| `prop_ash_banner` | *props-dungeon.* Dragonsworn banner: soot pole with a horned crossbar, a tall torn charcoal cloth with the ember wyrm sigil (sways). 4.7 m. **Collider 0.6 × 0.45** | Ashwyrm's Roost |
| `prop_altar` | *props-dungeon.* Stone altar with a vermilion runner, incense bowl, bell, fruit and four candles (`SetLit`). **Collider 2.4 × 1.2**; light (0, 1.25), `#ffd28a`, radius 3.5, intensity 0.8, flicker | crypts, raids |

All four trainers share the trainers' gold sash with a lantern medallion.

*models-c* keys live in `Units/UnitRecipes.CreaturesC.cs`. The drakes and the Ashwyrm use the winged-quad rig
extension (`QB.WingL/R`, `WingL2/R2`, `Tail3`; `QuadKit.Wings`, `QuadKit.LongTail`; `UnitModel.WingFold`, `WingBeat`,
`LongTail`): wings fold over the back at rest, spread in roars, casts, rearing attacks and dodges, and beat while a
quad hovers (`FloatHeight` > 0). The data `size` is the height to the top of the head/horns; the Core collision
radius is `clamp(size × 0.22, 0.3, 1.6)`, so the 7.5 m bosses get 1.6 m although their bodies are ≈ 4 m long.

*models-a* keys live in `Units/UnitRecipes.CreaturesA.cs` (all BipedKit bipeds). Gnolls and the mire hag are
hunched: their torso, neck and head lean forward about the hips and the arms move with the shoulders (a local helper,
`MakeHunch`/`EndHunch`; the bind pose still hangs the arms straight down). The data `size` is the height to the top of
the head or headwear (banner poles and hat brims stand above it); "r" is the Core collision radius at that size,
`clamp(size × 0.22, 0.3, 1.6)`. Glows are emissive only (candle, crystals, lantern, pearl, orbs, ember slits); none of
these models carries a light of its own.

3D-only props (`PropModels`, `World/Props/PropGarden.cs`; the village garden by the cottages): `prop_stone_wall`
(dry-stone wall), `prop_veg_patch` (vegetable bed with a watering can), `prop_washing_line` (laundry on a line, sways).

*props-dungeon* keys live in `World/Props/PropDungeon.cs` (hook, palette, helpers), `PropDungeonCave.cs`,
`PropDungeonCrypt.cs` and `PropDungeonRaid.cs`. Collider sizes are the recommended `collider.w × h` ellipses (centred,
no offset); light offsets are `[x, height]` in metres and pick the nearest light anchor of the model. The variant keys
`prop_cave_mouth_snow`, `prop_crypt_door_golden` and `prop_raid_portal_ember` share their base recipe. Light-less
`SetLit` props (brazier, torch sconce, altar, door and stair lamps) follow the night glow; with a non-`nightOnly`
light they always burn.

### props-wild: highland, fen, peak and town props (Expansion §10; `World/Props/PropWild*.cs`)

Every model stands centred on its pivot, faces −Z and is finished on all sides (the deep maps are seen from every yaw),
so its recommended collider is a **centred** ellipse (`offset` 0) over the solid footprint; building corners may clip the
ellipse by up to ≈ 0.3 m, which the units' own clearance absorbs. "none" = walk-through dressing or a walkable deck:
leave `collider` out. The light column gives the map JSON `light.offset` `[x, height]`, which snaps the light to the
model's nearest light anchor (`MapView.AddLight`), and a suggested light. Lit models switch their glass and flames with
`SetLit`: `nightOnly: true` for windows and lamps, `false` for fires and spirit glows that always burn. Sizes at scale 1.

| key | model | size w × d, h (m) | collider w × h | light offset → suggested light |
|---|---|---|---|---|
| `prop_standing_stone` | lichen-flecked menhir with a carved ring-and-stave rune (faintly glowing on odd seeds), chips, golden grass | 1.3 × 1.1, 2.7 | 1.0 × 0.7 | – |
| `prop_scarecrow` | patched coat on a cross post, sack head with a stitched grin, floppy straw hat, a crow on the arm (sways) | 1.8 × 0.8, 2.3 | 0.6 × 0.4 | – |
| `prop_beehive` | three straw skeps on a bench, a honey pot, lavender | 2.1 × 0.8, 1.2 | 2.0 × 0.9 | – |
| `prop_wheat` | a patch of ripe wheat in sheaf-shaped bunches with bristling ears, poppies and cornflowers (sways); tiles edge to edge every 3.6 × 2.2 | 3.6 × 2.1, 1.1 | 3.4 × 1.8 (or none: a walk-through crop) | – |
| `prop_watchtower_ruin` | round sandstone tower broken open on one side, floor beams, a red pennant, fallen blocks | Ø 3.8, 8.0 | 3.8 × 3.8 | – |
| `prop_gnoll_tent` | Duskmane hide tent: a horned skull over the door, painted marks, guy ropes, a fire glow inside | 3.5 × 3.2, 3.6 | 2.8 × 2.6 | `[0, 0.75]` → `#ff9a4a`, r 3, i 0.9, flicker, not nightOnly |
| `prop_gnoll_totem` | stacked carved gnoll heads under an antlered skull, a crossbar with bones and red rags (sways) | 1.5 × 1.0, 3.9 | 0.8 × 0.6 | – |
| `prop_bonepile` | a mound of skulls, ribs and horns on churned earth, a broken red shield | 1.6 × 1.2, 0.9 | 1.6 × 1.2 | – |
| `prop_tree_golden` | golden autumn tree: amber-to-orange canopy over a forked trunk; fallen leaves as a ground part (sways) | Ø 6.4, 9.2 | 1.2 × 0.8 (trunk) | – |
| `prop_cairn` | stacked lichen stones, a finger stone with a hazel wand and ribbon, a pebble ring | 1.4 × 1.1, 1.5 | 1.1 × 0.8 | – |
| `prop_farmhouse` | whitewashed stone farmhouse under turf-ridged thatch, door lantern, shutters and flower box, red plank barn wing with hayloft, hay rolls, churn | 7.5 × 4.9, 6.1 | 8.4 × 5.2 | `[-0.35, 2.45]` (door lantern) → `#ffb062`, r 3.5, i 0.7, nightOnly |
| `prop_quarry_cart` | the Mudpaw tunnelers' cart of cut stone on a stub of rails, a pick leaning on it | 2.1 × 1.0, 1.25 | 2.2 × 1.1 | – |
| `prop_reeds` | tall reed clump with brown plumes (no ink; sways) | 1.5 × 1.1, 1.7 | none | – |
| `prop_cattails` | cattail clump: long leaves, brown heads (sways) | 1.7 × 1.5, 1.7 | none | – |
| `prop_mangrove` | mangrove on arching stilt roots, aerial roots and seed pods, a moss mat | 6.9 × 4.5, 6.5 | 2.2 × 1.8 | – |
| `prop_willow` | weeping willow: soft crown, hanging fronds (sways) | 5.6 × 4.8, 7.8 | 1.2 × 0.8 (trunk) | – |
| `prop_lilypads` | lily pads with pink water lilies at the water line (fen pools, ponds) | 2.0 × 1.8, 0.35 | none | – |
| `prop_stilt_hut` | thatched fen hut on stilts over the water: porch, ladder, bunting, round window, lit windows on every side | 4.1 × 4.4, 5.1 | 4.8 × 4.6 | `[-0.75, 2.35]` → `#ffb062`, r 3.5, i 0.7, nightOnly |
| `prop_boardwalk` | 3 m plank segment on piles, deck top 0.08; chains every 3.0 m along X. **`prop_boardwalk_y`**: the same along the map's depth | 3.0 × 1.7, 0.4 | none (walkable) | – |
| `prop_fen_lantern` | crooked pole with a hanging glass jar lantern (pale gold-green), charms, glow mushrooms | 1.0 × 0.5, 2.3 | 0.4 × 0.4 | `[0.55, 1.78]` → `#e8f0b0`, r 4, i 0.9, flicker, not nightOnly |
| `prop_mire_totem` | Mother Mire's coven totem: driftwood post under an antlered skull with green-glowing eyes, charms, a moss mound (sways) | 1.1 × 0.9, 3.2 | 0.8 × 0.6 | optional `[0, 2.45]` → `#7cf0a0`, r 2.5, i 0.6 |
| `prop_sunken_statue` | serene stone head and shoulder sinking into the mire, a raised stone hand with a lantern, moss, reeds | 3.0 × 1.7, 1.9 | 2.8 × 1.7 | – |
| `prop_mushroom_giant` | giant spotted mushroom (red, blue, orange or violet by seed) with glowing gills, a half-size one leaning out beside it, little ones at its foot | 3.8 × 3.7, 4.2 | 1.9 × 1.1 | optional `[0.2, 2.45]` → `#86f2d8`, r 3, i 0.6, nightOnly |
| `prop_fishing_rack` | drying rack hung with fish, a net, a stool and a basket (sways) | 2.6 × 1.0, 2.0 | 2.4 × 0.7 | – |
| `prop_pine_snow` | snowy pine: six tiers under white caps with drooping tongues, a drift at the foot (sways) | Ø 4.5, 9.5–11 | 1.0 × 0.6 (trunk) | – |
| `prop_rock_snow` | cool-grey boulders with snow on every upward face, banked drifts, frost-bitten grass | 2.5 × 1.7, 1.2 | 2.0 × 1.2 | – |
| `prop_ice_spire` | cluster of pale, faintly luminous ice crystals (to 3.1 m) on a snowy rock, frost sparkles | 1.7 × 1.3, 3.1 | 1.4 × 1.0 | optional `[0, 1.5]` → `#bfe8ff`, r 3, i 0.7, not nightOnly |
| `prop_snowdrift` | wind-carved drift with a cornice; a buried fence post (even seeds) or a frosted shrub with berries (odd) | 3.2 × 1.2, 0.6 (post 1.0) | 2.4 × 0.9 (or none) | – |
| `prop_mountain_hut` | herders' log hut: crossed log corners, fieldstone plinth, a broad snow-quilted gable with icicles and red bargeboards, front balcony with an airing blanket, wreath, door lantern; woodpile (left), skis, sled and shovel (right), back door and chimney | 6.5 × 5.8, 5.6 | 7.0 × 5.2 | `[0.2, 2.1]` (door lantern) → `#ffb062`, r 3.5, i 0.8, nightOnly |
| `prop_prayer_flags` | two poles on little cairns with strings of blue-white-red-green-yellow flags and a third string down to a stake, white scarves, a mani stone (sways) | 5.8 × 1.2, 3.7 | none (walk under) | – |
| `prop_ruined_tower` | broken ten-sided Heronguard tower in grey stone, scorched near the top, a snowy string course with icicles, burnt joists, a half-open door, a tattered blue heron banner, fallen blocks under the breach (sways) | Ø 3.5 (4.6 with rubble), 8.2 | 4.0 × 4.0 | – |
| `prop_dragon_bones` | an old dragon's skeleton half sunk in snow: a long horned skull resting on its jaw (left), the spine to a curled tail, ribs rising in curved arches, a folded wing, a broken spear with a blue pennant | 7.8 × 4.0, 3.4 | 8.0 × 4.2 | – |
| `prop_dock` | timber jetty: 2.2 × 5.2 m deck along local Z, its far end (+Z) with the ladder down and a lamp post; piles, bollards, crates, barrel, fish basket, net. **`prop_dock_x`**: the same along X, far end +X (`flip`: −X) | 3.0 × 5.6, 2.5 | none (walkable; over blocking water add a `water.crossings` rect, which draws as a ford there) | `[-0.95, 2.0]` (`_x`: `[2.25, 2.0]`) → `#ffd9a0`, r 3.5, i 0.8, nightOnly |
| `prop_boat` | rowing boat along X: painted hull per seed with a stripe and a tarred bottom, thwarts, oars, rope coil, creel, a heron figurehead, a paper lantern on the stern pole; the keel sits 0.3 m below the ground, so it floats at the water line | 3.7 × 1.3, 1.55 | none afloat; 3.4 × 1.3 beached | `[-1.62, 1.25]` → `#ffd9a0`, r 3, i 0.7, nightOnly |
| `prop_river_house` | tall river-town house: dressed stone ground floor with quoins, arched door, shop window under a striped awning, fish sign, door lantern; jettied pastel half-timbered upper floor with shutters and a flower balcony; steep slate (even seeds) or terracotta roof with dormers, gable round windows, chimney; back door to the water, mooring ring, hanging basket, rain barrel, ivy | 5.4 × 5.4, 8.3 | 5.2 × 5.0 | `[-0.2, 2.15]` (door lantern) → `#ffb062`, r 3.5, i 0.8, nightOnly |
| `prop_town_hall` | Brightwater's town hall: a stone arcade with two open arches and hanging lanterns, the great blue door, broad steps; half-timbered upper floor with tall windows, a balustraded balcony under the heron emblem and two banners; slate roof with dormers and chimneys; a clock tower with clocks on all four faces, an open belfry with its bell, a slate spire with a gold heron vane and pennants | 9.8 × 6.2, 15.1 | 10.8 × 6.8 | `[0, 2.2]` (arcade lanterns), `[0, 4.2]` (balcony door) or `[0, 9.1]` (clock) → `#ffb062`, r 4, i 0.8, nightOnly |
| `prop_fountain` | twelve-sided basin with carved panels in a ring of paving, two bowls on a pedestal, a bronze heron spouting water, curtains of water, lily pads, coins on the coping | Ø 3.8, 2.7 | 3.0 × 3.0 | optional `[0, 1.3]` → `#cfeaff`, r 3, i 0.5, nightOnly |
| `prop_bridge_stone` | stone road bridge: a 7.6 m deck along X with a low hump (0.07–0.19 m, so units walk on it), parapets with coping and moss flaring to wing walls, cutwater piers, the heron roundel on both faces, ivy, four newel pillars with lanterns. **`prop_bridge_stone_y`**: the same along the map's depth | 8.5 × 4.2, 2.0 | none (walkable: see below) | `[-3.95, 1.75]` or `[3.95, 1.75]` (one light per prop: the nearest pillar) → `#ffd9a0`, r 4, i 0.9, nightOnly |
| `prop_market_awning` | market stall under a scalloped striped awning on four posts (colour per seed): a slanted tray of river fish, crayfish, apples, pears, honey jars, a brass scale, strings of onions and garlic, two paper lanterns, crates, barrel and sack behind, a chalk sign | 3.2 × 2.2, 2.8 | 3.4 × 1.8 | `[-1.45, 2.05]` or `[1.45, 2.05]` → `#ffc070`, r 3, i 0.8, nightOnly |

**Bridges and water.** To the terrain, any prop whose art contains `bridge` is a bridge:
* On a map with `MapDef.water`, a `water.crossings` rect with a bridge prop's pivot inside it (± 1.5 m) keeps the river's
  deep bed under it, because the bridge spans it; every other crossing becomes a ford, a gravel bar under shallow water
  (`MapTerrain.Water.cs`, AnalyseWater). The crossing rect is also what makes the deck walkable in the nav, since water
  blocks movement. Give it the deck's size (`prop_bridge_stone`: about 7.6 × 2.2 along X; 2.2 × 7.6 for `_y`) and leave
  the bridge's collider out. Bridges never lift units: the decks stay within 0.2 m of the ground.
* On a map without `MapDef.water`, the first bridge prop lays the legacy brook through the whole map depth at its x
  (`MapTerrain.Analyse`). That fallback is off as soon as the map has data water, so a bridge never adds a second stream.
