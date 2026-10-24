using System.Runtime.CompilerServices;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk weapons and tomes from chests: a chest opened by a Megabonk survivor holds, with chest_upgrade_chance, a
    /// Megabonk upgrade instead of its item. The opener gets a bonus pick (the level-up menu: three weapons or tomes, as
    /// for a level-up or a Charge Shrine). Host only; the pick travels as the hidden bonus-picks item, like the shrines'.
    /// </summary>
    public static class Chests
    {
        static readonly ConditionalWeakTable<GameObject, CharacterBody> openedBy = new ConditionalWeakTable<GameObject, CharacterBody>();

        [HarmonyPatch]
        static class Patches
        {
            [HarmonyPrefix, HarmonyPatch(typeof(PurchaseInteraction), nameof(PurchaseInteraction.OnInteractionBegin))]
            static void Opened(PurchaseInteraction __instance, Interactor activator)
            {
                if (!NetworkServer.active || !__instance || !activator) return;
                var body = activator.GetComponent<CharacterBody>();
                if (!body) return;
                openedBy.Remove(__instance.gameObject);
                openedBy.Add(__instance.gameObject, body);
            }

            [HarmonyPrefix, HarmonyPatch(typeof(ChestBehavior), nameof(ChestBehavior.ItemDrop))]
            static bool Drop(ChestBehavior __instance)
            {
                if (!NetworkServer.active || !__instance) return true;
                string name = __instance.gameObject.name;
                // Item chests only: equipment barrels, lunar pods and the void cradles keep their own drops.
                if (!name.Contains("Chest") || name.Contains("Lunar") || name.Contains("Equipment") || name.Contains("Void")) return true;
                if (!openedBy.TryGetValue(__instance.gameObject, out var body) || !Weapons.IsMegabonk(body) || !body.master || !body.master.inventory) return true;
                if (Random.value >= Tune.ChestUpgradeChance) return true;
                body.master.inventory.GiveItemPermanent(Upgrades.BonusPicksItem, 1);
                var fx = WeaponFire.Prefab("RoR2/Base/Common/VFX/LevelUpEffect.prefab");
                if (fx) EffectManager.SpawnEffect(fx, new EffectData { origin = __instance.transform.position + Vector3.up, scale = 2f }, true);
                Chat.AddMessage($"<style=cIsUtility>{body.GetUserName()} found a Megabonk upgrade in the chest!</style>");
                Log.Info($"chest: {name} gave {body.GetUserName()} an upgrade pick");
                openedBy.Remove(__instance.gameObject);
                return false;
            }
        }

        public static void Init(Harmony harmony) => harmony.PatchAll(typeof(Patches));
    }
}
