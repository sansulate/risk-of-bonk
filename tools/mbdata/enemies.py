# Decodes Megabonk EnemyData (and AnimatedMeshScriptableObject) assets dumped raw by mbread
# (RAW=1 BIN=dir ... EnemyData,AnimatedMeshScriptableObject):  python enemies.py <dir> [out.json]
from mbraw import *

EE = ('Skeleton GoldenSkeleton XpSkeleton ArmoredSkeleton SkeletonDusty ArmoredSkeletonDusty Ghoul MinibossPig Mummy Slime Test Test2 '
      'Goblin GoblinStrong GoblinTank MinibossGolem Ghost GreaterGhost SkeletonMage Ent1 Ent2 Ent3 BoomerSpider Golem Bee MinibossGolemSand '
      'Scorpion MinibossScorpion Wisp CactusShooter ScorpionMedium MummyTank MummyAncient Tumblebone Pharaoh1 Pharaoh2 Pharaoh3 Bandit Bush '
      'GhostRed GhostPurple Zombie Head GhostKing GhostGrave1 GhostGrave2 GhostGrave3 GhostGrave4 GhostInvincible Ghostham CalciumDad '
      'FrogGreen FrogBlue FrogRed').split()
MAPS = {1: 'Forest', 2: 'Desert', 4: 'Hell', 8: 'Graveyard'}

anims = {}
for p in sorted(glob.glob(os.path.join(sys.argv[1], 'AnimatedMeshScriptableObject__*.bin'))):
    b = open(p, 'rb').read(); r = R(b); name = header(r)
    pid = int(p.rsplit('_', 1)[1].split('.')[0])
    d = {'name': name, 'fps': r.i(), 'animations': []}
    for _ in range(r.i()):
        an = r.st(); meshes = [r.pptr()[1] for _ in range(r.i())]
        d['animations'].append({'name': an, 'meshes': meshes})
    anims[pid] = d

out = {}
for p in sorted(glob.glob(os.path.join(sys.argv[1], 'EnemyData__*.bin'))):
    b = open(p, 'rb').read(); r = R(b); name = header(r)
    d = {'enemy': EE[r.i()], 'material': r.pptr()[1], 'animation': r.pptr()[1]}
    d['rendererOffset'] = [r.f(), r.f(), r.f()]; d['rendererRotation'] = [r.f(), r.f(), r.f()]; d['rendererScale'] = r.f()
    d['colliderRadius'] = r.f(); d['overrideHeight'] = r.f(); d['colliderCenter'] = [r.f(), r.f(), r.f()]
    d['hp'] = r.i(); d['damage'] = r.i(); d['shield'] = r.i(); d['isPoison'] = r.bo(); d['knockbackResistance'] = r.f(); d['nukeProtection'] = r.bo()
    d['mass'] = r.f(); d['speed'] = r.f(); d['isFlying'] = r.bo(); d['teleportCooldown'] = r.f(); d['isRunningFromPlayer'] = r.bo()
    d['minStay'] = r.f(); d['maxStay'] = r.f(); d['xp'] = r.i(); d['despawnTime'] = r.f(); r.o += 4  # canBeElite, canBeExecuted (two bytes, padded)
    d['maxSuckTime'] = r.f()
    # specialAttacks: [SerializeReference] array -> managed reference ids (8 bytes each)
    n = r.i(); r.o += 8 * n if n < 50 else 0
    d['specialAttacks'] = n
    m = r.i(); d['maps'] = [v for k, v in MAPS.items() if m & k]; d['minStage'] = r.i(); d['creditCost'] = r.f(); d['canSpawnAfterTime'] = r.f()
    a = anims.get(d['animation'])
    d['anim'] = a['name'] if a else None
    d['clips'] = {x['name']: len(x['meshes']) for x in a['animations']} if a else {}
    out[name] = d
if len(sys.argv) > 2: json.dump({'enemies': out, 'animations': anims}, open(sys.argv[2], 'w'), indent=1)
for n, d in out.items():
    print(f"{n:24} {d['enemy']:20} maps {','.join(d['maps']):18} st{d['minStage']} cost {d['creditCost']:6} after {d['canSpawnAfterTime']:6} hp {d['hp']:5} spd {d['speed']:5} fly {d['isFlying']:d} scale {d['rendererScale']} anim {d['anim']} {d['clips']}")
