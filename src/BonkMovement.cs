using System.Runtime.CompilerServices;
using EntityStates;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk movement for player bodies, with the numbers measured from Megabonk's own PlayerMovement (sheets/tuning.json,
    /// system "movement"): always running (the Sprint key becomes Slide), quick acceleration to 10 m/s, counter-movement
    /// that stops you fast, speed above that which fades slowly while you keep moving (momentum), air steering that never
    /// slows you, a lower jump in lower gravity, and slides that boost and speed up downhill. Megabonk characters add their
    /// own move speed and jump height (characters.json), and Monke's WallClimb passive runs up walls while Jump is held
    /// (Megabonk's PlayerMovement.WallClimbing).
    /// RoR2 moves a body on the client that has authority over it, so all of this runs there; nothing new is networked.
    /// RoR2 still computes vertical speed (with Megabonk's gravity); the horizontal velocity is replaced.
    /// </summary>
    public static class BonkMovement
    {
        public static bool Enabled = true;
        /// <summary>Test only: the file BepInEx/RiskOfBonk/testmove logs your speed twice a second.</summary>
        public static bool LogSpeed;
        static float nextLog;

        sealed class State
        {
            public bool sliding, climbing, slideReady = true;
            public float boostReady, landedAt = -1f, climbReadyAt;
        }

        static readonly ConditionalWeakTable<CharacterMotor, State> states = new ConditionalWeakTable<CharacterMotor, State>();

        static bool Applies(CharacterMotor m) =>
            Enabled && m && m.body && m.body.isPlayerControlled && m.hasEffectiveAuthority && !m.isFlying;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Megabonk's jump, landing and slide sounds for Megabonk characters (sheets/sounds.json).</summary>
        static void MovementSound(CharacterMotor m, string id)
        {
            if (!m || !Weapons.IsMegabonk(m.body)) return;
            foreach (var s in Sheets.Sounds)
                if (s.Id == id) { MegabonkSounds.Play(s.MbSound, m.body.footPosition, 0.6f); return; }
        }

        /// <summary>Megabonk's run speed for this body: 10 m/s, scaled by its move speed items and buffs.</summary>
        static float RunSpeed(CharacterMotor m)
        {
            var b = m.body;
            float baseSpeed = b.baseMoveSpeed * (b.isSprinting ? b.sprintingSpeedMultiplier : 1f);
            var row = Weapons.Character(b);
            return Tune.RunSpeed * (1f + (row != null ? row.MbMoveSpeed : 0f)) * (baseSpeed > 0.01f ? m.walkSpeed / baseSpeed : 1f);
        }

        /// <summary>
        /// Megabonk's input push: accelerate along the input, but only while the speed that way is below run speed (input
        /// never brakes momentum, it just stops adding to it).
        /// </summary>
        static Vector3 Push(Vector3 h, Vector3 dir, float run, float accel, float dt)
        {
            if (dir.sqrMagnitude < 0.0001f) return h;
            Vector3 d = dir.normalized;
            float along = Vector3.Dot(h, d);
            float add = Mathf.Min(accel * dir.magnitude * dt, Mathf.Max(0f, run * dir.magnitude - along));
            return h + d * add;
        }

        // The raw (held) Sprint key; newer game builds than the reference assemblies have it, older ones fall back to sprint.
        static readonly AccessTools.FieldRef<InputBankTest, InputBankTest.ButtonState> rawSprint =
            AccessTools.Field(typeof(InputBankTest), "rawSprintInput") != null
                ? AccessTools.FieldRefAccess<InputBankTest, InputBankTest.ButtonState>("rawSprintInput") : null;

        static bool SlideHeld(InputBankTest bank) => bank && rawSprint != null && rawSprint(bank).down;

        /// <summary>True while this body is sliding (for effects and animation).</summary>
        public static bool IsSliding(CharacterMotor m) => m && states.TryGetValue(m, out var s) && s.sliding;

        /// <summary>A wall right in front of the body, in the direction it is pushing, or none.</summary>
        static bool FindWall(CharacterMotor m, Vector3 dir, out Vector3 normal)
        {
            normal = Vector3.zero;
            if (dir.sqrMagnitude < 0.01f) return false;
            float reach = (m.capsuleCollider ? m.capsuleCollider.radius : 0.5f) + 0.4f;
            if (Physics.Raycast(m.body.corePosition, dir.normalized, out var hit, reach, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.35f)
            {
                normal = hit.normal;
                return true;
            }
            return false;
        }

        static bool ClimbsWalls(CharacterBody body) => Weapons.Character(body)?.MbPassive == "WallClimb";

        [HarmonyPatch]
        static class Patches
        {
            // Auto-sprint: push "sprint held" every poll; the real Sprint key stays readable as rawSprintInput (= Slide).
            [HarmonyPrefix, HarmonyPatch(typeof(PlayerCharacterMasterController), "PollButtonInput")]
            static void SprintBefore(InputBankTest ___bodyInputs, out bool __state) => __state = ___bodyInputs && ___bodyInputs.sprint.down;

            [HarmonyPostfix, HarmonyPatch(typeof(PlayerCharacterMasterController), "PollButtonInput")]
            static void SprintAfter(PlayerCharacterMasterController __instance, InputBankTest ___bodyInputs, bool __state)
            {
                if (!Enabled || Tune.AutoSprint < 0.5f || !___bodyInputs) return;
                var body = __instance.master ? __instance.master.GetBody() : null;
                if (!body || (body.characterMotor && body.characterMotor.isFlying)) return;
                ___bodyInputs.sprint.down = ___bodyInputs.moveVector.sqrMagnitude > 0.01f;
                ___bodyInputs.sprint.wasDown = __state;
            }

            sealed class Before { public Vector3 v; public float g; }

            [HarmonyPrefix, HarmonyPatch(typeof(CharacterMotor), "PreMove")]
            static void MoveBefore(CharacterMotor __instance, out Before __state)
            {
                __state = new Before { v = __instance.velocity, g = __instance.gravityScale };
                // Megabonk's gravity for this step (RoR2 multiplies Physics.gravity by gravityScale).
                if (Applies(__instance) && Physics.gravity.y < 0f) __instance.gravityScale *= Tune.Gravity / -Physics.gravity.y;
            }

            [HarmonyPostfix, HarmonyPatch(typeof(CharacterMotor), "PreMove")]
            static void MoveAfter(CharacterMotor __instance, float deltaTime, Before __state)
            {
                var m = __instance;
                m.gravityScale = __state.g;
                if (!Applies(m) || m.disableAirControlUntilCollision || m.isAirControlForced) return;
                var st = states.GetOrCreateValue(m);
                st.boostReady -= deltaTime;

                float run = RunSpeed(m);
                Vector3 before = Flat(__state.v);
                Vector3 dir = Flat(m.moveDirection);
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                bool hasInput = dir.sqrMagnitude > 0.01f;
                bool slideHeld = SlideHeld(m.body.inputBank);
                float speed = before.magnitude;
                if (speed >= Tune.SlideBoostMinSpeed) st.slideReady = true;
                bool landing = Time.fixedTime - st.landedAt <= Tune.LandGrace;
                Vector3 h = before;

                // Monke's WallClimb: holding Jump while pushing into a wall (within 60 degrees of straight at it) runs up it at
                // max(10, 1.5 x run speed), pressed against it at 4 m/s; letting go or losing the wall waits 0.4 s before the next climb.
                var bank = m.body.inputBank;
                bool jumpHeld = bank && bank.jump.down;
                Vector3 wallNormal = Vector3.zero;
                bool climb = ClimbsWalls(m.body) && jumpHeld && Time.fixedTime >= st.climbReadyAt && hasInput
                    && FindWall(m, dir, out wallNormal) && Vector3.Dot(dir.normalized, -Flat(wallNormal).normalized) > Mathf.Cos(Tune.WallclimbAngle * Mathf.Deg2Rad);
                if (climb)
                {
                    if (!st.climbing && LogSpeed) Log.Info("move: wall climb start");
                    st.climbing = true;
                    st.sliding = false;
                    float up = Mathf.Max(Tune.WallclimbMinSpeed, run * 1.5f);
                    h = Vector3.ProjectOnPlane(h, Flat(wallNormal).normalized) - Flat(wallNormal).normalized * Tune.WallclimbPush;
                    m.velocity = new Vector3(h.x, Mathf.Max(m.velocity.y, up), h.z);
                    if (LogSpeed && Time.time >= nextLog) { nextLog = Time.time + 0.1f; Log.Info($"move: climbing vy {m.velocity.y:F2} y {m.transform.position.y:F2}"); }
                    return;
                }
                if (st.climbing)
                {
                    st.climbing = false;
                    st.climbReadyAt = Time.fixedTime + Tune.WallclimbCooldown;
                    if (LogSpeed) Log.Info("move: wall climb end");
                }

                if (m.isGrounded)
                {
                    if (slideHeld && speed > 0.5f)
                    {
                        if (!st.sliding)
                        {
                            st.sliding = true;
                            if (st.slideReady && st.boostReady <= 0f)
                            {
                                // Megabonk's slide push, along where you are going.
                                h += (hasInput ? dir.normalized : before.normalized) * Tune.SlideBoost;
                                st.boostReady = Tune.SlideCooldown;
                                MovementSound(m, "slide");
                                st.slideReady = false;
                            }
                        }
                        // Gravity along the ground (downhill faster, uphill slower), a fixed slide friction, and sideways
                        // steering only (forward input does nothing in a slide).
                        Vector3 downhill = Flat(Vector3.ProjectOnPlane(Vector3.down * Tune.Gravity, m.estimatedGroundNormal));
                        h += downhill * (Tune.SlideSlopeGain * deltaTime);
                        if (hasInput && h.sqrMagnitude > 0.01f)
                        {
                            Vector3 side = Vector3.Cross(Vector3.up, h.normalized);
                            h += side * (Vector3.Dot(dir, side) * Tune.SlideSteer * deltaTime);
                        }
                        float mag = Mathf.Max(0f, h.magnitude - Tune.SlideFriction * deltaTime);
                        h = h.sqrMagnitude > 1e-6f ? h.normalized * mag : Vector3.zero;
                        if (mag < 1f) st.sliding = false;
                    }
                    else
                    {
                        st.sliding = false;
                        h = Push(h, dir, run, Tune.RunAccel, deltaTime);
                        if (!landing)
                        {
                            // Counter-movement: speed in directions you are not pushing fades fast.
                            Vector3 along = hasInput && Vector3.Dot(h, dir) > 0f ? dir.normalized * Vector3.Dot(h, dir.normalized) : Vector3.zero;
                            Vector3 rest = h - along;
                            h = along + rest * Mathf.Exp(-Tune.GroundFriction * deltaTime);
                            // Momentum: speed above run speed fades slowly while you keep moving, faster when you let go.
                            float over = h.magnitude - run;
                            if (over > 0f)
                                h = h.normalized * (run + over * Mathf.Exp(-(hasInput ? Tune.OverspeedDecayHold : Tune.OverspeedDecayRelease) * deltaTime));
                        }
                    }
                }
                else
                {
                    st.sliding = false;
                    // Air steering: pushes like on the ground but weaker, and never slows you (no air drag).
                    h = Push(h, dir, run, Tune.AirAccel, deltaTime);
                }

                if (h.magnitude > Tune.MaxSpeed) h = h.normalized * Tune.MaxSpeed;
                m.velocity = new Vector3(h.x, m.velocity.y, h.z);
                if (LogSpeed && Time.time >= nextLog)
                {
                    nextLog = Time.time + 0.1f;
                    Log.Info($"move: {h.magnitude:F2} m/s vy {m.velocity.y:F2} (run {run:F1}) {(m.isGrounded ? "ground" : "air")}{(st.sliding ? " sliding" : "")}" +
                             $" y {m.transform.position.y:F2} input {dir.magnitude:F1} slideKey {slideHeld} t {System.DateTime.Now:HH:mm:ss.ff}");
                }
            }

            [HarmonyPostfix, HarmonyPatch(typeof(CharacterMotor), "OnLanded")]
            static void Landed(CharacterMotor __instance)
            {
                if (!Applies(__instance)) return;
                var st = states.GetOrCreateValue(__instance);
                if (Time.fixedTime - st.landedAt > 0.3f) MovementSound(__instance, "land");
                st.landedAt = Time.fixedTime;
            }

            // Megabonk jumps: launch at jump_speed (scaled by jump items), keeping all horizontal speed (bunny hop, slide jump).
            [HarmonyPrefix, HarmonyPatch(typeof(GenericCharacterMain), nameof(GenericCharacterMain.ApplyJumpVelocity))]
            static void JumpBefore(CharacterMotor characterMotor, out Vector3 __state) => __state = characterMotor ? characterMotor.velocity : Vector3.zero;

            [HarmonyPostfix, HarmonyPatch(typeof(GenericCharacterMain), nameof(GenericCharacterMain.ApplyJumpVelocity))]
            static void JumpAfter(CharacterMotor characterMotor, CharacterBody characterBody, float horizontalBonus, float verticalBonus, bool vault, Vector3 __state)
            {
                if (vault || !Applies(characterMotor) || !characterBody) return;
                var m = characterMotor;
                float items = characterBody.baseJumpPower > 0.01f ? characterBody.jumpPower / characterBody.baseJumpPower : 1f;
                // Jumping while still rising keeps half of the old upward speed, like Megabonk.
                var row = Weapons.Character(characterBody);
                float up = (Tune.JumpSpeed + (row != null ? row.MbJumpHeight : 0f)) * items * verticalBonus + (__state.y > 0.5f ? __state.y * 0.5f : 0f);
                Vector3 h = Flat(__state), pushed = Flat(m.velocity);
                if (horizontalBonus > 1.001f && pushed.magnitude > h.magnitude) h = pushed; // a jump with its own push keeps it
                m.velocity = new Vector3(h.x, up, h.z);
                if (states.TryGetValue(m, out var st)) st.sliding = false;
                MovementSound(m, "jump");
            }
        }

        public static void Init(Harmony harmony)
        {
            LogSpeed = System.IO.File.Exists(System.IO.Path.Combine(Plugin.DataDir, "testmove"));
            harmony.PatchAll(typeof(Patches));
        }
    }
}
