using System;
using BetterFG.Core;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Features.TimePlacement;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Tweaks;
using FGClient.FallFeed;
using FGClient;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterFG.Patches
{
    // FallFeedNotificationViewModel.ShowMessage has exactly one call site (CreateNotification), so
    // IL2CPP inlines it away — Harmony reports the patch bound, but it never actually fires. Patch
    // CreateNotification instead (confirmed firing) and reach into the just-shown row's TMP fields
    // directly instead of touching FallFeedMessageData's PlayerSlot beyond the one top-level string
    // (MessageBody) that's proven safe to read here — PlayerSlot.StaticDisplayName crashed the whole
    // game with an AccessViolationException (dangling native pointer by the time this postfix runs;
    // PlayerKey never threw only because every read short-circuited on it being empty before reaching
    // StaticDisplayName). vm._playerNameText.text carries the exact same value — it's what the game
    // used to populate the row in the first place — and it's a plain Unity Component read, not an
    // IL2CPP struct marshal, so use that instead of touching the slot at all.
    //
    // For qualify/eliminate rows specifically, don't even try to resolve identity from the fall feed
    // side: the notification appears the instant the game despawns that player's bean, so any HUD-row
    // scan (ResolveKeyByDisplayName) is chasing a row that's usually already gone. FeatureTimePlacement
    // already resolves a reliable key for every qualify/eliminate the moment it happens (that's how the
    // PB/roster time column works) — its progress messages land in the same order as the matching fall
    // feed notification, so queue keys there and dequeue them here instead of re-deriving identity.
    internal static class FallFeedNameCore
    {
        // CreateNotification doesn't hand us the ViewModel it just populated, so find it. Body text
        // alone isn't enough — every elimination (or every player sending the same phrase) shares the
        // exact same MessageBody, so a plain text match grabbed whatever row happened to sit at the
        // highest sibling index, not the one this call just populated (that's what was putting the
        // local player's styling on other people's rows, and delaying it onto a later one for our own).
        // _enableTime gets stamped to Time.time the instant a row is shown, and CreateNotification is
        // fully synchronous, so the row it just populated has the LARGEST _enableTime among any text
        // matches — same frame, same Time.time as this postfix.
        internal static FallFeedNotificationViewModel FindShownNotification(Transform container, string messageBody)
        {
            if (container == null) return null;
            FallFeedNotificationViewModel best = null;
            float bestEnableTime = float.NegativeInfinity;
            for (int i = 0; i < container.childCount; i++)
            {
                var vm = container.GetChild(i)?.GetComponent<FallFeedNotificationViewModel>();
                if (vm == null || vm._disposed) continue;
                if (vm._messageBodyText == null || vm._messageBodyText.text != messageBody) continue;
                if (vm._enableTime < bestEnableTime) continue;
                bestEnableTime = vm._enableTime;
                best = vm;
            }
            return best;
        }

        internal static void RestyleSlot(TextMeshProUGUI iconText, TextMeshProUGUI nameText, string localKey, string queuedKey)
        {
            if (nameText == null) return;
            string displayName = nameText.text ?? "";
            if (string.IsNullOrEmpty(displayName)) return;

            bool isLocal = IsLocalDisplayName(displayName);
            string fullKey = isLocal ? localKey
                : (!string.IsNullOrEmpty(queuedKey) ? queuedKey : NametagIconApplicator.ResolveKeyByDisplayName(displayName));

            string spriteName = FeatureMorePlatformIcon.SpriteNameForPlayerKey(fullKey);
            // outline sheet (not the plain one) — that's what fall feed used pre-update, since its
            // icons sit over a varied message background rather than a flat panel. 0.6f compensates
            // for the 1.8x IconScale every glyph in this sprite asset is baked with (BuildAsset),
            // not something tuned for the old inline-string layout specifically.
            if (!string.IsNullOrEmpty(spriteName) && iconText != null && NametagIconApplicator.ApplyInlinePlatformAssetOutline(iconText))
                NametagIconApplicator.ApplyInlinePlatform(iconText, spriteName, 0.6f);

            // rows are pooled — the game refreshes .text every show but never resets colour/gradient/
            // material, so whatever a previous occupant's row got styled to (gold gradient, custom
            // colour) sticks around for whoever reuses that TMP next: right name, someone else's look.
            nameText.enableVertexGradient = false;
            if (AssetManager.DefaultNameMaterial != null) nameText.fontSharedMaterial = AssetManager.DefaultNameMaterial;
            nameText.color = Color.white;

            var info = isLocal
                ? FeatureMorePlatformIcon.LocalNametagInfo()
                : (RemoteProfileStore.TryGet(fullKey)?.nametag ?? RemoteProfileStore.TryGet(displayName)?.nametag);
            if (info == null) return;

            string fallback = displayName;
            if (StripSizeTagsTweak.Active) fallback = StripSizeTagsTweak.Strip(fallback);
            NametagIconApplicator.ApplyRemoteToNameplate(nameText, fallback, info);
        }

        internal static bool IsLocalDisplayName(string displayName)
        {
            string localName = LocalPlayerInfo.FGlocalplayerusername;
            return !string.IsNullOrEmpty(localName) && displayName.Equals(localName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch(typeof(FallFeedContainer), "CreateNotification")]
    internal static class FallFeedNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedContainer __instance, FallFeedManager.FallFeedMessageData message)
        {
            try
            {
                if (message == null) return;
                string body = message.MessageBody ?? "";
                var vm = FallFeedNameCore.FindShownNotification(__instance._notificationContainer, body);
                if (vm == null) return;

                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";

                // the primary slot is the one who qualified/was eliminated — dequeue the matching
                // real key FeatureTimePlacement resolved for that same event, in the same order.
                string queuedKey = null;
                if (body.Contains("fallfeed-race")) FeatureTimePlacement.TryDequeueQualifyKey(out queuedKey);
                else if (body.Contains("fallfeed-eliminate")) FeatureTimePlacement.TryDequeueEliminateKey(out queuedKey);

                FallFeedNameCore.RestyleSlot(vm._platformIconText, vm._playerNameText, localKey, queuedKey);
                FallFeedNameCore.RestyleSlot(vm._platformIconText2, vm._playerNameText2, localKey, null);

                string primaryNameBefore = vm._playerNameText != null ? (vm._playerNameText.text ?? "") : "";
                string primaryKey = FallFeedNameCore.IsLocalDisplayName(primaryNameBefore)
                    ? localKey
                    : (!string.IsNullOrEmpty(queuedKey) ? queuedKey : NametagIconApplicator.ResolveKeyByDisplayName(primaryNameBefore));
                FallFeedQualTimeTweak.Instance?.Apply(vm._messageBodyText, primaryKey);

                // our text swap can change rendered width (custom name, colour tags, the qual-time
                // stamp) — the background bubble was already sized off the vanilla text, so it needs
                // the same refresh the old UI got for free from BuildMessageWithPlayerNames().
                vm.RefreshStructuredLayout();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message);
            }
        }
    }
}
