using System.Collections.Generic;
using System.Linq;
using RiskOfBonk.Generated;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RiskOfBonk
{
    /// <summary>
    /// The Megabonk level-up menu: it pops up while the local player has picks pending and offers three cards, chosen
    /// with 1/2/3. Cards are tomes, and for Megabonk survivors also weapons (new ones fill right mouse, Shift, R in order;
    /// owned ones level up). The game is not paused (online co-op): instead the host makes you invincible for
    /// tuning.upgrade_invincibility seconds, shown as a countdown that turns red and shakes in its last 30%; picking
    /// ends it early. Icons are Megabonk's own.
    /// </summary>
    public sealed class UpgradePanel : MonoBehaviour
    {
        struct Offer { public byte kind; public int index, rarity; public bool isNew; }

        sealed class Card
        {
            public RectTransform rt;
            public CanvasGroup group;
            public Image border, flash;
            public RawImage icon;
            public TextMeshProUGUI name, tag, desc;
        }

        RectTransform root, timerRt, timerFill;
        CanvasGroup rootGroup;
        TextMeshProUGUI title, timerText;
        Image timerFillImg;
        readonly List<Card> cards = new List<Card>();
        Offer[] offers;
        int offersForPicks = -1, waitingForPicks = -1, chosen = -1;
        float waitingSince, openedAt, closingAt = -1f;
        readonly System.Random rng = new System.Random();
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static readonly string[] slotNames = { "right mouse", "Shift", "R" };
        // Gamepad: the face buttons in the cards' order on the pad (X left, Y top, B right).
        static readonly KeyCode[] padButtons = { KeyCode.JoystickButton2, KeyCode.JoystickButton3, KeyCode.JoystickButton1 };
        static readonly string[] padNames = { "X", "Y", "B" };

        public static bool Open { get; private set; }

        /// <summary>Melty's test mode picks a card through this (0-2), as if its key was pressed.</summary>
        public static int TestPick = -1;

        internal static Texture2D Icon(string name)
        {
            if (icons.TryGetValue(name, out var t)) return t;
            var lib = MegabonkLoader.Library;
            if (lib != null)
            {
                RawTexture raw;
                lock (lib) raw = lib.FindTexture(new[] { name });
                t = UnityConvert.Texture(raw, lib);
            }
            icons[name] = t;
            return t;
        }

        void Build()
        {
            var canvas = BonkUI.Canvas(transform, "RiskOfBonkUpgrades", 60);
            root = BonkUI.Rect(canvas, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1060, 560));
            rootGroup = root.gameObject.AddComponent<CanvasGroup>();

            title = BonkUI.Label(root, "Title", 54, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(1060, 70));
            title.color = MenuStyle.C("accent");

            // Invincibility countdown: number and a shrinking bar.
            timerRt = BonkUI.Rect(root, "Timer", new Vector2(0.5f, 1f), new Vector2(0, -92), new Vector2(420, 44));
            timerText = BonkUI.Label(timerRt, "Seconds", 34, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(420, 40));
            var barBack = BonkUI.Rect(timerRt, "Bar", new Vector2(0.5f, 0f), new Vector2(0, -8), new Vector2(360, 10));
            BonkUI.Image(barBack, new Color(0, 0, 0, 0.6f));
            timerFill = BonkUI.Fill(barBack, "Fill", 1f);
            timerFill.pivot = new Vector2(0f, 0.5f);
            timerFillImg = BonkUI.Image(timerFill, Color.white);

            for (int i = 0; i < 3; i++)
            {
                var c = new Card();
                c.rt = BonkUI.Rect(root, "Card" + (i + 1), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 340, -60), new Vector2(310, 360));
                c.group = c.rt.gameObject.AddComponent<CanvasGroup>();
                c.border = BonkUI.Image(c.rt, Color.white);
                var inner = BonkUI.Fill(c.rt, "Inner", 6f);
                var panel = MenuStyle.C("panel");
                BonkUI.Image(inner, new Color(panel.r, panel.g, panel.b, 0.97f));
                var iconRt = BonkUI.Rect(inner, "Icon", new Vector2(0.5f, 1f), new Vector2(0, -88), new Vector2(128, 128));
                c.icon = iconRt.gameObject.AddComponent<RawImage>();
                c.name = BonkUI.Label(inner, "Name", 30, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0, -184), new Vector2(290, 40));
                c.tag = BonkUI.Label(inner, "Tag", 20, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0, -216), new Vector2(290, 28));
                c.desc = BonkUI.Label(inner, "Desc", 21, TextAlignmentOptions.Top, new Vector2(0.5f, 1f), new Vector2(0, -278), new Vector2(270, 90));
                c.desc.fontStyle = FontStyles.Bold;
                var key = BonkUI.Label(c.rt, "Key", 36, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0, -2), new Vector2(140, 50));
                key.text = $"{i + 1} <size=55%>/ {padNames[i]}</size>";
                key.color = MenuStyle.C("accent");
                var flashRt = BonkUI.Fill(c.rt, "Flash");
                c.flash = BonkUI.Image(flashRt, new Color(1, 1, 1, 0));
                c.flash.raycastTarget = false;
                cards.Add(c);
            }
            root.gameObject.SetActive(false);
        }

        void Show(bool on)
        {
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            Open = on;
        }

        void Update()
        {
            if (root == null) Build();
            var master = Run.instance ? LocalUserManager.GetFirstLocalUser()?.cachedMaster : null;
            var body = master ? master.GetBody() : null;
            int pending = Upgrades.Pending(master);

            if (closingAt >= 0f)
            {
                float t = (Time.unscaledTime - closingAt) / 0.22f;
                if (chosen >= 0) cards[chosen].flash.color = new Color(1, 1, 1, Mathf.Clamp01(1f - t));
                root.localScale = Vector3.one * Mathf.Lerp(1f, 0.85f, t);
                rootGroup.alpha = 1f - t;
                if (t < 1f) return;
                closingAt = -1f;
                Show(false);
            }

            if (pending <= 0 || !body || Upgrades.TomeItems.Length < 3) { Show(false); return; }
            int picks = master.inventory.GetItemCountPermanent(Upgrades.PicksItem);
            if (waitingForPicks == picks && Time.unscaledTime - waitingSince < 3f) return; // pick sent, waiting for the host
            if (offers == null || offersForPicks != picks)
            {
                Roll(body, picks);
                openedAt = Time.unscaledTime;
                Upgrades.MenuOpened();
            }
            Show(true);
            // Level-up picks come first; once they are used up the rest are from Charge Shrines.
            bool fromLevel = (int)TeamManager.instance.GetTeamLevel(master.teamIndex) - 1 > picks;
            string head = fromLevel ? "Level up!" : "Bonus upgrade!"; // Charge Shrines and chests
            title.text = pending > 1 ? $"{head}  <size=70%>x{pending}</size>" : head;
            Animate();

            for (int i = 0; i < 3; i++)
            {
                if (TestPick != i && !Input.GetKeyDown(KeyCode.Alpha1 + i) && !Input.GetKeyDown(KeyCode.Keypad1 + i) && !Input.GetKeyDown(padButtons[i])) continue;
                TestPick = -1;
                Upgrades.RequestPick(offers[i].kind, offers[i].index, offers[i].rarity);
                waitingForPicks = picks;
                waitingSince = Time.unscaledTime;
                offers = null;
                chosen = i;
                closingAt = Time.unscaledTime;
                cards[i].flash.color = Color.white;
                break;
            }
        }

        void Animate()
        {
            float t = Time.unscaledTime - openedAt;
            root.localScale = Vector3.one * Mathf.LerpUnclamped(0.7f, 1f, BonkUI.BackOut(t / 0.28f));
            rootGroup.alpha = Mathf.Clamp01(t / 0.12f);
            for (int i = 0; i < cards.Count; i++)
            {
                float ct = Mathf.Clamp01((t - 0.08f - i * 0.07f) / 0.25f);
                cards[i].group.alpha = ct;
                cards[i].rt.anchoredPosition = new Vector2((i - 1) * 340, -60 - 70f * (1f - BonkUI.BackOut(ct)));
                cards[i].flash.color = new Color(1, 1, 1, Mathf.Clamp01(0.6f - ct * 0.6f));
            }

            // Invincibility countdown: red and shaking in its last 30%.
            float total = Mathf.Max(0.1f, Tune.UpgradeInvincibility);
            float left = total - t;
            if (left > 0f)
            {
                float frac = left / total;
                bool danger = frac <= 0.3f;
                var col = danger ? new Color(1f, 0.2f, 0.15f) : Color.white;
                timerText.text = $"Invincible  {left:0.0}";
                timerText.color = col;
                timerFillImg.color = col;
                timerFill.localScale = new Vector3(frac, 1f, 1f);
                float shake = danger ? Mathf.Lerp(3f, 9f, 1f - frac / 0.3f) : 0f;
                timerRt.anchoredPosition = new Vector2(0, -92) + (danger ? Random.insideUnitCircle * shake : Vector2.zero);
                timerRt.localScale = Vector3.one * (danger ? 1f + 0.08f * Mathf.Abs(Mathf.Sin(t * 18f)) : 1f);
            }
            else
            {
                timerText.text = "Not invincible";
                timerText.color = new Color(0.6f, 0.6f, 0.65f);
                timerFill.localScale = new Vector3(0f, 1f, 1f);
                timerRt.anchoredPosition = new Vector2(0, -92);
                timerRt.localScale = Vector3.one;
            }
        }

        void Roll(CharacterBody body, int picks)
        {
            var inv = body.inventory;
            var list = new List<Offer>();
            var tomes = Enumerable.Range(0, Sheets.Upgrades.Length).OrderBy(_ => rng.Next()).ToList();
            var weaponPool = new List<(int w, bool isNew)>();
            var extras = Weapons.Extras(body);
            if (Weapons.IsMegabonk(body) && Weapons.Items.Length > 0)
            {
                int start = Weapons.StartWeapon(body);
                foreach (var w in extras.Prepend(start)) if (w >= 0) weaponPool.Add((w, false));
                if (extras.Count < Weapons.MaxExtras)
                    for (int w = 0; w < Sheets.Weapons.Length; w++)
                        if (w != start && !extras.Contains(w)) weaponPool.Add((w, true));
                weaponPool = weaponPool.OrderBy(_ => rng.Next()).ToList();
            }
            for (int i = 0; i < 3; i++)
            {
                bool weapon = weaponPool.Count > 0 && (tomes.Count == 0 || rng.NextDouble() < Tune.WeaponOfferChance);
                if (weapon)
                {
                    var (w, isNew) = weaponPool[0];
                    weaponPool.RemoveAt(0);
                    list.Add(new Offer { kind = Upgrades.KindWeapon, index = w, rarity = isNew ? 0 : Upgrades.RollRarity(rng, inv), isNew = isNew });
                }
                else
                {
                    list.Add(new Offer { kind = Upgrades.KindTome, index = tomes[0], rarity = Upgrades.RollRarity(rng, inv) });
                    tomes.RemoveAt(0);
                }
            }
            offers = list.ToArray();
            offersForPicks = picks;
            for (int i = 0; i < 3; i++)
            {
                var o = offers[i];
                var c = cards[i];
                var rar = Sheets.Rarities[o.rarity];
                ColorUtility.TryParseHtmlString(rar.Color, out var col);
                if (o.kind == Upgrades.KindWeapon)
                {
                    var w = Sheets.Weapons[o.index];
                    c.icon.texture = Icon(w.MbIcon);
                    c.name.text = w.DisplayName;
                    if (o.isNew)
                    {
                        col = MenuStyle.C("accent");
                        c.tag.text = "New weapon";
                        c.desc.text = $"{w.Description}\n<color=#FFC21A>Goes on {slotNames[Mathf.Min(extras.Count, 2)]}</color>";
                    }
                    else
                    {
                        int lv = Weapons.Level(body, o.index);
                        c.tag.text = $"{rar.Label} weapon upgrade";
                        c.desc.text = $"Level {lv} -> {lv + rar.Points}\n{Weapons.Describe(w, lv, rar.Points)}";
                    }
                }
                else
                {
                    var up = Sheets.Upgrades[o.index];
                    c.icon.texture = Icon(up.MbIcon);
                    c.name.text = up.DisplayName;
                    c.tag.text = rar.Label;
                    int have = Upgrades.Points(inv, o.index);
                    c.desc.text = Upgrades.Describe(o.index, o.rarity) + (have > 0 ? $"\n<size=80%>(have {have} pts)</size>" : "");
                }
                c.icon.enabled = c.icon.texture != null;
                c.border.color = col;
                c.name.color = col;
                c.tag.color = col;
            }
        }
    }
}
