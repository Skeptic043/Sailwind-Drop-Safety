using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

// Minimal compile-time surface for executing production hooks/transpiler under .NET 10.
// Installed Harmony targets the game's Mono runtime and its AccessTools initializer
// cannot execute here. These doubles do not test Harmony patch application or FieldRef setup.
namespace HarmonyLib
{
    internal sealed class CodeInstruction
    {
        internal OpCode opcode;
        internal object operand;
        internal readonly List<Label> labels = new List<Label>();
        internal readonly List<object> blocks = new List<object>();
        internal CodeInstruction(OpCode opcode, object operand = null)
        {
            this.opcode = opcode;
            this.operand = operand;
        }
    }

    internal static class AccessTools
    {
        internal delegate ref F FieldRef<T, F>(T instance);
        internal static MethodInfo Method(Type type, string name) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        internal static FieldInfo Field(Type type, string name) =>
            type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        internal static FieldRef<T, F> FieldRefAccess<T, F>(FieldInfo field) =>
            throw new NotSupportedException("The harness injects field accessors. Real Harmony setup requires the game runtime.");
    }

    internal sealed class HarmonyMethod
    {
        internal HarmonyMethod(Type type, string name) { }
    }

    internal sealed class Harmony
    {
        internal void Patch(MethodInfo original, HarmonyMethod transpiler) =>
            throw new NotSupportedException("The harness invokes the production transpiler directly.");
    }
}
