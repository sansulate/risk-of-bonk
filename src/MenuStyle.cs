using System.Collections.Generic;
using System.Linq;
using RiskOfBonk.Generated;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RiskOfBonk
{
    /// <summary>
    /// Gives Risk of Rain 2's menus the Megabonk look (sheets/menu_style.json): chunky outlined text, dark panels, bright
    /// buttons and the Megabonk logo read from the player's Megabonk copy. Only visuals change: every RoR2 button,
    /// including online multiplayer, keeps working as before.
    /// </summary>
    public static class MenuStyle
    {
        static readonly Dictionary<string, Color> palette = new Dictionary<string, Color>();
        static Sprite logo;
        static bool logoTried;

        public static Color C(string id)
        {
            if (palette.Count == 0)
                foreach (var r in Sheets.MenuStyle)
                    if (ColorUtility.TryParseHtmlString(r.Color, out var c)) { c.a = r.Alpha; palette[r.Id] = c; }
            return palette.TryGetValue(id, out var col) ? col : Color.white;
        }

        public static Sprite Logo()
        {
            if (logoTried) return logo;
            logoTried = true;
            var lib = MegabonkLoader.Library;
            var row = Sheets.MenuStyle.FirstOrDefault(r => r.Id == "logo");
            if (lib == null || row == null || row.MbSpriteHints.Length == 0) return null;
            RawTexture raw;
            lock (lib) raw = lib.FindTexture(row.MbSpriteHints);
            var tex = UnityConvert.Texture(raw, lib);
            if (tex != null)
            {
                logo = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                Log.Info($"Megabonk logo: {raw.Name} {tex.width}x{tex.height}");
            }
            return logo;
        }

        static readonly HashSet<int> styled = new HashSet<int>();

        /// <summary>Restyle everything under root that has not been styled yet (cheap to call repeatedly).</summary>
        public static void Apply(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (!styled.Add(t.GetInstanceID())) continue;
                t.fontStyle |= FontStyles.Bold | FontStyles.UpperCase;
                if (t.color.a > 0.05f) t.color = new Color(C("text").r, C("text").g, C("text").b, t.color.a);
                t.outlineColor = C("outline");
                t.outlineWidth = 0.22f;
            }
            foreach (var b in root.GetComponentsInChildren<Selectable>(true))
            {
                if (!styled.Add(b.GetInstanceID())) continue;
                var cb = b.colors;
                cb.normalColor = C("accent");
                cb.highlightedColor = Color.Lerp(C("accent"), Color.white, 0.35f);
                cb.selectedColor = cb.highlightedColor;
                cb.pressedColor = Color.Lerp(C("accent"), Color.black, 0.25f);
                cb.disabledColor = new Color(0.35f, 0.35f, 0.4f, 0.8f);
                b.colors = cb;
            }
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (!styled.Add(img.GetInstanceID())) continue;
                if (img.GetComponent<Selectable>() != null) continue; // tinted through its ColorBlock
                string n = img.name.ToLowerInvariant();
                if (n.Contains("logo")) continue;
                // Large panels get the dark Megabonk backdrop; small decorations keep their own tint.
                var rt = img.rectTransform.rect;
                if (rt.width * rt.height > 40000 && img.color.a > 0.1f)
                    img.color = new Color(C("panel").r, C("panel").g, C("panel").b, Mathf.Max(img.color.a, C("panel").a));
            }
        }

        public static void TitleLogo(Transform root)
        {
            if (root.Find("RiskOfBonkTitle") != null) return;
            var logoImg = root.GetComponentsInChildren<Image>(true).FirstOrDefault(i =>
                i.name.ToLowerInvariant().Contains("logo") || (i.sprite != null && i.sprite.name.ToLowerInvariant().Contains("logo")));
            var mb = Logo();
            if (logoImg != null && mb != null)
            {
                logoImg.sprite = mb;
                logoImg.preserveAspect = true;
                logoImg.color = Color.white;
            }
            else if (logoImg != null) logoImg.enabled = false;

            // The mashup's own name in Megabonk's chunky yellow, above the menu buttons.
            var anyText = root.GetComponentInChildren<TextMeshProUGUI>(true);
            var go = new GameObject("RiskOfBonkTitle", typeof(RectTransform));
            var parent = logoImg != null ? logoImg.transform.parent : root;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            if (logoImg != null)
            {
                var lr = logoImg.rectTransform;
                rt.anchorMin = lr.anchorMin; rt.anchorMax = lr.anchorMax; rt.pivot = lr.pivot;
                rt.anchoredPosition = lr.anchoredPosition + new Vector2(0, mb != null ? -lr.rect.height * 0.55f : 0);
                rt.sizeDelta = new Vector2(Mathf.Max(lr.rect.width, 900), 160);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.85f);
                rt.sizeDelta = new Vector2(1200, 180);
            }
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (anyText != null) tmp.font = anyText.font;
            tmp.text = "RISK OF BONK";
            tmp.fontSize = 120;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = 140;
            tmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = C("accent");
            tmp.outlineColor = C("outline");
            tmp.outlineWidth = 0.3f;
            tmp.raycastTarget = false;
            styled.Add(tmp.GetInstanceID());
        }

        /// <summary>Megabonk-style menus: restyles new UI as it appears (screens are created lazily).</summary>
        public sealed class Styler : MonoBehaviour
        {
            public bool title;
            float next;
            void Update()
            {
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.5f;
                Apply(transform);
                if (title) TitleLogo(transform);
            }
        }

        /// <summary>The Bonk style / Rain style button next to Ready on the character select screen.</summary>
        public sealed class StyleButton : MonoBehaviour
        {
            public CharacterSelectController controller;
            MPButton button;
            TextMeshProUGUI label;
            Image tint;

            void Start()
            {
                var ready = controller.readyButton;
                if (ready == null) { Destroy(this); return; }
                var go = Instantiate(ready.gameObject, ready.transform.parent);
                go.name = "RiskOfBonkStyleButton";
                foreach (var lang in go.GetComponentsInChildren<LanguageTextMeshController>(true)) Destroy(lang);
                // Ready's gamepad shortcut and its button glyph must not come along with the clone.
                foreach (var gp in go.GetComponentsInChildren<HGGamepadInputEvent>(true)) Destroy(gp);
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c != null && c.GetType().Name.Contains("InputBindingDisplay") && c.gameObject != go) Destroy(c.gameObject);
                button = go.GetComponent<MPButton>();
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(StageMode.Toggle);
                label = go.GetComponentInChildren<TextMeshProUGUI>(true);
                tint = go.GetComponent<Image>();
                var rt = (RectTransform)go.transform;
                var rr = (RectTransform)ready.transform;
                rt.anchoredPosition = rr.anchoredPosition + new Vector2(0, rr.rect.height + 12);
                go.SetActive(true);
                StageMode.Changed += Refresh;
                Refresh();
            }

            void OnDestroy() => StageMode.Changed -= Refresh;

            float nextFit;
            GridLayoutGroup grid;
            float gridHeight;

            void Update()
            {
                if (button != null) button.interactable = StageMode.CanChange;
                if (Time.unscaledTime >= nextFit) { nextFit = Time.unscaledTime + 0.5f; FitSurvivorGrid(); }
            }

            /// <summary>
            /// Twenty extra survivors overflow the grid onto the Overview/Skills tabs: add columns and shrink the icons
            /// until every row fits in the height the grid had for the vanilla roster.
            /// </summary>
            void FitSurvivorGrid()
            {
                if (grid == null)
                {
                    var icon = controller.GetComponentInChildren<SurvivorIconController>(true);
                    grid = icon != null ? icon.transform.parent.GetComponent<GridLayoutGroup>() : null;
                    if (grid == null) return;
                    var rt0 = (RectTransform)grid.transform;
                    // Room for the vanilla roster: 3 rows of the original cell size.
                    gridHeight = grid.padding.vertical + 3 * grid.cellSize.y + 2 * grid.spacing.y;
                    Log.Info($"Survivor grid: {grid.constraint} {grid.constraintCount}, cell {grid.cellSize}, width {rt0.rect.width}");
                }
                int n = 0;
                foreach (Transform c in grid.transform) if (c.gameObject.activeSelf) n++;
                if (n == 0) return;
                var rt = (RectTransform)grid.transform;
                float width = rt.rect.width - grid.padding.horizontal;
                float aspect = grid.cellSize.y / Mathf.Max(1f, grid.cellSize.x);
                for (int cols = Mathf.Max(1, grid.constraintCount); cols < 40; cols++)
                {
                    float cell = (width - grid.spacing.x * (cols - 1)) / cols;
                    int rows = (n + cols - 1) / cols;
                    float h = grid.padding.vertical + rows * cell * aspect + (rows - 1) * grid.spacing.y;
                    if (h <= gridHeight + 0.5f || cell < 24f)
                    {
                        if (grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount || grid.constraintCount != cols)
                        {
                            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                            grid.constraintCount = cols;
                            grid.cellSize = new Vector2(cell, cell * aspect);
                        }
                        return;
                    }
                }
            }

            void Refresh()
            {
                if (label == null) return;
                var row = StageMode.Row;
                label.text = row.Label.ToUpperInvariant();
                var c = C(row.Color);
                if (button != null)
                {
                    var cb = button.colors;
                    cb.normalColor = c;
                    cb.highlightedColor = Color.Lerp(c, Color.white, 0.35f);
                    cb.selectedColor = cb.highlightedColor;
                    button.colors = cb;
                }
                if (tint != null) tint.color = Color.white;
                var tip = button != null ? button.GetComponent<TooltipProvider>() : null;
                if (tip != null) Destroy(tip);
            }
        }
    }
}
