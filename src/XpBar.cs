using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's XP bar across the top of the screen: level badge, a fill that eases toward the team's XP, a pulse each
    /// time a gem lands, and on level up the bar fills, flashes white and a "LEVEL UP!" pops in with a bounce.
    /// </summary>
    public sealed class XpBar : MonoBehaviour
    {
        RectTransform root, fill, glowRt, popRt;
        Image glow, flash;
        TextMeshProUGUI levelText, popText, xpText;
        float shown, pulse, flashAmt, popAt = -10f;
        int lastLevel = -1;
        bool fillingToLevel;

        void Build()
        {
            var canvas = BonkUI.Canvas(transform, "RiskOfBonkXpBar", 40);
            root = BonkUI.Rect(canvas, "XpBar", new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(1100, 30));
            BonkUI.Image(root, Color.black);
            var back = BonkUI.Fill(root, "Back", 3f);
            BonkUI.Image(back, new Color(0.08f, 0.1f, 0.22f, 0.92f));
            fill = BonkUI.Fill(back, "Fill");
            fill.pivot = new Vector2(0f, 0.5f);
            BonkUI.Image(fill, new Color(0.25f, 0.7f, 1f));
            // Lighter band on the upper half, for Megabonk's glossy look.
            var shine = BonkUI.Fill(fill, "Shine");
            shine.anchorMin = new Vector2(0f, 0.55f);
            BonkUI.Image(shine, new Color(1f, 1f, 1f, 0.22f));
            glowRt = BonkUI.Fill(back, "Glow");
            glow = BonkUI.Image(glowRt, new Color(0.6f, 0.9f, 1f, 0f));
            flash = BonkUI.Image(BonkUI.Fill(root, "Flash"), new Color(1, 1, 1, 0));

            var badge = BonkUI.Rect(root, "Badge", new Vector2(0f, 0.5f), new Vector2(-50, 0), new Vector2(96, 46));
            BonkUI.Image(badge, Color.black);
            BonkUI.Image(BonkUI.Fill(badge, "In", 3f), MenuStyle.C("accent"));
            levelText = BonkUI.Label(badge, "Level", 26, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 46));
            xpText = BonkUI.Label(root, "Xp", 16, TextAlignmentOptions.Right, new Vector2(1f, 0.5f), new Vector2(-70, 0), new Vector2(130, 24));

            popRt = BonkUI.Rect(canvas, "LevelUp", new Vector2(0.5f, 1f), new Vector2(0, -95), new Vector2(700, 90));
            popText = BonkUI.Label(popRt, "Text", 72, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 90));
            popText.text = "Level up!";
            popText.color = MenuStyle.C("accent");
            popRt.gameObject.SetActive(false);

            XpGems.Collected += _ => pulse = 1f;
        }

        void Update()
        {
            if (root == null) Build();
            var tm = TeamManager.instance;
            bool on = Run.instance && tm && LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) { lastLevel = -1; popRt.gameObject.SetActive(false); return; }

            int level = (int)tm.GetTeamLevel(TeamIndex.Player);
            ulong cur = tm.GetTeamCurrentLevelExperience(TeamIndex.Player), next = tm.GetTeamNextLevelExperience(TeamIndex.Player);
            ulong exp = tm.GetTeamExperience(TeamIndex.Player);
            float target = next > cur ? Mathf.Clamp01((float)(exp - cur) / (next - cur)) : 0f;

            if (lastLevel < 0) { lastLevel = level; shown = target; }
            if (level > lastLevel)
            {
                lastLevel = level;
                fillingToLevel = true;
            }
            float dt = Time.unscaledDeltaTime;
            if (fillingToLevel)
            {
                shown = Mathf.MoveTowards(shown, 1f, dt * 3f);
                if (shown >= 1f)
                {
                    fillingToLevel = false;
                    shown = 0f;
                    flashAmt = 1f;
                    popAt = Time.unscaledTime;
                }
            }
            else shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-8f * dt));

            fill.localScale = new Vector3(Mathf.Clamp01(shown), 1f, 1f);
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 3f);
            flashAmt = Mathf.MoveTowards(flashAmt, 0f, dt * 2f);
            glow.color = new Color(0.6f, 0.9f, 1f, 0.55f * pulse);
            flash.color = new Color(1, 1, 1, flashAmt);
            root.localScale = new Vector3(1f, 1f + 0.35f * pulse + 0.4f * flashAmt, 1f);
            levelText.text = "Lv " + level;
            xpText.text = $"{exp - cur}/{next - cur}";

            // "LEVEL UP!": pops in with a bounce, wobbles, fades.
            float pt = Time.unscaledTime - popAt;
            bool popping = pt < 1.6f;
            if (popRt.gameObject.activeSelf != popping) popRt.gameObject.SetActive(popping);
            if (popping)
            {
                popRt.localScale = Vector3.one * BonkUI.BackOut(pt / 0.3f) * (1f + 0.05f * Mathf.Sin(pt * 14f));
                popRt.localRotation = Quaternion.Euler(0, 0, 4f * Mathf.Sin(pt * 9f) * (1f - pt / 1.6f));
                var c = popText.color;
                popText.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((1.6f - pt) / 0.4f));
            }
        }
    }
}
