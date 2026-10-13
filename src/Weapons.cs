using System;
using System.Collections.Generic;
using System.Linq;
using EntityStates;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk weapons as skills, for the Megabonk survivors only (sheets/weapons.json).
    /// A Megabonk survivor starts with its own weapon (characters.json startWeapon) on primary and nothing on the other
    /// three slots. Weapons picked on level up fill secondary, utility and special in pick order, so a run has four
    /// weapons; picking an owned weapon again levels it. Equipment (Q) is untouched.
    /// Weapon levels are hidden items: the game's inventory sync carries them to every player, and the slot order comes
    /// from the inventory's acquisition order, so every machine computes the same loadout.
    /// </summary>
    public static class Weapons
    {
        public static ItemDef[] Items = new ItemDef[0];
        static SkillDef[] primaryDefs = new SkillDef[0], slotDefs = new SkillDef[0];
        static SkillDef emptyDef;
        static readonly Dictionary<SkillDef, int> defToWeapon = new Dictionary<SkillDef, int>();
        public static readonly List<SkillFamily> Families = new List<SkillFamily>();
        public static readonly List<SkillDef> SkillDefs = new List<SkillDef>();

        public static int Index(string id) => Array.FindIndex(Sheets.Weapons, w => w.Id == id);
        public static WeaponRow Row(SkillDef def) => def != null && defToWeapon.TryGetValue(def, out int i) ? Sheets.Weapons[i] : null;

        /// <summary>Hidden level items, one per weapon (needs no Megabonk files).</summary>
        public static ItemDef[] CreateItems()
        {
            Items = Sheets.Weapons.Select(w =>
            {
                var d = ScriptableObject.CreateInstance<ItemDef>();
                d.name = "RiskOfBonk_weapon_" + w.Id;
                d.nameToken = "RISKOFBONK_ITEM_WEAPON_" + w.Id.ToUpperInvariant() + "_NAME";
                d.pickupToken = d.descriptionToken = d.loreToken = d.nameToken;
                Plugin.Tokens[d.nameToken] = w.DisplayName;
#pragma warning disable CS0618
                d.tier = ItemTier.NoTier;
#pragma warning restore CS0618
                d.hidden = true;
                d.canRemove = false;
                d.tags = new[] { ItemTag.CannotCopy, ItemTag.CannotSteal, ItemTag.AIBlacklist };
                return d;
            }).ToArray();
            return Items;
        }

        static Sprite IconSprite(string mbName)
        {
            var lib = MegabonkLoader.Library;
            if (lib == null) return null;
            RawTexture raw;
            lock (lib) raw = lib.FindTexture(new[] { mbName });
            var tex = UnityConvert.Texture(raw, lib);
            return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        static SkillDef MakeDef(string name, string nameToken, string descToken, Sprite icon, Type state, float cooldown, int stock)
        {
            var d = ScriptableObject.CreateInstance<SkillDef>();
            d.skillName = name;
            (d as ScriptableObject).name = name;
            d.skillNameToken = nameToken;
            d.skillDescriptionToken = descToken;
            d.icon = icon;
            d.activationStateMachineName = "Weapon";
            d.activationState = new SerializableEntityStateType(state);
            d.interruptPriority = InterruptPriority.Skill;
            d.baseRechargeInterval = cooldown;
            d.baseMaxStock = stock;
            d.rechargeStock = stock;
            d.requiredStock = 1;
            d.stockToConsume = 1;
            d.beginSkillCooldownOnSkillEnd = false;
            d.canceledFromSprinting = false;
            d.cancelSprintingOnActivation = false; // Megabonk: you keep running while attacking
            d.fullRestockOnAssign = true;
            d.isCombatSkill = true;
            d.mustKeyPress = false;
            d.resetCooldownTimerOnUse = false;
            SkillDefs.Add(d);
            return d;
        }

        /// <summary>Skill definitions for every weapon (primary and slot versions) and the empty slot. Needs Megabonk icons.</summary>
        public static void CreateSkills()
        {
            primaryDefs = new SkillDef[Sheets.Weapons.Length];
            slotDefs = new SkillDef[Sheets.Weapons.Length];
            for (int i = 0; i < Sheets.Weapons.Length; i++)
            {
                var w = Sheets.Weapons[i];
                string tok = "RISKOFBONK_WEAPON_" + w.Id.ToUpperInvariant();
                Plugin.Tokens[tok + "_NAME"] = w.DisplayName;
                Plugin.Tokens[tok + "_DESC"] = w.Description + " <style=cSub>Megabonk weapon.</style>";
                var icon = IconSprite(w.MbIcon);
                primaryDefs[i] = MakeDef("RiskOfBonkWeapon_" + w.Id, tok + "_NAME", tok + "_DESC", icon, typeof(WeaponFire), 0f, 1);
                slotDefs[i] = MakeDef("RiskOfBonkWeaponSlot_" + w.Id, tok + "_NAME", tok + "_DESC", icon, typeof(WeaponFire), w.Cooldown, 1);
                defToWeapon[primaryDefs[i]] = i;
                defToWeapon[slotDefs[i]] = i;
            }
            Plugin.Tokens["RISKOFBONK_WEAPON_EMPTY_NAME"] = "Empty weapon slot";
            Plugin.Tokens["RISKOFBONK_WEAPON_EMPTY_DESC"] = "Pick a Megabonk weapon when you level up to fill this slot.";
            emptyDef = MakeDef("RiskOfBonkWeaponEmpty", "RISKOFBONK_WEAPON_EMPTY_NAME", "RISKOFBONK_WEAPON_EMPTY_DESC",
                IconSprite("icon_question"), typeof(Idle), 0f, 0);
            emptyDef.isCombatSkill = false;
            emptyDef.requiredStock = 1; // stock 0: it can never fire
            Log.Info($"Megabonk weapons ready: {Sheets.Weapons.Length}");
        }

        static readonly AccessTools.FieldRef<GenericSkill, SkillFamily> familyField = AccessTools.FieldRefAccess<GenericSkill, SkillFamily>("_skillFamily");

        static SkillFamily Family(string name, SkillDef def)
        {
            var f = ScriptableObject.CreateInstance<SkillFamily>();
            (f as ScriptableObject).name = name;
            f.variants = new[] { new SkillFamily.Variant { skillDef = def, viewableNode = new ViewablesCatalog.Node(def.skillName, false, null) } };
            f.defaultVariantIndex = 0;
            Families.Add(f);
            return f;
        }

        /// <summary>Give a cloned Megabonk survivor body its own skill families: the start weapon and three empty slots.</summary>
        public static void SetupBody(GameObject body, CharacterRow row)
        {
            int start = Index(row.StartWeapon);
            var locator = body.GetComponent<SkillLocator>();
            if (start < 0 || locator == null || primaryDefs.Length == 0) return;
            if (!body.GetComponents<EntityStateMachine>().Any(m => m.customName == "Weapon"))
                Log.Warn($"{row.DisplayName}: body has no 'Weapon' state machine; weapons will not fire");
            string id = "RiskOfBonk" + MegabonkSurvivors.Pascal(row.Id);
            if (locator.primary) familyField(locator.primary) = Family(id + "Primary", primaryDefs[start]);
            if (locator.secondary) familyField(locator.secondary) = Family(id + "Secondary", emptyDef);
            if (locator.utility) familyField(locator.utility) = Family(id + "Utility", emptyDef);
            if (locator.special) familyField(locator.special) = Family(id + "Special", emptyDef);
            var lo = body.AddComponent<WeaponLoadout>();
            lo.startWeapon = start;
        }

        public static bool IsMegabonk(CharacterBody body) => body && body.GetComponent<WeaponLoadout>();

        public static int StartWeapon(CharacterBody body) => body && body.GetComponent<WeaponLoadout>() is WeaponLoadout lo ? lo.startWeapon : -1;

        /// <summary>Level of a weapon on this body (0 = not owned). The start weapon is level 1 plus its picks.</summary>
        public static int Level(CharacterBody body, int weapon)
        {
            if (!body || weapon < 0 || weapon >= Items.Length) return 0;
            int n = body.inventory ? body.inventory.GetItemCountPermanent(Items[weapon]) : 0;
            return weapon == StartWeapon(body) ? n + 1 : n;
        }

        /// <summary>Extra weapons (not the start one) in pick order.</summary>
        public static List<int> Extras(CharacterBody body)
        {
            var list = new List<int>();
            if (!body || !body.inventory) return list;
            int start = StartWeapon(body);
            foreach (var idx in body.inventory.itemAcquisitionOrder)
            {
                int w = Array.FindIndex(Items, it => it.itemIndex == idx);
                if (w >= 0 && w != start && !list.Contains(w)) list.Add(w);
            }
            return list;
        }

        public const int MaxExtras = 3;

        /// <summary>Puts the picked weapons on secondary, utility and special (in pick order) on every machine.</summary>
        public sealed class WeaponLoadout : MonoBehaviour
        {
            public int startWeapon = -1;
            CharacterBody body;
            readonly SkillDef[] applied = new SkillDef[3];

            void Start()
            {
                body = GetComponent<CharacterBody>();
                if (body) body.onInventoryChanged += Apply;
                Apply();
            }

            void OnDestroy() { if (body) body.onInventoryChanged -= Apply; }

            void Apply()
            {
                if (!body || !body.skillLocator) return;
                var extras = Extras(body);
                var slots = new[] { body.skillLocator.secondary, body.skillLocator.utility, body.skillLocator.special };
                for (int i = 0; i < 3; i++)
                {
                    var want = i < extras.Count ? slotDefs[extras[i]] : null;
                    if (applied[i] == want || !slots[i]) continue;
                    if (applied[i] != null) slots[i].UnsetSkillOverride(this, applied[i], GenericSkill.SkillOverridePriority.Replacement);
                    if (want != null) slots[i].SetSkillOverride(this, want, GenericSkill.SkillOverridePriority.Replacement);
                    applied[i] = want;
                }
            }
        }
    }
}
