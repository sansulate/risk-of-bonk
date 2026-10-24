using System.Collections.Generic;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's in-run HUD (its InventoryHud) in the top left: the health bar, a row with the four weapons and a row
    /// with every tome you own, each with its level. A Risk of Rain 2 survivor's row shows its four skills instead, with
    /// their cooldowns (a dark sweep and the seconds left) and charges, so the Megabonk HUD replaces Risk of Rain 2's skill
    /// icons, health bar and level display (hidden while this is up; buffs, money, items and equipment stay).
    /// </summary>
    public sealed class MegabonkHud : MonoBehaviour
    {
        public static bool Enabled = true;

        sealed class Slot
        {
            public RectTransform rt;
            public Image frame, sweep;
            public RawImage icon;
            public TextMeshProUGUI level, cooldown, key;
            public float pop;
            public int last = -1, levelValue = -1;
        }

        RectTransform root, hpFill, shieldFill, barrierFill;
        TextMeshProUGUI hpText;
        readonly Slot[] weapons = new Slot[4];
        readonly List<Slot> tomes = new List<Slot>();
        static readonly string[] keys = { "M1", "M2", "Shift", "R" };
        static readonly Color Gold = new Color(1f, 0.8f, 0.2f);

        const float WeaponSize = 70f, TomeSize = 50f, Gap = 8f, Left = 24f;

        void Build()
        {
            var canvas = BonkUI.Canvas(transform, "RiskOfBonkHud", 39);
            root = BonkUI.Rect(canvas, "Hud", new Vector2(0f, 1f), Vector2.zero, new Vector2(10, 10));
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(Left, -172f); // under Risk of Rain 2's money counter

            // Health: Megabonk's chunky red bar with the numbers on it; shield (blue) and barrier (gold) over it.
            var bar = BonkUI.Rect(root, "Hp", new Vector2(0f, 1f), Vector2.zero, new Vector2(270, 34));
            bar.pivot = new Vector2(0f, 1f);
            bar.anchoredPosition = Vector2.zero;
            BonkUI.Image(bar, Color.black);
            var back = BonkUI.Fill(bar, "Back", 3f);
            BonkUI.Image(back, new Color(0.25f, 0.05f, 0.06f, 0.95f));
            hpFill = Bar(back, "Fill", new Color(0.9f, 0.15f, 0.18f));
            var shine = BonkUI.Fill(hpFill, "Shine");
            shine.anchorMin = new Vector2(0f, 0.55f);
            BonkUI.Image(shine, new Color(1f, 1f, 1f, 0.2f));
            shieldFill = Bar(back, "Shield", new Color(0.35f, 0.6f, 1f, 0.9f));
            barrierFill = Bar(back, "Barrier", new Color(1f, 0.85f, 0.3f, 0.55f));
            hpText = BonkUI.Label(bar, "Text", 22, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(260, 30));

            for (int i = 0; i < weapons.Length; i++)
            {
                weapons[i] = MakeSlot("Weapon" + i, WeaponSize, new Vector2(i * (WeaponSize + Gap), -46f));
                weapons[i].key = BonkUI.Label(weapons[i].rt, "Key", 14, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(26, -10), new Vector2(50, 18));
                weapons[i].key.text = keys[i];
            }
        }

        static RectTransform Bar(Transform parent, string name, Color c)
        {
            var rt = BonkUI.Fill(parent, name);
            rt.pivot = new Vector2(0f, 0.5f);
            BonkUI.Image(rt, c);
            return rt;
        }

        Slot MakeSlot(string name, float size, Vector2 pos)
        {
            var s = new Slot();
            s.rt = BonkUI.Rect(root, name, new Vector2(0f, 1f), Vector2.zero, new Vector2(size, size));
            s.rt.pivot = new Vector2(0f, 1f);
            s.rt.anchoredPosition = pos;
            s.frame = BonkUI.Image(s.rt, Color.black);
            var inner = BonkUI.Fill(s.rt, "Inner", 3f);
            var panel = MenuStyle.C("panel");
            BonkUI.Image(inner, new Color(panel.r, panel.g, panel.b, 0.9f));
            var iconRt = BonkUI.Fill(inner, "Icon", size * 0.08f);
            s.icon = iconRt.gameObject.AddComponent<RawImage>();
            s.icon.raycastTarget = false;
            var sweepRt = BonkUI.Fill(inner, "Cooldown");
            s.sweep = BonkUI.Image(sweepRt, new Color(0f, 0f, 0f, 0.65f));
            s.sweep.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            s.sweep.type = Image.Type.Filled;
            s.sweep.fillMethod = Image.FillMethod.Radial360;
            s.sweep.fillOrigin = (int)Image.Origin360.Top;
            s.sweep.fillClockwise = false;
            s.sweep.fillAmount = 0f;
            s.cooldown = BonkUI.Label(s.rt, "Seconds", size * 0.42f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            s.level = BonkUI.Label(s.rt, "Level", size * 0.26f, TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-size * 0.5f + 2f, size * 0.5f - 2f), new Vector2(size, size));
            s.level.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return s;
        }

        void Update()
        {
            if (root == null) Build();
            var body = Run.instance ? LocalUserManager.GetFirstLocalUser()?.cachedBody : null;
            bool on = Enabled && body && body.healthComponent && body.healthComponent.alive;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            HideRoR2(on);
            if (!on) return;

            Health(body);
            if (Weapons.IsMegabonk(body)) WeaponRow(body); else SkillRow(body);
            TomeRow(body);
        }

        void Health(CharacterBody body)
        {
            var hc = body.healthComponent;
            float full = Mathf.Max(1f, hc.fullCombinedHealth);
            hpFill.localScale = new Vector3(Mathf.Clamp01(hc.health / full), 1f, 1f);
            shieldFill.anchorMin = new Vector2(Mathf.Clamp01(hc.health / full), 0f);
            shieldFill.localScale = new Vector3(Mathf.Clamp01(hc.shield / Mathf.Max(1f, full - hc.health)), 1f, 1f);
            barrierFill.localScale = new Vector3(Mathf.Clamp01(hc.barrier / full), 1f, 1f);
            hpText.text = $"{Mathf.CeilToInt(hc.combinedHealth)} / {Mathf.CeilToInt(hc.fullCombinedHealth)}";
        }

        void WeaponRow(CharacterBody body)
        {
            var loc = body.skillLocator;
            for (int i = 0; i < weapons.Length; i++)
            {
                var s = weapons[i];
                var skill = loc ? loc.GetSkill((SkillSlot)i) : null;
                var w = skill ? Weapons.Row(skill.skillDef) : null;
                s.key.text = "";
                s.cooldown.text = "";
                s.sweep.fillAmount = 0f;
                if (w == null)
                {
                    SetIcon(s, UpgradePanel.Icon("icon_question"), 0.2f);
                    s.level.text = "";
                    s.frame.color = Color.black;
                    Pop(s, 0);
                    continue;
                }
                int level = Weapons.Level(body, Weapons.Index(w.Id));
                SetIcon(s, UpgradePanel.Icon(w.MbIcon), 1f);
                s.level.text = "Lv " + level;
                s.level.color = Gold;
                Pop(s, level);
            }
        }

        void SkillRow(CharacterBody body)
        {
            var loc = body.skillLocator;
            for (int i = 0; i < weapons.Length; i++)
            {
                var s = weapons[i];
                var skill = loc ? loc.GetSkill((SkillSlot)i) : null;
                s.key.text = keys[i];
                if (!skill || !skill.skillDef)
                {
                    SetIcon(s, null, 0f);
                    s.level.text = s.cooldown.text = "";
                    s.sweep.fillAmount = 0f;
                    continue;
                }
                var tex = skill.icon ? skill.icon.texture : null;
                SetIcon(s, tex, 1f);
                if (skill.icon && s.icon.texture == tex)
                {
                    var r = skill.icon.textureRect;
                    s.icon.uvRect = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
                }
                // Cooldown: the dark sweep and the seconds while no charge is ready; charges in the corner.
                float interval = skill.finalRechargeInterval;
                bool waiting = skill.stock < skill.maxStock && interval > 0f;
                bool empty = skill.stock <= 0 || !skill.IsReady();
                float left = waiting ? Mathf.Max(0f, skill.cooldownRemaining) : 0f;
                s.sweep.fillAmount = empty && waiting ? Mathf.Clamp01(left / interval) : 0f;
                s.cooldown.text = empty && waiting && left > 0.05f ? (left < 1f ? left.ToString("0.0") : Mathf.CeilToInt(left).ToString()) : "";
                s.icon.color = new Color(1f, 1f, 1f, empty ? 0.55f : 1f);
                s.level.text = skill.maxStock > 1 ? skill.stock.ToString() : "";
                s.level.color = Color.white;
                // A flash on the frame when the skill comes back.
                int ready = skill.stock;
                if (s.last >= 0 && ready > s.last) s.pop = 1f;
                s.last = ready;
                s.pop = Mathf.MoveTowards(s.pop, 0f, Time.unscaledDeltaTime * 4f);
                s.frame.color = Color.Lerp(Color.black, Color.white, s.pop);
                s.rt.localScale = Vector3.one * (1f + 0.08f * s.pop);
            }
        }

        void TomeRow(CharacterBody body)
        {
            var inv = body.inventory;
            int shown = 0;
            for (int i = 0; i < Sheets.Upgrades.Length; i++)
            {
                int points = Upgrades.Points(inv, i);
                if (points <= 0) continue;
                if (shown >= tomes.Count)
                {
                    int col = shown % 8, row = shown / 8;
                    tomes.Add(MakeSlot("Tome" + shown, TomeSize, new Vector2(col * (TomeSize + Gap * 0.75f), -46f - WeaponSize - 10f - row * (TomeSize + 6f))));
                }
                var s = tomes[shown++];
                if (!s.rt.gameObject.activeSelf) s.rt.gameObject.SetActive(true);
                SetIcon(s, UpgradePanel.Icon(Sheets.Upgrades[i].MbIcon), 1f);
                s.level.text = "Lv " + points;
                s.level.color = Gold;
                if (s.last != i) { s.last = i; s.pop = 0f; }
                Pop(s, points + 1000 * i);
            }
            for (int k = shown; k < tomes.Count; k++)
                if (tomes[k].rt.gameObject.activeSelf) tomes[k].rt.gameObject.SetActive(false);
        }

        static void SetIcon(Slot s, Texture tex, float alpha)
        {
            if (s.icon.texture != tex) { s.icon.texture = tex; s.icon.uvRect = new Rect(0, 0, 1, 1); }
            s.icon.color = new Color(1f, 1f, 1f, tex ? alpha : 0f);
        }

        /// <summary>A slot pops (grows and flashes gold) when its level goes up, like Megabonk's.</summary>
        void Pop(Slot s, int value)
        {
            if (s.levelValue >= 0 && value > s.levelValue) s.pop = 1f;
            s.levelValue = value;
            s.pop = Mathf.MoveTowards(s.pop, 0f, Time.unscaledDeltaTime * 2.5f);
            s.frame.color = Color.Lerp(Color.black, Gold, s.pop);
            s.rt.localScale = Vector3.one * (1f + 0.15f * s.pop);
        }

        /// <summary>
        /// While the Megabonk HUD shows, the Risk of Rain 2 pieces it replaces are invisible: the skill icons, the health
        /// bar and the level/experience display (the Megabonk XP bar shows the level), and the sprint icon (Megabonk
        /// movement always runs). Buffs, money, items and equipment stay.
        /// </summary>
        void HideRoR2(bool hide)
        {
            foreach (var hud in HUD.readOnlyInstanceList)
            {
                if (!hud) continue;
                if (hud.skillIcons != null) foreach (var icon in hud.skillIcons) if (icon) Fade(icon.gameObject, hide);
                if (hud.healthBar) Fade(hud.healthBar.gameObject, hide);
                // The level display also holds the buffs: everything in it but them.
                var cluster = hud.levelText ? hud.levelText.transform.parent : null;
                if (cluster)
                    foreach (Transform child in cluster)
                        if (!(hud.buffDisplay && hud.buffDisplay.transform.IsChildOf(child))) Fade(child.gameObject, hide);
                if (hud.expBar) Fade(hud.expBar.gameObject, hide);
                if (hud.sprintIcon) Fade(hud.sprintIcon.gameObject, hide && BonkMovement.Enabled);
                if (TestKeys.On && !logged && hud.levelText)
                {
                    logged = true;
                    Log.Info($"HUD: level display {Path(hud.levelText.transform.parent)}, health bar {Path(hud.healthBar ? hud.healthBar.transform : null)}, buffs {Path(hud.buffDisplay ? hud.buffDisplay.transform : null)}");
                }
            }
        }

        bool logged;

        static string Path(Transform t)
        {
            string p = "";
            for (int i = 0; t && i < 5; i++, t = t.parent) p = "/" + t.name + p;
            return p;
        }

        static void Fade(GameObject go, bool hide)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (!group) group = go.AddComponent<CanvasGroup>();
            float alpha = hide ? 0f : 1f;
            if (group.alpha != alpha) group.alpha = alpha;
        }
    }
}
