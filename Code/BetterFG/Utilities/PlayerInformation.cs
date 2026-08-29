using FGClient;
using MPG.Utility;
using System;
using UnityEngine;

namespace BetterFG.Utilities
{
    public class PlayerInformation
    {
        public static FallGuysCharacterController GetPlayerFGCCByName(string name)
        {
            if (GlobalGameStateClient.Instance == null)
            {
                Plugin.Log.LogInfo("Instance null");
                return null;
            }

            var gsv = GlobalGameStateClient.Instance.GameStateView;
            if (gsv == null)
            {
                Plugin.Log.LogInfo("GameStateView null");
                return null;
            }

            if (!gsv.GetLiveClientGameManager(out var cgm) || cgm == null)
            {
                Plugin.Log.LogInfo("cgm null");
                return null;
            }

            if (cgm._clientPlayerManager == null)
            {
                Plugin.Log.LogInfo("Player manager null");
                return null;
            }

            if (cgm._clientPlayerManager._playerNetIdIndex == null)
            {
                Plugin.Log.LogInfo("PlayerNetIdIndex null");
                return null;
            }

            foreach (var data in cgm._clientPlayerManager._playerNetIdIndex)
            {
                if (data.value == null)
                {
                    Plugin.Log.LogInfo($"Null player data for key {data.key}");
                    continue;
                }

                if (!string.IsNullOrEmpty(data.value.playerKey) &&
                    data.value.playerKey.Contains(name, StringComparison.CurrentCultureIgnoreCase))
                {
                    var netObj = cgm.GetNetObjectByID(data.key);
                    if (netObj == null)
                    {
                        Plugin.Log.LogInfo($"NetObject null for key {data.key}");
                        continue;
                    }

                    if (netObj.FGCharacterController == null)
                    {
                        Plugin.Log.LogInfo($"FGCharacterController null for key {data.key}");
                        continue;
                    }

                    return netObj.FGCharacterController;
                }
            }

            Plugin.Log.LogInfo($"No player found matching: {name}");
            return null;
        }

        // New helper functions based on what we learned

        /// <summary>
        /// Gets the ClientPlayerManager from the game
        /// </summary>
        public static ClientPlayerManager GetClientPlayerManager()
        {
            try
            {
                ClientGameManager clientGameManager;
                if (SingletonBehaviour<GlobalGameStateClient>.Instance.GameStateView.GetLiveClientGameManager(out clientGameManager))
                {
                    return clientGameManager._clientPlayerManager;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("GetClientPlayerManager failed: {ex}");
            }
            return null;
        }


        /// <summary>
        /// Gets the local player's ID
        /// </summary>
        public static uint GetLocalPlayerId()
        {
            try
            {
                var clientPlayerManager = GetClientPlayerManager();
                if (clientPlayerManager?._playerIdIndex == null)
                    return 0;

                foreach (var kvp in clientPlayerManager._playerIdIndex)
                {
                    if (kvp.Value?.fgcc != null && kvp.Value.fgcc.IsLocalPlayer)
                    {
                        return kvp.Key;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("GetLocalPlayerId failed: {ex}");
            }
            return 0;
        }

        /// <summary>
        /// Gets the local player's NetworkPlayerDataClient
        /// </summary>
        public static NetworkPlayerDataClient GetLocalPlayerData()
        {
            try
            {
                var clientPlayerManager = GetClientPlayerManager();
                if (clientPlayerManager?._playerIdIndex == null)
                    return null;

                foreach (var kvp in clientPlayerManager._playerIdIndex)
                {
                    if (kvp.Value?.fgcc != null && kvp.Value.fgcc.IsLocalPlayer)
                    {
                        return kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("GetLocalPlayerData failed: {ex}");
            }
            return null;
        }

        static string _cachedLocalBareKey = "";

        /// <summary>
        /// The local player's playerKey in the SAME bare format everyone else's playerKey comes in
        /// (squad members, PlayerScores, PlayerKeyById off _playerIdIndex) - GlobalGameStateClient's
        /// own GetLocalPlayerKey() returns "&lt;platform&gt;_&lt;service&gt;_&lt;bareKey&gt;" instead, which won't
        /// match anything keyed off the roster. GetLocalPlayerData() goes empty once we're dead/
        /// spectating (no fgcc), so the bare key is cached the first time it's found and reused after.
        /// </summary>
        public static string GetLocalBarePlayerKey()
        {
            string key = GetLocalPlayerData()?.playerKey ?? "";

            if (string.IsNullOrEmpty(key))
            {
                string ggs = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
                // ggs is "<platform>_<service>_<bareKey>" (e.g. "pc_steam_zmxnczxcnjzxcnjzx"). don't
                // use LastIndexOf - a bareKey could itself contain '_' and we'd over-strip it.
                if (!string.IsNullOrEmpty(ggs))
                {
                    int first = ggs.IndexOf('_');
                    int second = first >= 0 ? ggs.IndexOf('_', first + 1) : -1;
                    key = second >= 0 && second < ggs.Length - 1 ? ggs.Substring(second + 1) : ggs;
                }
            }

            if (!string.IsNullOrEmpty(key)) _cachedLocalBareKey = key;
            else if (!string.IsNullOrEmpty(_cachedLocalBareKey)) key = _cachedLocalBareKey;
            return key;
        }

        /// <summary>
        /// Gets the local player's FallGuysCharacterController directly
        /// </summary>
        public static FallGuysCharacterController GetLocalPlayerFGCC()
        {
            try
            {
                var clientPlayerManager = GetClientPlayerManager();
                if (clientPlayerManager?._playerIdIndex == null)
                    return null;

                foreach (var kvp in clientPlayerManager._playerIdIndex)
                {
                    if (kvp.Value?.fgcc != null && kvp.Value.fgcc.IsLocalPlayer)
                    {
                        return kvp.Value.fgcc;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"GetLocalPlayerFGCC failed: {ex}");
            }
            return null;
        }

    }
}
