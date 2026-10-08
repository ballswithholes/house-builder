#!/usr/bin/env bash
# Re-renders the committed preview set (Docs/previews3d/), or into another directory given as $1.
# Every map gets a default view; the deep maps get spots in their northern band; the hidden entrances are shown
# revealed (--flags); outdoor zones at noon, dusk and night; indoor maps at their one (indoor) light.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tool="$here/preview3d.sh"
out="${1:-$here/../../Docs/previews3d}"
mkdir -p "$out"
# ---- the first slice (deepened)
"$tool" map lanternvale "$out/lanternvale_noon.png"
"$tool" map lanternvale "$out/lanternvale_golden.png" --hour 18
"$tool" map lanternvale "$out/lanternvale_night.png" --hour 22.5
"$tool" map lanternvale "$out/lanternvale_far.png" --zoom 10.4
"$tool" map lanternvale "$out/lanternvale_close.png" --zoom 3 --yaw 30
"$tool" map lanternvale "$out/lanternvale_kusu.png" --spawn camphor --hour 17.5 --flags moppet_found
"$tool" map lanternvale "$out/lanternvale_pasture.png" --at 84,9 --hour 9 --flags shepherd_quest,bandits_peaceful
"$tool" map lanternvale "$out/lanternvale_north.png" --at 45,30 --hour 11
"$tool" map lanternvale "$out/lanternvale_hollows_entrance.png" --at 20,16 --hour 15 --flags found_root_hollows
"$tool" map whisperwood "$out/whisperwood_noon.png" --hour 12.5
"$tool" map whisperwood "$out/whisperwood_greymane.png" --at 25,9 --hour 12.5 --flags shepherd_hunt
"$tool" map whisperwood "$out/whisperwood_north.png" --at 50,32 --hour 12.5
"$tool" map whisperwood "$out/whisperwood_mossdeep_entrance.png" --at 46,35 --hour 16 --flags found_mossdeep
"$tool" map shrine "$out/shrine_dusk.png"
"$tool" map shrine "$out/shrine_keeper.png" --at 44,8
"$tool" map shrine "$out/shrine_north.png" --at 35,28
"$tool" map shrine "$out/shrine_catacombs_entrance.png" --at 36,29 --flags found_lantern_catacombs
# ---- the zones of levels 12-30: the default view at noon, dusk and night, a spot in the north, the entrances
for z in amberfield brightwater mirefen skyreach; do
  "$tool" map "$z" "$out/${z}_noon.png" --hour 12.5
  "$tool" map "$z" "$out/${z}_dusk.png" --hour 18.6
  "$tool" map "$z" "$out/${z}_night.png" --hour 22.5
