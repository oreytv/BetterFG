using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Features.MorePlatformIcon;
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
    // For qualify/eliminate rows the notification appears the instant the game despawns that player's
    // bean, so the HUD-row scan in ResolveKeyByDisplayName is chasing a row that's usually already
    // gone. FeatureTimePlacement.OnServerPlayerProgress has the real key at the moment the server
    // message lands, so it seeds name->key into NametagIconApplicator as each player finishes/dies
    // and the lookup here just hits that cache. (An earlier version queued keys and dequeued one per
    // notification, matching by arrival order — a single missing or extra notification, e.g. a
    // survival round's end-of-round batch, shifted every later row onto the wrong player's profile.)
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

        // fall-feed name TMPs we've actually styled — a pooled row we never touched keeps whatever
        // material the game gave it, so a profile-less player never gets scrubbed to the shadow mat.
        private static readonly HashSet<IntPtr> _styledRows = new HashSet<IntPtr>();

        // each row's own font + material as the game first handed it to us. reverting to a single
        // global asap-bold-shadow material stomped CJK / other-language / custom-font rows onto the
        // wrong atlas — that's the garbled bold-shadow text. restore the row's real original instead.
        private static readonly Dictionary<IntPtr, ValueTuple<TMPro.TMP_FontAsset, Material>> _rowOrigin
            = new Dictionary<IntPtr, ValueTuple<TMPro.TMP_FontAsset, Material>>();

        private static void RestoreRowFont(TextMeshProUGUI t)
        {
            t.enableVertexGradient = false;
            if (_rowOrigin.TryGetValue(t.m_CachedPtr, out var o))
            {
                if (o.Item1 != null) t.font = o.Item1;
                if (o.Item2 != null) t.fontSharedMaterial = o.Item2;
            }
            else if (AssetManager.DefaultNameMaterial != null)
            {
                t.fontSharedMaterial = AssetManager.DefaultNameMaterial;
            }
            t.color = Color.white;
            // rows are pooled - whoever occupied this TMP before may have had an icon attached as a
            // sibling under it (AttachUIIcon), and that icon does NOT get cleared just because the
            // font/material got reset above. left alone it rides along onto whoever reuses this row
            // next, showing the WRONG player with our icon.
            NametagIconApplicator.RemoveInlineUIIcon(t.transform);
        }

        internal static void RestyleSlot(TextMeshProUGUI iconText, TextMeshProUGUI nameText, string localKey)
        {
            if (nameText == null) return;
            string displayName = nameText.text ?? "";
            if (string.IsNullOrEmpty(displayName)) return;

            if (!_rowOrigin.ContainsKey(nameText.m_CachedPtr))
                _rowOrigin[nameText.m_CachedPtr] = new ValueTuple<TMPro.TMP_FontAsset, Material>(nameText.font, nameText.fontSharedMaterial);

            bool isLocal = IsLocalDisplayName(displayName);
            string fullKey = isLocal ? localKey : NametagIconApplicator.ResolveKeyByDisplayName(displayName);

            string spriteName = FeatureMorePlatformIcon.SpriteNameForPlayerKey(fullKey);
            // outline sheet (not the plain one) — that's what fall feed used pre-update, since its
            // icons sit over a varied message background rather than a flat panel. 0.6f compensates
            // for the 1.8x IconScale every glyph in this sprite asset is baked with (BuildAsset),
            // not something tuned for the old inline-string layout specifically.
            if (!string.IsNullOrEmpty(spriteName) && iconText != null && NametagIconApplicator.ApplyInlinePlatformAssetOutline(iconText))
                NametagIconApplicator.ApplyInlinePlatform(iconText, spriteName, 0.6f);

            var info = isLocal
                ? FeatureMorePlatformIcon.LocalNametagInfo()
                : (RemoteProfileStore.TryGet(fullKey)?.nametag ?? RemoteProfileStore.TryGet(displayName)?.nametag);

            if (info == null)
            {
                // only undo our own styling if this exact row carried it — an untouched row is left
                // exactly as the game rendered it, material included.
                if (_styledRows.Remove(nameText.m_CachedPtr))
                    RestoreRowFont(nameText);
                return;
            }

            // rows are pooled — the game refreshes .text every show but never resets colour/gradient/
            // material, so whatever a previous occupant's row got styled to (gold gradient, custom
            // colour) sticks around for whoever reuses that TMP next: right name, someone else's look.
            RestoreRowFont(nameText);
            _styledRows.Add(nameText.m_CachedPtr);

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

                FallFeedNameCore.RestyleSlot(vm._platformIconText, vm._playerNameText, localKey);
                FallFeedNameCore.RestyleSlot(vm._platformIconText2, vm._playerNameText2, localKey);

                string primaryNameBefore = vm._playerNameText != null ? (vm._playerNameText.text ?? "") : "";
                bool primaryIsLocal = FallFeedNameCore.IsLocalDisplayName(primaryNameBefore);
                string primaryKey = primaryIsLocal ? localKey : NametagIconApplicator.ResolveKeyByDisplayName(primaryNameBefore);
                // _qualTimes is keyed by the bare playerKey (PlayerKeyById), not GlobalGameStateClient's
                // "<platform>_<service>_<bareKey>" format - only matters for the local player, everyone
                // else already resolves through that same bare-key space via ResolveKeyByDisplayName.
                string qualKey = primaryIsLocal ? BetterFG.Utilities.PlayerInformation.GetLocalBarePlayerKey() : primaryKey;
                FallFeedQualTimeTweak.Instance?.Apply(vm._messageBodyText, qualKey);

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
