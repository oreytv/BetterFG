using System;
using System.Collections.Generic;
using BetterFG;
using FG.Common;
using FallGuysLib.Players;
using FGClient;
using MPG.Utility;
using UnityEngine;

namespace BetterFG.Utilities
{
    /// <summary>
    /// Resolves network identity for Fall Guy roots and lists remote beans in a stable order.
    /// </summary>
    public static class BeanNetworkUtil
    {
        public const uint FakeBeanIdFloor = 100000u;
        public static bool IsFakeBean(uint playerId) => playerId >= FakeBeanIdFloor;

        public static MPGNetObject TryGetMpgNetObject(GameObject bean)
        {
            if (bean == null) return null;
            var n = bean.GetComponent<MPGNetObject>();
            if (n != null) return n;
            n = bean.GetComponentInChildren<MPGNetObject>(true);
            if (n != null) return n;
            return bean.GetComponentInParent<MPGNetObject>();
        }

        public static List<GameObject> GetRemotePlayerBeansSorted(GameObject localPlayerBean)
        {
            var result = new List<GameObject>();
            try
            {
                var cpm = FallGuysLib.Players.PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return result;

                var entries = new List<(uint playerId, GameObject go)>();
                foreach (var kvp in cpm._playerIdIndex)
                {
                    GameObject go;
                    try { go = kvp.Value?.fgcc?.gameObject; }
                    catch { continue; }
                    if (go == null || go == localPlayerBean) continue;
                    entries.Add((kvp.Key, go));
                }

                entries.Sort((a, b) => a.playerId.CompareTo(b.playerId));
                foreach (var e in entries)
                    result.Add(e.go);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("BeanNetworkUtil: GetRemotePlayerBeansSorted: " + ex.Message);
            }

            return result;
        }

        public static string TryGetPlayerKeyForBean(GameObject bean)
        {
            if (bean == null) return null;
            try
            {
                var cpm = FallGuysLib.Players.PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return null;

                foreach (var kvp in cpm._playerIdIndex)
                {
                    try
                    {
                        var data = kvp.Value;
                        if (data == null) continue;
                        var go = data.fgcc != null ? data.fgcc.gameObject : null;
                        if (go != bean) continue;
                        return data.playerKey ?? "";
                    }
                    catch { continue; }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("BeanNetworkUtil: TryGetPlayerKeyForBean: " + ex.Message);
            }

            return null;
        }

        // tears a synthetic (SpawnBeanUtils-spawned) bean down through the game's own unspawn path -
        // plain Destroy() leaves it dangling in the client's net object table and NREs the next
        // unspawn message walking that table (softlocked the game mid-round once). used by both the
        // local pet follower and remote pets spawned off someone else's profile.
        public static bool TryNetworkUnspawn(GameObject bean)
        {
            var netObj = TryGetMpgNetObject(bean);
            if (netObj == null || !netObj.NetID.IsValid()) return false;

            ClientGameManager cgm = null;
            SingletonBehaviour<GlobalGameStateClient>.Instance?.GameStateView?.GetLiveClientGameManager(out cgm);
            var mgr = cgm?._netObjectManager;
            if (mgr == null) return false;

            try
            {
                mgr.UnspawnNetObject(netObj.NetID, MPGNetObjectManager.UnspawnGameObjectPolicy.Destroy);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"network unspawn went sideways, falling back to a plain destroy: {ex.Message}");
                return false;
            }
        }
    }
}