done
"$tool" map amberfield "$out/amberfield_north.png" --at 65,46 --hour 15
"$tool" map amberfield "$out/amberfield_barrow_entrance.png" --at 64,43 --hour 16 --flags found_barrow
"$tool" map amberfield "$out/amberfield_haybright.png" --at 104,12.5 --hour 10 --markers auto --level 12
"$tool" map amberfield "$out/amberfield_cross_quests.png" --at 84,32 --hour 11 --zoom 8 --markers auto --level 14
"$tool" map amberfield "$out/amberfield_cross_harvest_night.png" --at 84,32 --hour 22.5 --zoom 8 --flags am_lamp_1,am_lamp_2,am_lamp_3,am_lamp_4,am_harvest_home,am_corlan_banner_home
"$tool" map amberfield "$out/amberfield_warcamp_dusk.png" --at 21,46 --hour 19.5 --zoom 8
"$tool" map amberfield "$out/amberfield_quarry.png" --at 15,10 --hour 11 --zoom 8
"$tool" map amberfield "$out/amberfield_watch.png" --at 64,12 --hour 14 --zoom 8
"$tool" map amberfield "$out/amberfield_bridge_dawn.png" --at 50,24 --hour 7
"$tool" map dgn_barrow "$out/dgn_barrow_tomb.png" --at 50,22
"$tool" map dgn_barrow "$out/dgn_barrow_hearth.png" --at 40,35
"$tool" map amberfield "$out/amberfield_kings_ring_door.png" --at 64,45 --hour 16 --flags found_barrow
"$tool" map brightwater "$out/brightwater_north.png" --at 42,36 --hour 10
"$tool" map brightwater "$out/brightwater_market.png" --at 55,18 --hour 11 --markers auto
"$tool" map brightwater "$out/brightwater_bridge_dusk.png" --at 33,20 --hour 18.6 --yaw 20
"$tool" map brightwater "$out/brightwater_harbour_night.png" --at 40,8 --hour 22.5
"$tool" map brightwater "$out/brightwater_memorial_lit.png" --at 22,16 --hour 21 --flags bw_memorial_lit
"$tool" map brightwater "$out/brightwater_chapel_night.png" --at 22,27 --hour 22
"$tool" map brightwater "$out/brightwater_inn.png" --at 70,26 --hour 16.5 --yaw -25
"$tool" map mirefen "$out/mirefen_north.png" --at 60,46 --hour 11
"$tool" map mirefen "$out/mirefen_vault_entrance.png" --at 30,41 --hour 17 --flags found_drowned_vault
"$tool" map mirefen "$out/mirefen_raid_portal.png" --at 12,45 --hour 21
"$tool" map mirefen "$out/mirefen_lowlantern_dusk.png" --at 99,26 --hour 18.6
"$tool" map mirefen "$out/mirefen_mere_lanterns.png" --at 63,30 --hour 21.5 --flags mf_lanterns_rise
"$tool" map mirefen "$out/mirefen_croaking_stones.png" --at 20,16 --hour 10
"$tool" map mirefen "$out/mirefen_heron_bridge.png" --at 40,20 --hour 16.5
"$tool" map mirefen "$out/mirefen_barrows.png" --at 57,47 --hour 19
"$tool" map dgn_drowned_vault "$out/dgn_drowned_vault_heart_well.png" --at 52,24 --flags dg5_tidewitch_defeated
"$tool" map skyreach "$out/skyreach_north.png" --at 60,50 --hour 12.5
"$tool" map skyreach "$out/skyreach_sanctum_entrance.png" --at 86,47 --hour 14 --flags found_frozen_sanctum
"$tool" map skyreach "$out/skyreach_raid_portal.png" --at 20,53 --hour 19
"$tool" map skyreach "$out/skyreach_cairnhollow_dusk.png" --at 90,14 --hour 18.6 --zoom 8 --markers auto --level 26
"$tool" map skyreach "$out/skyreach_cairnhollow_night.png" --at 92,15 --hour 22.5
"$tool" map skyreach "$out/skyreach_switchbacks.png" --at 60,30 --zoom 10.4
"$tool" map skyreach "$out/skyreach_bone_field.png" --at 60,46 --hour 15
"$tool" map skyreach "$out/skyreach_heronguard_tower.png" --at 40,46 --hour 17
"$tool" map skyreach "$out/skyreach_vanguard_night.png" --at 20,50 --hour 21.5
"$tool" map dgn_frozen_sanctum "$out/dgn_frozen_sanctum_hall.png" --at 8,22
"$tool" map dgn_frozen_sanctum "$out/dgn_frozen_sanctum_heart.png" --at 50,22
# ---- hidden dungeons and raids
for m in dgn_root_hollows dgn_mossdeep dgn_lantern_catacombs dgn_barrow dgn_drowned_vault dgn_frozen_sanctum raid_hollow_heart; do
  "$tool" map "$m" "$out/${m}.png"
  "$tool" map "$m" "$out/${m}_far.png" --at 30,22 --zoom 10.4 --yaw 25
