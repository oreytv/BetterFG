using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BetterFG.Features.CreativeText
{
    public sealed class SystemFont
    {
        public string Path;
        public string Display;
        public string Family;
    }

    public static class SystemFonts
    {
        private static List<SystemFont> _fonts;
        private static readonly Dictionary<string, Font> _previews = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        public static List<SystemFont> All()
        {
            if (_fonts != null) return _fonts;

            var found = new List<SystemFont>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts"),
            };

            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var pattern in new[] { "*.ttf", "*.otf" })
                    foreach (var file in Directory.GetFiles(dir, pattern))
                    {
                        var entry = Read(file);
                        if (entry == null) continue;
                        if (!seen.Add(entry.Display)) continue;
                        found.Add(entry);
                    }
            }

            found.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
            _fonts = found;
            return _fonts;
        }

        public static Font Preview(SystemFont font)
        {
            if (font == null) return null;
            if (_previews.TryGetValue(font.Family, out var cached)) return cached;
            Font made = null;
            try { made = Font.CreateDynamicFontFromOSFont(font.Family, 16); }
            catch (Exception ex) { Plugin.Log.LogDebug($"no OS font for {font.Family}: {ex.Message}"); }
            _previews[font.Family] = made;
            return made;
        }

        private static SystemFont Read(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    long start = 0;
                    uint tag = BE32(reader);
                    if (tag == 0x74746366)
                    {
                        reader.BaseStream.Position = 8;
                        uint count = BE32(reader);
                        if (count == 0) return null;
                        start = BE32(reader);
                        reader.BaseStream.Position = start;
                        tag = BE32(reader);
                    }
                    if (tag != 0x00010000 && tag != 0x4F54544F && tag != 0x74727565) return null;

                    int tables = BE16(reader);
                    reader.BaseStream.Position = start + 12;
                    long nameOffset = -1;
                    for (int i = 0; i < tables; i++)
                    {
                        uint t = BE32(reader);
                        BE32(reader);
                        uint off = BE32(reader);
                        BE32(reader);
                        if (t == 0x6E616D65) { nameOffset = off; break; }
                    }
                    if (nameOffset < 0) return null;

                    reader.BaseStream.Position = nameOffset;
                    BE16(reader);
                    int records = BE16(reader);
                    int storage = BE16(reader);

                    string family = null, style = null, full = null;
                    for (int i = 0; i < records; i++)
                    {
                        int platform = BE16(reader);
                        BE16(reader);
                        int language = BE16(reader);
                        int nameId = BE16(reader);
                        int length = BE16(reader);
                        int offset = BE16(reader);
                        if (nameId != 1 && nameId != 2 && nameId != 4) continue;
                        if (platform != 3 && platform != 1) continue;
                        if (platform == 3 && language != 0x0409 && family != null) continue;

                        long resume = reader.BaseStream.Position;
                        reader.BaseStream.Position = nameOffset + storage + offset;
                        var bytes = reader.ReadBytes(length);
                        reader.BaseStream.Position = resume;

                        string value = platform == 3
                            ? Encoding.BigEndianUnicode.GetString(bytes)
                            : Encoding.ASCII.GetString(bytes);
                        value = value.Trim();
                        if (value.Length == 0) continue;

                        if (nameId == 1 && (family == null || platform == 3)) family = value;
                        else if (nameId == 2 && (style == null || platform == 3)) style = value;
                        else if (nameId == 4 && (full == null || platform == 3)) full = value;
                    }

                    if (family == null && full == null) return null;
                    string display = full;
                    if (string.IsNullOrEmpty(display))
                        display = string.IsNullOrEmpty(style) || style.Equals("Regular", StringComparison.OrdinalIgnoreCase)
                            ? family
                            : family + " " + style;

                    return new SystemFont { Path = path, Display = display, Family = family ?? display };
                }
            }
            catch { return null; }
        }

        private static uint BE32(BinaryReader r)
        {
            var b = r.ReadBytes(4);
            if (b.Length < 4) return 0;
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }

        private static int BE16(BinaryReader r)
        {
            var b = r.ReadBytes(2);
            if (b.Length < 2) return 0;
            return (b[0] << 8) | b[1];
        }
    }
}
