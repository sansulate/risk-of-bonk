using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk's own weapon effects, rebuilt from the player's Megabonk install: each effect prefab's particle systems
    /// (every module this mod reads: main, emission, shape, colour/size/velocity/rotation over lifetime, limit velocity,
    /// force, noise, texture sheet animation, trails, inherit velocity), mesh, trail and line renderers, with Megabonk's
    /// effect textures. Megabonk's Retro Arsenal shaders become Risk of Rain 2's Cloud Remap shader with the remap off
    /// (texture x particle colour): glowing materials add light, the rest blend. Effects are only visuals, built and
    /// played on every machine from what that machine already knows (no network messages).
    /// Names are prefab names, optionally with a child path ("PoisonFlaskProjectile/SmokeTrail").
    /// </summary>
    public static class MegabonkFx
    {
        static readonly Dictionary<string, RawFx> raws = new Dictionary<string, RawFx>();
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        static readonly Dictionary<RawFxMaterial, Material> materials = new Dictionary<RawFxMaterial, Material>();
        static readonly Dictionary<RawModel, Mesh> meshes = new Dictionary<RawModel, Mesh>();
        static GameObject holder;
        static Shader cloud;
        static Texture2D white;

        /// <summary>Reads the effect prefabs (Megabonk's files must be open: while the content loads).</summary>
        public static void Prepare(MegabonkLibrary lib, IEnumerable<string> names)
        {
            var missing = new List<string>();
            foreach (var full in names.Where(n => !string.IsNullOrEmpty(n)).Distinct())
            {
                string root = full.Split('/')[0];
                if (!raws.ContainsKey(root))
                {
                    RawFx fx;
                    lock (lib) fx = lib.ReadFx(root);
                    raws[root] = fx;
                }
                if (Find(full) == null) missing.Add(full);
            }
            Log.Info($"Megabonk effects read: {raws.Count(r => r.Value != null)} prefabs" + (missing.Count > 0 ? $"; not found: {string.Join(", ", missing)}" : ""));
        }

        static RawFx Find(string full)
        {
            var parts = full.Split('/');
            if (!raws.TryGetValue(parts[0], out var fx) || fx == null) return null;
            for (int i = 1; i < parts.Length && fx != null; i++) fx = fx.Children.FirstOrDefault(c => c.Name == parts[i]);
            return fx;
        }

        public static bool Has(string name) => !string.IsNullOrEmpty(name) && Find(name) != null;

        // ---------------- spawning ----------------

        /// <summary>A one-shot effect at a place; removed once its particles are done.</summary>
        public static GameObject Spawn(string name, Vector3 pos, Quaternion rot, float scale = 1f, Transform parent = null)
        {
            var t = Template(name);
            if (!t) return null;
            var go = Object.Instantiate(t, pos, rot * t.transform.localRotation);
            if (parent) go.transform.SetParent(parent, true);
            go.transform.localScale = t.transform.localScale * scale;
            go.SetActive(true);
            var life = go.AddComponent<Life>();
            life.until = Time.time + Mathf.Min(8f, Duration(go));
            return go;
        }

        /// <summary>An effect that flies from one point to another at a speed (a bullet or arrow), then plays the hit effect.</summary>
        public static void Fly(string name, Vector3 from, Vector3 to, float speed, float scale, string hit, float hitScale)
        {
            var go = Spawn(name, from, Util.QuaternionSafeLookRotation(to - from), scale);
            if (!go) { if (Has(hit)) Spawn(hit, to, Quaternion.identity, hitScale); return; }
            var f = go.AddComponent<Flight>();
            f.to = to;
            f.speed = Mathf.Max(5f, speed);
            f.hit = hit;
            f.hitScale = hitScale;
            go.GetComponent<Life>().until = float.PositiveInfinity; // the flight ends it
        }

        /// <summary>A line from a point to a point (Space Noodle's beam), fading out over a moment.</summary>
        public static void Beam(string name, Vector3 from, Vector3 to, float width)
        {
            var go = Spawn(name, from, Quaternion.identity, 1f);
            var line = go ? go.GetComponentInChildren<LineRenderer>(true) : null;
            if (!line) return;
            line.enabled = true;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.widthMultiplier *= width;
            go.AddComponent<FadeLine>().line = line;
            go.GetComponent<Life>().until = Time.time + 0.35f;
        }

        /// <summary>A looping effect kept on a body while it is refreshed (an aura, a flame stream); gone a moment after the last refresh.</summary>
        public static GameObject Keep(GameObject body, string name, Vector3 offset, Quaternion rot, float scale)
        {
            if (!body || !Has(name)) return null;
            var key = body.GetComponents<Kept>().FirstOrDefault(k => k.name0 == name);
            if (!key)
            {
                var t = Template(name);
                if (!t) return null;
                var go = Object.Instantiate(t, body.transform);
                go.SetActive(true);
                key = body.AddComponent<Kept>();
                key.name0 = name;
                key.fx = go;
                key.baseScale = t.transform.localScale;
            }
            key.until = Time.time + 0.6f;
            key.fx.transform.localPosition = offset;
            key.fx.transform.rotation = rot * Template(name).transform.localRotation;
            key.fx.transform.localScale = key.baseScale * scale;
            return key.fx;
        }

        /// <summary>The effect's own radius at scale 1 (its widest particle, mesh or shape), to scale an aura to a reach.</summary>
        public static float Radius(string name)
        {
            var t = Template(name);
            if (!t) return 1f;
            float r = 0f;
            foreach (var ps in t.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                float size = main.startSize3D ? main.startSizeX.constantMax : main.startSize.constantMax;
                float mesh = pr && pr.renderMode == ParticleSystemRenderMode.Mesh && pr.mesh ? Mathf.Max(pr.mesh.bounds.extents.x, pr.mesh.bounds.extents.z) : 0.5f;
                float s = Scale(ps.transform, t.transform);
                r = Mathf.Max(r, size * mesh * s);
                if (ps.shape.enabled) r = Mathf.Max(r, ps.shape.radius * s);
            }
            return r > 0f ? r : 1f;
        }

        static float Scale(Transform t, Transform root)
        {
            float s = 1f;
            for (var x = t; x && x != root.parent; x = x.parent) s *= x.localScale.x;
            return s;
        }

        static float Duration(GameObject go)
        {
            float d = 0.5f;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                float life = m.startLifetime.mode == ParticleSystemCurveMode.Constant ? m.startLifetime.constant
                    : m.startLifetime.mode == ParticleSystemCurveMode.TwoConstants ? m.startLifetime.constantMax : m.startLifetime.curveMultiplier;
                float delay = m.startDelay.mode == ParticleSystemCurveMode.TwoConstants ? m.startDelay.constantMax : m.startDelay.constant;
                d = Mathf.Max(d, (m.loop ? 1f : m.duration) / Mathf.Max(0.05f, m.simulationSpeed) + delay + life);
            }
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true)) d = Mathf.Max(d, tr.time + 0.2f);
            return d;
        }

        /// <summary>Stops a spawned effect emitting; it goes once its particles and trails are gone.</summary>
        public static void Release(GameObject go)
        {
            if (!go) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>()) tr.emitting = false;
            go.transform.SetParent(null, true);
            var life = go.GetComponent<Life>() ?? go.AddComponent<Life>();
            float longest = 0.3f;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var m = ps.main.startLifetime;
                longest = Mathf.Max(longest, m.mode == ParticleSystemCurveMode.TwoConstants ? m.constantMax : m.mode == ParticleSystemCurveMode.Constant ? m.constant : m.curveMultiplier);
            }
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>()) longest = Mathf.Max(longest, tr.time);
            life.until = Time.time + longest + 0.1f;
        }

        sealed class Life : MonoBehaviour
        {
            public float until;
            void Update() { if (Time.time >= until) Destroy(gameObject); }
        }

        sealed class Flight : MonoBehaviour
        {
            public Vector3 to;
            public float speed, hitScale;
            public string hit;
            void Update()
            {
                var p = Vector3.MoveTowards(transform.position, to, speed * Time.deltaTime);
                transform.position = p;
                if ((p - to).sqrMagnitude > 1e-4f) return;
                if (Has(hit)) Spawn(hit, to, transform.rotation, hitScale);
                Destroy(this);
                Release(gameObject);
            }
        }

        sealed class FadeLine : MonoBehaviour
        {
            public LineRenderer line;
            float start, width;
            void Start() { start = Time.time; width = line.widthMultiplier; }
            void Update() { if (line) line.widthMultiplier = width * Mathf.Clamp01(1f - (Time.time - start) / 0.35f); }
        }

        sealed class Kept : MonoBehaviour
        {
            public string name0;
            public GameObject fx;
            public Vector3 baseScale;
            public float until;
            void Update()
            {
                if (Time.time < until) return;
                Release(fx);
                Destroy(this);
            }
            void OnDestroy() { if (fx && fx.transform.parent == transform) Destroy(fx); }
        }

        // ---------------- building ----------------

        public static GameObject Template(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (templates.TryGetValue(name, out var t)) return t;
            var raw = Find(name);
            if (raw != null)
            {
                try
                {
                    if (!holder) { holder = new GameObject("RiskOfBonkEffects"); holder.SetActive(false); Object.DontDestroyOnLoad(holder); }
                    t = Build(raw, holder.transform, true);
                    t.name = "MB_" + name.Replace('/', '_');
                    t.transform.localPosition = Vector3.zero;
                    t.SetActive(true); // active under the inactive holder: copies start active
                }
                catch (System.Exception e) { Log.Warn($"Megabonk effect {name}: {e.Message}"); t = null; }
            }
            templates[name] = t;
            return t;
        }

        static Vector3 V(RawNode n) => n == null ? Vector3.zero : new Vector3(n.F("x"), n.F("y"), n.F("z"));

        static GameObject Build(RawFx n, Transform parent, bool root)
        {
            var go = new GameObject(n.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = root ? Vector3.zero : new Vector3(n.Pos[0], n.Pos[1], n.Pos[2]);
            go.transform.localRotation = new Quaternion(n.Rot[0], n.Rot[1], n.Rot[2], n.Rot[3]);
            go.transform.localScale = new Vector3(n.Scale[0], n.Scale[1], n.Scale[2]);
            if (n.Particles != null) Particles(go, n);
            if (n.Mesh != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = MeshOf(n.Mesh);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mat(n.MeshMaterial, n.Mesh.SubMeshes.FirstOrDefault()?.Texture);
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (n.Trail != null) Trail(go, n);
            if (n.Line != null) Line(go, n);
            foreach (var c in n.Children) Build(c, go.transform, false);
            if (!root) go.SetActive(n.Active);
            return go;
        }

        static Mesh MeshOf(RawModel m)
        {
            if (!meshes.TryGetValue(m, out var mesh)) meshes[m] = mesh = UnityConvert.Mesh(m);
            return mesh;
        }

        static Mesh builtinQuad;

        static Mesh Builtin(string name)
        {
            if (name == "Quad")
            {
                if (!builtinQuad)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    builtinQuad = q.GetComponent<MeshFilter>().sharedMesh;
                    Object.Destroy(q);
                }
                return builtinQuad;
            }
            var type = name == "Cube" ? PrimitiveType.Cube : name == "Sphere" ? PrimitiveType.Sphere : name == "Cylinder" ? PrimitiveType.Cylinder : name == "Capsule" ? PrimitiveType.Capsule : PrimitiveType.Plane;
            var p = GameObject.CreatePrimitive(type);
            var m = p.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(p);
            return m;
        }

        static AnimationCurve Anim(RawNode c)
        {
            var keys = c?.At("m_Curve.Array")?.Kids;
            if (keys == null || keys.Count == 0) return AnimationCurve.Constant(0f, 1f, 1f);
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.F("time"), k.F("value"), k.F("inSlope"), k.F("outSlope"))).ToArray())
            {
                preWrapMode = WrapOf(c.I("m_PreInfinity", 2)),
                postWrapMode = WrapOf(c.I("m_PostInfinity", 2)),
            };
            return curve;
        }

        static WrapMode WrapOf(int i) => i == 0 ? WrapMode.PingPong : i == 1 ? WrapMode.Loop : WrapMode.ClampForever;

        static ParticleSystem.MinMaxCurve Curve(RawNode c, float mul = 1f)
        {
            if (c == null) return new ParticleSystem.MinMaxCurve(0f);
            float s = c.F("scalar") * mul, mn = c.F("minScalar") * mul;
            switch (c.I("minMaxState"))
            {
                case 1: return new ParticleSystem.MinMaxCurve(s, Anim(c.At("maxCurve")));
                case 2: return new ParticleSystem.MinMaxCurve(s, Anim(c.At("minCurve")), Anim(c.At("maxCurve")));
                case 3: return new ParticleSystem.MinMaxCurve(mn, s);
                default: return new ParticleSystem.MinMaxCurve(s);
            }
        }

        static Color Col(RawNode c) => c == null ? Color.white : new Color(c.F("r"), c.F("g"), c.F("b"), c.F("a"));

        static Gradient Grad(RawNode g)
        {
            var grad = new Gradient();
            if (g == null) return grad;
            int nc = Mathf.Clamp(g.I("m_NumColorKeys"), 0, 8), na = Mathf.Clamp(g.I("m_NumAlphaKeys"), 0, 8);
            var ck = new GradientColorKey[nc];
            for (int i = 0; i < nc; i++) ck[i] = new GradientColorKey(Col(g.Kid("key" + i)), g.F("ctime" + i) / 65535f);
            var ak = new GradientAlphaKey[na];
            for (int i = 0; i < na; i++) ak[i] = new GradientAlphaKey(g.F($"key{i}.a"), g.F("atime" + i) / 65535f);
            grad.SetKeys(ck, ak);
            grad.mode = g.I("m_Mode") == 1 ? GradientMode.Fixed : GradientMode.Blend;
            return grad;
        }

        static ParticleSystem.MinMaxGradient MinMaxGrad(RawNode c)
        {
            if (c == null) return new ParticleSystem.MinMaxGradient(Color.white);
            switch (c.I("minMaxState"))
            {
                case 1: return new ParticleSystem.MinMaxGradient(Grad(c.Kid("maxGradient")));
                case 2: return new ParticleSystem.MinMaxGradient(Col(c.Kid("minColor")), Col(c.Kid("maxColor")));
                case 3: return new ParticleSystem.MinMaxGradient(Grad(c.Kid("minGradient")), Grad(c.Kid("maxGradient")));
                case 4: return new ParticleSystem.MinMaxGradient(Grad(c.Kid("maxGradient"))) { mode = ParticleSystemGradientMode.RandomColor };
                default: return new ParticleSystem.MinMaxGradient(Col(c.Kid("maxColor")));
            }
        }

        static void Particles(GameObject go, RawFx n)
        {
            var p = n.Particles;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = Mathf.Max(0.05f, p.F("lengthInSec", 1f));
            main.loop = p.B("looping");
            main.prewarm = p.B("prewarm");
            main.startDelay = Curve(p.At("startDelay"));
            main.simulationSpeed = p.F("simulationSpeed", 1f);
            main.simulationSpace = p.I("moveWithTransform") == 1 ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            // Local scaling ignores the parents' scale; Hierarchy lets the whole effect grow with its spawn scale.
            main.scalingMode = p.I("scalingMode") == 2 ? ParticleSystemScalingMode.Shape : ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            var im = p.Kid("InitialModule");
            main.startLifetime = Curve(im.Kid("startLifetime"));
            main.startSpeed = Curve(im.Kid("startSpeed"));
            main.startSize3D = im.B("size3D");
            if (main.startSize3D)
            {
                main.startSizeX = Curve(im.Kid("startSize"));
                main.startSizeY = Curve(im.Kid("startSizeY"));
                main.startSizeZ = Curve(im.Kid("startSizeZ"));
            }
            else main.startSize = Curve(im.Kid("startSize"));
            main.startRotation3D = im.B("rotation3D");
            if (main.startRotation3D)
            {
                main.startRotationX = Curve(im.Kid("startRotationX"));
                main.startRotationY = Curve(im.Kid("startRotationY"));
                main.startRotationZ = Curve(im.Kid("startRotation"));
            }
            else main.startRotation = Curve(im.Kid("startRotation"));
            main.flipRotation = im.F("randomizeRotationDirection");
            main.startColor = MinMaxGrad(im.Kid("startColor"));
            main.gravityModifier = Curve(im.Kid("gravityModifier"));
            main.maxParticles = Mathf.Clamp(im.I("maxNumParticles", 1000), 1, 2000);

            var e = p.Kid("EmissionModule");
            var em = ps.emission;
            em.enabled = e.B("enabled");
            em.rateOverTime = Curve(e.Kid("rateOverTime"));
            em.rateOverDistance = Curve(e.Kid("rateOverDistance"));
            var bursts = e.At("m_Bursts.Array")?.Kids ?? new List<RawNode>();
            em.SetBursts(bursts.Select(b => new ParticleSystem.Burst(b.F("time"), Curve(b.Kid("countCurve")), b.I("cycleCount", 1), Mathf.Max(0.0001f, b.F("repeatInterval", 0.01f))) { probability = b.F("probability", 1f) }).ToArray());

            var s = p.Kid("ShapeModule");
            var sh = ps.shape;
            sh.enabled = s.B("enabled");
            if (sh.enabled)
            {
                int type = s.I("type");
                sh.shapeType = type == 1 ? ParticleSystemShapeType.Sphere : type == 3 ? ParticleSystemShapeType.Hemisphere : type == 11 ? ParticleSystemShapeType.Circle
                    : type == 7 ? ParticleSystemShapeType.Cone : type == 9 ? ParticleSystemShapeType.ConeVolume : (ParticleSystemShapeType)type;
                if (type == 1 || type == 3 || type == 11 || type == 7 || type == 9) sh.radiusThickness = 0f; // the old "shell" types emit from the surface only
                else sh.radiusThickness = s.F("radiusThickness", 1f);
                sh.radius = Mathf.Max(0.0001f, s.F("radius.value", 1f));
                sh.angle = s.F("angle");
                sh.arc = s.F("arc.value", 360f);
                sh.length = s.F("length");
                sh.scale = V(s.Kid("m_Scale"));
                sh.position = V(s.Kid("m_Position"));
                sh.rotation = V(s.Kid("m_Rotation"));
                sh.alignToDirection = s.B("alignToDirection");
                sh.randomDirectionAmount = s.F("randomDirectionAmount");
                sh.sphericalDirectionAmount = s.F("sphericalDirectionAmount");
                sh.donutRadius = s.F("donutRadius", 0.2f);
                sh.boxThickness = V(s.Kid("boxThickness"));
            }

            var c = p.Kid("ColorModule");
            if (c != null && c.B("enabled")) { var col = ps.colorOverLifetime; col.enabled = true; col.color = MinMaxGrad(c.Kid("gradient")); }

            var z = p.Kid("SizeModule");
            if (z != null && z.B("enabled"))
            {
                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                sz.separateAxes = z.B("separateAxes");
                if (sz.separateAxes) { sz.x = Curve(z.Kid("curve")); sz.y = Curve(z.Kid("y")); sz.z = Curve(z.Kid("z")); }
                else sz.size = Curve(z.Kid("curve"));
            }

            var v = p.Kid("VelocityModule");
            if (v != null && v.B("enabled"))
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.x = Curve(v.Kid("x")); vel.y = Curve(v.Kid("y")); vel.z = Curve(v.Kid("z"));
                vel.orbitalX = Curve(v.Kid("orbitalX")); vel.orbitalY = Curve(v.Kid("orbitalY")); vel.orbitalZ = Curve(v.Kid("orbitalZ"));
                vel.orbitalOffsetX = Curve(v.Kid("orbitalOffsetX")); vel.orbitalOffsetY = Curve(v.Kid("orbitalOffsetY")); vel.orbitalOffsetZ = Curve(v.Kid("orbitalOffsetZ"));
                vel.radial = Curve(v.Kid("radial"));
                vel.speedModifier = Curve(v.Kid("speedModifier"));
                vel.space = v.B("inWorldSpace") ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            }

            var r = p.Kid("RotationModule");
            if (r != null && r.B("enabled"))
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.separateAxes = r.B("separateAxes");
                if (rot.separateAxes) { rot.x = Curve(r.Kid("x")); rot.y = Curve(r.Kid("y")); }
                rot.z = Curve(r.Kid("curve"));
            }

            var l = p.Kid("ClampVelocityModule");
            if (l != null && l.B("enabled"))
            {
                var lim = ps.limitVelocityOverLifetime;
                lim.enabled = true;
                lim.separateAxes = l.B("separateAxis");
                if (lim.separateAxes) { lim.limitX = Curve(l.Kid("x")); lim.limitY = Curve(l.Kid("y")); lim.limitZ = Curve(l.Kid("z")); }
                else lim.limit = Curve(l.Kid("magnitude"));
                lim.dampen = l.F("dampen");
                lim.drag = Curve(l.Kid("drag"));
                lim.multiplyDragByParticleSize = l.B("multiplyDragByParticleSize");
                lim.multiplyDragByParticleVelocity = l.B("multiplyDragByParticleVelocity");
            }

            var f = p.Kid("ForceModule");
            if (f != null && f.B("enabled"))
            {
                var force = ps.forceOverLifetime;
                force.enabled = true;
                force.x = Curve(f.Kid("x")); force.y = Curve(f.Kid("y")); force.z = Curve(f.Kid("z"));
                force.space = f.B("inWorldSpace") ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
                force.randomized = f.B("randomizePerFrame");
            }

            var no = p.Kid("NoiseModule");
            if (no != null && no.B("enabled"))
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.separateAxes = no.B("separateAxes");
                if (noise.separateAxes) { noise.strengthX = Curve(no.Kid("strength")); noise.strengthY = Curve(no.Kid("strengthY")); noise.strengthZ = Curve(no.Kid("strengthZ")); }
                else noise.strength = Curve(no.Kid("strength"));
                noise.frequency = no.F("frequency", 0.5f);
                noise.damping = no.B("damping", true);
                noise.octaveCount = Mathf.Clamp(no.I("octaves", 1), 1, 4);
                noise.octaveMultiplier = no.F("octaveMultiplier", 0.5f);
                noise.octaveScale = no.F("octaveScale", 2f);
                noise.scrollSpeed = Curve(no.Kid("scrollSpeed"));
                noise.positionAmount = Curve(no.Kid("positionAmount"));
                noise.rotationAmount = Curve(no.Kid("rotationAmount"));
                noise.sizeAmount = Curve(no.Kid("sizeAmount"));
            }

            var u = p.Kid("UVModule");
            if (u != null && u.B("enabled"))
            {
                var uv = ps.textureSheetAnimation;
                uv.enabled = true;
                uv.mode = ParticleSystemAnimationMode.Grid;
                uv.numTilesX = Mathf.Max(1, u.I("tilesX", 1));
                uv.numTilesY = Mathf.Max(1, u.I("tilesY", 1));
                uv.animation = u.I("animationType") == 1 ? ParticleSystemAnimationType.SingleRow : ParticleSystemAnimationType.WholeSheet;
                uv.rowMode = u.B("randomRow") ? ParticleSystemAnimationRowMode.Random : ParticleSystemAnimationRowMode.Custom;
                uv.rowIndex = u.I("rowIndex");
                uv.frameOverTime = Curve(u.Kid("frameOverTime"));
                uv.startFrame = Curve(u.Kid("startFrame"));
                uv.cycleCount = Mathf.Max(1, (int)u.F("cycles", 1f));
            }

            var tr = p.Kid("TrailModule");
            if (tr != null && tr.B("enabled"))
            {
                var trails = ps.trails;
                trails.enabled = true;
                trails.mode = tr.I("mode") == 1 ? ParticleSystemTrailMode.Ribbon : ParticleSystemTrailMode.PerParticle;
                trails.ratio = tr.F("ratio", 1f);
                trails.lifetime = Curve(tr.Kid("lifetime"));
                trails.minVertexDistance = tr.F("minVertexDistance", 0.2f);
                trails.worldSpace = tr.B("worldSpace");
                trails.dieWithParticles = tr.B("dieWithParticles", true);
                trails.sizeAffectsWidth = tr.B("sizeAffectsWidth", true);
                trails.sizeAffectsLifetime = tr.B("sizeAffectsLifetime");
                trails.inheritParticleColor = tr.B("inheritParticleColor", true);
                trails.colorOverLifetime = MinMaxGrad(tr.Kid("colorOverLifetime"));
                trails.widthOverTrail = Curve(tr.Kid("widthOverTrail"));
                trails.colorOverTrail = MinMaxGrad(tr.Kid("colorOverTrail"));
            }

            var iv = p.Kid("InheritVelocityModule");
            if (iv != null && iv.B("enabled"))
            {
                var inherit = ps.inheritVelocity;
                inherit.enabled = true;
                inherit.mode = iv.I("m_Mode") == 1 ? ParticleSystemInheritVelocityMode.Current : ParticleSystemInheritVelocityMode.Initial;
                inherit.curve = Curve(iv.Kid("m_Curve"));
            }

            var pr = go.GetComponent<ParticleSystemRenderer>();
            var rr = n.ParticleRenderer;
            pr.renderMode = (ParticleSystemRenderMode)Mathf.Clamp(rr.I("m_RenderMode"), 0, 5);
            pr.lengthScale = rr.F("m_LengthScale", 2f);
            pr.velocityScale = rr.F("m_VelocityScale");
            pr.cameraVelocityScale = rr.F("m_CameraVelocityScale");
            pr.alignment = (ParticleSystemRenderSpace)Mathf.Clamp(rr.I("m_RenderAlignment"), 0, 4);
            pr.pivot = V(rr.Kid("m_Pivot"));
            pr.flip = V(rr.Kid("m_Flip"));
            pr.minParticleSize = rr.F("m_MinParticleSize");
            pr.maxParticleSize = rr.F("m_MaxParticleSize", 0.5f);
            pr.sortingFudge = rr.F("m_SortingFudge");
            pr.allowRoll = rr.B("m_AllowRoll", true);
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            if (pr.renderMode == ParticleSystemRenderMode.Mesh)
                pr.mesh = n.ParticleMesh != null ? MeshOf(n.ParticleMesh) : Builtin(n.ParticleMeshName ?? "Quad");
            pr.sharedMaterial = Mat(n.ParticleMaterial);
            if (n.ParticleTrailMaterial != null) pr.trailMaterial = Mat(n.ParticleTrailMaterial);
            else if (ps.trails.enabled) pr.trailMaterial = pr.sharedMaterial;
            ps.Play(true);
        }

        static void LineParams(RawNode p, out AnimationCurve width, out Gradient color, out float widthMul)
        {
            width = Anim(p?.Kid("widthCurve"));
            color = Grad(p?.Kid("colorGradient"));
            widthMul = p?.F("widthMultiplier", 1f) ?? 1f;
        }

        static void Trail(GameObject go, RawFx n)
        {
            var t = go.AddComponent<TrailRenderer>();
            t.time = n.Trail.F("m_Time", 0.2f);
            t.minVertexDistance = n.Trail.F("m_MinVertexDistance", 0.1f);
            LineParams(n.Trail.Kid("m_Parameters"), out var w, out var c, out var wm);
            t.widthCurve = w;
            t.colorGradient = c;
            t.widthMultiplier = wm;
            t.sharedMaterial = Mat(n.TrailMaterial);
            t.shadowCastingMode = ShadowCastingMode.Off;
            t.receiveShadows = false;
        }

        static void Line(GameObject go, RawFx n)
        {
            var line = go.AddComponent<LineRenderer>();
            LineParams(n.Line.Kid("m_Parameters"), out var w, out var c, out var wm);
            line.widthCurve = w;
            line.colorGradient = c;
            line.widthMultiplier = wm;
            line.textureMode = (LineTextureMode)n.Line.I("m_Parameters.textureMode");
            line.sharedMaterial = Mat(n.LineMaterial);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false; // shown by Beam
        }

        // ---------------- materials ----------------

        static Texture Tex(RawTexture raw)
        {
            if (raw == null)
            {
                if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
                return white;
            }
            var t = UnityConvert.Texture(raw);
            if (t) t.filterMode = raw.Filter == 0 ? FilterMode.Point : FilterMode.Bilinear;
            return t;
        }

        static Material Mat(RawFxMaterial m, RawTexture fallback = null)
        {
            if (m == null) return null;
            if (materials.TryGetValue(m, out var cached)) return cached;
            Material mat;
            if (m.Kind == "toon" && WeaponVisuals.Template)
            {
                // Megabonk's toon-shaded meshes (an arrow, a banana) look like the projectile models: the survivors' lit material.
                mat = new Material(WeaponVisuals.Template) { name = "MB_" + m.Name };
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Tex(m.Texture ?? fallback));
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                foreach (var p in new[] { "_NormalTex", "_BumpMap", "_EmTex", "_FresnelRamp", "_FlowHeightmap" })
                    if (mat.HasProperty(p)) mat.SetTexture(p, null);
                if (mat.HasProperty("_EmPower")) mat.SetFloat("_EmPower", 0f);
            }
            else
            {
                if (!cloud) cloud = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGCloudRemap.shader").WaitForCompletion();
                mat = new Material(cloud) { name = "MB_" + m.Name, renderQueue = 3000 };
                mat.SetTexture("_MainTex", Tex(m.Texture ?? fallback));
                mat.SetTexture("_RemapTex", Tex(null));
                // Set up like Risk of Rain 2's own particle materials (matOmniHitspark1, matTracerBright): vertex colour on,
                // vertex alpha off (that keyword erodes by alpha), remap off so the texture keeps its colours.
                foreach (var k in new[] { "DISABLEREMAP", "VERTEXCOLOR" }) mat.EnableKeyword(k);
                mat.SetFloat("_DisableRemapOn", 1f);
                mat.SetFloat("_VertexColorOn", 1f);
                mat.SetFloat("_VertexAlphaOn", 0f);
                // Retro Arsenal draws each particle blended and again added on top, scaled by _ExtraGlow: glowing
                // materials (sparks, slashes, rings) add light here, plain ones (smoke, dust) blend.
                float glow = m.Float("_ExtraGlow", 1f);
                bool additive = glow >= 2f || m.Kind == "beam";
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); // the atlases keep their shapes in alpha
                mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                mat.SetFloat("_InternalSimpleBlendMode", additive ? 0f : 1f);
                mat.SetFloat("_Boost", additive ? Mathf.Clamp(glow * 0.5f, 1f, 2.5f) : 1f);
                mat.SetFloat("_AlphaBoost", 1f);
                mat.SetFloat("_Cull", (float)CullMode.Off);
                mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                var tint = m.Color("_Tint");
                var color = m.Kind == "other" ? m.Color(m.Colors.ContainsKey("_MainColor") ? "_MainColor" : "_Color") : m.Color("_Color");
                mat.SetColor("_TintColor", new Color(tint[0] * color[0], tint[1] * color[1], tint[2] * color[2], 1f));
                if (m.Kind == "beam" || m.Colors.ContainsKey("_ScrollSpeed"))
                {
                    var sp = m.Color("_ScrollSpeed");
                    scrolling.Add((mat, new Vector2(sp[0], sp[1])));
                }
            }
            materials[m] = mat;
            return mat;
        }

        static readonly List<(Material, Vector2)> scrolling = new List<(Material, Vector2)>();

        /// <summary>Scrolls beam textures (Megabonk's Beam shaders move their texture along the beam).</summary>
        public sealed class Scroller : MonoBehaviour
        {
            void Update()
            {
                foreach (var (m, speed) in scrolling)
                    if (m) m.mainTextureOffset = new Vector2(Mathf.Repeat(Time.time * speed.x, 1f), Mathf.Repeat(Time.time * speed.y, 1f));
            }
        }
    }
}
