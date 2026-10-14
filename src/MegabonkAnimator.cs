using System.Collections.Generic;
using System.Linq;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Plays a Megabonk character's own animation clips (read from the player's Megabonk install) on its Megabonk
    /// skeleton, instead of copying the Risk of Rain 2 survivor's poses: idle, run, jump, falling, and the slide pose,
    /// chosen from what the body is doing and crossfaded. Megabonk has no attack animations (its weapons fire by
    /// themselves), so attacks keep the run/idle animation. Runs on every machine from the body's own motion.
    /// Clip roles and timings are in sheets/animations.json.
    /// </summary>
    public sealed class MegabonkAnimator : MonoBehaviour
    {
        public enum Role { Idle, Run, Jump, Fall, Slide }

        sealed class Bound
        {
            public RawClip clip;
            public Transform[] targets; // per track
            public bool loop;
        }

        public MegabonkRig rigRef;
        public string clipKey;
        MegabonkRig rig;
        CharacterBody body;
        readonly Bound[] roles = new Bound[5];
        Bound current, previous, pending;
        float pendingFor;
        float time, prevTime, fade = 1f;
        Vector3 lastPos;
        Vector3 smoothVel;
        bool hasLast;
        float nextLog;
        // Every transform any clip moves, and its rest pose.
        readonly Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> rest = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();

        public static readonly Dictionary<string, List<RawClip>> ClipsByModel = new Dictionary<string, List<RawClip>>();

        /// <summary>Megabonk clips onto every retargeted Megabonk skeleton under a model, when the character has clips.</summary>
        public static int Attach(Transform modelRoot, List<RawClip> clips)
        {
            if (clips == null || clips.Count == 0) return 0;
            int n = 0;
            foreach (var r in modelRoot.GetComponentsInChildren<MegabonkRig>(true))
            {
                if (r.allBones == null || r.skeleton == null) continue;
                // Only serialized fields survive Instantiate: each spawned copy sets itself up from these in Awake.
                string key = "clips" + ClipsByModel.Count;
                ClipsByModel[key] = clips;
                var a = r.gameObject.AddComponent<MegabonkAnimator>();
                a.rigRef = r;
                a.clipKey = key;
                a.Init(r, clips);
                n++;
            }
            return n;
        }

        static Role? RoleOf(string clipName)
        {
            string n = clipName.ToLowerInvariant();
            foreach (var row in Sheets.Animations)
                foreach (var word in row.ClipWords)
                    if (n.Contains(word.ToLowerInvariant()))
                        return (Role)System.Enum.Parse(typeof(Role), row.Id, true);
            return null;
        }

        void Init(MegabonkRig r, List<RawClip> clips)
        {
            rig = r;
            // Every suffix path of every bone under the skeleton, by Unity's path hash.
            var byHash = new Dictionary<uint, Transform>();
            foreach (var t in rig.skeleton.GetComponentsInChildren<Transform>(true))
            {
                var parts = new List<string>();
                for (var p = t; p != null && p != rig.skeleton; p = p.parent)
                {
                    parts.Insert(0, p.name);
                    uint h = MegabonkLibrary.PathHash(string.Join("/", parts));
                    if (!byHash.ContainsKey(h)) byHash[h] = t;
                }
            }
            for (int i = 0; i < rig.allBones.Length; i++)
                if (rig.allBones[i] != null) rest[rig.allBones[i]] = (rig.restPos[i], rig.restRot[i], rig.restScale[i]);
            foreach (var c in clips)
            {
                var role = RoleOf(c.Name);
                if (role == null || roles[(int)role] != null) continue;
                var b = new Bound { clip = c, loop = role != Role.Jump, targets = c.Tracks.Select(tr => byHash.TryGetValue(tr.PathHash, out var t) ? t : null).ToArray() };
                roles[(int)role] = b;
            }
            roles[(int)Role.Idle] ??= roles[(int)Role.Run];
            roles[(int)Role.Fall] ??= roles[(int)Role.Jump] ?? roles[(int)Role.Idle];
            roles[(int)Role.Jump] ??= roles[(int)Role.Fall];
            roles[(int)Role.Slide] ??= roles[(int)Role.Run];
            roles[(int)Role.Run] ??= roles[(int)Role.Idle];
            if (roles[(int)Role.Idle] == null) { enabled = false; return; }
            // The survivor's poses no longer drive this skeleton; back to Megabonk's own rest pose.
            rig.enabled = false;
            foreach (var kv in rest) { kv.Key.localPosition = kv.Value.p; kv.Key.localRotation = kv.Value.r; kv.Key.localScale = kv.Value.s; }
            current = roles[(int)Role.Idle];
        }

        CharacterModel model;

        void Awake()
        {
            if (current == null && rigRef != null && clipKey != null && ClipsByModel.TryGetValue(clipKey, out var clips)) Init(rigRef, clips);
        }

        void Start() => model = rig != null && rig.modelRoot ? rig.modelRoot.GetComponent<CharacterModel>() : null;

        Role Want(float dt)
        {
            if (!body) return Role.Idle;
            var pos = body.transform.position;
            if (hasLast && dt > 0f) smoothVel = Vector3.Lerp(smoothVel, (pos - lastPos) / dt, Mathf.Clamp01(dt * 12f));
            lastPos = pos;
            hasLast = true;
            var motor = body.characterMotor;
            bool grounded;
            if (motor && motor.hasEffectiveAuthority) grounded = motor.isGrounded;
            else grounded = Physics.Raycast(body.footPosition + Vector3.up * 0.3f, Vector3.down, 0.7f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            var flat = new Vector3(smoothVel.x, 0f, smoothVel.z);
            if (motor && BonkMovement.IsSliding(motor)) return Role.Slide;
            if (!grounded) return smoothVel.y > Tune.AnimFallSpeed ? Role.Jump : Role.Fall;
            return flat.magnitude > Tune.AnimRunSpeed ? Role.Run : Role.Idle;
        }

        void LateUpdate()
        {
            if (current == null) return;
            if (!body && model) body = model.body; // set by the body after the model spawns
            float dt = Time.deltaTime;
            var wantRole = Want(dt);
            var want = roles[(int)wantRole] ?? current;
            // A new clip has to be wanted for a moment first (no flicker at the edge of run speed or a slide),
            // except jumping, which starts at once.
            if (want != pending) { pending = want; pendingFor = 0f; }
            pendingFor += dt;
            if (want != current && wantRole != Role.Jump && pendingFor < Tune.AnimHold) want = current;
            if (BonkMovement.LogSpeed && body && Time.time >= nextLog)
            {
                nextLog = Time.time + 0.25f;
                var probe = current.targets.FirstOrDefault(t => t != null);
                Log.Info($"anim: {body.name} {current.clip.Name} t {time:F2} fade {fade:F2} bone {(probe ? probe.name + " " + probe.localEulerAngles.ToString("F0") : "-")}");
            }
            if (want != current)
            {
                if (BonkMovement.LogSpeed && body) Log.Info($"anim: {body.name} {current.clip.Name} -> {want.clip.Name}");
                previous = current; prevTime = time;
                current = want; time = 0f; fade = 0f;
            }
            float speed = 1f;
            if (current == roles[(int)Role.Run] && body)
            {
                float h = new Vector3(smoothVel.x, 0f, smoothVel.z).magnitude;
                speed = Mathf.Clamp(h / Tune.RunSpeed, 0.6f, 1.8f);
            }
            time += dt * speed;
            prevTime += dt;
            fade = Mathf.Min(1f, fade + dt / Mathf.Max(0.01f, Tune.AnimBlend));

            // Bones a clip leaves alone go back to Megabonk's rest pose, then the clips (crossfaded) pose the rest.
            foreach (var kv in rest) { kv.Key.localPosition = kv.Value.p; kv.Key.localRotation = kv.Value.r; kv.Key.localScale = kv.Value.s; }
            if (previous == null || fade >= 1f) { Apply(current, time, 1f); previous = null; }
            else { Apply(previous, prevTime, 1f); Apply(current, time, fade); }
        }

        static float Wrap(RawClip c, bool loop, float t, out int f0, out int f1, out float w)
        {
            float local = loop && c.Length > 0f ? Mathf.Repeat(t, c.Length) : Mathf.Min(t, c.Length);
            float f = local * c.SampleRate;
            f0 = Mathf.Clamp((int)f, 0, c.Frames - 1);
            f1 = Mathf.Min(c.Frames - 1, f0 + 1);
            w = Mathf.Clamp01(f - f0);
            return local;
        }

        /// <summary>Pose from a clip; weight below 1 blends it over what the bones already hold.</summary>
        static void Apply(Bound b, float t, float weight)
        {
            var c = b.clip;
            Wrap(c, b.loop, t, out int f0, out int f1, out float w);
            for (int i = 0; i < c.Tracks.Count; i++)
            {
                var target = b.targets[i];
                if (target == null) continue;
                var tr = c.Tracks[i];
                var v = tr.Values;
                int k = tr.Width, o0 = f0 * k, o1 = f1 * k;
                switch (tr.Kind)
                {
                    case 1:
                    {
                        var p = Vector3.Lerp(new Vector3(v[o0], v[o0 + 1], v[o0 + 2]), new Vector3(v[o1], v[o1 + 1], v[o1 + 2]), w);
                        target.localPosition = weight >= 1f ? p : Vector3.Lerp(target.localPosition, p, weight);
                        break;
                    }
                    case 2:
                    {
                        var q = Quaternion.Slerp(new Quaternion(v[o0], v[o0 + 1], v[o0 + 2], v[o0 + 3]).normalized,
                                                 new Quaternion(v[o1], v[o1 + 1], v[o1 + 2], v[o1 + 3]).normalized, w);
                        target.localRotation = weight >= 1f ? q : Quaternion.Slerp(target.localRotation, q, weight);
                        break;
                    }
                    case 3:
                    {
                        var s = Vector3.Lerp(new Vector3(v[o0], v[o0 + 1], v[o0 + 2]), new Vector3(v[o1], v[o1 + 1], v[o1 + 2]), w);
                        target.localScale = weight >= 1f ? s : Vector3.Lerp(target.localScale, s, weight);
                        break;
                    }
                    case 4:
                    {
                        var q = Quaternion.Slerp(Quaternion.Euler(v[o0], v[o0 + 1], v[o0 + 2]), Quaternion.Euler(v[o1], v[o1 + 1], v[o1 + 2]), w);
                        target.localRotation = weight >= 1f ? q : Quaternion.Slerp(target.localRotation, q, weight);
                        break;
                    }
                }
            }
        }
    }
}
