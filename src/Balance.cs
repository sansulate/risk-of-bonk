using System.Collections.Generic;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Keeps the Megabonk survivors as strong as the Risk of Rain 2 survivors playing next to them, so a horde that is
    /// fair for Commando is just as hard for Megachad.
    ///
    /// Both games' scaling are compared on one footing: power = damage per second in units of the body's base damage,
    /// times the enemies each hit reaches in a horde. Everything that grows a Risk of Rain 2 survivor (levels, items,
    /// tomes: damage, attack speed, crit) grows a Megabonk survivor the same way through the same stats, so only what is
    /// Megabonk's own needs comparing: the weapons and their levels.
    ///  - A Risk of Rain 2 kit is worth balance_reference_power (Commando about 5 per second, little area).
    ///  - A Megabonk survivor's raw power is the sum over its weapons of Megabonk's damage x shots / attack cycle x the
    ///    enemies it reaches (areas: melee, aura, projectile, lightning, pierce), from the same formulas WeaponFire uses.
    ///  - Megabonk survivors take weapon picks where Risk of Rain 2 survivors take tomes, so each weapon level may add as
    ///    much power as a tome point does (balance_pick_value of the reference).
    /// The weapon damage multiplier pulls raw power to that allowance (balance_strength), so a fresh Megachad's aura
    /// (strong, hits everything around) is cut and a lone revolver stays about as it is.
    /// Health and base damage come from Commando's numbers instead of the moveset body's (Loader and Acrid have 160 health
    /// and 20 armor), with the character's Megabonk health modifier on top (Passives).
    /// </summary>
    public static class Balance
    {
        // Commando's base stats: the standard Risk of Rain 2 survivor.
        const float BaseHealth = 110f, LevelHealth = 33f, BaseRegen = 1f, LevelRegen = 0.2f, BaseDamage = 12f, LevelDamage = 2.4f;

        /// <summary>The cloned Megabonk survivor body gets the standard survivor's base stats.</summary>
        public static void SetupBody(CharacterBody cb)
        {
            cb.baseMaxHealth = BaseHealth;
            cb.levelMaxHealth = LevelHealth;
            cb.baseRegen = BaseRegen;
            cb.levelRegen = LevelRegen;
            cb.baseDamage = BaseDamage;
            cb.levelDamage = LevelDamage;
            cb.baseArmor = 0f;
            cb.levelArmor = 0f;
            cb.baseMaxShield = 0f;
            cb.levelMaxShield = 0f;
        }

        // Risk of Rain 2 heals a body by the rise in max health inside RecalculateStats, before this mod's health bonuses
        // (Megabonk character health, HP Tomes) are added in postfixes. Without this, a Megachad starts at 110/132 and an
        // HP Tome owner heals less on every level-up. First: what RecalculateStats started from and what the game made of
        // it; last (after every other postfix): heal the rest of the rise, or cap health that is now above the maximum.
        sealed class Before { public float health, shield, gameHealth, gameShield; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterBody, Before> before = new System.Runtime.CompilerServices.ConditionalWeakTable<CharacterBody, Before>();

        [HarmonyPatch]
        static class HealthPatches
        {
            [HarmonyPrefix, HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
            static void Start(CharacterBody __instance)
            {
                var b = before.GetOrCreateValue(__instance);
                b.health = __instance.maxHealth;
                b.shield = __instance.maxShield;
            }

            [HarmonyPostfix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
            static void Game(CharacterBody __instance)
            {
                if (!before.TryGetValue(__instance, out var b)) return;
                b.gameHealth = __instance.maxHealth;
                b.gameShield = __instance.maxShield;
            }

            [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
            static void Final(CharacterBody __instance)
            {
                var hc = __instance.healthComponent;
                if (!NetworkServer.active || !hc || !before.TryGetValue(__instance, out var b)) return;
                // The game already healed max(0, its own max - the old max); the bonuses' share of the rise is the rest.
                float rise = Mathf.Max(0f, __instance.maxHealth - b.health) - Mathf.Max(0f, b.gameHealth - b.health);
                if (rise > 0.01f && hc.alive)
                {
                    hc.Networkhealth = Mathf.Min(__instance.maxHealth, hc.health + rise);
                    if (TestKeys.On && __instance.isPlayerControlled) Log.Info($"health: {__instance.GetDisplayName()} healed {rise:0.#} of the bonuses' max health rise, now {hc.health:0}/{__instance.maxHealth:0}");
                }
                float shieldRise = Mathf.Max(0f, __instance.maxShield - b.shield) - Mathf.Max(0f, b.gameShield - b.shield);
                if (shieldRise > 0.01f && hc.alive) hc.Networkshield = Mathf.Min(__instance.maxShield, hc.shield + shieldRise);
                if (hc.health > __instance.maxHealth) hc.Networkhealth = __instance.maxHealth;
                if (hc.shield > __instance.maxShield) hc.Networkshield = __instance.maxShield;
            }
        }

        public static void Init(Harmony harmony) => harmony.PatchAll(typeof(HealthPatches));

        public static float Area(WeaponRow w)
        {
            switch (w.Kind)
            {
                case "melee": return Tune.BalanceAreaMelee;
                case "aura": return Tune.BalanceAreaAura;
                case "projectile": return Tune.BalanceAreaProjectile;
                case "lightning": return Tune.BalanceAreaLightning;
                default: return w.Pierce ? Tune.BalanceAreaPierce : Mathf.Max(1f, Mathf.Min(w.Count, 3) * 0.67f);
            }
        }

        /// <summary>One weapon's power at a level: damage coefficient per second x crit x enemies reached.</summary>
        public static float Power(WeaponRow w, int level)
        {
            var s = Weapons.Stats(w, level);
            int shots = s.shots;
            float interval = w.MbBurstTime > 0f ? Mathf.Max(w.MbBurstTime / shots, w.MbMinBurstInterval) : 0f;
            float cycle = Mathf.Max(0.1f, w.MbEndCooldown + shots * interval);
            float coefficient = w.MbDamage > 0f ? s.damage * Tune.WeaponDamageScale : w.Damage * (1f + 0.25f * s.damageSteps);
            // Crits double a hit (plus the weapon's crit damage); each bounce reaches about half an enemy more in a horde.
            return coefficient * shots / cycle * (1f + Mathf.Clamp01(s.crit) * (1f + s.critDamage)) * (Area(w) + 0.5f * s.bounces);
        }

        sealed class Entry { public float time = -1f, scale = 1f, raw, allowed; }
        static readonly Dictionary<CharacterBody, Entry> cache = new Dictionary<CharacterBody, Entry>();

        /// <summary>Weapon damage multiplier for this Megabonk survivor (1 for anyone else).</summary>
        public static float WeaponScale(CharacterBody body)
        {
            if (!body || !Weapons.IsMegabonk(body)) return 1f;
            if (!cache.TryGetValue(body, out var e))
            {
                if (cache.Count > 64) cache.Clear();
                cache[body] = e = new Entry();
            }
            if (Time.time - e.time < 1f && e.time >= 0f) return e.scale;
            e.time = Time.time;

            float raw = 0f;
            int levels = 0;
            var owned = new List<int>(Weapons.Extras(body));
            int start = Weapons.StartWeapon(body);
            if (start >= 0) owned.Insert(0, start);
            foreach (int w in owned)
            {
                int level = Mathf.Max(1, Weapons.Level(body, w));
                raw += Power(Sheets.Weapons[w], level);
                levels += level;
            }
            float allowed = Tune.BalanceReferencePower * (1f + Tune.BalancePickValue * Mathf.Max(0, levels - 1));
            float scale = raw <= 0f ? 1f : Mathf.Min(Tune.BalanceMaxBoost, Mathf.Pow(allowed / raw, Mathf.Clamp01(Tune.BalanceStrength)));
            if (Mathf.Abs(scale - e.scale) > 0.01f || e.raw == 0f)
                Log.Info($"Balance: {body.GetDisplayName()} weapons {owned.Count}, levels {levels}: raw power {raw:0.#}, allowed {allowed:0.#} -> weapon damage x{scale:0.##}");
            e.scale = scale; e.raw = raw; e.allowed = allowed;
            return scale;
        }
    }
}
