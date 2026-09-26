using System;
using System.Collections.Generic;
using System.Text;

namespace DropSafety
{
    public enum KeyNameResult
    {
        /// <summary>The text names exactly one key.</summary>
        Key,

        /// <summary>The text is blank or "None", so no key is set.</summary>
        NoKey,

        /// <summary>The text is not a single key name.</summary>
        Invalid,
    }

    /// <summary>
    /// Parses a typed key name such as "left alt" into an enum value. Pure, so tests can run it
    /// without Unity. Matching ignores case and whitespace.
    /// </summary>
    public static class KeyNameParser
    {
        /// <summary>Builds the lookup from normalized name to value for an enum such as KeyCode.</summary>
        public static Dictionary<string, T> BuildMap<T>() where T : struct, Enum
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (string name in Enum.GetNames(typeof(T)))
            {
                string key = Normalize(name);
                if (!map.ContainsKey(key))
                    map.Add(key, (T)Enum.Parse(typeof(T), name));
            }
            return map;
        }

        public static KeyNameResult Parse<T>(string text, IDictionary<string, T> map, out T value) where T : struct
        {
            value = default(T);
            string key = Normalize(text);
            if (key.Length == 0 || key == "none")
                return KeyNameResult.NoKey;
            if (map.TryGetValue(key, out value))
                return KeyNameResult.Key;
            value = default(T);
            return KeyNameResult.Invalid;
        }

        /// <summary>Lowercase with all whitespace removed, so "Left Alt" and "leftalt" match.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
