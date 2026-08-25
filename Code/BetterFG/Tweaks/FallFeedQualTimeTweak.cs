using System;
using BetterFG.Features.TimePlacement;

namespace BetterFG.Tweaks
{
    // stamps qualification fallfeeds (the ones with the fallfeed-race sprite) with the time the
    // player qualified at — the server's qualifyTime, captured by FeatureTimePlacement, so the stamp
    // matches the in-game leaderboard's time column exactly. driven from FallFeedNamePatch's postfix
    // on FallFeedNotificationViewModel.ShowMessage so we don't add another patch, but all the
    // behaviour lives here. writes straight to the rendered TMP text — MessageBody on the message
    // struct is a copy by the time ShowMessage returns, editing it does nothing.
    public class FallFeedQualTimeTweak : BfgTweak
    {
        public FallFeedQualTimeTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "fallfeed_qual_time";
        public override string TweakLabel => "Fall Feed Qualification Time";
        public override bool DefaultEnabled => false;

        public static FallFeedQualTimeTweak Instance { get; private set; }
        void Awake() => Instance = this;

        // was "fallfeed-race" pre-update, gating on it as a qualify-message filter. The client update
        // split that into separate playerQualifiedIcon/playerQualifiedFirstIcon sprite names (per
        // FallFeedDataSO) and neither is confirmed live, so don't gate on a sprite name at all — having
        // a stored qual time for this exact player IS the qualify signal, no guessing needed. Kept only
        // as a best-effort insertion point if it happens to still be present.
        const string RaceSprite = "fallfeed-race";
        const string TimeColor = "#FFFF00"; // pure yellow

        public void Apply(TMPro.TextMeshProUGUI messageBodyText, string primaryPlayerKey)
        {
            if (!IsEnabled || messageBodyText == null) return;
            try
            {
                string cur = messageBodyText.text;
                if (string.IsNullOrEmpty(cur)) return;
                if (cur.Contains(TimeColor)) return;     // already stamped

                // server qualifyTime for this fallfeed's player, captured by FeatureTimePlacement. no
                // stored time = don't stamp (don't invent one off the live clock).
                if (!TryGetQualTime(primaryPlayerKey, out string qualTime)) return;

                string stamp = $" <color={TimeColor}>{qualTime}</color>";
                int idx = cur.IndexOf("<sprite name=\"" + RaceSprite, StringComparison.OrdinalIgnoreCase);
                messageBodyText.text = idx >= 0 ? cur.Insert(idx, stamp + " ") : cur + stamp;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("FallFeed: qualtime " + ex.Message);
            }
        }

        // the server qualifyTime FeatureTimePlacement captured for THIS fallfeed's primary player,
        // formatted mm:ss:ms to match the leaderboard's time column. false if we have no stored time.
        static bool TryGetQualTime(string key, out string formatted)
        {
            formatted = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (!FeatureTimePlacement.TryGetQualTime(key, out float seconds)) return false;

            TimeSpan t = TimeSpan.FromSeconds(seconds);
            formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            return true;
        }
    }
}
