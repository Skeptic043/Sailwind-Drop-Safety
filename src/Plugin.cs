using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DropSafety
{
    [BepInPlugin(Guid, PluginName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.skeptic043.sailwind.dropsafety";
        public const string PluginName = "Drop Safety";
        public const string Version = "1.0.0";

        private const string Section = "Drop Safety";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> DisableDrop;
        internal static ConfigEntry<bool> RequireModifier;
        internal static ConfigEntry<string> ModifierKey;

        /// <summary>Parsed ModifierKey. Only meaningful while HasModifierKey is true.</summary>
        internal static KeyCode ModifierKeyCode;
        internal static bool HasModifierKey;

        private static Dictionary<string, KeyCode> keyNames;
        private static readonly HashSet<string> warnedKeyTexts = new HashSet<string>(StringComparer.Ordinal);

        private void Awake()
        {
            Log = Logger;

            // Bound in display order. ConfigurationManager lists higher Order first.
            DisableDrop = Config.Bind(Section, "DisableDrop", true, new ConfigDescription(
                "Prevents the pick up/interact button from dropping what you're holding.",
                null, new ConfigurationManagerAttributes { Order = 3 }));
            RequireModifier = Config.Bind(Section, "RequireModifier", false, new ConfigDescription(
                "Pick up/interact button only drops held item while ModifierKey is held. Works with DisableDrop enabled.",
                null, new ConfigurationManagerAttributes { Order = 2 }));
            ModifierKey = Config.Bind(Section, "ModifierKey", "LeftAlt", new ConfigDescription(
                "The key to hold when RequireModifier is on. Type a key name such as LeftAlt, RightControl or Mouse3. "
                + "Capitals and spaces don't matter, so left alt works too. Leave it blank to turn it off.",
                null, new ConfigurationManagerAttributes { Order = 1 }));

            keyNames = KeyNameParser.BuildMap<KeyCode>();
            ModifierKey.SettingChanged += (sender, args) => RefreshModifierKey();
            RefreshModifierKey();

            try
            {
                if (GoPointerPatch.Apply(new Harmony(Guid)))
                    Log.LogInfo("Patched GoPointer.LateUpdate.");
            }
            catch (Exception e)
            {
                Log.LogError("Drop Safety could not patch the game and is inactive: " + e);
            }
        }

        /// <summary>Parses ModifierKey once per change so the per-frame check is a plain Input.GetKey.</summary>
        private static void RefreshModifierKey()
        {
            string text = ModifierKey.Value;
            KeyNameResult result = KeyNameParser.Parse(text, keyNames, out KeyCode key);

            ModifierKeyCode = key;
            HasModifierKey = result == KeyNameResult.Key;

            if (result == KeyNameResult.Invalid && warnedKeyTexts.Add(text ?? string.Empty))
            {
                Log.LogWarning("ModifierKey is off because '" + text
                    + "' is not a key name. Use a name such as LeftAlt, RightControl or Mouse3.");
            }
        }
    }
}
