using System.Collections.Generic;
using System.Linq;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's bounce upgrades (ProjectileBounces: Revolver, Bone, Bluetooth Blade): when a shot reaches the enemy it was
    /// aimed at, it jumps on to the nearest enemy it has not hit yet (within 15 m), once per bounce, flying there with the
    /// weapon's own Megabonk shot effect. Every machine shows the hops; the player who owns the body deals their damage
    /// (a bullet from one enemy to the next, as Risk of Rain 2 networks bullets).
    /// </summary>
    public sealed class Bounces : MonoBehaviour
    {
        const float Range = 15f;

        sealed class Hop
        {
            public float at, damage, proc, size, speed;
            public CharacterBody body;
            public WeaponRow weapon;
            public Vector3 from;
            public readonly List<HealthComponent> hit = new List<HealthComponent>();
            public bool crit, authority;
            public int left;
            public DamageTypeCombo type;
        }

        static Bounces instance;
        static readonly List<Hop> hops = new List<Hop>();

        /// <summary>Starts the bounces of a shot fired at target; the first hop leaves once the shot arrives (travel s).</summary>
        public static void Start(CharacterBody body, WeaponRow weapon, HurtBox target, float damage, bool crit, int bounces, float speed,
                                 float travel, bool authority, DamageTypeCombo type, float proc, float size)
        {
            if (!body || !target || !target.healthComponent || bounces <= 0) return;
            if (!instance)
            {
                var go = new GameObject("RiskOfBonkBounces");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Bounces>();
            }
            var h = new Hop
            {
                at = Time.time + travel, body = body, weapon = weapon, from = target.transform.position, damage = damage, crit = crit,
                left = bounces, authority = authority, type = type, proc = proc, size = size, speed = Mathf.Max(20f, speed),
            };
            h.hit.Add(target.healthComponent);
            hops.Add(h);
        }

        void Update()
        {
            for (int i = hops.Count - 1; i >= 0; i--)
            {
                var h = hops[i];
                if (Time.time < h.at) continue;
                hops.RemoveAt(i);
                if (Jump(h)) hops.Add(h);
            }
        }

        /// <summary>One hop; true when the shot bounces again later.</summary>
        static bool Jump(Hop h)
        {
            if (!h.body || !h.body.teamComponent) return false;
            var search = new BullseyeSearch
            {
                searchOrigin = h.from,
                teamMaskFilter = TeamMask.GetEnemyTeams(h.body.teamComponent.teamIndex),
                filterByLoS = false,
                maxDistanceFilter = Range,
                maxAngleFilter = 180f,
                sortMode = BullseyeSearch.SortMode.Distance,
            };
            search.RefreshCandidates();
            var next = search.GetResults().FirstOrDefault(hb => hb && hb.healthComponent && hb.healthComponent.alive && !h.hit.Contains(hb.healthComponent));
            if (!next) return false;
            var to = next.transform.position;
            var dir = to - h.from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return false;
            dir /= dist;
            WeaponVisuals.Hop(h.weapon, h.from, to, h.size, h.speed);
            if (TestKeys.On) Log.Info($"bounce: {h.weapon.Id} {dist:0.#} m to {next.healthComponent.name}, {h.left - 1} left");
            if (h.authority)
                new BulletAttack
                {
                    owner = h.body.gameObject,
                    weapon = h.body.gameObject,
                    // From just past the enemy it bounced off, so that one is not hit again.
                    origin = h.from + dir * Mathf.Min(1.5f, dist * 0.5f),
                    aimVector = dir,
                    minSpread = 0f,
                    maxSpread = 0f,
                    bulletCount = 1,
                    damage = h.damage,
                    procCoefficient = h.proc,
                    force = 100f,
                    isCrit = h.crit,
                    radius = 0.6f,
                    smartCollision = true,
                    maxDistance = dist + 1f,
                    stopperMask = LayerIndex.world.mask | LayerIndex.entityPrecise.mask,
                    damageType = h.type,
                    falloffModel = BulletAttack.FalloffModel.None,
                }.Fire();
            h.hit.Add(next.healthComponent);
            h.from = to;
            if (--h.left <= 0) return false;
            h.at = Time.time + dist / h.speed;
            return true;
        }
    }
}