done
"$tool" map raid_ashwyrm_roost "$out/raid_ashwyrm_roost.png"
"$tool" map raid_ashwyrm_roost "$out/raid_ashwyrm_roost_north.png" --at 55,58 --zoom 5 --yaw -20
"$tool" map raid_ashwyrm_roost "$out/raid_ashwyrm_roost_night.png" --hour 22.5
# ---- lv: Lanternvale's northern band (the Root Hollows mouth moved north of Kusu's canopy) and the Root Hollows
"$tool" map lanternvale "$out/lanternvale_kusu_roots.png" --at 23.6,31 --hour 16 --flags found_root_hollows
"$tool" map lanternvale "$out/lanternvale_kusu_roots_night.png" --at 23.6,31 --hour 22.5 --flags found_root_hollows
"$tool" map lanternvale "$out/lanternvale_west_road.png" --at 9,28 --hour 11 --flags lv2_oil_hunt
"$tool" map lanternvale "$out/lanternvale_millpond.png" --at 45,33 --zoom 10.4 --hour 17
"$tool" map lanternvale "$out/lanternvale_meadow_night.png" --at 46,33 --zoom 8 --hour 22.5
"$tool" map lanternvale "$out/lanternvale_orchard.png" --at 62,28 --zoom 7.5 --hour 16 --markers auto --level 12
"$tool" map lanternvale "$out/lanternvale_farm.png" --at 78,27 --hour 14
"$tool" map lanternvale "$out/lanternvale_lantern_hill.png" --at 84,37 --hour 17.5 --yaw 30
"$tool" map dgn_root_hollows "$out/dgn_root_hollows_grove.png" --at 27,20 --zoom 5 --markers auto --level 12
"$tool" map dgn_root_hollows "$out/dgn_root_hollows_gallery.png" --at 24,32
"$tool" map dgn_root_hollows "$out/dgn_root_hollows_rootwarden.png" --at 49,21
# ---- ww: Whisperwood's old forest (the brook, its fords and the Old Bridge) and Mossdeep Grotto
"$tool" map whisperwood "$out/whisperwood_old_bridge.png" --at 60,8 --hour 12.5
"$tool" map whisperwood "$out/whisperwood_lanterncap_glade.png" --at 19,27 --hour 18.6
"$tool" map whisperwood "$out/whisperwood_grandmother_night.png" --at 32,34 --hour 21.5
"$tool" map whisperwood "$out/whisperwood_heron_ford.png" --at 62,27 --hour 15 --flags ww2_pell_ford
"$tool" map whisperwood "$out/whisperwood_heron_watch_night.png" --at 80,34 --hour 22 --flags ww2_beacon_lit
"$tool" map whisperwood "$out/whisperwood_tamsin_dusk.png" --at 10,37 --hour 19
"$tool" map dgn_mossdeep "$out/dgn_mossdeep_pools.png" --at 26,31
"$tool" map dgn_mossdeep "$out/dgn_mossdeep_throne.png" --at 54,21
# ---- sh: the shrine's terraced climb (sh2) and the Lantern Catacombs (dg3)
"$tool" map shrine "$out/shrine_lantern_steps.png" --at 24,18 --flags heart_lantern_lit
"$tool" map shrine "$out/shrine_pilgrims_rest.png" --at 13,26 --flags heart_lantern_lit --level 13 --markers auto
"$tool" map shrine "$out/shrine_keepers_lodge.png" --at 56,25 --flags heart_lantern_lit
"$tool" map shrine "$out/shrine_high_terrace.png" --at 36,33 --flags heart_lantern_lit,found_lantern_catacombs
"$tool" map shrine "$out/shrine_lookout.png" --at 8,34 --yaw -30 --flags heart_lantern_lit
"$tool" map shrine "$out/shrine_terraces_far.png" --at 35,26 --zoom 10.4 --flags heart_lantern_lit
"$tool" map dgn_lantern_catacombs "$out/dgn_lantern_catacombs_hall.png" --at 23,36
"$tool" map dgn_lantern_catacombs "$out/dgn_lantern_catacombs_ossuary.png" --at 25,8 --yaw 15
"$tool" map dgn_lantern_catacombs "$out/dgn_lantern_catacombs_chapel.png" --at 48,18 --yaw -15
# ---- r1: the Hollow Heart raid (the camp, the bosses, the thorn gate, the relit grove)
"$tool" map raid_hollow_heart "$out/raid_hollow_heart_camp.png" --at 9,40 --zoom 8.5 --markers auto --flags mq2_drowned_lanterns_done --level 22
"$tool" map raid_hollow_heart "$out/raid_hollow_heart_thornmaw.png" --at 51,14 --zoom 8.5 --yaw 15
"$tool" map raid_hollow_heart "$out/raid_hollow_heart_thorn_gate.png" --at 47,27 --zoom 7 --yaw -25
"$tool" map raid_hollow_heart "$out/raid_hollow_heart_pools.png" --at 49,44 --zoom 9
"$tool" map raid_hollow_heart "$out/raid_hollow_heart_pools_relit.png" --at 47,45 --zoom 10.4 --yaw -20 --flags enc_enc_r1_thornmaw,enc_enc_r1_twins,r1_grove_relit,r1_lore_twins
"$tool" map raid_hollow_heart "$out/raid_hollow_heart_mother_mire.png" --at 86,13 --zoom 8
"$tool" map raid_hollow_heart "$out/raid_hollow_heart_heart.png" --at 91,49 --zoom 10.4
# ---- sheets
"$tool" units "$out/units.png" all --tile 260
"$tool" props "$out/props_all.png" all --views g --tile 240 > /dev/null
# keep the committed set light: JPEG (quality 88) instead of PNG when Pillow is available
if python3 -c "import PIL" 2>/dev/null; then
  python3 - "$out" <<'PY'
import os, sys
from PIL import Image
d = sys.argv[1]
for f in sorted(os.listdir(d)):
    if f.endswith(".png"):
        p = os.path.join(d, f)
        Image.open(p).convert("RGB").save(p[:-4] + ".jpg", quality=88, optimize=True)
        os.remove(p)
PY
fi
