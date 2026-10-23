import sys,glob,os,re
sys.path.insert(0,os.path.dirname(__file__))
from mbraw import *
B=sys.argv[1]
ids={}
for p in glob.glob(os.path.join(B,'*.bin')):
    m=re.match(r'(\w+)__(.+)__sharedassets1\.assets_(\d+)\.bin',os.path.basename(p))
    if m: ids[int(m.group(3))]=m.group(2)
STAT="MaxHealth HealthRegen Shield Thorns Armor Evasion Evolve DamageReductionMultiplier DamageCooldownMultiplier SizeMultiplier DurationMultiplier ProjectileSpeedMultiplier DamageMultiplier Unused0 EffectDurationMultiplier AttackSpeed Projectiles Lifesteal CritChance CritDamage FireDamage IceDamage LightningDamage EliteDamageMultiplier KnockbackMultiplier MoveSpeedMultiplier JumpHeight FallDamageReduction Slam PickupRange Luck GoldIncreaseMultiplier XpIncreaseMultiplier ChestIncreaseMultiplier ChestPriceMultiplier ShopPriceReduction Holiness Wickedness Difficulty EliteSpawnIncrease PowerupBoostMultiplier PowerupChance BurnChance FreezeChance WeaponBurstCooldown ProjectileBounces ExtraJumps".split()
for p in sorted(glob.glob(os.path.join(B,'CharacterData__*.bin'))):
    b=open(p,'rb').read(); r=R(b); name=header(r)
    try:
        unlockable(r)
    except AssertionError as e:
        print(name,'skip',e); continue
    ech=r.i(); r.pptr()
    n=r.i(); mods=[]
    for _ in range(n):
        st=r.i(); ty=r.i(); v=r.f(); mods.append(f"{STAT[st] if st<len(STAT) else st}{'+' if ty==0 else 'x' if ty==1 else '='}{v}")
    r.pptr()
    n=r.i(); [r.pptr() for _ in range(n)]
    r.pptr()
    w=r.pptr()[1]; pa=r.pptr()[1]; ch=r.f(); cw=r.f()
    print(f"{name:12} weapon={ids.get(w,w):22} passive={ids.get(pa,pa):14} collider {ch}x{cw} mods {' '.join(mods)}")
