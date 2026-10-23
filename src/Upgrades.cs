using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's level-up upgrades (sheets/upgrades.json, sheets/rarities.json). Every Risk of Rain 2 team level-up
    /// gives each player one pick: three random tomes with rolled rarities, chosen with 1/2/3 (UpgradePanel).
    /// Tomes are hidden items, so the game's own inventory sync carries them to every player and the stat bonus is
    /// computed the same everywhere. A pick is sent to the host, which checks it and gives the items.
    /// Pending picks = team level - 1 + Charge Shrine bonus picks - picks taken (hidden "bonus picks" and "picks" items),
    /// which every machine can compute.
    /// </summary>
    public static class Upgrades
    {
        public const short MsgType = 0x5B0C, MenuMsgType = 0x5B0D;
        public const byte KindTome = 0, KindWeapon = 1;
        public static ItemDef[] TomeItems = new ItemDef[0];
        public static ItemDef PicksItem, BonusPicksItem;

        sealed class PickMessage : MessageBase
        {
            public byte kind, upgrade, rarity;
            public override void Serialize(NetworkWriter w) { w.Write(kind); w.Write(upgrade); w.Write(rarity); }
            public override void Deserialize(NetworkReader r) { kind = r.ReadByte(); upgrade = r.ReadByte(); rarity = r.ReadByte(); }
        }

        sealed class MenuMessage : MessageBase
        {
            public override void Serialize(NetworkWriter w) { }
            public override void Deserialize(NetworkReader r) { }
        }

        // Host side: the picks count each player's menu invincibility was last given for (one grant per pick).
        static readonly Dictionary<CharacterMaster, int> invincibleFor = new Dictionary<CharacterMaster, int>();

        /// <summary>Hidden items, one per tome plus the picks counter; added to the mod's content pack.</summary>
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
            TomeItems = Sheets.Upgrades.Select(u => Make("tome_" + u.Id, u.DisplayName)).ToArray();
            PicksItem = Make("upgrade_picks", "Upgrade picks");
            BonusPicksItem = Make("bonus_picks", "Bonus upgrade picks");
            return TomeItems.Concat(new[] { PicksItem, BonusPicksItem }).ToArray();
        }

        public static int Points(Inventory inv, int upgrade) =>
            inv && upgrade < TomeItems.Length && TomeItems[upgrade].itemIndex != ItemIndex.None ? inv.GetItemCount(TomeItems[upgrade].itemIndex) : 0;

        static int StatPoints(Inventory inv, string stat)
        {
            int n = 0;
            for (int i = 0; i < Sheets.Upgrades.Length; i++) if (Sheets.Upgrades[i].Stat == stat) n += Points(inv, i);
            return n;
        }

        static float PerPoint(string stat) => Sheets.Upgrades.FirstOrDefault(u => u.Stat == stat)?.PerPoint ?? 0f;

        public static int Pending(CharacterMaster master)
        {
            if (!master || !master.inventory || !TeamManager.instance || PicksItem == null || PicksItem.itemIndex == ItemIndex.None) return 0;
            int level = (int)TeamManager.instance.GetTeamLevel(master.teamIndex);
            return Mathf.Max(0, level - 1 + Bonus(master) - master.inventory.GetItemCount(PicksItem.itemIndex));
        }

        /// <summary>Extra picks from Megabonk Charge Shrines.</summary>
        public static int Bonus(CharacterMaster master) =>
            master && master.inventory && BonusPicksItem != null && BonusPicksItem.itemIndex != ItemIndex.None ? master.inventory.GetItemCount(BonusPicksItem.itemIndex) : 0;

        /// <summary>Luck shifts rarity weights toward the rarer rows.</summary>
        public static int RollRarity(System.Random rng, Inventory inv)
        {
            float luck = StatPoints(inv, "luck") * PerPoint("luck");
            var w = Sheets.Rarities.Select((r, i) => r.Weight * (1f + luck * i)).ToArray();
            float roll = (float)rng.NextDouble() * w.Sum();
            for (int i = 0; i < w.Length; i++) { roll -= w[i]; if (roll <= 0f) return i; }
            return 0;
        }

        public static string Describe(int upgrade, int rarity)
        {
            var u = Sheets.Upgrades[upgrade];
            float total = u.PerPoint * Sheets.Rarities[rarity].Points;
            return string.Format(u.Description, Mathf.RoundToInt(total * 100f), total.ToString("0.#"));
        }

        /// <summary>Client side: ask the host for this pick.</summary>
        public static void RequestPick(byte kind, int upgrade, int rarity)
        {
            var conn = ClientScene.readyConnection;
            if (conn == null) return;
            conn.Send(MsgType, new PickMessage { kind = kind, upgrade = (byte)upgrade, rarity = (byte)rarity });
        }

        /// <summary>Client side: the level-up menu just opened (the host grants the menu invincibility).</summary>
        public static void MenuOpened()
        {
            var conn = ClientScene.readyConnection;
            if (conn != null) conn.Send(MenuMsgType, new MenuMessage());
        }

        static CharacterMaster Sender(NetworkMessage msg)
        {
            var user = NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u.connectionToClient == msg.conn);
            return user ? user.master : null;
        }

        public static void Init(Harmony harmony)
        {
            NetworkManagerSystem.onStartServerGlobal += () =>
            {
                NetworkServer.RegisterHandler(MsgType, msg =>
                {
                    var m = msg.ReadMessage<PickMessage>();
                    var master = Sender(msg);
                    if (!master || m.rarity >= Sheets.Rarities.Length || Pending(master) <= 0) return;
                    int points = Sheets.Rarities[m.rarity].Points;
                    string what;
                    if (m.kind == KindWeapon)
                    {
                        var body = master.GetBody();
                        if (m.upgrade >= Weapons.Items.Length || !Weapons.IsMegabonk(body)) return;
                        bool owned = Weapons.Level(body, m.upgrade) > 0;
                        if (!owned && Weapons.Extras(body).Count >= Weapons.MaxExtras) return;
                        int levels = owned ? points : 1; // a new weapon starts at level 1; upgrades add the rarity's levels
                        master.inventory.GiveItemPermanent(Weapons.Items[m.upgrade], levels);
                        what = owned ? $"{Sheets.Weapons[m.upgrade].DisplayName} +{levels} levels ({Weapons.Describe(Sheets.Weapons[m.upgrade], Weapons.Level(body, m.upgrade) - levels, levels)})" : $"new weapon {Sheets.Weapons[m.upgrade].DisplayName}";
                    }
                    else
                    {
                        if (m.upgrade >= TomeItems.Length) return;
                        master.inventory.GiveItemPermanent(TomeItems[m.upgrade], points);
                        what = $"{Sheets.Rarities[m.rarity].Label} {Sheets.Upgrades[m.upgrade].DisplayName} (+{points})";
                    }
                    master.inventory.GiveItemPermanent(PicksItem, 1);
                    // Picking ends the menu invincibility.
                    var b = master.GetBody();
                    if (b) b.ClearTimedBuffs(RoR2Content.Buffs.HiddenInvincibility);
                    Log.Info($"Upgrade: {master.playerCharacterMasterController?.networkUser?.userName} took {what}");
                });
                NetworkServer.RegisterHandler(MenuMsgType, msg =>
                {
                    msg.ReadMessage<MenuMessage>();
                    var master = Sender(msg);
                    var body = master ? master.GetBody() : null;
                    if (!body || Pending(master) <= 0) return;
                    int picks = master.inventory.GetItemCountPermanent(PicksItem);
                    if (invincibleFor.TryGetValue(master, out int had) && had == picks) return;
                    invincibleFor[master] = picks;
                    body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, Tune.UpgradeInvincibility);
                });
            };
            harmony.PatchAll(typeof(Patches));
        }

        // Stat setters are private in the game: open delegates to them, built once.
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
                var inv = b.inventory;
                if (!inv || TomeItems.Length == 0) return;
                int n;
                if ((n = StatPoints(inv, "damage")) > 0) Set(b, "damage", b.damage * (1f + n * PerPoint("damage")));
                if ((n = StatPoints(inv, "cooldown")) > 0)
                {
                    float f = n * PerPoint("cooldown");
                    Set(b, "attackSpeed", b.attackSpeed * (1f + f));
                    if (b.skillLocator)
                        foreach (var s in b.skillLocator.allSkills) if (s) s.cooldownScale *= 1f / (1f + f);
                }
                if ((n = StatPoints(inv, "crit")) > 0) Set(b, "crit", b.crit + n * PerPoint("crit"));
                if ((n = StatPoints(inv, "health")) > 0) Set(b, "maxHealth", b.maxHealth * (1f + n * PerPoint("health")));
                if ((n = StatPoints(inv, "regen")) > 0) Set(b, "regen", b.regen + n * PerPoint("regen") * (1f + 0.2f * (b.level - 1f)));
                if ((n = StatPoints(inv, "armor")) > 0) Set(b, "armor", b.armor + n * PerPoint("armor"));
                if ((n = StatPoints(inv, "speed")) > 0) Set(b, "moveSpeed", b.moveSpeed * (1f + n * PerPoint("speed")));
                if ((n = StatPoints(inv, "shield")) > 0) Set(b, "maxShield", b.maxShield + b.maxHealth * n * PerPoint("shield"));
            }

            // Evasion: the host rolls a dodge for each hit on a body with Evasion Tome points.
            [HarmonyPrefix, HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.TakeDamage))]
            static void Evade(HealthComponent __instance, DamageInfo damageInfo)
            {
                if (damageInfo == null || damageInfo.rejected || !__instance.body) return;
                int n = StatPoints(__instance.body.inventory, "evasion");
                if (n <= 0) return;
                float chance = Mathf.Min(0.6f, n * PerPoint("evasion"));
                if (UnityEngine.Random.value < chance) damageInfo.rejected = true;
            }
        }
    }
}
