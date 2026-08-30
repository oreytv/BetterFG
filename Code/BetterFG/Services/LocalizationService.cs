using System;
using System.Collections.Generic;

namespace BetterFG.Services
{
    // loads localization.bak (tab-separated: header row of language codes, then one row per key)
    // and exposes it as key -> current-language string. the file lives loose next to BetterFG.dll in
    // the plugin folder (not embedded) — the build syncs newly-added ids into it every build, and the
    // WinForms editor (or LoadFromFile) can edit that same file directly, no rebuild needed.
    public static class LocalizationService
    {
        private static readonly string FilePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            "localization.bak");
        private const string SETTINGS_KEY = "ui.language";

        // "debug" isn't a real language - it's never stored in the .bak, never has translated values.
        // it's always tacked onto the end of _languages so it stays pickable in the language cycle, and
        // Get() falls back to the raw key for it since the table never has a "debug" entry to find.
        private const string DEBUG_LANG = "debug";

        private static Dictionary<string, Dictionary<string, string>> _table = new Dictionary<string, Dictionary<string, string>>();
        private static string[] _languages = new[] { "en", DEBUG_LANG };
        private static string _current = "en";

        public static event Action LanguageChanged;

        public static string CurrentLanguage => _current;
        public static string[] AvailableLanguages => _languages;

        public static void Init()
        {
            string raw = null;
            try
            {
                if (System.IO.File.Exists(FilePath))
                    raw = System.IO.File.ReadAllText(FilePath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("couldn't read localization.bak: " + ex.Message);
            }

            Parse(raw);
            MergeSideFiles();

            _current = SettingsService.Get(SETTINGS_KEY, "en");
            if (Array.IndexOf(_languages, _current) < 0) _current = _languages.Length > 0 ? _languages[0] : "en";

            Plugin.Log?.LogInfo($"localization loaded from {FilePath}: {_table.Count} keys, lang {_current}");
        }

        private static void Parse(string raw)
        {
            _table.Clear();
            if (string.IsNullOrEmpty(raw))
            {
                _languages = new[] { "en", DEBUG_LANG };
                return;
            }

            var lines = raw.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0) { _languages = new[] { "en", DEBUG_LANG }; return; }

            var fileLangs = lines[0].Split('\t');
            _languages = new string[fileLangs.Length + 1];
            Array.Copy(fileLangs, _languages, fileLangs.Length);
            _languages[fileLangs.Length] = DEBUG_LANG;

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var cols = lines[i].Split('\t');
                string key = Unescape(cols[0]);
                if (key.Length == 0) continue;
                var perLang = new Dictionary<string, string>();
                for (int c = 0; c < fileLangs.Length; c++)
                    perLang[fileLangs[c]] = c + 1 < cols.Length ? Unescape(cols[c + 1]) : key;
                _table[key] = perLang;
            }
        }

        private static void MergeSideFiles()
        {
            var dir = System.IO.Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;

            var added = new List<string>();
            foreach (var path in System.IO.Directory.GetFiles(dir, "*.bak"))
            {
                if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) continue;

                string[] lines;
                try
                {
                    lines = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8)
                        .Replace("\r\n", "\n").Split('\n');
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"skipped {System.IO.Path.GetFileName(path)}: {ex.Message}");
                    continue;
                }
                if (lines.Length < 2) continue;

                var head = lines[0].Split('\t');
                var langs = new string[head.Length - 1];
                for (int c = 1; c < head.Length; c++)
                {
                    var code = head[c].Trim();
                    if (code.Length == 0)
                        code = System.IO.Path.GetFileNameWithoutExtension(path);
                    langs[c - 1] = code;
                }
                if (langs.Length == 0) continue;

                int rows = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) continue;
                    var cols = lines[i].Split('\t');
                    string key = Unescape(cols[0]);
                    if (key.Length == 0) continue;
                    if (!_table.TryGetValue(key, out var perLang))
                    {
                        perLang = new Dictionary<string, string>();
                        _table[key] = perLang;
                    }
                    for (int c = 0; c < langs.Length; c++)
                    {
                        if (c + 1 >= cols.Length) continue;
                        var val = Unescape(cols[c + 1]);
                        if (val.Length == 0) continue;
                        perLang[langs[c]] = val;
                    }
                    rows++;
                }

                foreach (var code in langs)
                    if (Array.IndexOf(_languages, code) < 0 && !added.Contains(code))
                        added.Add(code);

                Plugin.Log?.LogInfo($"picked up {System.IO.Path.GetFileName(path)} — {string.Join("/", langs)}, {rows} rows");
            }

            if (added.Count == 0) return;

            var merged = new List<string>(_languages);
            merged.RemoveAll(l => l == DEBUG_LANG);
            merged.AddRange(added);
            merged.Add(DEBUG_LANG);
            _languages = merged.ToArray();
        }

        private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");
        private static string Escape(string s) => s.Replace("\n", "\\n").Replace("\t", "\\t");

        // loads a localization.bak from disk (the WinForms editor's output), replacing the whole
        // table live, and copies it over the canonical file next to the dll so it survives a restart
        // without needing to be re-imported. picks up whatever languages the file defines.
        public static bool LoadFromFile(string path)
        {
            try
            {
                string raw = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                Parse(raw);
                MergeSideFiles();
                if (Array.IndexOf(_languages, _current) < 0)
                    _current = _languages.Length > 0 ? _languages[0] : "en";
                SettingsService.Set(SETTINGS_KEY, _current);

                System.IO.File.Copy(path, FilePath, true);

                LanguageChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("couldn't import localization file: " + ex.Message);
                return false;
            }
        }

        // writes the current table (whatever's loaded from the plugin's localization.bak) out to a
        // .bak file, so someone without one yet has a starting point to hand to the WinForms editor.
        public static bool ExportToFile(string path)
        {
            try
            {
                var realLangs = Array.FindAll(_languages, l => l != DEBUG_LANG);
                var sb = new System.Text.StringBuilder();
                sb.Append(string.Join("\t", realLangs)).Append('\n');
                foreach (var kv in _table)
                {
                    sb.Append(Escape(kv.Key));
                    foreach (var lang in realLangs)
                    {
                        kv.Value.TryGetValue(lang, out var v);
                        sb.Append('\t').Append(Escape(v ?? kv.Key));
                    }
                    sb.Append('\n');
                }
                System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("couldn't export localization file: " + ex.Message);
                return false;
            }
        }

        public static string Get(string key)
        {
            if (key == null) return "";
            if (!_table.TryGetValue(key, out var perLang)) return key;
            if (perLang.TryGetValue(_current, out var v) && v.Length > 0) return v;
            if (_current != DEBUG_LANG && perLang.TryGetValue("en", out var en) && en.Length > 0) return en;
            return key;
        }

        // Get(id) with the result run through string.Format — for status/tooltip text with runtime
        // values baked in ("reading replays... {0}/{1}"). the table stores the format string itself.
        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        public static void SetLanguage(string lang)
        {
            if (lang == _current || Array.IndexOf(_languages, lang) < 0) return;
            _current = lang;
            SettingsService.Set(SETTINGS_KEY, lang);
            LanguageChanged?.Invoke();
        }

        public static void CycleLanguage(int delta)
        {
            if (_languages.Length == 0) return;
            int idx = Array.IndexOf(_languages, _current);
            idx = ((idx + delta) % _languages.Length + _languages.Length) % _languages.Length;
            SetLanguage(_languages[idx]);
        }
    }
}
