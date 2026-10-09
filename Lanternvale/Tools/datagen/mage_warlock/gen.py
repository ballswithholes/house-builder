import sys
sys.path.insert(0, '.')
from mwcommon import write_bundle
import mage, warlock

ROOT = '/home/user/house-builder/Lanternvale/Assets/Lanternvale/Resources/Data/classes/'
from conv import CONVENTIONS
mage.BUNDLE = {"_note": "Mage - WoW Classic 1.12 kit translated to Lanternvale. Generated data; see _conventions.",
               "_conventions": CONVENTIONS, **mage.BUNDLE}
warlock.BUNDLE = {"_note": "Warlock - WoW Classic 1.12 kit translated to Lanternvale, with demons and soul shards. Generated data; see _conventions.",
                  "_conventions": CONVENTIONS, **warlock.BUNDLE}
write_bundle(ROOT + 'mage.json', mage.BUNDLE)
write_bundle(ROOT + 'warlock.json', warlock.BUNDLE)
for name, b in [('mage', mage.BUNDLE), ('warlock', warlock.BUNDLE)]:
    ab = b['abilities']
    print(name, 'abilities', len(ab), 'trainable', sum(1 for a in ab if not a.get('hidden') and not a.get('fromTalent')),
          'talent', sum(1 for a in ab if a.get('fromTalent')), 'hidden', sum(1 for a in ab if a.get('hidden')),
          'auras', len(b['auras']), 'items', len(b['items']), 'creatures', len(b.get('creatures', [])),
          'specials', len(b['specials']), 'talents', [len(t['talents']) for t in b['talentTrees']])
