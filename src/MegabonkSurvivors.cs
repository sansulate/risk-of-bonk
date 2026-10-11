using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RiskOfBonk.Generated;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace RiskOfBonk
{
    /// <summary>
    /// Registers every Megabonk character (sheets/characters.json) as a Risk of Rain 2 survivor: a network-registered
    /// clone of its moveset survivor whose visible model is the Megabonk model read from the player's own Megabonk copy.
    /// </summary>
    public sealed class MegabonkSurvivors : IContentPackProvider
    {
        public string identifier => Plugin.Guid;
        readonly ContentPack pack = new ContentPack();
        public static readonly List<GameObject> NetworkPrefabs = new List<GameObject>();
        public static readonly List<string> Registered = new List<string>();
        static GameObject holder;

        public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
        {
            pack.identifier = identifier;
            while (!MegabonkLoader.Done) yield return null;
            if (MegabonkLoader.Library == null)
            {
                Log.Warn("Megabonk was not found or could not be read: no Megabonk characters this session. " + MegabonkLoader.Error);
                yield break;
            }

            holder = new GameObject("RiskOfBonkPrefabs");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);

            var keys = AddressableKeys();
            var bodies = new List<GameObject>();
            var survivors = new List<SurvivorDef>();
            foreach (var row in Sheets.Characters)
            {
                SurvivorDef def = null;
                try { def = Build(row, keys, bodies); }
                catch (Exception e) { Log.Error($"{row.DisplayName}: {e}"); }
                if (def != null) survivors.Add(def);
                yield return null;
            }
            pack.bodyPrefabs.Add(bodies.ToArray());
            pack.survivorDefs.Add(survivors.ToArray());
            Log.Info($"Megabonk survivors ready: {string.Join(", ", Registered)} ({Registered.Count}/{Sheets.Characters.Length})");
        }

        public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
        {
            ContentPack.Copy(pack, args.output);
            yield break;
        }

        public IEnumerator FinalizeAsync(FinalizeAsyncArgs args) { yield break; }

        static Dictionary<string, string> AddressableKeys()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var locator in Addressables.ResourceLocators)
                foreach (var k in locator.Keys)
                    if (k is string s && s.EndsWith(".prefab") && s.StartsWith("RoR2/"))
                    {
                        string file = s.Substring(s.LastIndexOf('/') + 1);
                        if (!map.ContainsKey(file)) map[file] = s;
                    }
            return map;
        }

        static SurvivorDef Build(CharacterRow row, Dictionary<string, string> keys, List<GameObject> bodies)
        {
            var lib = MegabonkLoader.Library;
            RawModel raw;
            lock (lib) raw = lib.FindModel(row.MbNameHints);
            if (raw == null || raw.VertexCount == 0)
            {
                Log.Warn($"{row.DisplayName}: no model matching [{string.Join(", ", row.MbNameHints)}] in Megabonk; skipped (see megabonk_inventory.txt).");
                return null;
            }
            RawTexture portraitRaw;
            lock (lib) portraitRaw = lib.FindTexture(new[] { row.MbPortrait });

            if (!keys.TryGetValue(row.MovesetBody + ".prefab", out var bodyKey))
                throw new Exception($"moveset body {row.MovesetBody} not in Addressables");
            string displayName = row.MovesetBody.Replace("Body", "Display") + ".prefab";
            keys.TryGetValue(displayName, out var displayKey);

            var srcBody = Addressables.LoadAssetAsync<GameObject>(bodyKey).WaitForCompletion();
            var srcDisplay = displayKey != null ? Addressables.LoadAssetAsync<GameObject>(displayKey).WaitForCompletion() : null;
            if (srcDisplay == null) throw new Exception($"no display prefab {displayName}");

            string id = "MB" + Pascal(row.Id);
            var body = Clone(srcBody, id + "Body", true);
            var display = Clone(srcDisplay, id + "Display", false);

            var mesh = UnityConvert.Mesh(raw);
            string tok = "RISKOFBONK_" + row.Id.ToUpperInvariant();
            Plugin.Tokens[tok + "_NAME"] = row.DisplayName;
            Plugin.Tokens[tok + "_SUBTITLE"] = "From Megabonk";
            Plugin.Tokens[tok + "_DESC"] = row.Description + "\n\n<style=cSub>Megabonk character, read from your own Megabonk install. Moveset: " + MovesetLabel(row.MovesetBody) + ".</style>";
            Plugin.Tokens[tok + "_OUTRO"] = "...and so " + row.DisplayName + " left, still bonking.";
            Plugin.Tokens[tok + "_FAIL"] = "...and so " + row.DisplayName + " vanished, bonked at last.";
            ColorUtility.TryParseHtmlString(row.PrimaryColor, out var color);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = tok + "_NAME";
            cb.subtitleNameToken = tok + "_SUBTITLE";
            cb.bodyColor = color;
            var portrait = UnityConvert.Texture(portraitRaw, lib) ?? SolidPortrait(color);
            cb.portraitIcon = portrait;

            var bodyModel = body.GetComponent<ModelLocator>()?.modelTransform;
            if (bodyModel == null) throw new Exception("moveset body has no model");
            Reskin(bodyModel, raw, mesh, row, lib);
            var displayModel = display.GetComponentInChildren<CharacterModel>(true)?.transform;
            if (displayModel != null)
            {
                Reskin(displayModel, raw, mesh, row, lib);
                FillLight(displayModel);
            }

            var def = ScriptableObject.CreateInstance<SurvivorDef>();
            def.cachedName = "RiskOfBonk" + id;
            def.bodyPrefab = body;
            def.displayPrefab = display;
            def.displayNameToken = tok + "_NAME";
            def.descriptionToken = tok + "_DESC";
            def.outroFlavorToken = tok + "_OUTRO";
            def.mainEndingEscapeFailureFlavorToken = tok + "_FAIL";
            def.primaryColor = color;
            def.desiredSortPosition = row.SortPosition;

            bodies.Add(body);
            NetworkPrefabs.Add(body);
            Registered.Add(row.DisplayName);
            Log.Info($"{row.DisplayName}: Megabonk model '{raw.SourceName}' ({raw.VertexCount} verts, {raw.SubMeshes.Count} parts) on {row.MovesetBody}; portrait {(portraitRaw != null ? portraitRaw.Name : "none (color)")}");
            return def;
        }

        static string MovesetLabel(string body) => body switch
        {
            "CommandoBody" => "Commando", "HuntressBody" => "Huntress", "Bandit2Body" => "Bandit", "ToolbotBody" => "MUL-T",
            "EngiBody" => "Engineer", "MageBody" => "Artificer", "MercBody" => "Mercenary", "TreebotBody" => "REX",
            "LoaderBody" => "Loader", "CrocoBody" => "Acrid", "CaptainBody" => "Captain", "RailgunnerBody" => "Railgunner",
            "VoidSurvivorBody" => "Void Fiend", _ => body.Replace("Body", ""),
        };

        static string Pascal(string id) => string.Concat(id.Split('_').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));

        static GameObject Clone(GameObject src, string name, bool networked)
        {
            var c = UnityEngine.Object.Instantiate(src, holder.transform);
            c.name = name;
            var ni = c.GetComponent<NetworkIdentity>();
            if (ni != null)
            {
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Plugin.Guid + "/" + name));
                ni.m_AssetId = NetworkHash128.Parse(BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
            }
            return c;
        }

        /// <summary>
        /// Character select lights its pedestal dim and red, which turns the flat-colored Megabonk models into
        /// silhouettes: a soft white light in front of the display model shows their colors.
        /// </summary>
        static void FillLight(Transform displayModel)
        {
            var go = new GameObject("MB_FillLight");
            go.transform.SetParent(displayModel, false);
            go.transform.position = displayModel.position + displayModel.forward * 1.6f + Vector3.up * 1.5f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.shadows = LightShadows.None;
        }

        static Texture2D SolidPortrait(Color c)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            t.SetPixels(Enumerable.Repeat(c, 16).ToArray());
            t.Apply();
            return t;
        }

        /// <summary>Replace the survivor's visible renderers with the auto-rigged Megabonk model.</summary>
        static void Reskin(Transform modelRoot, RawModel raw, Mesh mesh, CharacterRow row, MegabonkLibrary lib)
        {
            var cm = modelRoot.GetComponent<CharacterModel>();
            var oldInfos = cm != null && cm.baseRendererInfos != null ? cm.baseRendererInfos : new CharacterModel.RendererInfo[0];
            var template = TemplateMaterial(modelRoot, oldInfos);

            // The survivor's body mesh drives the Megabonk model: the skinned mesh with the most vertices (weapons and
            // gauntlets often reference the whole skeleton too). Its bind pose is where the model is fitted.
            var mainSmr = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                              .Where(s => s.bones.Length > 0)
                              .Select(s => (s, m: SkinMesh(modelRoot, s)))
                              .OrderByDescending(x => x.m != null && x.s.bones.Length >= 10 ? x.m.vertexCount : -1)
                              .ThenByDescending(x => x.s.bones.Length)
                              .Select(x => x.s).FirstOrDefault()
                          ?? (cm != null ? cm.mainSkinnedMeshRenderer : null);
            Transform[] bones = mainSmr != null && mainSmr.bones.Length > 0
                ? mainSmr.bones
                : modelRoot.GetComponentsInChildren<Transform>(true).Where(t => t != modelRoot).ToArray();

            var mats = raw.SubMeshes.Select(sm =>
            {
                var m = new Material(template);
                m.name = "MB_" + raw.SourceName;
                var tex = UnityConvert.Texture(sm.Texture, lib);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(sm.Color[0], sm.Color[1], sm.Color[2], 1f));
                foreach (var p in new[] { "_NormalTex", "_BumpMap", "_EmTex", "_FresnelRamp", "_FlowHeightmap" })
                    if (m.HasProperty(p)) m.SetTexture(p, null);
                if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", 0f);
                if (m.HasProperty("_NormalStrength")) m.SetFloat("_NormalStrength", 0f);
                return m;
            }).ToArray();

            var fitted = AutoRig.Fit(modelRoot, mainSmr, SkinMesh(modelRoot, mainSmr)?.bindposes, bones, mesh, mats, row.YawOffsetDeg, "MB_" + row.Id, raw);
            // CharacterModel draws each renderer with one material, so every part has its own renderer.
            var parts = AutoRig.SplitParts(fitted);
            var smr = parts[0];

            // Hide every original renderer (body, weapons, attachments).
            foreach (var r in modelRoot.GetComponentsInChildren<Renderer>(true))
                if (System.Array.IndexOf(parts, r) < 0 && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer))
                {
                    // Skins load meshes lazily and re-enable renderers, so also force them off for good.
                    r.enabled = false;
                    r.forceRenderingOff = true;
                    if (r is SkinnedMeshRenderer o) o.sharedMesh = null;
                }

            var infos = parts.Select(part => new CharacterModel.RendererInfo
            {
                renderer = part,
                defaultMaterial = part.sharedMaterial,
                defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                ignoreOverlays = false,
            }).ToArray();
            if (cm != null)
            {
                cm.baseRendererInfos = infos;
                cm.mainSkinnedMeshRenderer = smr;
            }

            // Skins would bring the original renderers back: replace them with one Megabonk skin.
            var msc = modelRoot.GetComponent<ModelSkinController>();
            if (msc != null)
            {
                var skin = ScriptableObject.CreateInstance<SkinDef>();
                skin.name = "MB_" + row.Id + "Default";
                SetMember(skin, "nameToken", "RISKOFBONK_" + row.Id.ToUpperInvariant() + "_NAME");
                SetMember(skin, "rootObject", modelRoot.gameObject);
                // Newer game builds moved renderer lists into SkinDefParams; fill whichever this build has.
                if (!SetMember(skin, "rendererInfos", infos))
                {
                    var paramsField = typeof(SkinDef).GetField("skinDefParams", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (paramsField != null)
                    {
                        var p = ScriptableObject.CreateInstance(paramsField.FieldType);
                        SetMember(p, "rendererInfos", infos);
                        paramsField.SetValue(skin, p);
                    }
                }
                msc.skins = new[] { skin };
            }
        }

        /// <summary>
        /// A survivor material to copy. Newer game builds leave RendererInfo.defaultMaterial empty and load it from
        /// defaultMaterialAddress, and Shader.Find("Standard") is rejected by the game's legacy shader hook.
        /// </summary>
        static Material TemplateMaterial(Transform modelRoot, CharacterModel.RendererInfo[] infos)
        {
            var m = infos.Select(i => i.defaultMaterial).FirstOrDefault(x => x != null);
            if (m != null) return m;
            var addrField = typeof(CharacterModel.RendererInfo).GetField("defaultMaterialAddress");
            if (addrField != null)
                foreach (object info in infos)
                    if (addrField.GetValue(info) is AssetReference r && r.RuntimeKeyIsValid())
                    {
                        m = Addressables.LoadAssetAsync<Material>(r.RuntimeKey).WaitForCompletion();
                        if (m != null) return m;
                    }
            m = modelRoot.GetComponentsInChildren<Renderer>(true).Select(x => x.sharedMaterial).FirstOrDefault(x => x != null && x.shader.name.Contains("HGStandard"));
            if (m != null) return m;
            var shader = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            return new Material(shader);
        }

        /// <summary>
        /// The mesh a survivor's skinned renderer shows. Newer game builds ship renderers without a mesh and load it
        /// from the default skin's SkinDefParams (meshReplacements[].meshAddress). The skin points at the original
        /// prefab's renderers, so ours are matched by name.
        /// </summary>
        static Mesh SkinMesh(Transform modelRoot, SkinnedMeshRenderer r)
        {
            if (r == null) return null;
            if (r.sharedMesh != null) return r.sharedMesh;
            var msc = modelRoot.GetComponent<ModelSkinController>();
            if (msc == null || msc.skins == null || msc.skins.Length == 0 || msc.skins[0] == null) return null;
            object skin = msc.skins[0];
            var sources = new[]
            {
                skin,
                GetMember(skin, "skinDefParams") ?? LoadAddress<ScriptableObject>(GetMember(skin, "skinDefParamsAddress")),
                GetMember(skin, "optimizedSkinDefParams") ?? LoadAddress<ScriptableObject>(GetMember(skin, "optimizedSkinDefParamsAddress")),
            };
            foreach (var src in sources)
                if (src != null && GetMember(src, "meshReplacements") is Array reps)
                    foreach (var rep in reps)
                        if (GetMember(rep, "renderer") is Renderer rr && rr != null && rr.name == r.name)
                        {
                            var mesh = GetMember(rep, "mesh") as Mesh ?? LoadAddress<Mesh>(GetMember(rep, "meshAddress"));
                            if (mesh != null) return mesh;
                        }
            Log.Info($"{modelRoot.name}: no skin mesh for {r.name}; skin {msc.skins[0].name}, sources " +
                     string.Join(" | ", sources.Select(x => x == null ? "null" : x.GetType().Name + ":" +
                         string.Join(",", (GetMember(x, "meshReplacements") as Array ?? new object[0]).Cast<object>()
                             .Select(rep => (GetMember(rep, "renderer") as Renderer)?.name ?? "?")))));
            return null;
        }

        static object GetMember(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var v = f?.GetValue(o);
            return v is UnityEngine.Object uo && uo == null ? null : v;
        }

        static T LoadAddress<T>(object reference) where T : UnityEngine.Object
        {
            if (!(reference is AssetReference ar) || !ar.RuntimeKeyIsValid()) return null;
            try { return Addressables.LoadAssetAsync<T>(ar.RuntimeKey).WaitForCompletion(); }
            catch (Exception e) { Log.Warn($"load {ar.RuntimeKey}: {e.Message}"); return null; }
        }

        static bool SetMember(object o, string name, object value)
        {
            var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(o, value);
            return true;
        }

        public static void RegisterNetworkPrefabs()
        {
            foreach (var p in NetworkPrefabs) ClientScene.RegisterPrefab(p);
        }
    }
}
