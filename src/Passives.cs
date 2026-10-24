using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk characters' stat modifiers (characters.json mbStats) and passives (mbPassive), with the numbers from Megabonk's
    /// CharacterData and PassiveAbility classes. Megabonk passives grow with the player's level ("per level" = Risk of Rain 2's
    /// level). Everything that only depends on the level, the character and the inventory is worked out the same on every
    /// machine (health, armor, shield, crit, attack speed); damage and speed bonuses that change from moment to moment are
    /// worked out where the body is simulated, which is where its attacks and movement are computed; hits, kills, blocks and
    /// explosions are the host's.
    /// </summary>
    public static class Passives
    {
        /// <summary>Megabonk's base health; MaxHealth and Shield modifiers are flat amounts on top of it.</summary>
        const float MbBaseHealth = 100f;

        public static ItemDef FlexItem, GambaItem;
        public static BuffDef MarkBuff;

        public static ItemDef[] CreateItems()
        {
            ItemDef Make(string id, string name)
            {
                var d = ScriptableObject.CreateInstance<ItemDef>();
                d.name = "RiskOfBonk_" + id;
                d.nameToken = "RISKOFBONK_ITEM_" + id.ToUpperInvariant() + "_NAME";
                d.pickupToken = d.descriptionToken = d.loreToken = d.nameToken;
                Plugin.Tokens[d.nameToken] = name;
#pragma warning disable CS0618
                d.tier = ItemTier.NoTier;
#pragma warning restore CS0618
                d.hidden = true;
                d.canRemove = false;
                d.tags = new[] { ItemTag.CannotCopy, ItemTag.CannotSteal, ItemTag.AIBlacklist };
                return d;
            }
            FlexItem = Make("flex_stacks", "Flexes");
            GambaItem = Make("gamba_rolls", "Gamba rolls");
            return new[] { FlexItem, GambaItem };
        }

        public static BuffDef[] CreateBuffs()
        {
            MarkBuff = ScriptableObject.CreateInstance<BuffDef>();
            MarkBuff.name = "RiskOfBonk_BullseyeMark";
            MarkBuff.buffColor = new Color(1f, 0.25f, 0.2f);
            MarkBuff.canStack = false;
            MarkBuff.isDebuff = false; // a mark, not a debuff (Death Mark and the like don't count it)
            return new[] { MarkBuff };
        }

        // ---------------- character data ----------------

        static readonly Dictionary<CharacterRow, Dictionary<string, float>> statCache = new Dictionary<CharacterRow, Dictionary<string, float>>();

        /// <summary>The character's Megabonk stat modifier (mbStats), 0 when it has none.</summary>
        /// <summary>Extra XP gem pickup radius (m) from the Megabonk character's PickupRange stat.</summary>
        public static float PickupRange(CharacterBody body) => Mathf.Max(0f, Stat(Weapons.Character(body), "PickupRange"));

        public static float Stat(CharacterRow row, string stat)
        {
            if (row == null) return 0f;
            if (!statCache.TryGetValue(row, out var d))
            {
                d = new Dictionary<string, float>();
                foreach (var s in row.MbStats)
                {
                    int c = s.IndexOf(':');
                    if (c > 0 && float.TryParse(s.Substring(c + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                        d[s.Substring(0, c)] = d.TryGetValue(s.Substring(0, c), out float had) ? had + v : v;
                }
                statCache[row] = d;
            }
            return d.TryGetValue(stat, out float r) ? r : 0f;
        }

        static float Level(CharacterBody b) => Mathf.Max(1f, b.level);

        static void Note(string s) { if (TestKeys.On) Log.Info("passive: " + s); }

        static CharacterRow Row(CharacterBody b, string passive)
        {
            var row = Weapons.Character(b);
            return row != null && row.MbPassive == passive ? row : null;
        }

        static bool Has(CharacterBody b, string passive) => Row(b, passive) != null;

        /// <summary>Chance to dodge a hit from the character and its passive (Shadowstep: +0.5% per level).</summary>
        public static float Evasion(CharacterBody b)
        {
            var row = Weapons.Character(b);
            if (row == null) return 0f;
            return Stat(row, "Evasion") + (row.MbPassive == "Shadowstep" ? 0.005f * Level(b) : 0f);
        }

        /// <summary>Luck for level-up rarities (Fox: base 15%, RNG Blessing +1.5% per level).</summary>
        public static float Luck(CharacterBody b)
        {
            var row = Weapons.Character(b);
            if (row == null) return 0f;
            return Stat(row, "Luck") + (row.MbPassive == "RngBlessing" ? 0.015f * Level(b) : 0f);
        }

        /// <summary>Weapon size (melee and aura reach): Amog's +20%, Enduring's +0.75% per level.</summary>
        public static float SizeBonus(CharacterBody b)
        {
            var row = Weapons.Character(b);
            if (row == null) return 0f;
            return Stat(row, "SizeMultiplier") + (row.MbPassive == "Enduring" ? Mathf.Min(4f, 0.0075f * Level(b)) : 0f);
        }

        /// <summary>XP from gems this body collects: Spaceman +5%, Quantum XP Theory +1% per level.</summary>
        public static float XpMultiplier(CharacterBody b)
        {
            var row = Weapons.Character(b);
            if (row == null) return 1f;
            return 1f + Stat(row, "XpIncreaseMultiplier") + (row.MbPassive == "Quantum" ? 0.01f * Level(b) : 0f);
        }

        static float GoldMultiplier(CharacterBody b)
        {
            var row = Weapons.Character(b);
            if (row == null) return 1f;
            return 1f + Stat(row, "GoldIncreaseMultiplier") + (row.MbPassive == "Stonks" ? 0.01f * Level(b) : 0f);
        }

        /// <summary>Curse: Sir Chadwell's base 10% difficulty and +1% per level (more enemies).</summary>
        static float Difficulty(CharacterBody b)
        {
            var row = Weapons.Character(b);
            if (row == null) return 0f;
            return Stat(row, "Difficulty") + (row.MbPassive == "Curse" ? 0.01f * Level(b) : 0f);
        }

        /// <summary>Stonks' damage from gold, Megabonk's tiers: +20% per 1000 gold up to 10k, 8.5% to 200k, 6% to 1m, 3.5% beyond.</summary>
        static float StonksDamage(float gold)
        {
            float d = 0f;
            d += Mathf.Min(gold, 10000f) / 1000f * 0.2f;
            if (gold > 10000f) d += (Mathf.Min(gold, 200000f) - 10000f) / 1000f * 0.085f;
            if (gold > 200000f) d += (Mathf.Min(gold, 1000000f) - 200000f) / 1000f * 0.06f;
            if (gold > 1000000f) d += (gold - 1000000f) / 1000f * 0.035f;
            return d;
        }

        // ---------------- per-body state ----------------

        /// <summary>A Megabonk body's passive state that changes over time.</summary>
        public sealed class State : MonoBehaviour
        {
            CharacterBody body;
            CharacterRow row;
            /// <summary>Damage and move speed bonuses that change over time (fractions), as last applied to the stats.</summary>
            public float dynamicDamage, speedDemon;
            float lastHealth = -1f, nextTick, nextSecond, loggedLevel;
            // host
            public float flexReadyAt, zapCharge, nextMark, nextServerTick;
            Vector3 lastPos;
            bool hasLastPos;

            void Awake()
            {
                body = GetComponent<CharacterBody>();
                row = Weapons.Character(body);
            }

            void FixedUpdate()
            {
                if (!body || row == null) return;
                float now = Time.fixedTime;
                if (body.hasEffectiveAuthority) AuthorityTick(now);
                if (NetworkServer.active) ServerTick(now);
            }

            void AuthorityTick(float now)
            {
                var hc = body.healthComponent;
                if (!hc || !hc.alive) return;
                float L = Level(body);
                if (TestKeys.On && L != loggedLevel)
                {
                    loggedLevel = L;
                    Note($"{row.Id} ({row.MbPassive}) lv {L}: hp {body.maxHealth:0} shield {body.maxShield:0} armor {body.armor:0.#} crit {body.crit:0.#} critx {body.critMultiplier:0.##} " +
                         $"attack speed {body.attackSpeed:0.##} damage {body.damage:0.#} speed {body.moveSpeed:0.##} evasion {Evasion(body):0.###} luck {Luck(body):0.###} size +{SizeBonus(body):0.###} xp x{XpMultiplier(body):0.##}");
                }
                // Speed Demon: +0.85% speed every second up to +150%; each hit takes away 1.5 / (4 + level/10, at most 15) of it.
                if (row.MbPassive == "SpeedDemon")
                {
                    if (lastHealth >= 0f && hc.combinedHealth < lastHealth - 0.5f)
                    {
                        int hits = Mathf.Min(15, 4 + Mathf.FloorToInt(L / 10f));
                        speedDemon = Mathf.Max(0f, speedDemon - 1.5f / hits);
                    }
                    if (now >= nextSecond) { nextSecond = now + 1f; speedDemon = Mathf.Min(1.5f, speedDemon + 0.0085f); }
                }
                lastHealth = hc.combinedHealth;
                if (now < nextTick) return;
                nextTick = now + 0.5f;
                float d = 0f;
                switch (row.MbPassive)
                {
                    case "Float":
                        if (body.characterMotor && !body.characterMotor.isGrounded) d = 0.01f * L;
                        break;
                    case "LockIn":
                        d = (1f - hc.combinedHealthFraction) * (1f + 0.01f * L);
                        break;
                    case "SpeedDemon":
                    {
                        // Damage by how fast you go: up to 0.5 x the speed bonus + 0.75% per level at full speed.
                        float speed = body.characterMotor ? new Vector2(body.characterMotor.velocity.x, body.characterMotor.velocity.z).magnitude : 0f;
                        float ratio = Mathf.Clamp01(speed * 0.75f / Mathf.Max(1f, body.moveSpeed));
                        d = ratio * (0.5f * speedDemon + 0.0075f * L);
                        break;
                    }
                    case "Stonks":
                        d = body.master ? StonksDamage(body.master.money) : 0f;
                        break;
                    case "Enduring":
                        d = Mathf.Min(3f, 0.015f * FrozenEnemies(body.corePosition));
                        break;
                }
                if (Mathf.Abs(d - dynamicDamage) > 0.005f || row.MbPassive == "SpeedDemon")
                {
                    if (Mathf.Abs(d - dynamicDamage) > 0.05f) Note($"{row.Id} {row.MbPassive} damage +{d:0.###}{(row.MbPassive == "SpeedDemon" ? $" speed +{speedDemon:0.###}" : "")}");
                    dynamicDamage = d;
                    body.MarkAllStatsDirty();
                }
            }

            void ServerTick(float now)
            {
                var hc = body.healthComponent;
                if (!hc || !hc.alive) { hasLastPos = false; return; }
                float L = Level(body);
                switch (row.MbPassive)
                {
                    case "Zoombonk":
                        // Charge 0.6% per metre moved (checked every half second); full charge zaps on the next hit.
                        if (now >= nextServerTick)
                        {
                            nextServerTick = now + 0.5f;
                            var p = body.footPosition;
                            if (hasLastPos) zapCharge = Mathf.Min(1f, zapCharge + Vector3.Distance(p, lastPos) * 0.006f);
                            lastPos = p;
                            hasLastPos = true;
                        }
                        break;
                    case "Bullseye":
                        // Mark a random enemy nearby every 5 s (0.04 s quicker per level, at least 1 s); a mark lasts 30 s.
                        if (now >= nextMark)
                        {
                            nextMark = now + Mathf.Max(1f, 5f - 0.04f * L);
                            var enemies = Enemies(body.corePosition, 60f, body.teamComponent.teamIndex).Where(e => !e.HasBuff(MarkBuff)).ToList();
                            if (enemies.Count > 0) { enemies[UnityEngine.Random.Range(0, enemies.Count)].AddTimedBuff(MarkBuff, 30f); Note("bullseye marked an enemy"); }
                        }
                        break;
                    case "Gamba":
                        // A random tome point each level-up.
                        if (body.master && body.master.inventory && GambaItem && GambaItem.itemIndex != ItemIndex.None && Upgrades.TomeItems.Length > 0)
                        {
                            var inv = body.master.inventory;
                            int due = Mathf.FloorToInt(L) - 1 - inv.GetItemCount(GambaItem.itemIndex);
                            if (due > 0)
                            {
                                int t = UnityEngine.Random.Range(0, Upgrades.TomeItems.Length);
                                inv.GiveItemPermanent(Upgrades.TomeItems[t], 1);
                                inv.GiveItemPermanent(GambaItem, 1);
                                Chat.AddMessage($"<style=cIsUtility>Gamba!</style> {body.GetUserName()} rolled +1 {Sheets.Upgrades[t].DisplayName}");
                                Note("gamba gave " + Sheets.Upgrades[t].Id);
                            }
                        }
                        break;
                }
            }
        }

        static State StateOf(CharacterBody b)
        {
            if (!b || Weapons.Character(b) == null) return null;
            var s = b.GetComponent<State>();
            return s ? s : b.gameObject.AddComponent<State>();
        }

        static IEnumerable<CharacterBody> Enemies(Vector3 at, float radius, TeamIndex team)
        {
            float r2 = radius * radius;
            foreach (var b in CharacterBody.readOnlyInstancesList)
            {
                var tc = b ? b.teamComponent : null;
                if (!tc || tc.teamIndex == team || tc.teamIndex == TeamIndex.None || tc.teamIndex == TeamIndex.Neutral) continue;
                if (!b.healthComponent || !b.healthComponent.alive) continue;
                if ((b.corePosition - at).sqrMagnitude <= r2) yield return b;
            }
        }

        /// <summary>Enemies near you that are slowed or frozen (Noelle's Frostwalker slows; Megabonk's freezes).</summary>
        static int FrozenEnemies(Vector3 at)
        {
            int n = 0;
            foreach (var b in Enemies(at, 60f, TeamIndex.Player))
                if (b.HasBuff(RoR2Content.Buffs.Slow50) || b.HasBuff(RoR2Content.Buffs.Slow60) || b.HasBuff(RoR2Content.Buffs.Slow80)
                    || b.healthComponent.isInFrozenState) n++;
            return n;
        }

        // ---------------- host events ----------------

        static void Blast(CharacterBody attacker, Vector3 at, float radius, float damage, float force, GameObject effect)
        {
            if (effect) EffectManager.SpawnEffect(effect, new EffectData { origin = at, scale = radius }, true);
            new BlastAttack
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                teamIndex = attacker.teamComponent.teamIndex,
                position = at,
                radius = radius,
                baseDamage = damage,
                procCoefficient = 0f,
                crit = attacker.RollCrit(),
                falloffModel = BlastAttack.FalloffModel.None,
                baseForce = force,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                damageColorIndex = DamageColorIndex.Item,
            }.Fire();
        }

        static void DamageDealt(DamageReport r)
        {
            var a = r.attackerBody;
            var row = Weapons.Character(a);
            if (row == null || !r.victimBody || r.damageInfo == null) return;
            // Lifesteal: Vlad's 5% and Vampire's 1% per level.
            float steal = Stat(row, "Lifesteal") + (row.MbPassive == "Vampire" ? 0.01f * Level(a) : 0f);
            if (steal > 0f && a.healthComponent && r.damageDealt > 0f) a.healthComponent.Heal(r.damageDealt * steal, default);
            // Passive blasts deal no proc damage and don't chain into themselves.
            if (r.damageInfo.procCoefficient <= 0f && r.damageInfo.damageColorIndex == DamageColorIndex.Item) return;
            var st = a.GetComponent<State>();
            if (row.MbPassive == "Zoombonk" && st && st.zapCharge >= 1f)
            {
                // Zoombonk: a full charge zaps everything within 6 m of the enemy hit for 3x damage.
                st.zapCharge = 0f;
                Note("zoombonk zap");
                Blast(a, r.victimBody.corePosition, 6f, a.damage * 3f, 0f, WeaponFire.Prefab("RoR2/Base/Common/VFX/LightningStrikeImpact.prefab"));
            }
            if (row.MbPassive == "Bullseye" && r.victimBody.HasBuff(MarkBuff))
            {
                // A marked enemy explodes when hit (10 m, the hit's damage again).
                r.victimBody.RemoveBuff(MarkBuff);
                Note("bullseye mark exploded");
                Blast(a, r.victimBody.corePosition, 10f, r.damageDealt, 600f, WeaponFire.Prefab("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab"));
            }
        }

        static void Died(DamageReport r)
        {
            // Plague: a poisoned enemy bursts, poisoning enemies within 4 m (+0.05 m per level, at most 12 m) for 4 s.
            var victim = r.victimBody;
            if (!victim) return;
            var dots = DotController.FindDotController(victim.gameObject);
            if (!dots || !(dots.HasDotActive(DotController.DotIndex.Poison) || dots.HasDotActive(DotController.DotIndex.Blight))) return;
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                var amog = pc && pc.master ? pc.master.GetBody() : null;
                if (!Has(amog, "Plague")) continue;
                float radius = Mathf.Min(12f, 4f + 0.05f * Level(amog));
                Note($"plague burst r {radius:0.#}");
                var fx = WeaponFire.Prefab("RoR2/Base/Croco/CrocoDiseaseImpactEffect.prefab");
                if (fx) EffectManager.SpawnEffect(fx, new EffectData { origin = victim.corePosition, scale = radius }, true);
                foreach (var e in Enemies(victim.corePosition, radius, amog.teamComponent.teamIndex).ToList())
                    if (e != victim)
                    {
                        var dot = new InflictDotInfo { victimObject = e.gameObject, attackerObject = amog.gameObject, dotIndex = DotController.DotIndex.Poison, duration = 4f, damageMultiplier = 1f };
                        DotController.InflictDot(ref dot);
                    }
                break;
            }
        }

        /// <summary>Host: a hit on this body was dodged (Upgrades' evasion roll). Shadowstep executes the attacker.</summary>
        public static void Evaded(CharacterBody victim, DamageInfo info)
        {
            if (!Has(victim, "Shadowstep") || !info.attacker) return;
            var attacker = info.attacker.GetComponent<CharacterBody>();
            if (!attacker || attacker.isChampion || attacker.isBoss || !attacker.healthComponent || !attacker.healthComponent.alive) return;
            if (attacker.teamComponent && attacker.teamComponent.teamIndex == victim.teamComponent.teamIndex) return;
            attacker.healthComponent.TakeDamage(new DamageInfo
            {
                attacker = victim.gameObject,
                inflictor = victim.gameObject,
                damage = attacker.healthComponent.fullCombinedHealth * 2f,
                damageType = DamageType.BypassArmor | DamageType.BypassBlock,
                position = attacker.corePosition,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Item,
                crit = true,
            });
            Note("shadowstep executed " + attacker.name);
            var fx = WeaponFire.Prefab("RoR2/Base/Merc/MercExposeConsumeEffect.prefab");
            if (fx) EffectManager.SpawnEffect(fx, new EffectData { origin = attacker.corePosition, scale = 2f }, true);
        }

        // Curse: the monster directors earn this much more credit (the sum over every cursed player).
        static float CurseCredit()
        {
            float c = 0f;
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                var b = pc && pc.master ? pc.master.GetBody() : null;
                if (b) c += Difficulty(b);
            }
            return c;
        }

        // ---------------- stats ----------------

        static readonly Dictionary<string, Action<CharacterBody, float>> setters = new Dictionary<string, Action<CharacterBody, float>>();

        static void Set(CharacterBody b, string prop, float v)
        {
            if (!setters.TryGetValue(prop, out var s))
            {
                var mi = AccessTools.PropertySetter(typeof(CharacterBody), prop);
                s = mi == null ? null : (Action<CharacterBody, float>)Delegate.CreateDelegate(typeof(Action<CharacterBody, float>), mi);
                setters[prop] = s;
            }
            s?.Invoke(b, v);
        }

        [HarmonyPatch]
        static class Patches
        {
            [HarmonyPostfix, HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
            static void Stats(CharacterBody __instance)
            {
                var b = __instance;
                var row = Weapons.Character(b);
                if (row == null) return;
                var st = StateOf(b);
                float L = Level(b);
                string p = row.MbPassive;

                // Health: Megabonk's base 100 plus the character's modifier (Monke Grip: +2 per level), as a fraction.
                // Shield is flat on Megabonk's base too (CL4NK: 40 health + 50 shield = 90% of a standard survivor).
                float hp = MbBaseHealth + Stat(row, "MaxHealth") + (p == "WallClimb" ? 2f * L : 0f);
                float baseHealth = b.maxHealth;
                Set(b, "maxHealth", baseHealth * Mathf.Max(0.1f, hp / MbBaseHealth));
                float shield = Stat(row, "Shield");
                if (shield > 0f) Set(b, "maxShield", b.maxShield + baseHealth * shield / MbBaseHealth);
                // (Balance heals the rise in max health and caps health above it, after every bonus.)
                // Armor: Megabonk's armor takes that fraction off each hit; Risk of Rain 2 armor a takes a / (100 + a).
                float armor = Mathf.Min(0.9f, Stat(row, "Armor") + (p == "Reinforced" ? 0.01f * L : 0f));
                if (armor > 0f) Set(b, "armor", b.armor + 100f * armor / (1f - armor));
                float crit = Stat(row, "CritChance") + (p == "CritHappens" ? 0.01f * L : 0f);
                if (crit > 0f) Set(b, "crit", b.crit + crit * 100f);
                if (p == "Bullseye") Set(b, "critMultiplier", b.critMultiplier + 0.01f * L);
                if (p == "Flowstate") Set(b, "attackSpeed", b.attackSpeed * (1f + 0.01f * L));

                float damage = Stat(row, "DamageMultiplier") + (p == "Warrior" ? 0.015f * L : 0f) + (st ? st.dynamicDamage : 0f);
                if (p == "Flex" && b.inventory && FlexItem && FlexItem.itemIndex != ItemIndex.None) damage += 0.025f * b.inventory.GetItemCount(FlexItem.itemIndex);
                if (damage != 0f) Set(b, "damage", b.damage * Mathf.Max(0.1f, 1f + damage));
                if (p == "SpeedDemon" && st && st.speedDemon > 0f) Set(b, "moveSpeed", b.moveSpeed * (1f + st.speedDemon));
            }

            [HarmonyPrefix, HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.TakeDamage))]
            static void Hit(HealthComponent __instance, DamageInfo damageInfo)
            {
                var b = __instance.body;
                if (damageInfo == null || damageInfo.rejected || !b) return;
                // Plague: +1% poison damage per level.
                if (damageInfo.dotIndex == DotController.DotIndex.Poison && damageInfo.attacker && Has(damageInfo.attacker.GetComponent<CharacterBody>(), "Plague"))
                    damageInfo.damage *= 1f + 0.01f * Level(damageInfo.attacker.GetComponent<CharacterBody>());
                var row = Weapons.Character(b);
                if (row == null) return;
                // Fall damage reduction (Tony McZoom takes none).
                if ((damageInfo.damageType & DamageType.FallDamage) != 0)
                {
                    damageInfo.damage *= Mathf.Clamp01(1f - Stat(row, "FallDamageReduction"));
                    if (damageInfo.damage <= 0f) { damageInfo.rejected = true; return; }
                }
                var st = StateOf(b);
                var attacker = damageInfo.attacker ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
                bool fromEnemy = attacker && attacker != b && damageInfo.damage > 0f;
                // Flex: blocks a hit, pushes enemies within 10 m away and hurts them, and adds a 2.5% damage stack.
                // Cooldown 12 s, 0.2 s less per level, at least 4 s.
                if (row.MbPassive == "Flex" && st && fromEnemy && Time.fixedTime >= st.flexReadyAt)
                {
                    st.flexReadyAt = Time.fixedTime + Mathf.Max(4f, 12f - 0.2f * Level(b));
                    damageInfo.rejected = true;
                    Blast(b, b.corePosition, 10f, b.damage, 2500f, WeaponFire.Prefab("RoR2/Base/Loader/LoaderGroundSlam.prefab"));
                    if (b.master && b.master.inventory && FlexItem) b.master.inventory.GiveItemPermanent(FlexItem, 1);
                    Util.PlaySound("Play_loader_R_shock", b.gameObject);
                    Note($"flex blocked {damageInfo.damage:0} damage, next in {st.flexReadyAt - Time.fixedTime:0.#} s");
                    return;
                }
                // Thorns: Athena's 5 and Lock In's 2 per level, as Megabonk damage (x weapon_damage_scale x base damage).
                float thorns = Stat(row, "Thorns") + (row.MbPassive == "LockIn" ? 2f * Level(b) : 0f);
                if (thorns > 0f && fromEnemy && attacker.healthComponent && attacker.healthComponent.alive && (damageInfo.damageType & DamageType.DoT) == 0
                    && attacker.teamComponent && b.teamComponent && attacker.teamComponent.teamIndex != b.teamComponent.teamIndex)
                    attacker.healthComponent.TakeDamage(new DamageInfo
                    {
                        attacker = b.gameObject,
                        inflictor = b.gameObject,
                        damage = thorns * Tune.WeaponDamageScale * b.damage,
                        position = attacker.corePosition,
                        procCoefficient = 0f,
                        damageColorIndex = DamageColorIndex.Item,
                    });
            }

            [HarmonyPrefix, HarmonyPatch(typeof(CharacterMaster), nameof(CharacterMaster.GiveMoney), new[] { typeof(uint) })]
            static void Gold(CharacterMaster __instance, ref uint amount)
            {
                var b = __instance ? __instance.GetBody() : null;
                float m = GoldMultiplier(b);
                if (m != 1f) amount = (uint)Mathf.RoundToInt(amount * m);
            }

            [HarmonyPrefix, HarmonyPatch(typeof(CombatDirector), "FixedUpdate")]
            static void CreditBefore(CombatDirector __instance, out float __state) => __state = __instance.monsterCredit;

            [HarmonyPostfix, HarmonyPatch(typeof(CombatDirector), "FixedUpdate")]
            static void CreditAfter(CombatDirector __instance, float __state)
            {
                float gained = __instance.monsterCredit - __state;
                if (gained > 0f && __instance.teamIndex == TeamIndex.Monster && !__instance.combatSquad) __instance.monsterCredit += gained * CurseCredit();
            }
        }

        public static void Init(Harmony harmony)
        {
            GlobalEventManager.onServerDamageDealt += DamageDealt;
            GlobalEventManager.onCharacterDeathGlobal += Died;
            harmony.PatchAll(typeof(Patches));
            RoR2Application.onLoad += () => { if (MarkBuff && RoR2Content.Buffs.DeathMark) MarkBuff.iconSprite = RoR2Content.Buffs.DeathMark.iconSprite; };
        }
    }
}
