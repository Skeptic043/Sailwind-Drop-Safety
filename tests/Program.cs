using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DropSafety.Tests
{
    internal static class Program
    {
        private const string ExpectedGuid = "com.skeptic043.sailwind.dropsafety";
        private const string ExpectedName = "Drop Safety";
        private const string ExpectedVersion = "1.0.0";

        private static int passed;
        private static readonly List<string> failures = new List<string>();

        private static int Main()
        {
            string managed = Metadata("SailwindManaged");
            string core = Metadata("BepInExCore");
            string plugin = Metadata("PluginOutput");

            Section("Decision function");
            DecisionTable();

            Section("Matcher on synthetic IL");
            SyntheticMatcher();

            Section("Matcher on installed GoPointer.LateUpdate");
            InstalledAssembly(managed);

            Section("ModifierKey parser");
            KeyParser(managed);

            Section("Built plugin");
            BuiltPlugin(plugin, managed, core);

            Section("Config bindings in the built plugin");
            ConfigBindings(plugin);

            Section("Public prose");
            PublicProse(Metadata("RepoRoot"));

            Console.WriteLine();
            Console.WriteLine($"{passed} passed, {failures.Count} failed");
            foreach (string f in failures)
                Console.WriteLine("  FAILED: " + f);
            return failures.Count == 0 ? 0 : 1;
        }

        // ---- decision -------------------------------------------------------------

        private static void DecisionTable()
        {
            // (disableDrop, requireModifier, modifierHeld) -> allowed
            var cases = new (bool disable, bool require, bool held, bool expected)[]
            {
                (false, false, false, true),  // vanilla
                (false, false, true,  true),  // vanilla, modifier irrelevant
                (true,  false, false, false), // default: never drop on release
                (true,  false, true,  false), // modifier irrelevant without RequireModifier
                (false, true,  false, false), // modifier required, not held
                (false, true,  true,  true),  // modifier required, held
                (true,  true,  false, false), // RequireModifier wins over DisableDrop
                (true,  true,  true,  true),  // RequireModifier wins over DisableDrop
            };
            foreach (var c in cases)
            {
                bool actual = DropDecision.IsClickDropAllowed(c.disable, c.require, c.held);
                Check($"IsClickDropAllowed(disableDrop={c.disable}, require={c.require}, held={c.held}) == {c.expected}",
                    actual == c.expected);
            }
        }

        // ---- synthetic matcher cases ---------------------------------------------

        private static IlOp Ld(int value, string op = null, bool target = false) =>
            new IlOp(op ?? (value == 8 ? "ldc.i4.8" : "ldc.i4.s"), value, null, target);

        private static IlOp Call(string target, bool jumpTarget = false) =>
            new IlOp("call", null, target, jumpTarget);

        private static IlOp Other(string op) => new IlOp(op, null, null, false);

        private static void SyntheticMatcher()
        {
            string up = IlPatternMatcher.GetKeyUpTarget;
            string key = IlPatternMatcher.GetKeyTarget;

            var vanillaLike = new List<IlOp>
            {
                Ld(10), Call(key), Other("brtrue.s"),
                Other("ldsfld"), Other("brfalse.s"),
                Ld(8), Call(key), Other("brfalse.s"),
                Ld(10), Call(up), Other("brtrue.s"),
                Ld(8), Call(up), Other("brfalse"),
            };
            bool ok = IlPatternMatcher.TryFindPatchSites(vanillaLike, out int upIdx, out int keyIdx, out string err);
            Check("vanilla-shaped IL matches", ok && err == null);
            Check("GetKeyUp site index is the PickUp one, not Throw", upIdx == 11);
            Check("GetKey site index is the PickUp one, not Throw", keyIdx == 5);

            var throwOnly = new List<IlOp> { Ld(10), Call(key), Ld(10), Call(up) };
            Check("Throw-only IL does not match", !IlPatternMatcher.TryFindPatchSites(throwOnly, out _, out _, out err) && err != null);

            var doubled = new List<IlOp>(vanillaLike) { Ld(8), Call(up), Other("pop") };
            Check("two GetKeyUp(PickUp) sites are rejected",
                !IlPatternMatcher.TryFindPatchSites(doubled, out _, out _, out err) && err.Contains("found 2 and 1"));

            var missingKey = vanillaLike.Where((_, i) => i != 5 && i != 6).ToList();
            Check("missing GetKey(PickUp) site is rejected",
                !IlPatternMatcher.TryFindPatchSites(missingKey, out _, out _, out err) && err.Contains("found 1 and 0"));

            var callIsTarget = new List<IlOp> { Ld(8), Call(key), Ld(8), Call(up, jumpTarget: true) };
            Check("a call that is itself a jump target is not matched",
                IlPatternMatcher.FindPickUpCalls(callIsTarget, up).Count == 0);

            var loadIsTarget = new List<IlOp> { Ld(8, target: true), Call(up) };
            Check("a jump target on the constant load is allowed",
                IlPatternMatcher.FindPickUpCalls(loadIsTarget, up).SequenceEqual(new[] { 0 }));

            var callvirt = new List<IlOp> { Ld(8), new IlOp("callvirt", null, up, false) };
            Check("callvirt is not matched", IlPatternMatcher.FindPickUpCalls(callvirt, up).Count == 0);

            var separated = new List<IlOp> { Ld(8), Other("nop"), Call(up) };
            Check("non-adjacent load and call are not matched", IlPatternMatcher.FindPickUpCalls(separated, up).Count == 0);

            Check("DecodeLdcI4 handles every ldc.i4 form",
                IlOp.DecodeLdcI4("ldc.i4.8", null) == 8
                && IlOp.DecodeLdcI4("ldc.i4.s", (sbyte)8) == 8
                && IlOp.DecodeLdcI4("ldc.i4", 8) == 8
                && IlOp.DecodeLdcI4("ldc.i4.m1", null) == -1
                && IlOp.DecodeLdcI4("ldc.r4", 8f) == null);
        }

        // ---- installed game assembly ---------------------------------------------

        private static void InstalledAssembly(string managed)
        {
            string path = Path.Combine(managed, "Assembly-CSharp.dll");
            if (!File.Exists(path))
            {
                Fail("Assembly-CSharp.dll not found at " + path + " (set -p:SailwindManaged=...)");
                return;
            }
            Console.WriteLine("  Assembly-CSharp.dll SHA-256: " + Sha256(path));

            using (var asm = AssemblyDefinition.ReadAssembly(path))
            {
                TypeDefinition goPointer = asm.MainModule.GetType("GoPointer");
                Check("GoPointer type exists", goPointer != null);
                if (goPointer == null) return;

                MethodDefinition lateUpdate = goPointer.Methods.SingleOrDefault(m => m.Name == "LateUpdate" && !m.HasParameters);
                Check("GoPointer.LateUpdate exists with a body", lateUpdate != null && lateUpdate.HasBody);
                if (lateUpdate == null || !lateUpdate.HasBody) return;
                Check("GoPointer.LateUpdate is an instance method (ldarg.0 is 'this')", !lateUpdate.IsStatic);

                FieldDefinition power = goPointer.Fields.SingleOrDefault(f => f.Name == "currentThrowPower");
                Check("GoPointer.currentThrowPower is an instance float field",
                    power != null && !power.IsStatic && power.FieldType.FullName == "System.Single");

                List<Instruction> body = lateUpdate.Body.Instructions.ToList();
                List<IlOp> ops = ToIlOps(lateUpdate.Body);

                List<int> ups = IlPatternMatcher.FindPickUpCalls(ops, IlPatternMatcher.GetKeyUpTarget);
                List<int> keys = IlPatternMatcher.FindPickUpCalls(ops, IlPatternMatcher.GetKeyTarget);
                Check($"exactly one PickUp GetKeyUp site (found {ups.Count})", ups.Count == 1);
                Check($"exactly one PickUp GetKey site (found {keys.Count})", keys.Count == 1);

                bool ok = IlPatternMatcher.TryFindPatchSites(ops, out int upIdx, out int keyIdx, out string err);
                Check("TryFindPatchSites succeeds" + (err == null ? "" : ": " + err), ok);
                if (ok)
                {
                    Console.WriteLine($"  GetKeyUp(PickUp) at IL_{body[upIdx].Offset:x4}, GetKey(PickUp) at IL_{body[keyIdx].Offset:x4}");
                    Check("GetKeyUp(PickUp) site is IL_0667 (research note)", body[upIdx].Offset == 0x0667);
                    Check("GetKey(PickUp) site is IL_0624 (research note)", body[keyIdx].Offset == 0x0624);
                }

                // The Throw checks (InputName.Throw = 10) must stay out of the match set.
                int throwUps = CountCalls(ops, 10, IlPatternMatcher.GetKeyUpTarget);
                int throwKeys = CountCalls(ops, 10, IlPatternMatcher.GetKeyTarget);
                Console.WriteLine($"  Throw sites left alone: GetKeyUp x{throwUps}, GetKey x{throwKeys}");
                Check("Throw GetKeyUp/GetKey sites exist and are distinct from the patched ones", throwUps >= 1 && throwKeys >= 1);
            }
        }

        private static int CountCalls(List<IlOp> ops, int constant, string target)
        {
            int n = 0;
            for (int i = 0; i + 1 < ops.Count; i++)
                if (ops[i].IntConstant == constant && ops[i + 1].OpCode == "call" && ops[i + 1].CallTarget == target)
                    n++;
            return n;
        }

        private static List<IlOp> ToIlOps(Mono.Cecil.Cil.MethodBody body)
        {
            var targets = new HashSet<Instruction>();
            foreach (Instruction ins in body.Instructions)
            {
                if (ins.Operand is Instruction t) targets.Add(t);
                else if (ins.Operand is Instruction[] ts) targets.UnionWith(ts);
            }
            foreach (ExceptionHandler h in body.ExceptionHandlers)
            {
                foreach (Instruction b in new[] { h.TryStart, h.TryEnd, h.HandlerStart, h.HandlerEnd, h.FilterStart })
                    if (b != null) targets.Add(b);
            }

            var ops = new List<IlOp>(body.Instructions.Count);
            foreach (Instruction ins in body.Instructions)
            {
                string name = ins.OpCode.Name;
                string callTarget = null;
                if ((ins.OpCode.Code == Code.Call || ins.OpCode.Code == Code.Callvirt) && ins.Operand is MethodReference mr)
                {
                    callTarget = IlOp.FormatCallTarget(mr.DeclaringType.FullName, mr.Name,
                        mr.Parameters.Select(p => p.ParameterType.FullName).ToList());
                }
                ops.Add(new IlOp(name, IlOp.DecodeLdcI4(name, ins.Operand), callTarget, targets.Contains(ins)));
            }
            return ops;
        }

        // ---- built plugin ---------------------------------------------------------

        private static void BuiltPlugin(string pluginPath, string managed, string core)
        {
            if (!File.Exists(pluginPath))
            {
                Fail("built plugin not found at " + pluginPath);
                return;
            }
            Console.WriteLine("  " + pluginPath);
            Console.WriteLine("  DropSafety.dll SHA-256: " + Sha256(pluginPath) + $" ({new FileInfo(pluginPath).Length} bytes)");

            using (var asm = AssemblyDefinition.ReadAssembly(pluginPath))
            {
                ModuleDefinition module = asm.MainModule;
                Check("assembly name is DropSafety", asm.Name.Name == "DropSafety");
                Check("assembly version is 1.0.0.0", asm.Name.Version == new Version(1, 0, 0, 0));

                var pluginAttrs = module.Types
                    .SelectMany(t => t.CustomAttributes.Select(a => (type: t, attr: a)))
                    .Where(x => x.attr.AttributeType.FullName == "BepInEx.BepInPlugin")
                    .ToList();
                Check($"exactly one [BepInPlugin] (found {pluginAttrs.Count})", pluginAttrs.Count == 1);
                if (pluginAttrs.Count == 1)
                {
                    var args = pluginAttrs[0].attr.ConstructorArguments.Select(a => a.Value as string).ToList();
                    Console.WriteLine($"  [BepInPlugin(\"{string.Join("\", \"", args)}\")] on {pluginAttrs[0].type.FullName}");
                    Check("BepInPlugin GUID", args.Count == 3 && args[0] == ExpectedGuid);
                    Check("BepInPlugin name", args.Count == 3 && args[1] == ExpectedName);
                    Check("BepInPlugin version", args.Count == 3 && args[2] == ExpectedVersion);
                }

                // Nothing from the game, BepInEx, Harmony or Unity may be embedded or merged.
                Check("no embedded resources", module.Resources.Count == 0);
                string[] foreignNamespaces = { "BepInEx", "HarmonyLib", "Harmony", "UnityEngine", "MonoMod", "Mono.Cecil", "Oculus" };
                string[] gameTypes = { "GoPointer", "GameInput", "InputName", "Settings", "ShipItem", "PickupableItem" };
                var foreign = module.GetTypes()
                    .Where(t => foreignNamespaces.Any(ns => t.Namespace == ns || t.Namespace.StartsWith(ns + ".", StringComparison.Ordinal))
                                || (t.Namespace.Length == 0 && gameTypes.Contains(t.Name)))
                    .Select(t => t.FullName).ToList();
                Check("no foreign types defined in DropSafety.dll" + (foreign.Count == 0 ? "" : ": " + string.Join(", ", foreign)),
                    foreign.Count == 0);
                Check("file is small (< 64 KB), i.e. nothing merged in", new FileInfo(pluginPath).Length < 64 * 1024);

                var refs = module.AssemblyReferences.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                Console.WriteLine("  references: " + string.Join(", ", refs));
                foreach (string needed in new[] { "Assembly-CSharp", "Oculus.VR", "BepInEx", "0Harmony" })
                    Check($"references {needed} (external, not embedded)", refs.Contains(needed));

                string outDir = Path.GetDirectoryName(pluginPath);
                string[] leaked = Directory.GetFiles(outDir, "*.dll")
                    .Select(Path.GetFileName)
                    .Where(n => !n.Equals("DropSafety.dll", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                Check("build output holds no copied game or BepInEx DLLs" + (leaked.Length == 0 ? "" : ": " + string.Join(", ", leaked)),
                    leaked.Length == 0);

                // Hook signatures decide stack balance of the rewrite:
                //   ldc.i4.8; call GetKeyUp(InputName)  -> ldarg.0; call PickUpReleased(GoPointer)   (net +1 bool)
                //   ldc.i4.8; call GetKey(InputName)    -> call PickUpHeld()                         (net +1 bool)
                TypeDefinition patch = module.GetType("DropSafety.GoPointerPatch");
                Check("DropSafety.GoPointerPatch exists", patch != null);
                if (patch != null)
                {
                    MethodDefinition released = patch.Methods.SingleOrDefault(m => m.Name == "PickUpReleased");
                    MethodDefinition held = patch.Methods.SingleOrDefault(m => m.Name == "PickUpHeld");
                    Check("PickUpReleased is static bool (GoPointer)",
                        released != null && released.IsStatic && released.IsPublic
                        && released.ReturnType.FullName == "System.Boolean"
                        && released.Parameters.Count == 1 && released.Parameters[0].ParameterType.FullName == "GoPointer");
                    Check("PickUpHeld is static bool ()",
                        held != null && held.IsStatic && held.IsPublic
                        && held.ReturnType.FullName == "System.Boolean" && held.Parameters.Count == 0);

                    // The hooks must read only the PickUp input (8), never Throw (10).
                    foreach (MethodDefinition hook in new[] { released, held }.Where(m => m != null))
                    {
                        var hookOps = ToIlOps(hook.Body);
                        int pickUps = CountCalls(hookOps, 8, IlPatternMatcher.GetKeyUpTarget) + CountCalls(hookOps, 8, IlPatternMatcher.GetKeyTarget);
                        int throws = CountCalls(hookOps, 10, IlPatternMatcher.GetKeyUpTarget) + CountCalls(hookOps, 10, IlPatternMatcher.GetKeyTarget);
                        Check($"{hook.Name} reads PickUp once and never Throw", pickUps == 1 && throws == 0);
                    }
                }
            }
        }

        // ---- ModifierKey parser ---------------------------------------------------

        private enum SampleKey { None = 0, LeftAlt = 308, Mouse3 = 326, JoystickButton4 = 334 }

        private static void KeyParser(string managed)
        {
            // Generic BuildMap on a local enum.
            Dictionary<string, SampleKey> sample = KeyNameParser.BuildMap<SampleKey>();
            Check("BuildMap keys are normalized names",
                sample.Count == 4 && sample["leftalt"] == SampleKey.LeftAlt && sample["joystickbutton4"] == SampleKey.JoystickButton4);

            // The real KeyCode names and values, read from the installed UnityEngine.CoreModule.dll.
            string core = Path.Combine(managed, "UnityEngine.CoreModule.dll");
            if (!File.Exists(core))
            {
                Fail("UnityEngine.CoreModule.dll not found at " + core);
                return;
            }
            var names = new List<(string name, int value)>();
            using (var asm = AssemblyDefinition.ReadAssembly(core))
            {
                TypeDefinition keyCode = asm.MainModule.GetType("UnityEngine.KeyCode");
                Check("UnityEngine.KeyCode found", keyCode != null && keyCode.IsEnum);
                if (keyCode == null) return;
                foreach (FieldDefinition f in keyCode.Fields.Where(f => f.IsStatic && f.HasConstant))
                    names.Add((f.Name, Convert.ToInt32(f.Constant)));
            }

            // Same construction as BuildMap<KeyCode>(), over the name/value pairs.
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            int collisions = 0;
            foreach (var (name, value) in names)
            {
                string key = KeyNameParser.Normalize(name);
                if (map.ContainsKey(key)) collisions++;
                else map.Add(key, value);
            }
            Check($"no two KeyCode names collide after normalizing ({names.Count} names)", collisions == 0);
            int Code(string name) => names.Single(n => n.name == name).value;

            void Expect(string text, KeyNameResult result, string keyName = null)
            {
                KeyNameResult actual = KeyNameParser.Parse(text, map, out int value);
                bool ok = actual == result && (keyName == null ? value == 0 : value == Code(keyName));
                string shown = text == null ? "null" : "\"" + text + "\"";
                Check($"Parse({shown}) -> {result}{(keyName == null ? "" : " " + keyName)}", ok);
            }

            Expect("LeftAlt", KeyNameResult.Key, "LeftAlt");
            Expect("leftalt", KeyNameResult.Key, "LeftAlt");
            Expect("LEFTALT", KeyNameResult.Key, "LeftAlt");
            Expect("left alt", KeyNameResult.Key, "LeftAlt");
            Expect("  Left \t Alt  ", KeyNameResult.Key, "LeftAlt");
            Expect("RightControl", KeyNameResult.Key, "RightControl");
            Expect("Mouse3", KeyNameResult.Key, "Mouse3");
            Expect("mouse 0", KeyNameResult.Key, "Mouse0");
            Expect("JoystickButton4", KeyNameResult.Key, "JoystickButton4");
            Expect("joystick button 4", KeyNameResult.Key, "JoystickButton4");
            Expect("Joystick1Button0", KeyNameResult.Key, "Joystick1Button0");
            Expect("", KeyNameResult.NoKey);
            Expect("   ", KeyNameResult.NoKey);
            Expect(null, KeyNameResult.NoKey);
            Expect("None", KeyNameResult.NoKey);
            Expect("none", KeyNameResult.NoKey);
            Expect("LeftAtl", KeyNameResult.Invalid);
            Expect("Alt", KeyNameResult.Invalid);
            Expect("LeftAlt + G", KeyNameResult.Invalid);
            Expect("LeftAlt G", KeyNameResult.Invalid);
            Expect("LeftAlt,G", KeyNameResult.Invalid);
            Expect("308", KeyNameResult.Invalid);
        }

        // ---- config bindings in the built DLL -------------------------------------

        private static void ConfigBindings(string pluginPath)
        {
            if (!File.Exists(pluginPath))
            {
                Fail("built plugin not found at " + pluginPath);
                return;
            }
            using (var asm = AssemblyDefinition.ReadAssembly(pluginPath))
            {
                ModuleDefinition module = asm.MainModule;
                var allStrings = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions).Where(i => i.OpCode.Code == Code.Ldstr)
                    .Select(i => (string)i.Operand).ToList();
                Check("no \"DisableClickDrop\" string anywhere in the DLL",
                    !allStrings.Any(s => s.Contains("DisableClickDrop")));

                MethodDefinition awake = module.GetType("DropSafety.Plugin")?.Methods.SingleOrDefault(m => m.Name == "Awake");
                Check("Plugin.Awake found", awake != null && awake.HasBody);
                if (awake == null || !awake.HasBody) return;
                List<Instruction> body = awake.Body.Instructions.ToList();

                // Key = the literal right after the section literal. Type = the generic argument of Bind<T>.
                var keys = new List<string>();
                for (int i = 0; i + 1 < body.Count; i++)
                    if (body[i].OpCode.Code == Code.Ldstr && (string)body[i].Operand == "Drop Safety" && body[i + 1].OpCode.Code == Code.Ldstr)
                        keys.Add((string)body[i + 1].Operand);
                var binds = body.Where(i => i.Operand is GenericInstanceMethod g && g.Name == "Bind"
                                            && g.DeclaringType.FullName == "BepInEx.Configuration.ConfigFile")
                    .Select(i => (GenericInstanceMethod)i.Operand).ToList();
                Console.WriteLine("  Bind keys in order: " + string.Join(", ", keys));
                Console.WriteLine("  Bind types in order: " + string.Join(", ", binds.Select(b => b.GenericArguments[0].FullName)));
                Check("three Bind calls with keys DisableDrop, RequireModifier, ModifierKey in that order",
                    binds.Count == 3 && keys.SequenceEqual(new[] { "DisableDrop", "RequireModifier", "ModifierKey" }));
                Check("Bind types are bool, bool, string",
                    binds.Select(b => b.GenericArguments[0].FullName)
                        .SequenceEqual(new[] { "System.Boolean", "System.Boolean", "System.String" }));
                Check("every Bind uses the ConfigDescription overload",
                    binds.All(b => b.Parameters.Count == 4 && b.Parameters[3].ParameterType.FullName == "BepInEx.Configuration.ConfigDescription"));

                string[] descriptions =
                {
                    "Prevents the pick up/interact button from dropping what you're holding.",
                    "Pick up/interact button only drops held item while ModifierKey is held. Works with DisableDrop enabled.",
                    "The key to hold when RequireModifier is on. Type a key name such as LeftAlt, RightControl or Mouse3. Capitals and spaces don't matter, so left alt works too. Leave it blank to turn it off.",
                };
                var awakeStrings = body.Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToList();
                for (int d = 0; d < descriptions.Length; d++)
                    Check($"description {d + 1} is exact", awakeStrings.Contains(descriptions[d]));
                Check("ModifierKey default is \"LeftAlt\"", awakeStrings.Contains("LeftAlt"));
                Check("no semicolons in descriptions", descriptions.All(s => !s.Contains(';')));

                // ConfigurationManager reads the tag by type name and copies public instance fields.
                TypeDefinition cma = module.GetTypes().SingleOrDefault(t => t.Name == "ConfigurationManagerAttributes");
                FieldDefinition order = cma?.Fields.SingleOrDefault(f => f.Name == "Order");
                Check("ConfigurationManagerAttributes has a public instance int? Order field",
                    order != null && order.IsPublic && !order.IsStatic
                    && order.FieldType.FullName == "System.Nullable`1<System.Int32>");

                // Order values: the int pushed right before each new Nullable<int>(value).
                var orders = new List<int>();
                for (int i = 1; i < body.Count; i++)
                {
                    if (body[i].OpCode.Code == Code.Newobj && body[i].Operand is MethodReference ctor
                        && ctor.DeclaringType.FullName == "System.Nullable`1<System.Int32>")
                    {
                        int? v = IlOp.DecodeLdcI4(body[i - 1].OpCode.Name, body[i - 1].Operand);
                        if (v.HasValue) orders.Add(v.Value);
                    }
                }
                Console.WriteLine("  Order tags in bind order: " + string.Join(", ", orders));
                Check("Order tags are 3, 2, 1 (higher shows first)", orders.SequenceEqual(new[] { 3, 2, 1 }));
            }
        }

        // ---- public prose ---------------------------------------------------------

        private static void PublicProse(string repoRoot)
        {
            foreach (string file in new[] { "README.md", "CHANGELOG.md" })
            {
                string path = Path.Combine(repoRoot, file);
                bool exists = File.Exists(path);
                Check($"{file} exists", exists);
                if (!exists) continue;
                string text = File.ReadAllText(path);
                Check($"{file} has no semicolons", !text.Contains(';'));
                Check($"{file} does not mention DisableClickDrop", !text.Contains("DisableClickDrop"));
            }
        }

        // ---- helpers --------------------------------------------------------------

        private static string Metadata(string key) =>
            typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value;

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(path))
                return Convert.ToHexString(sha.ComputeHash(s));
        }

        private static void Section(string title) => Console.WriteLine("\n== " + title);

        private static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  ok   " + name); }
            else Fail(name);
        }

        private static void Fail(string name)
        {
            failures.Add(name);
            Console.WriteLine("  FAIL " + name);
        }
    }
}
