using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using TMPro;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using FG.Common;

namespace BetterFG.Customization.Profiles
{
    public static class LobbyProfileService
    {
        private const string PARTY_CONTROLLER_PATH =
            "3D Environment/MainMenu_Environment/PlinthRig/FGPartyController";
        private const string HOLDER_PREFIX = "CharacterAndPlinthHolder_Party";
        private const string PLINTH_MESH_NAME = "ENV_Plinth_MO";
        private const string CHARACTER_NAME = "PB_UI_Character";

        public static void ApplyToFallGuy(MainMenuFallGuy fg)
        {
            if (fg == null) return;
            var tmp = fg.partyNameTag?._usernameText;
            if (tmp == null || string.IsNullOrEmpty(tmp.text)) return;

            string cleanName = PlayerUtils.CleanPlayerName(StripRichText(tmp.text));
            if (string.IsNullOrEmpty(cleanName)) return;

            var rp = ProfileService.GetRemoteProfileForName(cleanName);
            if (rp == null)
            {
                Plugin.Log.LogInfo($"party member '{cleanName}' (tag reads '{tmp.text}') has no profile — loaded keys: {ProfileService.LoadedKeys()}");
                return;
            }

            var bean = fg.gameObject;
            Transform holder = bean.transform;
            while (holder != null && !holder.name.StartsWith(HOLDER_PREFIX)) holder = holder.parent;

            ApplyToHolder(cleanName, rp, bean, holder, tmp, fg.partyNameTag.transform);
        }

        public static void ReapplyTag(PartyNameTag tag)
        {
            var tmp = tag != null ? tag._usernameText : null;
            if (tmp == null || string.IsNullOrEmpty(tmp.text)) return;

            string shown = StripRichText(tmp.text);
            string cleanName = PlayerUtils.CleanPlayerName(shown);
            if (string.IsNullOrEmpty(cleanName)) return;

            BetterFG.Network.NetworkClient.PrimeProfilesForLobby();
            var p = ProfileService.GetRemoteProfileForName(cleanName);
            if (p?.nametag != null)
            {
                BetterFG.Customization.Player.CustomizationHandler.ApplyToPartyTag(
                    p, BetterFG.Customization.Player.NametagSurface.Party(tmp, tag.transform));
                return;
            }

            if (!BetterFG.Nametag.CrownRankService.IsLocalPlayerKey(cleanName)) return;
            BetterFG.Nametag.NametagIconApplicator.ApplyToNameplate(tmp, shown);
            BetterFG.Nametag.NametagIconApplicator.ApplyLocalBacking(tag.transform);
            BetterFG.Nametag.NametagIconApplicator.ApplyLocalNickname(tag.transform, party: true);
        }

        private static bool _polling;

        public static void ApplyToLobby()
        {
            var host = BeanMonitorService.Instance;
            if (host == null || _polling) return;
            if (ProfileService.GetRemoteProfiles().Count == 0) return;
            BeanMonitorService.ClearRemoteLobbyBeans();
            host.StartCoroutine(PollAndApply().WrapToIl2Cpp());
        }

        public static void ClearLobby()
        {
            BeanMonitorService.ClearRemoteLobbyBeans();
            var host = BeanMonitorService.Instance;
            if (host != null)
                host.StartCoroutine(PollAndApply().WrapToIl2Cpp());
        }

        private static IEnumerator PollAndApply()
        {
            _polling = true;
            try { yield return PollAndApplyInner().WrapToIl2Cpp(); }
            finally { _polling = false; }
        }

        private static IEnumerator PollAndApplyInner()
        {
            float elapsed = 0f;
            while (elapsed < 8f)
            {
                var controller = GameObject.Find(PARTY_CONTROLLER_PATH);
                if (controller != null && HasAnyMatch(controller.transform))
                {
                    yield return new WaitForSeconds(0.5f);
                    ScanAndApply(controller.transform);
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
        }

        private static bool HasAnyMatch(Transform controller)
        {
            for (int i = 0; i < controller.childCount; i++)
            {
                var holder = controller.GetChild(i);
                if (holder == null || !holder.name.StartsWith(HOLDER_PREFIX)) continue;

                var charT = FindDescendant(holder, CHARACTER_NAME);
                var fg = charT != null ? charT.GetComponent<MainMenuFallGuy>() : null;
                var tmp = fg?.partyNameTag?._usernameText;
                if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;

                string cleanName = PlayerUtils.CleanPlayerName(StripRichText(tmp.text));
                if (!string.IsNullOrEmpty(cleanName) && ProfileService.GetRemoteProfileForName(cleanName) != null)
                    return true;
            }
            return false;
        }

        private static void ScanAndApply(Transform controller)
        {
            for (int i = 0; i < controller.childCount; i++)
            {
                var holder = controller.GetChild(i);
                if (holder == null || !holder.name.StartsWith(HOLDER_PREFIX)) continue;

                var charT = FindDescendant(holder, CHARACTER_NAME);
                if (charT == null) continue;

                var fg = charT.GetComponent<MainMenuFallGuy>();
                if (fg == null) continue;

                var tag = fg.partyNameTag;
                var tmp = tag != null ? tag._usernameText : null;
                if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;

                string cleanName = PlayerUtils.CleanPlayerName(StripRichText(tmp.text));
                if (string.IsNullOrEmpty(cleanName)) continue;

                var rp = ProfileService.GetRemoteProfileForName(cleanName);
                if (rp == null) continue;

                ApplyToHolder(cleanName, rp, charT.gameObject, holder, tmp, fg.partyNameTag.transform);
            }
        }

        private static void ApplyToHolder(string cleanName, BfgProfile rp, GameObject bean, Transform holder,
                                          TMPro.TextMeshProUGUI tmp, Transform partyTagTransform)
        {
            BeanMonitorService.PushRemoteLobbyBean(bean);
            ResolvePlinthHolderAndMesh(holder, out var holderGO, out var meshGO);
            CustomizationHandler.ApplyToLobbyBean(rp, bean, holderGO, meshGO, tmp, partyTagTransform);
            Plugin.Log.LogInfo($"'{cleanName}' -> {(holder != null ? holder.name : bean.name)}");
        }

        private static void ResolvePlinthHolderAndMesh(Transform partyHolder, out GameObject holderGO, out GameObject meshGO)
        {
            holderGO = null;
            meshGO = null;
            if (partyHolder == null) return;

            Transform plinthHolder = partyHolder.Find(PLINTH_MESH_NAME);
            if (plinthHolder == null) return;
            holderGO = plinthHolder.gameObject;

            Transform inner = plinthHolder.Find(PLINTH_MESH_NAME);
            meshGO = inner != null ? inner.gameObject : plinthHolder.gameObject;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == name)
                    return all[i];
            return null;
        }

        private static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "").Trim();
        }
    }
}
