using System.Runtime.CompilerServices;
using EntityStates;
using HarmonyLib;
using RiskOfBonk.Generated;
using RoR2;
using UnityEngine;

namespace RiskOfBonk
{
    /// <summary>
    /// Megabonk movement for player bodies (sheets/tuning.json, system "movement"): always at full speed (the Sprint key
    /// becomes Slide), slides that boost and speed up downhill, momentum that fades instead of being cut off, bunny hops
    /// that keep it, and air strafing that steers without slowing you down.
    /// RoR2 moves a body on the client that has authority over it, so all of this runs there; nothing new is networked.
    /// RoR2 still computes gravity and vertical speed; only the horizontal velocity is replaced.
    /// </summary>
    public static class BonkMovement
    {
        public static bool Enabled = true;
        /// <summary>Test only: the file BepInEx/RiskOfBonk/testmove logs your speed twice a second.</summary>
        public static bool LogSpeed;
        static float nextLog;

        sealed class State
        {
            public bool sliding;
            public float boostReady, landedAt = -1f;
        }

        static readonly ConditionalWeakTable<CharacterMotor, State> states = new ConditionalWeakTable<CharacterMotor, State>();

        static bool Applies(CharacterMotor m) =>
            Enabled && m && m.body && m.body.isPlayerControlled && m.hasEffectiveAuthority && !m.isFlying;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // The raw (held) Sprint key; newer game builds than the reference assemblies have it, older ones fall back to sprint.
        static readonly AccessTools.FieldRef<InputBankTest, InputBankTest.ButtonState> rawSprint =
            AccessTools.Field(typeof(InputBankTest), "rawSprintInput") != null
                ? AccessTools.FieldRefAccess<InputBankTest, InputBankTest.ButtonState>("rawSprintInput") : null;

        static bool SlideHeld(InputBankTest bank) => bank && rawSprint != null && rawSprint(bank).down;

        /// <summary>True while this body is sliding (for effects and animation).</summary>
        public static bool IsSliding(CharacterMotor m) => m && states.TryGetValue(m, out var s) && s.sliding;

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

            [HarmonyPrefix, HarmonyPatch(typeof(CharacterMotor), "PreMove")]
            static void MoveBefore(CharacterMotor __instance, out Vector3 __state) => __state = __instance.velocity;

