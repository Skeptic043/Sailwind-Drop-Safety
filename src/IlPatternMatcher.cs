using System.Collections.Generic;

namespace DropSafety
{
    /// <summary>
    /// A loader-neutral view of one IL instruction. The plugin builds these from Harmony
    /// CodeInstructions and the tests build them from Mono.Cecil, so both run the same matcher.
    /// </summary>
    public struct IlOp
    {
        /// <summary>Opcode name as IL spells it, e.g. "ldc.i4.8" or "call".</summary>
        public string OpCode;

        /// <summary>The constant pushed by an ldc.i4 form, otherwise null.</summary>
        public int? IntConstant;

        /// <summary>"DeclaringType::Name(ParamType,...)" for call/callvirt, otherwise null.</summary>
        public string CallTarget;

        /// <summary>True if a branch, switch or exception block starts at this instruction.</summary>
        public bool IsJumpTarget;

        public IlOp(string opCode, int? intConstant, string callTarget, bool isJumpTarget)
        {
            OpCode = opCode;
            IntConstant = intConstant;
            CallTarget = callTarget;
            IsJumpTarget = isJumpTarget;
        }

        /// <summary>Decodes the constant of any ldc.i4 form. Operand is the raw instruction operand.</summary>
        public static int? DecodeLdcI4(string opCode, object operand)
        {
            switch (opCode)
            {
                case "ldc.i4.m1": return -1;
                case "ldc.i4.0": return 0;
                case "ldc.i4.1": return 1;
                case "ldc.i4.2": return 2;
                case "ldc.i4.3": return 3;
                case "ldc.i4.4": return 4;
                case "ldc.i4.5": return 5;
                case "ldc.i4.6": return 6;
                case "ldc.i4.7": return 7;
                case "ldc.i4.8": return 8;
                case "ldc.i4.s":
                case "ldc.i4":
                    if (operand is sbyte sb) return sb;
                    if (operand is byte b) return b;
                    if (operand is int i) return i;
                    return null;
                default:
                    return null;
            }
        }

        public static string FormatCallTarget(string declaringType, string name, IList<string> parameterTypes)
        {
            return declaringType + "::" + name + "(" + string.Join(",", parameterTypes) + ")";
        }
    }

    /// <summary>Finds the two PickUp checks in GoPointer.LateUpdate.</summary>
    public static class IlPatternMatcher
    {
        /// <summary>InputName.PickUp in Oculus.VR.dll.</summary>
        public const int PickUpInputName = 8;

        public const string GetKeyUpTarget = "GameInput::GetKeyUp(InputName)";
        public const string GetKeyTarget = "GameInput::GetKey(InputName)";

        /// <summary>
        /// Start indices of every "push 8; call target" pair. The call itself must not be a
        /// jump target, because the replacement moves only the first instruction's labels.
        /// </summary>
        public static List<int> FindPickUpCalls(IList<IlOp> ops, string callTarget)
        {
            var hits = new List<int>();
            for (int i = 0; i + 1 < ops.Count; i++)
            {
                IlOp load = ops[i];
                IlOp call = ops[i + 1];
                if (load.IntConstant != PickUpInputName)
                    continue;
                if (call.OpCode != "call" || call.CallTarget != callTarget)
                    continue;
                if (call.IsJumpTarget)
                    continue;
                hits.Add(i);
            }
            return hits;
        }

        /// <summary>
        /// Succeeds only when each pattern occurs exactly once. On failure, error says why.
        /// </summary>
        public static bool TryFindPatchSites(IList<IlOp> ops, out int keyUpIndex, out int keyIndex, out string error)
        {
            keyUpIndex = -1;
            keyIndex = -1;
            error = null;

            List<int> ups = FindPickUpCalls(ops, GetKeyUpTarget);
            List<int> helds = FindPickUpCalls(ops, GetKeyTarget);

            if (ups.Count != 1 || helds.Count != 1)
            {
                error = "expected exactly one PickUp " + GetKeyUpTarget + " and one PickUp " + GetKeyTarget
                    + ", found " + ups.Count + " and " + helds.Count;
                return false;
            }

            keyUpIndex = ups[0];
            keyIndex = helds[0];
            return true;
        }
    }
}
