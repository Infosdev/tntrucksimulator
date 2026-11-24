using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TNLorry.Services
{
    /// <summary>
    /// Minimal string-table localization (section P). Deliberately independent of the Unity
    /// Localization package so the project compiles and runs before any package is installed;
    /// the API surface (Get / GetFormat / SetLanguage) is what the rest of the game uses, so
    /// swapping the backing store for Unity Localization later touches only this file.
    ///
    /// Tables live at Resources/Localization/strings_&lt;code&gt;.json as a flat
    /// { "key": "value" } object.
    ///
    /// NOTE (section P.2): Tamil is a complex script. This class only resolves strings --
    /// it does not shape them. TextMeshPro needs an Indic-shaping plugin or pre-rendered
    /// textures for Tamil glyphs to render correctly. Verify on a real device in week 1.
    /// </summary>
    public class LocalizationService
    {
        public const string Tamil   = "ta";
        public const string English = "en";

        static readonly string[] Supported = { English, Tamil };

        readonly Dictionary<string, string> _current  = new Dictionary<string, string>(512);
        readonly Dictionary<string, string> _fallback = new Dictionary<string, string>(512);

        public string LanguageCode { get; private set; } = English;
        public event System.Action OnLanguageChanged;

        LocalizationService() { }

        public static async Task<LocalizationService> CreateAsync(string savedLanguage)
        {
            var service = new LocalizationService();
            await service.LoadTableInto(service._fallback, English);
            await service.SetLanguage(string.IsNullOrEmpty(savedLanguage)
                ? DetectDeviceLanguage()
                : savedLanguage);
            return service;
        }

        /// <summary>Tamil device or Tamil-region system language -> Tamil, else English.</summary>
        public static string DetectDeviceLanguage()
            => Application.systemLanguage == SystemLanguage.Unknown
                ? English
                : (Application.systemLanguage.ToString() == "Tamil" ? Tamil : English);

        public async Task SetLanguage(string code)
        {
            if (System.Array.IndexOf(Supported, code) < 0) code = English;
            LanguageCode = code;

            _current.Clear();
            if (code == English)
            {
                foreach (var kv in _fallback) _current[kv.Key] = kv.Value;
            }
            else
            {
                await LoadTableInto(_current, code);
            }

            OnLanguageChanged?.Invoke();
        }

        Task LoadTableInto(Dictionary<string, string> target, string code)
        {
            var path = "Localization/strings_" + code;
            var asset = Resources.Load<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogError("[Localization] Missing table Resources/" + path + ".json");
                return Task.CompletedTask;
            }

            foreach (var kv in FlatJson.Parse(asset.text)) target[kv.Key] = kv.Value;
            Resources.UnloadAsset(asset);
            return Task.CompletedTask;
        }

        /// <summary>Returns the key itself when missing, so gaps are obvious on screen instead of blank.</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_current.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            if (_fallback.TryGetValue(key, out var f) && !string.IsNullOrEmpty(f)) return f;
            return key;
        }

        public string GetFormat(string key, params object[] args)
        {
            var template = Get(key);
            try { return string.Format(template, args); }
            catch (System.FormatException)
            {
                Debug.LogError("[Localization] Bad format placeholders in '" + key + "': " + template);
                return template;
            }
        }
    }

    /// <summary>
    /// Tiny reader for a flat { "key": "string" } JSON object. JsonUtility cannot deserialise
    /// a dictionary and pulling in a JSON library for ~500 strings is not worth the APK size.
    /// Handles \" \\ \n \t \uXXXX escapes -- \u matters for Tamil tables authored as ASCII.
    /// </summary>
    public static class FlatJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>(512);
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            while (i < json.Length)
            {
                if (!SeekString(json, ref i, out var key)) break;

                // expect ':' between key and value
                while (i < json.Length && json[i] != ':' && json[i] != '}') i++;
                if (i >= json.Length || json[i] == '}') break;
                i++;

                if (!SeekString(json, ref i, out var value)) break;
                result[key] = value;
            }
            return result;
        }

        static bool SeekString(string s, ref int i, out string value)
        {
            value = null;
            while (i < s.Length && s[i] != '"') i++;
            if (i >= s.Length) return false;
            i++; // past opening quote

            var sb = new System.Text.StringBuilder(64);
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;

                char esc = s[i++];
                switch (esc)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length &&
                            int.TryParse(s.Substring(i, 4), System.Globalization.NumberStyles.HexNumber,
                                         System.Globalization.CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(esc); break;   // covers \" \\ \/
                }
            }
            return false;
        }
    }
}