            [HarmonyPostfix, HarmonyPatch(typeof(CharacterMotor), "PreMove")]
            static void MoveAfter(CharacterMotor __instance, float deltaTime, Vector3 __state)
            {
                var m = __instance;
                if (!Applies(m) || m.disableAirControlUntilCollision || m.isAirControlForced) return;
                var st = states.GetOrCreateValue(m);
                st.boostReady -= deltaTime;

                float walk = m.walkSpeed;
                Vector3 before = Flat(__state), after = Flat(m.velocity);
                Vector3 dir = Flat(m.moveDirection);
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                bool hasInput = dir.sqrMagnitude > 0.01f;
                bool slideHeld = SlideHeld(m.body.inputBank);
                float speed = before.magnitude;
                Vector3 h;

                if (m.isGrounded)
                {
                    if (slideHeld && (st.sliding || speed >= walk * Tune.SlideMinSpeed))
                    {
                        if (!st.sliding)
                        {
                            st.sliding = true;
                            if (st.boostReady <= 0f)
                            {
                                Vector3 along = hasInput ? dir.normalized : before.normalized;
                                before += along * Tune.SlideBoost;
                                st.boostReady = Tune.SlideCooldown;
                            }
                        }
                        // Gravity along the ground: downhill speeds the slide up, uphill slows it.
                        Vector3 n = m.estimatedGroundNormal;
                        Vector3 downhill = Flat(Vector3.ProjectOnPlane(Physics.gravity, n));
                        h = before + downhill * (Tune.SlideSlopeGain * deltaTime);
                        float mag = Mathf.Max(0f, h.magnitude - Tune.SlideFriction * deltaTime);
                        h = h.sqrMagnitude > 1e-6f ? h.normalized * mag : Vector3.zero;
                        if (hasInput && mag > 0.1f)
                            h = Vector3.RotateTowards(h, dir.normalized * mag, Tune.SlideSteer * Mathf.Deg2Rad * deltaTime, 0f);
                        if (mag < walk * 0.6f) st.sliding = false;
                    }
                    else
                    {
                        st.sliding = false;
                        if (speed > walk + 0.05f && Time.fixedTime - st.landedAt <= Tune.BhopWindow)
                            h = before; // just landed: keep everything for a bunny hop
                        else if (speed > walk + 0.05f && hasInput)
                        {
                            // Momentum fades toward walk speed and turns toward the input, instead of snapping.
                            float mag = Mathf.Max(walk * dir.magnitude, speed - Tune.MomentumDecay * deltaTime);
                            h = Vector3.RotateTowards(before, dir.normalized * mag, Tune.MomentumTurn * Mathf.Deg2Rad * deltaTime, 0f);
                            h = h.normalized * mag;
                        }
                        else
                            h = after; // normal RoR2 ground movement (also braking when there is no input)
                    }
                }
                else
                {
                    st.sliding = false;
                    // Air strafing: steer freely, but never slower than the speed you had (no air drag to walk speed).
                    float keep = Mathf.Max(speed, walk);
                    h = before + dir * (m.acceleration * Tune.AirControl * deltaTime);
                    if (h.magnitude > keep) h = h.normalized * keep;
                }

                float cap = walk * Tune.MaxSpeed;
                if (h.magnitude > cap) h = h.normalized * cap;
                m.velocity = new Vector3(h.x, m.velocity.y, h.z);
                if (LogSpeed && Time.time >= nextLog)
                {
                    nextLog = Time.time + 0.5f;
                    Log.Info($"move: {h.magnitude:F1} m/s (walk {walk:F1}) {(m.isGrounded ? "ground" : "air")}{(st.sliding ? " sliding" : "")}" +
                             $" sprint {m.body.isSprinting} slideKey {slideHeld} monsters {TeamComponent.GetTeamMembers(TeamIndex.Monster).Count}");
                }
            }

            [HarmonyPostfix, HarmonyPatch(typeof(CharacterMotor), "OnLanded")]
            static void Landed(CharacterMotor __instance)
            {
                if (Applies(__instance)) states.GetOrCreateValue(__instance).landedAt = Time.fixedTime;
            }

            // Jumps keep momentum (bunny hop, slide jump) and are a bit higher.
            [HarmonyPrefix, HarmonyPatch(typeof(GenericCharacterMain), nameof(GenericCharacterMain.ApplyJumpVelocity))]
            static void JumpBefore(CharacterMotor characterMotor, out Vector3 __state) => __state = characterMotor ? characterMotor.velocity : Vector3.zero;

            [HarmonyPostfix, HarmonyPatch(typeof(GenericCharacterMain), nameof(GenericCharacterMain.ApplyJumpVelocity))]
            static void JumpAfter(CharacterMotor characterMotor, bool vault, Vector3 __state)
            {
                if (vault || !Applies(characterMotor)) return;
                var m = characterMotor;
                Vector3 v = m.velocity, before = Flat(__state), now = Flat(v);
                if (before.magnitude > now.magnitude)
                {
                    Vector3 dir = Flat(m.moveDirection);
                    Vector3 along = dir.sqrMagnitude > 0.01f ? dir.normalized : before.normalized;
                    float mag = Mathf.Min(before.magnitude + Tune.BhopBoost, m.walkSpeed * Tune.MaxSpeed);
                    now = along * mag;
                }
                m.velocity = new Vector3(now.x, v.y * Tune.JumpHeight, now.z);
                if (states.TryGetValue(m, out var st)) st.sliding = false;
            }
        }

        public static void Init(Harmony harmony)
        {
            LogSpeed = System.IO.File.Exists(System.IO.Path.Combine(Plugin.DataDir, "testmove"));
            harmony.PatchAll(typeof(Patches));
        }
    }
}
