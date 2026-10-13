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
    /// One attack of a Megabonk weapon (the weapon comes from the skill that started this state). Built from Risk of
    /// Rain 2's own attacks: bullets, projectiles, a melee blast with a sword swing, an aura nova, or chain lightning.
    /// Damage grows 25% per weapon level; attack speed shortens the time between attacks.
    /// </summary>
    public class WeaponFire : BaseSkillState
    {
        WeaponRow weapon;
        float duration;

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        public static GameObject Prefab(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (prefabs.TryGetValue(address, out var p)) return p;
            try { p = Addressables.LoadAssetAsync<GameObject>(address).WaitForCompletion(); }
            catch (System.Exception e) { Log.Warn($"weapon asset {address}: {e.Message}"); }
            prefabs[address] = p;
            return p;
        }

        DamageTypeCombo DamageType() => weapon.DamageType switch
        {
            "ignite" => RoR2.DamageType.IgniteOnHit,
            "bleed" => RoR2.DamageType.BleedOnHit,
            "slow" => RoR2.DamageType.SlowOnHit,
            _ => RoR2.DamageType.Generic,
        };

        public override void OnEnter()
        {
            base.OnEnter();
            weapon = Weapons.Row(activatorSkillSlot ? activatorSkillSlot.skillDef : null);
            if (weapon == null) { duration = 0.1f; return; }
            duration = Mathf.Max(0.08f, weapon.Interval / Mathf.Max(0.1f, attackSpeedStat));
            int level = Mathf.Max(1, Weapons.Level(characterBody, Weapons.Index(weapon.Id)));
            float damage = damageStat * weapon.Damage * (1f + 0.25f * (level - 1));
            if (weapon.Id == "dice") damage *= Random.Range(1, 7) / 3.5f;
            var aim = GetAimRay();
            StartAimMode(aim, 2f);
            if (!string.IsNullOrEmpty(weapon.Sound)) Util.PlaySound(weapon.Sound, gameObject);
            switch (weapon.Kind)
            {
                case "bullet": Bullet(aim, damage); break;
                case "projectile": Projectile(aim, damage); break;
                case "melee": Melee(aim, damage); break;
                case "aura": Aura(damage); break;
                case "lightning": Lightning(aim, damage); break;
            }
        }

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
                damage = damage,
                procCoefficient = 1f,
                force = 200f,
                tracerEffectPrefab = Prefab(weapon.Effect),
                hitEffectPrefab = Prefab("RoR2/Base/Characters/Commando/HitsparkCommando.prefab"),
                isCrit = RollCrit(),
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
            bool crit = RollCrit();
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
            var centre = characterBody.corePosition + dir * (weapon.Range * 0.6f);
            // The swing is shown on every machine; the hit is dealt once, by the player who owns the body.
            var fx = Prefab(weapon.Effect);
            if (fx)
            {
                var go = Object.Instantiate(fx, characterBody.corePosition + dir * 0.8f, Util.QuaternionSafeLookRotation(dir));
                go.transform.localScale *= Mathf.Max(1f, weapon.Range / 3f);
                Object.Destroy(go, 1.5f);
            }
            if (!isAuthority) return;
            new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = GetTeam(),
                position = centre,
                radius = weapon.Range,
                baseDamage = damage,
                procCoefficient = 1f,
                crit = RollCrit(),
                falloffModel = BlastAttack.FalloffModel.None,
                baseForce = 400f,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                damageType = DamageType(),
            }.Fire();
        }

        void Aura(float damage)
        {
            var fx = Prefab(weapon.Effect);
            if (fx) EffectManager.SpawnEffect(fx, new EffectData { origin = characterBody.corePosition, scale = weapon.Range }, false);
            if (!isAuthority) return;
            new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = GetTeam(),
                position = characterBody.corePosition,
                radius = weapon.Range,
                baseDamage = damage,
                procCoefficient = 0.7f,
                crit = RollCrit(),
                falloffModel = BlastAttack.FalloffModel.None,
                baseForce = 200f,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                damageType = DamageType(),
            }.Fire();
        }

        void Lightning(Ray aim, float damage)
        {
            // Orbs are the host's job; the host runs this state too.
            if (!NetworkServer.active) return;
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
            if (!target) return;
            OrbManager.instance.AddOrb(new LightningOrb
            {
                origin = aim.origin,
                damageValue = damage,
                isCrit = RollCrit(),
                bouncesRemaining = 3,
                teamIndex = GetTeam(),
                attacker = gameObject,
                procCoefficient = 0.8f,
                lightningType = LightningOrb.LightningType.Ukulele,
                damageColorIndex = DamageColorIndex.Default,
                range = 20f,
                target = target,
                bouncedObjects = new List<HealthComponent>(),
            });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && fixedAge >= duration) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
