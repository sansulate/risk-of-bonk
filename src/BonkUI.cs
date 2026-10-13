using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RiskOfBonk
{
    /// <summary>Small helpers for the mod's own Megabonk-style HUD pieces (screen overlay canvases, 1920x1080 reference).</summary>
    public static class BonkUI
    {
        static TMP_FontAsset font;

        public static TMP_FontAsset Font() =>
            font != null ? font : font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.ToLowerInvariant().Contains("bomb"))
                                         ?? TMP_Settings.defaultFontAsset;

        public static Transform Canvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;
            return go.transform;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Stretch to fill the parent, inset by the given margin.</summary>
        public static RectTransform Fill(Transform parent, string name, float inset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Image(RectTransform rt, Color c)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.color = c;
            return i;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, float size, TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var rt = Rect(parent, name, anchor, pos, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font();
            t.fontSize = size;
            t.alignment = align;
            t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            t.color = MenuStyle.C("text");
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            // Chunky Megabonk outline (own material instance per label).
            var mat = t.fontMaterial;
            if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
                mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.25f);
            }
            if (mat.HasProperty(ShaderUtilities.ID_UnderlayColor)) mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, 0.8f));
            return t;
        }

        /// <summary>0..1 ease with a little overshoot, for pop-in animations.</summary>
        public static float BackOut(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
        }
    }
}
