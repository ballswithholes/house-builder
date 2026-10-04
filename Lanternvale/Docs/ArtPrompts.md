# Lanternvale — Production Art Prompts

These prompts replace the procedural placeholders written by `Tools/artgen` (see `ArtKeys.md` for the key
catalogue). They are written for Midjourney v6+/v7 and SDXL-class models; the same text works for both
(Midjourney flags at the end, SDXL notes in §1.4). Every key in `ArtKeys.md` is covered — individually or by a
family template with a per-key subject line.

Contents: 1 Style bible · 2 Environment (backgrounds, ground, decals, foreground) · 3 Props · 4 Characters,
companions, NPCs, portraits · 5 Creatures, pets, demons, totems · 6 Effects · 7 Icons & crests · 8 UI ·
9 How to replace placeholders.

---

## 1. Style bible

### 1.1 Shared style tokens (paste into every prompt)

* **ENV** (environment, props, foreground, creatures):
  `hand-painted watercolor, Studio Ghibli inspired, cozy whimsical fantasy, soft pastel palette, warm cream highlights, sage and moss greens, dusty blue and violet shadows, terracotta and honey accents, soft painterly edges, subtle paper grain, gentle rim light, 2d game asset`
* **CHAR** (heroes, companions, NPCs, humanoid foes):
  `stylized anime JRPG character art inspired by the Final Fantasy X era, painterly cel shading, clean soft dark outline, layered fabrics with belts, straps, buckles and ornate trims, asymmetric costume details, expressive hair, about 6.5 heads tall, 2d game sprite`
* **NEG** (negative prompt / `--no`):
  `text, watermark, signature, logo, frame, border, cropped, cut off, ground plane, cast shadow on floor, drop shadow, busy background, scenery, photorealistic, 3d render, harsh black outlines, neon, oversaturated, motion blur, depth of field, multiple views, extra limbs`

Palette anchors (hex, for colour-matching/LUTs): cream `#FFF4DC`, honey `#E8B04F`, terracotta `#C9785A`,
sage `#A8BC8A`, moss `#7D9A55`, forest `#3F6146`, dusty blue `#8FA7C4`, distant violet `#A9A1C8`,
outline ink `#3A2F42` (outlines are a soft dark violet-brown, never pure black). Hollow blight: grey-violet
`#8E8899` with glowing violet `#C9A8FF`.

### 1.2 Framing rules (all sprites)

* **Orthographic, front-facing** (props: front elevation with a slight oblique view of the right side wall and the
  roof; characters: front with a slight 3/4 turn).
* **Everything faces screen-right.** The engine mirrors units with `flipX`, so heroes turn slightly to the
  right and all creatures/pets face right.
* **Isolated on a pure white or transparent background**, no ground plane, no cast shadow (the engine draws a
  soft ground shadow for anything with `"shadow": true`). Remove the background afterwards (see §9).
* **Feet / base on the bottom pivot line** — leave ~3–6 % empty margin under the feet/base and small margins on
  the other sides; never crop the top of hair, horns, antlers, weapons or chimneys.
* **Lights glow:** windows, lanterns, campfires, orbs and runes need a bright warm (or violet) emissive core —
  the engine adds 2D light and bloom on top.
* Effects are **white / greyscale** (tinted in engine). Icons are **pure white silhouettes**.

### 1.3 Aspect ratios

| family | aspect | notes |
|---|---|---|
| background loops | `--ar 4:1` (2:1 also fine) | **seamless horizontal loop**, transparent/empty sky above the layer |
| ground tiles | `--ar 1:1` | **seamless tiling texture, top-down**, tiles in both axes |
| buildings, big trees, gate, boss | `--ar 1:1` | |
| tall props (windmill, pine, birch, lamp, lanterns, banner, signpost, pillar) | `--ar 1:2` | |
| wide props (smithy, bridge, log, bench, fence, bushes) | `--ar 2:1` | |
| characters, NPCs, humanoid foes | `--ar 1:2` | full body, feet at the bottom |
| quadrupeds, spider, felhunter | `--ar 2:1` | side 3/4 view facing right |
| portraits | `--ar 1:1` | bust, head and shoulders |
| icons, crests | `--ar 1:1` | white glyph on flat black (then convert to alpha) |

### 1.4 Model notes

* **Midjourney:** append `--ar <ratio> --style raw --stylize 150 --no <NEG>`. Lock the look with a style
  reference of an approved hero image (`--sref <url> --sw 200`) and keep a character sheet per hero for
  `--cref` (`--cw 60` to keep the face/hair, change the outfit freely). Use the same seed per family.
* **SDXL / Flux:** 1024-base resolutions (1024×1024, 768×1344 for 1:2, 1344×768 for 2:1, 1536×384 then
  outpaint for loops). CFG 5–6, 30–40 steps, DPM++ 2M Karras. Use a watercolour/Ghibli-style LoRA at 0.6–0.8
  for ENV and an anime-JRPG LoRA for CHAR; the negative prompt is NEG. For loops use a tiling/seamless option
  (`tile x` / circular padding) or paint the seam out with an offset-and-inpaint pass.
