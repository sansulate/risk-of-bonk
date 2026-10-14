using System.Collections.Generic;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's look on Risk of Rain 2's stages ("Rain style: original levels, styled like Megabonk", and the Bonk style
    /// maps too): more saturated, a little brighter and punchier, on top of each stage's own colour grading (a volume of
    /// our own would replace the stage's values instead of adding to them, and made stages darker). Only how this
    /// player's screen looks; numbers in sheets/tuning.json (system "look").
    /// </summary>
    public static class StageLook
    {
        sealed class Graded
        {
            public ColorGrading cg;
            public float sat, con, exp, bri;
            public bool satOn, conOn, expOn, briOn;
        }

        static readonly List<Graded> graded = new List<Graded>();
        static readonly List<(RampFog fog, float original)> fogs = new List<(RampFog, float)>();
        static PostProcessVolume fallback;
        static bool on = true, usingFallback;

        static void Set(bool look)
        {
            foreach (var g in graded)
            {
                if (g.cg == null) continue;
                g.cg.saturation.overrideState = look || g.satOn;
                g.cg.saturation.value = g.sat + (look ? Tune.LookSaturation : 0f);
                g.cg.contrast.overrideState = look || g.conOn;
                g.cg.contrast.value = g.con + (look ? Tune.LookContrast : 0f);
                g.cg.postExposure.overrideState = look || g.expOn;
                g.cg.postExposure.value = g.exp + (look ? Tune.LookExposure : 0f);
                g.cg.brightness.overrideState = look || g.briOn;
                g.cg.brightness.value = g.bri + (look ? Tune.LookBrightness : 0f);
            }
            foreach (var (fog, original) in fogs) if (fog != null) fog.fogIntensity.value = look ? original * Tune.LookFog : original;
            if (fallback) fallback.enabled = look && usingFallback;
        }

        /// <summary>Test key: the look on or off on this stage, to compare.</summary>
        public static void Toggle()
        {
            on = !on;
            Set(on);
            Log.Info($"Megabonk look {(on ? "on" : "off")}");
        }

        static void Apply(Scene scene)
        {
            graded.Clear();
            fogs.Clear();
            usingFallback = false;
            on = true;
            var def = SceneCatalog.GetSceneDefFromSceneName(scene.name);
            if (!Run.instance || !def || def.sceneType != SceneType.Stage) { if (fallback) fallback.enabled = false; return; }
            foreach (var volume in Object.FindObjectsOfType<PostProcessVolume>())
            {
                if (!volume || volume == fallback || !volume.sharedProfile) continue;
                // volume.profile is this volume's own copy, so the game's asset stays as it is.
                var profile = volume.profile;
                if (profile.TryGetSettings<ColorGrading>(out var cg))
                    graded.Add(new Graded
                    {
                        cg = cg,
                        sat = cg.saturation.overrideState ? cg.saturation.value : 0f, satOn = cg.saturation.overrideState,
                        con = cg.contrast.overrideState ? cg.contrast.value : 0f, conOn = cg.contrast.overrideState,
                        exp = cg.postExposure.overrideState ? cg.postExposure.value : 0f, expOn = cg.postExposure.overrideState,
                        bri = cg.brightness.overrideState ? cg.brightness.value : 0f, briOn = cg.brightness.overrideState,
                    });
                if (Tune.LookFog != 1f && profile.TryGetSettings<RampFog>(out var fog) && fog.fogIntensity.overrideState)
                    fogs.Add((fog, fog.fogIntensity.value));
            }
            if (graded.Count == 0)
            {
                // No grading of its own: one global volume for the session (volumes destroyed with a scene can stay in
                // the game's post-processing list and break it), switched on for such stages.
                if (!fallback)
                {
                    var go = new GameObject("RiskOfBonkLook") { layer = LayerIndex.postProcess.intVal };
                    Object.DontDestroyOnLoad(go);
                    go.SetActive(false);
                    fallback = go.AddComponent<PostProcessVolume>();
                    fallback.isGlobal = true;
                    fallback.priority = -100f;
                    var p = ScriptableObject.CreateInstance<PostProcessProfile>();
                    var cg = p.AddSettings<ColorGrading>();
                    cg.saturation.Override(Tune.LookSaturation);
                    cg.contrast.Override(Tune.LookContrast);
                    cg.brightness.Override(Tune.LookBrightness);
                    fallback.sharedProfile = p;
                    go.SetActive(true);
                }
                usingFallback = true;
            }
            Set(true);
            Log.Info($"Megabonk look on {scene.name}: {graded.Count} graded volumes{(usingFallback ? " (none of its own: global look)" : "")}, {fogs.Count} fog volumes");
        }

        public static void Init()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (mode != LoadSceneMode.Single) return;
                try { Apply(scene); } catch (System.Exception e) { Log.Warn($"Megabonk look: {e.Message}"); }
            };
        }
    }
}
