using System.Collections.Generic;
using System.Linq;
using EntityStates;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// One attack of a Megabonk weapon (the weapon comes from the skill that started this state), timed like Megabonk's
    /// WeaponAttack: mbProjectiles shots one after another over mbBurstTime (at least mbMinBurstInterval apart), then
    /// mbEndCooldown of waiting, all divided by attack speed; the state lasts the whole cycle, so the next attack starts
    /// when it ends. Shot n goes to the n-th closest enemy in range (Megabonk's EnemyTargeting); with none in range the shot
    /// goes where you aim, or is held back for weapons that only fire at close enemies. Each shot is one of Risk of Rain
    /// 2's own attacks: bullets, projectiles, a melee blast with a sword swing, an aura nova, or chain lightning.
    /// Damage is Megabonk's (mbDamage x weapon_damage_scale); levels add Megabonk's own upgrades (Weapons.Stats).
    /// </summary>
    public class WeaponFire : BaseSkillState
    {
        WeaponRow weapon;
        float duration, shotInterval, damage, size = 1f, critBonus, lastSoundAge = -1f;
        int shots, shot;
        static int swingCount;
        static readonly Dictionary<GameObject, float> lastAuraFx = new Dictionary<GameObject, float>();

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        /// <summary>Weapon attacks so far (Melty's test mode checks it goes up).</summary>
        public static int Fired;

        public static GameObject Prefab(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (prefabs.TryGetValue(address, out var p)) return p;
            p = Load(address);
            if (!p && FindByName(address) is string found)
            {
                Log.Warn($"weapon asset {address} not found; using {found}");
                p = Load(found);
            }
            if (!p) Log.Warn($"weapon asset {address} not found");
            prefabs[address] = p;
            return p;
        }

        static GameObject Load(string address)
        {
            try { return Addressables.LoadAssetAsync<GameObject>(address).WaitForCompletion(); }
            catch (System.Exception) { return null; }
        }

        static Dictionary<string, string> byName;

        /// <summary>The game's real address for a prefab with the same file name (addresses move between game versions).</summary>
        static string FindByName(string address)
        {
            if (byName == null)
            {
                byName = new Dictionary<string, string>();
                foreach (var locator in Addressables.ResourceLocators)
                    foreach (var key in locator.Keys)
                        if (key is string s && s.EndsWith(".prefab"))
                        {
                            string n = s.Substring(s.LastIndexOf('/') + 1);
                            if (!byName.ContainsKey(n)) byName[n] = s;
                        }
            }
            return byName.TryGetValue(address.Substring(address.LastIndexOf('/') + 1), out var a) && a != address ? a : null;
        }

        DamageTypeCombo DamageType() => weapon.DamageType switch
        {
            "ignite" => RoR2.DamageType.IgniteOnHit,
            "bleed" => RoR2.DamageType.BleedOnHit,
            "slow" => RoR2.DamageType.SlowOnHit,
            "poison" => RoR2.DamageType.PoisonOnHit,
            _ => RoR2.DamageType.Generic,
        };

        public override void OnEnter()
        {
            base.OnEnter();
            weapon = Weapons.Row(activatorSkillSlot ? activatorSkillSlot.skillDef : null);
            if (weapon == null)
            {
                if (TestKeys.On) Log.Warn($"weapon state without a weapon: {(activatorSkillSlot && activatorSkillSlot.skillDef ? activatorSkillSlot.skillDef.skillName : "no skill")}");
                duration = 0.1f;
                return;
            }
            float speed = Mathf.Max(0.1f, attackSpeedStat);
            int level = Mathf.Max(1, Weapons.Level(characterBody, Weapons.Index(weapon.Id)));
            var stats = Weapons.Stats(weapon, level);
            shots = stats.shots;
            size = stats.size + Passives.SizeBonus(characterBody);
            critBonus = stats.crit;
            shotInterval = weapon.MbBurstTime > 0f ? Mathf.Max(weapon.MbBurstTime / shots, weapon.MbMinBurstInterval) / speed : 0f;
            duration = Mathf.Max(0.1f, weapon.MbEndCooldown / speed + shots * shotInterval);
            float coefficient = weapon.MbDamage > 0f ? stats.damage * Tune.WeaponDamageScale : weapon.Damage * (1f + 0.25f * stats.damageSteps);
            damage = damageStat * coefficient;
            if (TestKeys.On) Log.Info($"weapon {weapon.Id} ({weapon.Kind}) lv {level}, {damage:0.#} damage x {shots} over {shots * shotInterval:0.##} s, cycle {duration:0.##} s, authority {isAuthority}");
            FireDue();
        }

        /// <summary>Fires every shot of the burst that is due by now.</summary>
        void FireDue()
        {
            while (shot < shots && fixedAge >= shot * shotInterval - 0.0001f)
            {
                Shoot(shot);
                shot++;
            }
        }

        float Range => weapon.MbRange > 0f ? Mathf.Min(weapon.MbRange, Mathf.Max(weapon.Range, 40f)) : Mathf.Max(weapon.Range * 1.5f, 6f);

        /// <summary>The n-th closest enemy in range that can be seen (n wraps around), or none.</summary>
        HurtBox Target(int n)
        {
            var search = new BullseyeSearch
            {
                searchOrigin = characterBody.corePosition,
                teamMaskFilter = TeamMask.GetEnemyTeams(GetTeam()),
                filterByLoS = true,
                maxDistanceFilter = Range,
                maxAngleFilter = 180f,
                sortMode = BullseyeSearch.SortMode.Distance,
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(gameObject);
            var found = search.GetResults().ToList();
            return found.Count == 0 ? null : found[n % found.Count];
        }

        void Shoot(int n)
        {
            var aim = GetAimRay();
            var target = weapon.Kind == "aura" ? null : Target(n);
            if (target) aim.direction = (target.transform.position - aim.origin).normalized;
            else if (weapon.MbOnlyClose) return; // Megabonk's revolver waits for an enemy in range
            float hit = damage;
            if (weapon.Id == "dice") hit *= Random.Range(1, 7) / 3.5f;
            Fired++;
            if (fixedAge - lastSoundAge > 0.05f || lastSoundAge < 0f)
            {
                lastSoundAge = fixedAge;
                WeaponVisuals.Fired(gameObject, weapon, characterBody.corePosition); // Megabonk's sound, and its projectile look
            }
            WeaponVisuals.Attack(characterBody, weapon, aim, target, weapon.Kind == "aura" || weapon.Kind == "melee" ? Reach : Range, size);
            switch (weapon.Kind)
            {
                case "bullet": Bullet(aim, hit); break;
                case "projectile": Projectile(aim, hit); break;
                case "melee": Melee(aim, hit); break;
                case "aura": Aura(hit); break;
                case "lightning": Lightning(aim, hit, target); break;
            }
        }

        bool Crit() => Util.CheckRoll(critStat + critBonus * 100f, characterBody.master);

        /// <summary>Melee and aura reach, grown by the weapon's size upgrades.</summary>
        float Reach => weapon.Range * size;

        void Bullet(Ray aim, float damage)
        {
            if (!isAuthority) return;
            new BulletAttack
            {
                owner = gameObject,
                weapon = gameObject,
                origin = aim.origin,
                aimVector = aim.direction,
                minSpread = 0f,
                maxSpread = weapon.Spread,
                bulletCount = (uint)Mathf.Max(1, weapon.Count),
                damage = weapon.Count > 1 ? damage * 2f / weapon.Count : damage, // Megabonk's shotgun cone hits each enemy once
                procCoefficient = weapon.MbProc,
                force = 200f,
                // Megabonk's bullet and hit effects (WeaponVisuals) replace Risk of Rain 2's tracer and hit spark.
                tracerEffectPrefab = WeaponVisuals.HasShot(weapon) ? null : Prefab(weapon.Effect),
                hitEffectPrefab = WeaponVisuals.HasShot(weapon) ? null : Prefab("RoR2/Base/Commando/HitsparkCommando.prefab"),
                isCrit = Crit(),
                radius = 0.4f,
                smartCollision = true,
                maxDistance = weapon.Range,
                stopperMask = weapon.Pierce ? LayerIndex.world.mask : (LayerMask)(LayerIndex.world.mask | LayerIndex.entityPrecise.mask),
                damageType = DamageType(),
                falloffModel = BulletAttack.FalloffModel.None,
            }.Fire();
        }

        void Projectile(Ray aim, float damage)
        {
            if (!isAuthority) return;
            var prefab = Prefab(weapon.Prefab);
            if (!prefab) return;
            bool crit = Crit();
            for (int i = 0; i < Mathf.Max(1, weapon.Count); i++)
            {
                var dir = weapon.Spread > 0f ? Util.ApplySpread(aim.direction, 0f, weapon.Spread, 1f, 1f) : aim.direction;
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = prefab,
                    position = aim.origin,
                    rotation = Util.QuaternionSafeLookRotation(dir),
                    owner = gameObject,
                    damage = damage,
                    force = 300f,
                    crit = crit,
                    damageTypeOverride = weapon.DamageType == "none" ? (DamageTypeCombo?)null : DamageType(),
                });
            }
        }

        void Melee(Ray aim, float damage)
        {
            var dir = aim.direction;
            var centre = characterBody.corePosition + dir * (Reach * 0.6f);
            // The swing is shown on every machine; the hit is dealt once, by the player who owns the body.
            var fx = WeaponVisuals.HasShot(weapon) ? null : Prefab(weapon.Effect); // Megabonk's slash replaces Risk of Rain 2's
            // Mercenary's slash animation where the body has it (alternating swings), with the slash on the blade's muzzle.
            string swing = (swingCount++ & 1) == 0 ? "GroundLight1" : "GroundLight2";
            var animator = GetModelAnimator();
            bool hasSwing = false;
            if (animator)
            {
                int hash = Animator.StringToHash(swing);
                foreach (var layer in new[] { "Gesture, Additive", "Gesture, Override" })
                {
                    int li = animator.GetLayerIndex(layer);
                    if (li < 0 || !animator.HasState(li, hash)) continue;
                    PlayCrossfade(layer, swing, "GroundLight.playbackRate", duration, 0.05f);
                    hasSwing = true;
                }
            }
            var locator = GetModelChildLocator();
            if (fx && hasSwing && locator && locator.FindChild(swing))
                EffectManager.SimpleMuzzleFlash(fx, gameObject, swing, false);
            else if (fx)
                EffectManager.SpawnEffect(fx, new EffectData
                {
                    origin = characterBody.corePosition + dir * 0.8f,
                    rotation = Util.QuaternionSafeLookRotation(dir),
                    scale = Mathf.Max(1f, Reach / 3f),
                }, false);
            if (!isAuthority) return;
            new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = GetTeam(),
                position = centre,
                radius = Reach,
                baseDamage = damage,
                procCoefficient = weapon.MbProc,
                crit = Crit(),
                falloffModel = BlastAttack.FalloffModel.None,
                baseForce = 400f,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                damageType = DamageType(),
            }.Fire();
        }

        void Aura(float damage)
        {
            // Megabonk's aura is a steady ring that hurts every tick; the nova shows about once a second, not on every tick.
            var fx = WeaponVisuals.HasShot(weapon) ? null : Prefab(weapon.Effect);
            if (fx && (!lastAuraFx.TryGetValue(gameObject, out float last) || Time.time - last > 0.9f))
            {
                lastAuraFx[gameObject] = Time.time;
                EffectManager.SpawnEffect(fx, new EffectData { origin = characterBody.corePosition, scale = Reach }, false);
            }
            if (!isAuthority) return;
            new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = GetTeam(),
                position = characterBody.corePosition,
                radius = Reach,
                baseDamage = damage,
                procCoefficient = weapon.MbProc,
                crit = Crit(),
                falloffModel = BlastAttack.FalloffModel.None,
                baseForce = 200f,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                damageType = DamageType(),
            }.Fire();
        }

        void Lightning(Ray aim, float damage, HurtBox target)
        {
            // Orbs are the host's job; the host runs this state too.
            if (!NetworkServer.active) return;
            if (!target) target = FindLightningTarget(aim);
            if (!target) return;
            OrbManager.instance.AddOrb(new LightningOrb
            {
                origin = aim.origin,
                damageValue = damage,
                isCrit = Crit(),
                bouncesRemaining = 3,
                teamIndex = GetTeam(),
                attacker = gameObject,
                procCoefficient = weapon.MbProc,
                lightningType = LightningOrb.LightningType.Ukulele,
                damageColorIndex = DamageColorIndex.Default,
                range = 20f,
                target = target,
                bouncedObjects = new List<HealthComponent>(),
            });
        }

        HurtBox FindLightningTarget(Ray aim)
        {
            var search = new BullseyeSearch
            {
                searchOrigin = aim.origin,
                searchDirection = aim.direction,
                teamMaskFilter = TeamMask.GetEnemyTeams(GetTeam()),
                filterByLoS = false,
                maxDistanceFilter = weapon.Range,
                maxAngleFilter = 60f,
                sortMode = BullseyeSearch.SortMode.Angle,
            };
            search.RefreshCandidates();
            var target = search.GetResults().FirstOrDefault();
            if (!target)
            {
                search.maxAngleFilter = 180f;
                search.sortMode = BullseyeSearch.SortMode.Distance;
                search.RefreshCandidates();
                target = search.GetResults().FirstOrDefault();
            }
            return target;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (weapon != null) FireDue();
            if (isAuthority && fixedAge >= duration) outer.SetNextStateToMain();
        }

        // Until the attack and its wait are over nothing (not even the same weapon) may restart it; then anything may.
        public override InterruptPriority GetMinimumInterruptPriority() => fixedAge < duration ? InterruptPriority.PrioritySkill : InterruptPriority.Any;
    }
}
