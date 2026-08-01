using System.Globalization;
using System.Text;
using FG.Common;

namespace BetterFG.Tweaks
{
    public class StripSizeTagsTweak : BfgTweak
    {
        public StripSizeTagsTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "strip_size_tags";
        public override string TweakLabel => "Strip Rich Text Tags (anti-grief)";
        public override bool DefaultEnabled => true;

        public static StripSizeTagsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        internal static bool Active => Instance != null && Instance.IsEnabled;

        internal static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string s = text;
            for (int pass = 0; pass < 4; pass++)
            {
                string next = TextMeshProTagsSanitiser.RemoveTextMeshProTags(DecodeEscapes(s));
                if (next == s) break;
                s = next;
            }
            return s;
        }

        private static string DecodeEscapes(string s)
        {
            if (s.IndexOf('\\') < 0) return s;

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length && (s[i + 1] == 'u' || s[i + 1] == 'U'))
                {
                    int len = s[i + 1] == 'u' ? 4 : 8;
                    if (i + 2 + len <= s.Length &&
                        int.TryParse(s.Substring(i + 2, len), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp) &&
                        cp <= 0x10FFFF && (cp < 0xD800 || cp > 0xDFFF))
                    {
                        sb.Append(char.ConvertFromUtf32(cp));
                        i += 1 + len;
                        continue;
                    }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
