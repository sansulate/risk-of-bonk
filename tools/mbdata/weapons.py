from mbraw import *
EW="None FireStaff Bone Sword Revolver Aura Axe Bow Aegis Test LightningStaff Flamewalker Rockets Bananarang Tornado Dexecutioner Sniper Frostwalker SpaceNoodle DragonsBreath Chunkers Mine PoisonFlask BlackHole Katana BloodMagic BluetoothDagger Dice HeroSword CorruptSword Shotgun Scythe".split()
EL=None
out={}
for p in sorted(glob.glob(os.path.join(sys.argv[1],'WeaponData__*.bin'))):
    b=open(p,'rb').read(); r=R(b); name=header(r); d=unlockable(r)
    d['eWeapon']=EW[r.i()+1]; r.pptr()
    d['onlySpawnWhenCloseEnemies']=r.bo()
    for k in 'damage knockback critChance'.split(): d[k]=r.f()
    d['element']=r.i(); d['projectiles']=r.i(); d['projectileBounces']=r.i()
    for k in 'attackDuration maxDuration maxSizeMultiplier effectDuration projectileSpeed endCooldown burstTime minBurstInterval'.split(): d[k]=r.f()
    d['canBounce']=r.bo(); d['amplificationMode']=r.i(); d['procCoefficient']=r.f()
    for k in 'useVision canMultiHit hasCrosshair'.split(): d[k]=r.bo()
    d['spawnProjectileRange']=r.f(); d['isAura']=r.bo(); d['spawnOffset']=[r.f(),r.f(),r.f()]
    d['attack']=r.pptr()[1]; d['upgradeData']=r.pptr()[1]
    out[name]=d
json.dump(out,open(sys.argv[2],'w'),indent=1)
keys=['eWeapon','damage','projectiles','burstTime','minBurstInterval','endCooldown','attackDuration','maxDuration','projectileSpeed','knockback','critChance','element','projectileBounces','spawnProjectileRange','isAura','useVision','canMultiHit','procCoefficient','effectDuration','maxSizeMultiplier']
print(' '.join(k[:9] for k in keys))
for n,d in out.items(): print(n[6:18].ljust(13),' '.join(str(d[k])[:9].ljust(9) for k in keys[1:]))