* Generate at 2× the final size and downscale; keep line weight consistent across a family.

---

## 2. Environment

### 2.1 Background layers — category Background, `loop: true`

Template (the client's background prompt):

> `Beautiful hand-painted watercolor <SUBJECT>, studio ghibli style, soft pastel colors, atmospheric perspective, 2d game asset, flat color background, seamless horizontal loop, transparent empty sky above, layer for parallax, <ENV> --ar 4:1`

| key | SUBJECT | notes |
|---|---|---|
| `bg_clouds` | soft cumulus cloud band, lit cream tops, lavender-blue undersides, a few thin high wisps | transparent below the clouds; no sky colour (engine draws the sky gradient) |
| `bg_mountains_far` | distant blue-violet mountain range with snow caps, faceted lit and shadowed faces, mist pooling at the base | the farthest layer; very low contrast |
| `bg_hills_far` | rolling pale-green watercolour hills, tiny tree clumps and field patches, haze toward the bottom | |
| `bg_hills_near` | nearer rolling hills with clumps of fluffy round trees, richer greens, sunlit crests | |
| `bg_forest_far` | misty layered forest silhouettes, rows of rounded crowns and pointed firs fading into blue mist | |
| `bg_forest_near` | darker forest edge, tall mossy trunks, heavy canopy, soft god rays, ferns and bushes at the bottom | canopy must stay inside the image (sky shows above) |
| `bg_shrine_cliffs` | tall mossy cliffs of grey-violet stone columns, old stone stairs climbing to a vermilion shrine gate, small ruins, pines, a thin waterfall, mist below | |
| `bg_village_far` | distant cosy village on a green hill: rooftops in terracotta, blue tile and thatch, a small windmill, chimney smoke curling, glowing windows | `--ar 4:1` (image is 2048×512) |

Verify loops by offsetting the image by half its width; fix the seam by inpainting.

### 2.2 Ground tiles — category Ground (`1024×1024`, tiles in both axes, 6 m per tile)

> `Seamless tiling top-down texture of <SUBJECT>, hand-painted watercolor, studio ghibli style, soft pastel colors, even lighting, no shadows from objects, 2d game ground texture <ENV> --ar 1:1 --tile`

| key | SUBJECT |
|---|---|
| `ground_meadow` | soft meadow grass with tonal swathes, clover, tiny white, yellow, pink and lavender wildflowers in small clusters |
| `ground_forest` | dark mossy forest floor, soft moss cushions, drifts of fallen leaves in honey, rust and brick, twigs, pebbles |
| `ground_shrine` | old mossy flagstones, irregular warm-grey stones with moss in the joints, a few fallen petals |
| `ground_village` | packed warm earth with pebbles and small grass patches |

Keep detail scale small: a grass blade ≈ 3–5 % of the tile, flowers ≈ 1–2 %.

### 2.3 Ground decals — category Prop (flat on the ground, transparent edges)

> `Top-down hand-painted watercolor <SUBJECT>, isolated on transparent background, soft irregular edges, 2d game decal <ENV>`

| key | SUBJECT | aspect / notes |
|---|---|---|
| `decal_path_dirt` | gently curving dirt path segment 8 m long with wheel ruts, pebbles and grass tufts overhanging the edges | `--ar 2:1`; path enters and exits at the vertical centre of the left/right edges so segments chain |
| `decal_path_stone` | curving path of mossy flagstones set in soil, same layout as the dirt path | `--ar 2:1` |
| `decal_flowers` | round patch of wildflowers and grass (3 m) | `--ar 1:1` |
| `decal_blight` | grey creeping blight stain with violet veins and tendrils, faint violet glow at the centre, ashen grass (4 m) | `--ar 1:1` |

### 2.4 Foreground — category Foreground (drawn in front, extra parallax)

Template (client's foreground prompt):

> `Stylized 2D game asset, <SUBJECT>, isolated on a clean white background, front-facing orthographic view, hand-painted watercolor, soft dark outline <ENV> --ar 2:1`

| key | SUBJECT |
|---|---|
| `fg_stones_a` | cluster of three or four mossy stones with soft green grass tufts (1.2 m) |
| `fg_stones_b` | a big mossy boulder flanked by two smaller stones, grass at the base |
| `fg_grass_a` | row of soft green grass tufts, varied heights, lit tips (1.0 m) |
| `fg_grass_b` | taller, wilder grass clumps leaning gently |
| `fg_flowers_a` | vibrant wildflowers — white daisies, buttercups, pink blossoms — among grass (1.0 m) |
| `fg_flowers_b` | vibrant wildflowers — violet bellflowers, red poppies, yellow blooms — among grass |
| `fg_ferns` | lush arching fern fronds (1.3 m) |

---

## 3. Props — category Prop

Template (client's midground prompt):

> `Whimsical cozy fantasy <SUBJECT>, isolated on a white background, hand-painted asset, clean edges, 2.5D platformer prop, orthographic view, front elevation with a slight view of the right side wall and roof, base on the bottom edge <ENV> --ar <A>`

| key | SUBJECT | A |
|---|---|---|
| `prop_cottage_a` | cottage with a thick rounded thatched roof and eyebrow dormer, cream plaster with timber frame, stone footing, arched wooden door, big round window glowing warm, flower box, ivy, crooked stone chimney | 1:1 |
| `prop_cottage_b` | tall crooked two-storey cottage leaning slightly, stone ground floor, rosy plaster upper floor jutting out, steep terracotta tile roof, very tall crooked chimney, teal shutters, flower boxes, round attic window | 1:1 |
| `prop_cottage_c` | single-storey cottage with a blue scalloped tile roof, plank skirt, blue shutters, glowing windows, a small iron lantern by the door, potted plants | 1:1 |
| `prop_inn` | larger two-storey timber-framed inn "The Sleepy Lantern", terracotta roof with two dormers, balcony, many glowing windows, hanging wooden sign with a crescent moon and lantern emblem (no lettering), paper lanterns by the door, barrels | 1:1 |
| `prop_shop_stall` | market stall with a cream and terracotta striped awning, shelves of jars, baskets of apples, pears and squash, paper lantern | 1:1 |
| `prop_smithy` | small open smithy: shed roof on posts, plank back wall with tools, anvil on a stump, stone forge with glowing coals and a stone chimney, water barrel, sacks | 2:1 |
| `prop_windmill` | small windmill: stone base, plaster upper tower, thatched cap, four lattice sails with cream cloth, gallery railing, glowing window | 1:2 |
| `prop_well` | round stone well with a small tiled roof on two posts, crank, rope and bucket, moss and ivy | 1:1 |
| `prop_fence` | rustic wooden fence segment 3 m wide, two rails, three posts, grass at the base | 2:1 |
| `prop_lamp_post` | wooden post with a crossbar arm and a hanging glowing paper lantern | 1:2 |
| `prop_spirit_lantern` | stone shrine lantern (tōrō): base, pillar, fire box with a warmly glowing window, curled roof, finial, patches of moss | 1:2 |
| `prop_spirit_lantern_dark` | the same tōrō lantern gone dark and blighted: grey-violet stone, cracks, dead window with a faint violet ember, small violet crystals at the base | 1:2 |
| `prop_tree_oak` | round fluffy oak, cauliflower-like leaf clumps lit from the upper left, sturdy trunk with root flare | 1:1 |
| `prop_tree_pine` | tall layered pine with drooping tiers | 1:2 |
| `prop_tree_birch` | slender white birch with dark bark marks and an airy yellow-green canopy | 1:2 |
| `prop_tree_great` | enormous ancient camphor tree with buttress roots, a sacred shimenawa rope with zigzag paper streamers around the trunk, tiny glowing spirit motes in the canopy | 1:1 |
| `prop_tree_dead` | gnarled blighted grey tree, bare branching limbs, violet veins, a few violet crystals at the roots | 1:1 |
| `prop_bush_a` | round leafy bush | 2:1 |
| `prop_bush_b` | round flowering bush with small pink blossoms | 2:1 |
| `prop_rock_large` | big rounded mossy boulder, moss cap, a few cracks | 1:1 |
| `prop_rock_small` | cluster of three small mossy rocks | 2:1 |
| `prop_stump` | tree stump with growth rings, moss and small orange mushrooms | 1:1 |
| `prop_log` | fallen mossy log with a cut end showing rings, fern and mushrooms | 2:1 |
| `prop_cart` | wooden hand cart with big spoked wheels, sacks, a crate and pumpkins | 1:1 |
| `prop_barrel` | oak barrel with dark iron hoops | 1:1 |
| `prop_crate` | wooden crate with a diagonal brace | 1:1 |
| `prop_hay` | round golden hay bale tied with red cords | 1:1 |
| `prop_signpost` | wooden signpost with three arrow boards (painted marks, no readable text) | 1:2 |
| `prop_noticeboard` | quest notice board with a small tiled roof and pinned paper notes (no readable text) | 1:1 |
| `prop_bench` | simple wooden park bench | 2:1 |
| `prop_campfire` | campfire of crossed logs in a stone ring, warm flames, a few sparks | 1:1 |
| `prop_tent` | canvas travel tent with an open flap, warm glow inside, small pennant | 1:1 |
| `prop_chest` | wooden treasure chest with brass bands and lock, closed | 1:1 |
| `prop_chest_open` | the same chest opened, lid tilted back, gold coins glowing inside | 1:1 |
| `prop_mushrooms` | cluster of glowing teal mushrooms with pale spots | 1:1 |
| `prop_ruin_pillar` | broken mossy stone pillar with fluting and ivy, rubble | 1:2 |
| `prop_ruin_arch` | ruined stone arch with one broken side, moss and ivy | 1:1 |
| `prop_shrine_gate` | vermilion shrine gate (torii-like) with black post feet, a plaque, a straw rope with paper streamers | 1:1 |
| `prop_spirit_statue` | small mossy stone fox spirit statue sitting on a pedestal, red cloth bib | 1:1 |
| `prop_blight_crystal` | cluster of grey-violet blight crystals with faceted lit and dark faces and a violet inner glow | 1:1 |
| `prop_banner` | cloth banner on a pole, deep blue with a gold trim and a glowing lantern emblem | 1:2 |
| `prop_bridge` | small arched wooden footbridge with vermilion railings, side view | 2:1 |

---

## 4. Characters

All humanoids share one scale: the image is **2.2 m tall**, feet on the pivot line 5 % above the bottom, an
adult figure ≈ 1.8 m (≈ 82 % of the image height), heads ≈ 6.5. Front view with a slight 3/4 turn to
screen-right, standing idle pose, weapon held so it stays inside the frame.

Template:

> `Full body <SUBJECT>, standing idle pose, front view with a slight three-quarter turn to the right, feet at the bottom of the frame, isolated on a white background, <CHAR> --ar 1:2`

### 4.1 Heroes (`char_*`) — one per class, distinctive silhouette and colour identity

| key | SUBJECT |
|---|---|
| `char_warrior` | stoic young swordsman in a heavy crimson long coat with gold trims worn open over dark steel plate, very high stiff collar, broad leather belt with a brass buckle, slung hip belt with pouch, layered steel pauldron on one shoulder only, gauntlets, knee guards over tall boots, spiky black hair, amber eyes, a huge greatsword resting on his shoulder |
| `char_hunter` | athletic islander hunter, sun-tanned, sleeveless open leather vest with yellow trim, a cream fur pelt over one shoulder, baggy knee-length teal shorts with a patterned hem, honey sash knotted at the hip, turquoise and gold bead necklace, leather bracers, shin wraps and short boots, braided dark hair with beads and a beaded headband, green eyes, recurve bow in hand, quiver on the back |
| `char_paladin` | radiant knight in polished silver plate and chainmail, white tabard with a golden sun emblem and blue trim, blue cape, big rounded pauldrons, blond neat hair, blue eyes, a blue kite shield with a sun emblem on one arm and a heavy warhammer resting head-down beside him |
| `char_mage` | elegant dark sorceress, pale skin, long floor-length plum-black dress made of many overlapping belts fanning out with silver buckles, cream fur stole around the shoulders, corset bodice, long sleeves with fur cuffs, black hair in a large low bun with hairpins and long beaded braids, plum eyes, tall dark staff topped with a glowing violet orb |
| `char_priest` | gentle young summoner, white kimono-style top with pink trim and floral details, wide detached sleeves with blue flowered hems, golden obi with a large bow at the back, long pleated blue hakama skirt with a slit and pink hem, knee boots, chin-length brown bob with one long wrapped lock and bead ornament, teal eyes, a golden ringed staff (shakujo) with a pink streamer |
| `char_rogue` | playful treasure hunter girl, tan skin, brass goggles with teal lenses on her forehead, long orange scarf trailing, cropped yellow top, short green shorts, loose belt with pouches, thigh strap pouch, arm bands, fingerless gloves, laced knee boots, wild blonde hair with many beaded braids and a ponytail, green eyes, grin, twin daggers (one in reverse grip) |
| `char_warlock` | aristocratic sorcerer, pale skin, long deep-indigo coat with gold trims and an enormously tall flared collar, glowing violet runes along the coat panels, ivory inner vest with gold buttons, violet sash, swept-back long silver-blue hair with a few upswept spikes, violet eyes, an open grimoire glowing violet in one hand and a violet flame in the other |
| `char_shaman` | tall horned beast-folk warrior (2 m), slate-blue fur, curled cream ram horns, shaggy pale mane, feline muzzle, gold eyes, red-ochre cloth sash across the chest and long loin panel with a gold trim, broad leather belt hung with carved wooden totem charms, fur leg wraps, bead necklace, tail, a tall spear with a red tassel and feathers |

### 4.2 Companions (`comp_*`) — same specs, own palette and hair

| key | class | SUBJECT |
|---|---|---|
| `comp_kael` | Warrior | weathered veteran guardian (late 30s), heavy dark-crimson greatcoat worn with the left arm tucked inside so the empty sleeve hangs loose, tall stiff collar, mail skirt and dark plate beneath, a pale scar closing his left eye, swept-back dark hair streaked with grey, stern, a battered tea flask at the hip, an enormous notched greatsword resting on his shoulder |
| `comp_lys` | Mage | aloof sorceress, long dark slate-and-ink dress made of crossing belts with silver buckles, pale fur collar, very dark hair with two thick front braids and long needles pinned through a knot at the back, staff topped with a storm-blue orb, carries a small pressed-flower book |
| `comp_seren` | Priest | young summoner-pilgrim, pale blue kimono-style top with wide sleeves and floral hems, long flowing violet hakama skirt, lavender obi, golden ringed staff that chimes, dark hair in a high ponytail with a single white streak and a beaded hair ornament, gentle smile |
| `comp_rook` | Hunter | big broad-shouldered islander athlete, sun-browned skin, ginger hair in beaded braids under a blue knotted bandana, sporty blue vest with a big number seven, fur pelt, orange baggy shorts, red sash, a ringball on his hip, a pale driftwood recurve bow, big grin |
| `comp_pip` | Rogue | tiny cheerful tinker girl (1.5 m), blonde twin buns, oversized three-lens brass goggles on her forehead, bright pink scarf, teal cropped top, short purple shorts, mismatched sleeves (one long yellow sleeve, one bare arm), belt of pouches, twin cog-bladed daggers |
| `comp_torvan` | Shaman | towering horned beast-folk oathkeeper, silver-grey fur and pale mane, great curled ram horns hung with braided cords, beads and feathers, blue tribal markings on the face, moss-green sash and loin panel with gold trim, totem charms, leaf-bladed spear-staff, solemn |
| `comp_aldric` | Paladin | sunny, brash young knight-errant (about 18), youthful clean-shaven face, tousled spiky honey-blond hair, a confident grin, light armour, an asymmetric one-shouldered bright gold tabard with a sun motif and sky-blue trim, one bare sword arm with a leather bracer, sky-blue cape, cream kite shield painted with a golden sun, polished warhammer |
| `comp_morwen` | Warlock | elegant gothic scholar, long black hair, high-collared dark plum long coat with violet lining and silver trims, layered belts, violet runes stitched along the hems, silver rings, a leather grimoire in hand, a violet flame, and a tiny red imp familiar perched on her shoulder holding an inkpot |

### 4.3 NPCs (`npc_*`)

Same template, NPCs use a softer, simpler costume (`<CHAR>`, less armour, fewer trims).

| key | SUBJECT |
|---|---|
| `npc_elder` | kindly village elder, bald with white side hair and a very long white beard, bushy brows, brown robe with a honey sash and a green mantle with gold trim, gnarled cane with a small paper lantern |
| `npc_innkeeper` | plump smiling innkeeper woman, rose dress, cream apron with pocket, green patterned headscarf, towel over the shoulder, holding a frothy wooden mug, hand on hip |
| `npc_merchant` | travelling merchant with a short beard, wide-brim hat with a pink feather, green vest over a cream shirt, big backpack with a rolled blanket and a pot, coin pouch, coins in hand |
| `npc_smith` | burly bald blacksmith with a dark beard, rolled cream sleeves, heavy leather apron, thick gloves, hammer and tongs |
| `npc_villager_a` | village woman with a yellow headscarf, blue dress with laced bodice, cream apron, basket of flowers on her arm |
| `npc_villager_b` | farmer in a straw hat, cream shirt, blue overalls, holding a hoe |
| `npc_child` | small child (1.2 m), messy brown hair, green tunic with a cream collar, shorts, little boots, holding a tiny glowing paper lantern on a stick |
| `npc_guard` | village guard with a kettle helmet, chainmail, teal and cream tabard, steel pauldrons, round teal-and-cream shield, spear |
| `npc_trainer` | generic class trainer in a long dusty-blue robe with a gold sash, plum stole, hood down, short grey beard, holding a book |
| `npc_spirit` | friendly forest spirit (kodama-like), small (0.9 m) pale white-green body, round head with three dark hollow dots for eyes and mouth, a sprout on its head, soft glow — original design, cute and quiet |

### 4.4 Portraits (`portrait_<suffix>` for every char_/comp_/npc_)

> `Portrait bust of <SUBJECT from 4.1–4.3>, head and shoulders, facing slightly right, soft watercolor background wash in <class colour>, <CHAR>, square --ar 1:1`

Class colours for the wash: warrior red, hunter green, paladin gold, mage violet, priest sky-blue, rogue orange,
warlock purple, shaman blue; villagers sage. Keep the face from the matching full-body sprite (use `--cref`).

---

## 5. Creatures — category Creature

Quadrupeds are drawn **side 3/4 view facing right**. Template:

> `<SUBJECT>, full body, standing, side three-quarter view facing right, isolated on a white background, charming but readable game monster, <ENV> --ar <A>`

| key | SUBJECT | A |
|---|---|---|
| `cr_wolf` | grey wolf with a cream chest and muzzle, darker saddle, amber eyes, bared fangs (1.1 m) | 2:1 |
| `cr_wolf_blighted` | wolf corrupted by the Hollow: grey-violet fur darkening toward the legs, glowing violet vein cracks, small violet crystals on the back, glowing violet eyes (1.2 m) | 2:1 |
| `cr_boar` | bristly brown boar with a spiky dark mane, big tusks, small eyes (1.0 m) | 2:1 |
| `cr_spider` | big forest spider with a patterned abdomen with moss on it, banded legs, cluster of red eyes (0.9 m) | 2:1 |
| `cr_mossling` | small mischievous round moss creature with big shiny eyes, toothy grin, stubby arms and feet, wearing a big leaf as a hat (0.9 m) | 1:1 |
| `cr_mossling_shaman` | mossling with a twig staff topped by a glowing teal bead, bead necklace, feathers in its leaf hat (1.0 m) | 1:1 |
| `cr_bandit` | bandit cutthroat in a brown hood and red scarf mask, leather jerkin and straps, two daggers (1.8 m, use the character template) | 1:2 |
| `cr_bandit_archer` | hooded bandit archer, olive cloak and scarf mask, shortbow, quiver (1.8 m) | 1:2 |
| `cr_bandit_hexer` | hooded bandit spellcaster in plum robes with bone and wood charms, skull-topped staff, violet hex glowing in one hand (1.8 m) | 1:2 |
| `cr_bandit_chief` | burly bandit leader (2 m), red mohawk and beard, eyepatch, fur mantle, one steel pauldron, big axe on his shoulder | 1:2 |
| `cr_hollow_wisp` | floating grey-violet will-o'-wisp with a flame-like tail, glowing core, two dark eyes (1.0 m, floats) | 1:1 |
| `cr_hollow_spirit` | hollowed humanoid spirit floating in tattered grey-violet robes fading at the hem, pale mask face with hollow glowing eyes, long reaching arms, faint runes (1.9 m) | 1:2 |
| `cr_hollow_treant` | small blighted treant: grey-violet bark body with glowing violet hollow eyes and mouth, branch arms, root legs, dead leaves and violet crystals (2.6 m) | 1:2 |
| `cr_hollow_warden` | **boss**: enormous corrupted stag spirit (4.5 m), pale ethereal coat darkening to ink-violet on the legs and belly as the Hollow creeps up, glowing violet vein cracks, tattered violet spirit cloth over the back with a straw rope, white shaggy throat mane, vermilion spirit mask marking on the face, glowing violet eye, vast branching antlers hung with paper lanterns — some glowing gold, some guttering violet; majestic, sad and imposing | 1:1 |
| `cr_training_dummy` | straw training dummy on a wooden post with crossbar arms, burlap head with stitched X eyes, red rope, target painted on the chest (1.6 m) | 1:2 |

### 5.1 Pets, demons and totems

| key | SUBJECT | A |
|---|---|---|
| `pet_wolf` | friendly brown-grey wolf companion with a red scarf collar, gentle green eyes (1.0 m) | 2:1 |
| `pet_cat` | orange tabby lynx-like cat with ear tufts and a blue collar with a gold bell (0.75 m) | 2:1 |
| `pet_boar` | friendly young boar with small tusks and a teal saddle blanket (0.9 m) | 2:1 |
| `pet_bear` | sturdy brown bear with a teal neckerchief (1.4 m at the shoulder) | 2:1 |
| `pet_owl` | big round brown owl with cream chest chevrons and huge amber eyes perched on a branch (0.8 m) | 1:1 |
| `demon_imp` | small mischievous fire imp, red-orange skin, big pointed ears, horns, bat wings, devil tail, holding a fireball (0.9 m) | 1:1 |
| `demon_voidwalker` | hulking blue-violet void spirit with muscular arms and gold bracers, no legs — a smoky tail, hooded head with glowing eyes (2.2 m) | 1:2 |
| `demon_succubus` | tall elegant demoness with lavender skin, black horns, bat wings, long dark hair, floor-length violet gown with dark armour plates and gold trim, whip, tail — tasteful, fully clothed (1.85 m) | 1:2 |
| `demon_felhunter` | demonic hound, dark violet hide, bone spines along the back, head tentacles with glowing tips, green glowing runes and eyes (1.2 m) | 2:1 |
| `demon_infernal` | hulking burning stone golem demon (2.6 m), dark basalt rock plates with glowing orange lava cracks, molten core in the chest, massive fists, a crown of flames on its small head, glowing eyes | 1:1 |
| `totem_earth` | carved wooden totem pole (1.2 m) with two stylised faces, topped by stone shards and a leaf, green accents | 1:2 |
| `totem_fire` | carved totem pole with red-orange accents topped by a flame | 1:2 |
| `totem_water` | carved grey-blue totem pole topped by a water orb held by curling waves | 1:2 |
| `totem_air` | pale carved totem pole topped by white wings and a sky-blue orb | 1:2 |

---

## 6. Effects — category Effect (white/greyscale, tinted in engine)

> `<SUBJECT>, pure white and light grey on a black background, soft glow, game VFX sprite, centred, no colour --ar <A>`

Convert to alpha afterwards (luminance → alpha, RGB white). `fx_shadow` is the exception: a soft dark ellipse.

| key | SUBJECT | A |
|---|---|---|
| `fx_glow` | soft round radial glow | 1:1 |
| `fx_spark` | four-point star sparkle with a small halo | 1:1 |
| `fx_ring` | thin glowing ring | 1:1 |
| `fx_bolt` | glowing projectile orb flying right with a tapering streak tail | 2:1 |
| `fx_arrow` | arrow pointing right, greyscale shading | 4:1 |
| `fx_slash` | crescent sword-slash arc, thick in the middle, fading tail | 1:1 |
| `fx_heal` | rising plus signs and sparkles, fading toward the top | 1:2 |
| `fx_smoke` | soft puffy smoke cloud, light grey | 1:1 |
| `fx_leaf` | single drifting leaf with veins, greyscale | 1:1 |
| `fx_firefly` | tiny glowing dot with a soft halo | 1:1 |
| `fx_shadow` | soft dark elliptical ground shadow | 2:1 |
| `fx_target_ring` | flattened selection ellipse with four tick marks (ground-plane perspective) | 2:1 |
| `fx_rune_circle` | magic circle: concentric rings, a hexagram, small runes around the band | 1:1 |

---

## 7. Icons — category Icon (128×128 white silhouette on transparent)

> `Simple bold game ability icon of <SUBJECT>, single solid white silhouette on a flat black background, flat vector glyph, thick shapes, a few cut-out details, centred, readable at 40 pixels, no gradient, no frame --ar 1:1`

Then: luminance → alpha, RGB white, downscale to 128×128 with ~8 px margin. The UI draws the school-coloured
frame. One icon per glyph name in `DataSchema.md` → *Icon glyphs* (`glyph_<name>`):

| glyph | SUBJECT | glyph | SUBJECT |
|---|---|---|---|
| sword | a sword diagonal | swords | two crossed swords |
| axe | a battle axe | mace | a spiked mace |
| hammer | a war hammer | dagger | a dagger |
| daggers | two crossed daggers | spear | a spear with leaf blade |
| staff | a staff with an orb in a crescent | shield | a heater shield with a cross |
| shield_bash | a shield with impact lines | bow | a recurve bow with a nocked arrow |
| arrow | one arrow | arrows | three parallel arrows |
| multishot | three arrows fanning out | gun | a flintlock pistol |
| trap | a bear trap with teeth | paw | a paw print |
| claw | three claw slashes | fang | two fangs |
| wolf | a wolf head profile | eagle | an eagle with spread wings |
| hawk | a hawk head profile | monkey | a monkey face |
| cheetah | a big-cat head with tear marks and spots | turtle | a turtle from above |
| snake | a coiled snake | scorpion | a scorpion |
| spider | a spider | owl | an owl face |
| boar | a boar head with tusks | bear | a bear head |
| fire | a flame | fireball | a fireball with a trail |
| flame_wave | a wave of flames | meteor | a meteor with streaks |
| ember | a small flame with sparks | frost | a cluster of ice shards |
| snowflake | a snowflake | ice_shard | a single ice shard |
| ice_block | an ice cube | frost_nova | a burst of ice spikes around a snowflake |
| arcane | an arcane star | arcane_orb | an orb with an orbit ring |
| missiles | three star bolts with trails | portal | an oval portal with a spiral |
| blink | double chevron with a sparkle | sheep | a fluffy sheep |
| brain | a brain | eye | an eye |
| holy | a radiant sunburst with a cross | cross | a cross |
| sun | a sun with rays | halo | a halo above a head |
| wings | a pair of wings | hand | an open hand |
| hands_pray | praying hands | heart | a heart |
| heal_plus | a rounded plus | renew | a circular arrow around a leaf |
| shadow | a dark crescent swirl | skull | a skull |
| moon | a crescent moon | void | a vortex spiral |
| tentacle | a curled tentacle | demon | a horned demon head |
| imp | an imp face with big ears | curse | an evil eye with drips |
| fear | a screaming ghost | drain | a drop drawing streams |
| soul_shard | a faceted crystal shard | nature | a sprout |
| leaf | a leaf | lightning | a lightning bolt |
| chain_lightning | a forking lightning chain | storm | a storm cloud with a bolt |
| earth | mountain peaks | rock | a boulder |
| wave | a curling wave | water_drop | a water drop |
| totem | a totem pole | totem_fire | a totem topped with a flame |
| totem_earth | a totem topped with rock shards | totem_water | a totem topped with a drop |
| totem_air | a totem topped with wind arcs | wind | wind swirls |
| wolf_spirit | a wolf head with spirit flames | stealth | a hooded head |
| mask | a domino mask | poison | a drop with a skull and bubbles |
| vial | a slim vial | coin | a coin with a star |
| kick | a kicking boot with speed lines | boot | a boot |
| fist | a fist | shout | a megaphone mouth with sound waves |
| banner | a swallowtail banner on a pole | rage | an anger vein mark |
| blood | blood drops | whirlwind | a tornado |
| charge | a rushing arrow with speed lines | stance_battle | crossed swords in a ring |
| stance_defensive | a shield over a sword | stance_berserker | an axe with rage marks |
| armor | a breastplate | aura | concentric radiating arcs |
| seal | a scalloped wax seal with a sun | judgement | a gavel radiating light |
| blessing | a hand with stars above | lock | a padlock |
| key | a key | potion_red | a round potion flask |
| potion_blue | a tall conical potion flask | food | a drumstick |
| drink | a frothy mug | star | a five-point star |
| sparkle | a four-point sparkle | clock | a clock face |
| hourglass | an hourglass | feather | a feather |
| bandage | crossed bandages | crown | a crown |

### 7.1 Class crests (`crest_<class>`)

> `Ornate circular class crest emblem, a ring border with four diamond studs enclosing <SYMBOL>, single solid white silhouette on a flat black background, flat vector, bold --ar 1:1`

warrior: crossed swords · hunter: bow and arrow · paladin: sun with a cross · mage: arcane star ·
priest: wings with a halo · rogue: crossed daggers · warlock: horned demon head · shaman: totem pole.

---

## 8. UI — category UI

| key | prompt |
|---|---|
| `ui_parchment` | `Seamless tiling warm parchment paper texture, soft cream and light tan, subtle fibres and faint stains, no text, even lighting --ar 1:1 --tile` (512×512) |
| `ui_vignette` | engine overlay: dark violet-brown `#231B2C` with alpha rising from transparent centre to ~75 % at the corners (generate procedurally; no prompt needed) |
| `logo_lanternvale` | `Hand-lettered whimsical title logo "Lanternvale", rounded brush lettering in warm cream fading to honey with a thick dark violet outline and a soft golden glow, the letter l curls into a lamp hook holding a small glowing paper lantern, a leafy sprig underline, fireflies, isolated on transparent, cozy Ghibli-like fantasy game title --ar 2:1` (verify the spelling!) |

---

## 9. How to replace placeholders

1. **Same file name and path.** Overwrite `Assets/Lanternvale/Resources/Art/<Folder>/<key>.png` (folders:
   Backgrounds, Ground, Props, Foreground, Characters, Portraits, Creatures, Effects, Icons, UI). Keys and
   paths are listed in `Resources/Art/art_manifest.json` (`path` is Resources-relative, e.g.
   `Art/Props/prop_cottage_a`). Nothing else needs to change; `ArtLibrary` loads by key.
2. **Keep a transparent background** (PNG with alpha). Remove white/black backgrounds with a matting tool,
   trim stray halos, and keep the soft dark outline inside the alpha. Leave a small transparent margin.
   Effects and icons: RGB white, shape in alpha. Real art may be any size; power-of-two is only needed for
   the placeholders.
3. **Height and pivot live in `art_manifest.json`.** `height` is the world height in metres of the **whole
   image** (the engine sets pixels-per-unit = texture height / height); `pivot` is the normalised point that
   stands on the ground (feet/base). After replacing an image either
   * paint it with the same framing as the placeholder (characters: 2.2 m canvas, feet at 5 % from the
     bottom, figure ≈ 82 % of the height; props/creatures: base on the pivot line, top of the object near the
     top edge), or
   * edit that entry's `height`/`pivot` by hand. Re-running the generator **overwrites** both the PNGs and
     the manifest — once real art is in, run it only with `--only <keys>` for keys that are still
     placeholders (it re-writes the manifest for all keys, measuring object heights from the PNGs on disk,
     so check hand-edited values afterwards or keep a copy).
4. **Loops and tiles:** `loop: true` backgrounds must tile horizontally; ground textures (and
   `ui_parchment`) must tile in both axes. `generate.py --manifest-only` re-checks seams, power-of-two sizes
   (placeholder rule — ignore for real art) and that every documented key exists.
5. **Facing:** unit sprites face screen-right; the engine mirrors them.
6. **Unity import settings** (sprite mode, filtering, wrap mode for loops/ground, compression, max size,
   alpha-is-transparency) are applied automatically by the editor postprocessor when the PNG is imported —
   no manual Inspector changes are needed.
7. Run the previews to check the new art in context:
   `python3 Lanternvale/Tools/artgen/contact_sheet.py` → `Docs/art_previews/*.png`.
