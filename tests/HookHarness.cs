using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DropSafety.Tests
{
    /// <summary>
    /// Executes linked production hook/transpiler source against small input and game doubles.
    /// This does not simulate Unity raycasts, OnDrop, collision checks or frame ordering.
    /// </summary>
    internal static class HookHarness
    {
        internal static void Run(Action<string, bool> check)
        {
            AccessTools.FieldRef<GoPointer, float> power = GoPointer.PowerRef;
            AccessTools.FieldRef<ShipItem, bool> wallRange = ShipItem.WallRangeRef;
            SetPatchField("throwPowerRef", power);
            SetPatchField("inRangeOfWallRef", wallRange);

            Plugin.DisableDrop.Value = true;
            Plugin.RequireModifier.Value = false;
            Plugin.HasModifierKey = true;
            GameInput.PickUpHeld = true;
            GameInput.PickUpReleased = true;

            foreach (bool inventoryOnly in new[] { false, true })
            {
                Plugin.InventoryItemsOnly.Value = inventoryOnly;
                // Every native placement precondition independently prevents the exception.
                for (int flags = 0; flags < 16; flags++)
                {
                    var item = new ShipItem
                    {
                        sold = (flags & 1) != 0,
                        wallAttachment = (flags & 2) != 0,
                        forceDisableRedOutline = (flags & 8) != 0,
                    };
                    wallRange(item) = (flags & 4) != 0;
                    var pointer = new GoPointer { HeldItem = item };
                    power(pointer) = 2f;
                    string scenario = $"inventoryOnly={inventoryOnly}, sold={item.sold}, wall={item.wallAttachment}, inRange={(flags & 4) != 0}, obstructed={item.forceDisableRedOutline}";
                    check("held gate blocks power accumulation: " + scenario, !GoPointerPatch.PickUpHeld(pointer));
                    check("held gate itself leaves power untouched: " + scenario, power(pointer) == 2f);
                    bool released = GoPointerPatch.PickUpReleased(pointer);
                    check("release allows only valid native mount: " + scenario, released == (flags == 7));
                    check("blocked/mount release clears accumulated power: " + scenario, power(pointer) == 0f);
                }
            }

            var wallItem = new ShipItem { sold = true, wallAttachment = true };
            wallRange(wallItem) = true;
            var wallPointer = new GoPointer { HeldItem = wallItem };
            foreach (bool inventoryOnly in new[] { false, true })
            {
                Plugin.InventoryItemsOnly.Value = inventoryOnly;
                Plugin.RequireModifier.Value = true;
                UnityEngine.Input.ModifierHeld = false;
                power(wallPointer) = 3f;
                check("modifier not held still allows deliberate wall mount", GoPointerPatch.PickUpReleased(wallPointer));
                check("mount with missing modifier clears power", power(wallPointer) == 0f);
                check("modifier not held never allows wall autoThrow", !GoPointerPatch.PickUpHeld(wallPointer));

                UnityEngine.Input.ModifierHeld = true;
                power(wallPointer) = 3f;
                check("explicit modifier release remains allowed", GoPointerPatch.PickUpReleased(wallPointer));
                check("explicit modifier release preserves native power", power(wallPointer) == 3f);
                check("explicit modifier allows held gate", GoPointerPatch.PickUpHeld(wallPointer));

                Plugin.RequireModifier.Value = false;
                Plugin.DisableDrop.Value = false;
                power(wallPointer) = 4f;
                check("disabled protection permits wall release", GoPointerPatch.PickUpReleased(wallPointer));
                check("disabled protection preserves native power", power(wallPointer) == 4f);
                check("disabled protection permits held gate", GoPointerPatch.PickUpHeld(wallPointer));
                Plugin.DisableDrop.Value = true;
            }

            Plugin.InventoryItemsOnly.Value = true;
            var bigItem = new ShipItem { big = true, wallAttachment = true, sold = true };
            wallRange(bigItem) = true;
            var bigPointer = new GoPointer { HeldItem = bigItem };
            power(bigPointer) = 5f;
            check("excluded large item retains held input", GoPointerPatch.PickUpHeld(bigPointer));
            check("excluded large item retains release input", GoPointerPatch.PickUpReleased(bigPointer));
            check("excluded large item retains native throw power even near wall", power(bigPointer) == 5f);

            GameInput.PickUpReleased = false;
            power(wallPointer) = 6f;
            check("no PickUp release never triggers a mount", !GoPointerPatch.PickUpReleased(wallPointer));
            check("no PickUp release does not reset power", power(wallPointer) == 6f);
            GameInput.PickUpHeld = false;
            check("no held PickUp never allows held gate", !GoPointerPatch.PickUpHeld(wallPointer));
            GameInput.PickUpReleased = true;
            Plugin.InventoryItemsOnly.Value = false;
            check("null pointer safely blocks protected release", !GoPointerPatch.PickUpReleased(null));
            var empty = new GoPointer();
            power(empty) = 1f;
            check("empty pointer safely blocks protected release", !GoPointerPatch.PickUpReleased(empty) && power(empty) == 0f);

            Transpiler(check);
        }

        private static void SetPatchField(string name, object value) =>
            typeof(GoPointerPatch).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);

        internal static void CheckInstalledTranspiler(Mono.Cecil.MethodDefinition method, int releaseIndex,
            int heldIndex, Action<string, bool> check)
        {
            // Translate the real game's instruction stream without loading game code into .NET.
            // Only GameInput calls need reflection operands for the production matcher's lookup.
            var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
                .ToDictionary(o => o.Name);
            var original = method.Body.Instructions.ToList();
            var converted = original.Select(i => new CodeInstruction(opcodes[i.OpCode.Name], i.Operand)).ToList();
            for (int i = 0; i < original.Count; i++)
            {
                if (original[i].Operand is Mono.Cecil.MethodReference call && call.DeclaringType.FullName == "GameInput"
                    && (call.Name == "GetKey" || call.Name == "GetKeyUp"))
                    converted[i].operand = typeof(GameInput).GetMethod(call.Name);
            }
            var generator = new DynamicMethod("installedLabels", typeof(void), Type.EmptyTypes).GetILGenerator();
            var targets = original.SelectMany(i => i.Operand is Mono.Cecil.Cil.Instruction t
                ? new[] { t } : i.Operand as Mono.Cecil.Cil.Instruction[] ?? Array.Empty<Mono.Cecil.Cil.Instruction>()).Distinct();
            foreach (var target in targets)
                converted[original.IndexOf(target)].labels.Add(generator.DefineLabel());
            foreach (var handler in method.Body.ExceptionHandlers)
            foreach (var boundary in new[] { handler.TryStart, handler.TryEnd, handler.HandlerStart, handler.HandlerEnd, handler.FilterStart })
                if (boundary != null) converted[original.IndexOf(boundary)].blocks.Add(boundary);

            var snapshot = converted.Select(i => (i.opcode, i.operand, labels: i.labels.ToArray(), blocks: i.blocks.ToArray())).ToArray();
            var transpiler = typeof(GoPointerPatch).GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static);
            var patched = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { converted })).ToList();
            check("installed IL production transpiler activates", GoPointerPatch.Active);
            check("installed IL transformed instruction count is unchanged", patched.Count == snapshot.Length);
            if (patched.Count != snapshot.Length) return;
            var rewritten = new HashSet<int> { releaseIndex, releaseIndex + 1, heldIndex, heldIndex + 1 };
            check("every non-PickUp instruction and operand is unchanged, including all Throw calls and branches",
                Enumerable.Range(0, snapshot.Length).Where(i => !rewritten.Contains(i)).All(i =>
                    patched[i].opcode == snapshot[i].opcode && Equals(patched[i].operand, snapshot[i].operand)));
            check("all installed IL branch labels and exception boundaries are preserved",
                Enumerable.Range(0, snapshot.Length).All(i => patched[i].labels.SequenceEqual(snapshot[i].labels)
                    && patched[i].blocks.SequenceEqual(snapshot[i].blocks)));
            check("installed IL held site loads pointer then calls PickUpHeld",
                patched[heldIndex].opcode == OpCodes.Ldarg_0 && patched[heldIndex + 1].opcode == OpCodes.Call
                && patched[heldIndex + 1].operand is MethodInfo held && held.Name == "PickUpHeld");
            check("installed IL release site loads pointer then calls PickUpReleased",
                patched[releaseIndex].opcode == OpCodes.Ldarg_0 && patched[releaseIndex + 1].opcode == OpCodes.Call
                && patched[releaseIndex + 1].operand is MethodInfo released && released.Name == "PickUpReleased");
        }

        private static void Transpiler(Action<string, bool> check)
        {
            MethodInfo getKey = typeof(GameInput).GetMethod(nameof(GameInput.GetKey));
            MethodInfo getKeyUp = typeof(GameInput).GetMethod(nameof(GameInput.GetKeyUp));
            var code = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)10), new CodeInstruction(OpCodes.Call, getKey),
                new CodeInstruction(OpCodes.Ldc_I4_8), new CodeInstruction(OpCodes.Call, getKey),
                new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)10), new CodeInstruction(OpCodes.Call, getKeyUp),
                new CodeInstruction(OpCodes.Ldc_I4_8), new CodeInstruction(OpCodes.Call, getKeyUp),
            };
            Label label = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
            code[2].labels.Add(label);
            var transpiler = typeof(GoPointerPatch).GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static);
            var patched = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { code })).ToList();
            check("production transpiler preserves instruction count", patched.Count == 8);
            check("production transpiler passes this into held hook", patched[2].opcode == OpCodes.Ldarg_0
                && patched[3].operand is MethodInfo held && held.Name == "PickUpHeld" && held.GetParameters().Length == 1);
            check("production transpiler passes this into release hook", patched[6].opcode == OpCodes.Ldarg_0
                && patched[7].operand is MethodInfo released && released.Name == "PickUpReleased" && released.GetParameters().Length == 1);
            check("production transpiler preserves entry label", patched[2].labels.Contains(label));
            check("production transpiler leaves Throw held/release calls untouched",
                (sbyte)patched[0].operand == 10 && Equals(patched[1].operand, getKey)
                && (sbyte)patched[4].operand == 10 && Equals(patched[5].operand, getKeyUp));
        }
    }
}

