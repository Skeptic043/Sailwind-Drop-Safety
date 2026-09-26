using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DropSafety
{
    /// <summary>
    /// Gates the two PickUp-key checks in GoPointer.LateUpdate:
    ///   GameInput.GetKeyUp(PickUp)  (the drop on release)
    ///   GameInput.GetKey(PickUp)    (autoThrow power while held)
    /// The Throw key checks are left untouched.
    /// </summary>
    internal static class GoPointerPatch
    {
        private static AccessTools.FieldRef<GoPointer, float> throwPowerRef;
        private static bool reportedMismatch;

        /// <summary>True once the transpiler has rewritten both PickUp checks.</summary>
        internal static bool Active { get; private set; }

        internal static bool Apply(Harmony harmony)
        {
            MethodInfo original = AccessTools.Method(typeof(GoPointer), "LateUpdate");
            if (original == null)
            {
                Plugin.Log.LogError("Drop Safety is inactive: GoPointer.LateUpdate was not found. The game may have changed.");
                return false;
            }

            FieldInfo powerField = AccessTools.Field(typeof(GoPointer), "currentThrowPower");
            if (powerField == null || powerField.FieldType != typeof(float))
            {
                Plugin.Log.LogError("Drop Safety is inactive: GoPointer.currentThrowPower was not found. The game may have changed.");
                return false;
            }
            throwPowerRef = AccessTools.FieldRefAccess<GoPointer, float>(powerField);

            harmony.Patch(original, transpiler: new HarmonyMethod(typeof(GoPointerPatch), nameof(Transpiler)));
            return Active;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            List<IlOp> ops = code.Select(ToIlOp).ToList();

            if (!IlPatternMatcher.TryFindPatchSites(ops, out int keyUpIndex, out int keyIndex, out string error))
            {
                Active = false;
                if (!reportedMismatch)
                {
                    reportedMismatch = true;
                    Plugin.Log.LogError("Drop Safety is inactive: GoPointer.LateUpdate does not look as expected ("
                        + error + "). The game or another mod may have changed it. Items drop as in vanilla.");
                }
                return code;
            }

            MethodInfo releasedHook = AccessTools.Method(typeof(GoPointerPatch), nameof(PickUpReleased));
            MethodInfo heldHook = AccessTools.Method(typeof(GoPointerPatch), nameof(PickUpHeld));

            // Rewrite the later site first so the earlier index stays valid when the list shrinks.
            var sites = new[]
            {
                new { Index = keyUpIndex, IsRelease = true },
                new { Index = keyIndex, IsRelease = false },
            }.OrderByDescending(s => s.Index);

            foreach (var site in sites)
            {
                CodeInstruction load = code[site.Index];
                CodeInstruction call = code[site.Index + 1];

                if (site.IsRelease)
                {
                    // Was: ldc.i4.8; call GetKeyUp(InputName)   stack: -> bool
                    // Now: ldarg.0;  call PickUpReleased(GoPointer)  stack: -> bool
                    var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
                    TakeLabelsAndBlocks(loadThis, load);
                    var hook = new CodeInstruction(OpCodes.Call, releasedHook);
                    TakeLabelsAndBlocks(hook, call);
                    code[site.Index] = loadThis;
                    code[site.Index + 1] = hook;
                }
                else
                {
                    // Was: ldc.i4.8; call GetKey(InputName)   stack: -> bool
                    // Now: call PickUpHeld()                  stack: -> bool
                    var hook = new CodeInstruction(OpCodes.Call, heldHook);
                    TakeLabelsAndBlocks(hook, load);
                    TakeLabelsAndBlocks(hook, call);
                    code[site.Index] = hook;
                    code.RemoveAt(site.Index + 1);
                }
            }

            Active = true;
            return code;
        }

        /// <summary>Replaces GameInput.GetKeyUp(InputName.PickUp) inside LateUpdate.</summary>
        public static bool PickUpReleased(GoPointer pointer)
        {
            if (!GameInput.GetKeyUp(InputName.PickUp))
                return false;
            if (ClickDropAllowedNow())
                return true;

            // Blocked release: drop any power built up so a later Throw tap stays a plain drop.
            if (pointer != null)
                throwPowerRef(pointer) = 0f;
            return false;
        }

        /// <summary>Replaces GameInput.GetKey(InputName.PickUp) inside LateUpdate.</summary>
        public static bool PickUpHeld()
        {
            return GameInput.GetKey(InputName.PickUp) && ClickDropAllowedNow();
        }

        private static bool ClickDropAllowedNow()
        {
            bool requireModifier = Plugin.RequireModifier.Value;
            bool modifierHeld = requireModifier && Plugin.HasModifierKey && Input.GetKey(Plugin.ModifierKeyCode);
            return DropDecision.IsClickDropAllowed(Plugin.DisableDrop.Value, requireModifier, modifierHeld);
        }

        private static void TakeLabelsAndBlocks(CodeInstruction target, CodeInstruction source)
        {
            target.labels.AddRange(source.labels);
            source.labels.Clear();
            target.blocks.AddRange(source.blocks);
            source.blocks.Clear();
        }

        private static IlOp ToIlOp(CodeInstruction ci)
        {
            string opName = ci.opcode.Name;
            string callTarget = null;
            if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodBase method)
            {
                string[] parameters = method.GetParameters().Select(p => p.ParameterType.FullName).ToArray();
                callTarget = IlOp.FormatCallTarget(method.DeclaringType?.FullName, method.Name, parameters);
            }
            bool isJumpTarget = ci.labels.Count > 0 || ci.blocks.Count > 0;
            return new IlOp(opName, IlOp.DecodeLdcI4(opName, ci.operand), callTarget, isJumpTarget);
        }
    }
}