// Only the test executable defines these doubles. The plugin references real game types.
internal class GoPointer
{
    private float currentThrowPower;
    internal static ref float PowerRef(GoPointer pointer) => ref pointer.currentThrowPower;
    internal PickupableItem HeldItem;
    public PickupableItem GetHeldItem() => HeldItem;
}

internal class PickupableItem
{
    public bool big;
    public bool forceDisableRedOutline;
    public T GetComponent<T>() where T : class => this as T;
}

internal class ShipItem : PickupableItem
{
    public bool wallAttachment;
    public bool sold;
    private bool inRangeOfWall;
    internal static ref bool WallRangeRef(ShipItem item) => ref item.inRangeOfWall;
}

internal class ShipItemBottle : ShipItem
{
    public float GetCapacity() => 10f;
}

internal enum InputName { PickUp = 8, Throw = 10 }
internal static class GameInput
{
    internal static bool PickUpHeld;
    internal static bool PickUpReleased;
    public static bool GetKey(InputName input) => input == InputName.PickUp && PickUpHeld;
    public static bool GetKeyUp(InputName input) => input == InputName.PickUp && PickUpReleased;
}

namespace UnityEngine
{
    internal enum KeyCode { LeftAlt }
    internal static class Input
    {
        internal static bool ModifierHeld;
        public static bool GetKey(KeyCode key) => ModifierHeld;
    }
}

namespace DropSafety
{
    internal static class Plugin
    {
        internal sealed class Entry { public bool Value; }
        internal sealed class Logger { public void LogError(string message) => Console.WriteLine(message); }
        internal static readonly Logger Log = new Logger();
        internal static readonly Entry DisableDrop = new Entry();
        internal static readonly Entry RequireModifier = new Entry();
        internal static readonly Entry InventoryItemsOnly = new Entry();
        internal static bool HasModifierKey;
        internal static UnityEngine.KeyCode ModifierKeyCode = UnityEngine.KeyCode.LeftAlt;
    }
}
