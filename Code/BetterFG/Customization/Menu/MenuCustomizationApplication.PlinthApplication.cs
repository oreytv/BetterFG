using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Customization.Player;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;

namespace BetterFG.Customization.Menu
{
    public partial class MenuCustomizationApplication
    {
        private const string PLINTH_PATH = "3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO";
        private const string PLINTH_MESH_PATH = PLINTH_PATH + "/ENV_Plinth_MO";

        public Vector3 internaloffset = new Vector3(0f, 2.4387f, 0f);
        public Vector3 internaloffsetVictory = new Vector3(-0.565f, 0.7669f, 0.381f);

        // main-menu slot
        private GameObject _appliedPlinth;
        private string _appliedFile;
        private bool _origActive = true;

        // extra slots (victory, reward, etc.) keyed by holderGO instance id
        private readonly Dictionary<int, GameObject> _extraApplied = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, bool> _extraOrigActive = new Dictionary<int, bool>();

        // one bundle per file — never double-load
        private readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>();

        // last applied — so BeanMonitorService.PushPlinth can immediately apply to late-arriving slots
        private SkinInfo _lastInfo;
        private AssetBundle _lastBundle;

        // ── Bundle registry ───────────────────────────────────────────────────

        public bool TryGetBundle(string file, out AssetBundle bundle)
        {
            bundle = null;
            if (string.IsNullOrEmpty(file)) return false;
            return _bundles.TryGetValue(file, out bundle) && bundle != null;
        }

        public AssetBundle GetOrRegisterBundle(string file, AssetBundle incoming)
        {
            if (string.IsNullOrEmpty(file)) return incoming;
            if (_bundles.TryGetValue(file, out var existing) && existing != null) return existing;
            if (incoming != null) _bundles[file] = incoming;
            return incoming;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ReapplyToMainMenu()
        {
            if (_lastInfo == null || _lastBundle == null)
            {
                // runtime state's gone (game rebuilt the plinth screen, or initial restore never
                // landed) but a plinth is still saved — pull it back from settings and re-apply.
                SkinApplicationService.Instance?.RestorePlinthFromSettings();
                return;
            }
            if (_appliedPlinth != null) { Destroy(_appliedPlinth); _appliedPlinth = null; }
            _origActive = true;
            StartCoroutine(ApplyToMainMenuCoroutine(_lastInfo, _lastBundle).WrapToIl2Cpp());
        }

        public void ApplyPlinth(SkinInfo info, AssetBundle bundle)
        {
            if (info == null || bundle == null) return;

            bundle = GetOrRegisterBundle(info.file, bundle);
            _lastInfo = info;
            _lastBundle = bundle;
            // commit the active file NOW (not at the end of the async coroutine) so a second Apply
            // press while this one is still loading sees ActiveFile==file and skips re-applying
            _appliedFile = info.file;

            StartCoroutine(ApplyToMainMenuCoroutine(info, bundle).WrapToIl2Cpp());

            BeanMonitorService.ClearDestroyedPlinths();
            foreach (var slot in BeanMonitorService.GetTrackedPlinths())
                StartCoroutine(ApplyToSlotCoroutine(slot, info, bundle).WrapToIl2Cpp());
        }

        public void ApplyToPlinthSlot(PlinthSlot slot)
        {
            if (slot == null || _lastInfo == null || _lastBundle == null) return;
            StartCoroutine(ApplyToSlotCoroutine(slot, _lastInfo, _lastBundle).WrapToIl2Cpp());
        }

        // applies a profile's plinth (loaded from raw bytes) to one specific holder slot WITHOUT
        // touching the local plinth state (_lastInfo/_appliedPlinth). used for lobby remote players —
        // each party holder can get a different person's plinth.
        public void ApplyProfilePlinthToSlot(SkinInfo info, byte[] bytes, PlinthSlot slot)
        {
            if (info == null || bytes == null || slot == null) return;
            StartCoroutine(ApplyProfilePlinthToSlotCoroutine(info, bytes, slot).WrapToIl2Cpp());
        }

        private IEnumerator ApplyProfilePlinthToSlotCoroutine(SkinInfo info, byte[] bytes, PlinthSlot slot)
        {
            AssetBundle bundle;
            if (!TryGetBundle(info.file, out bundle) || bundle == null)
            {
                var loadReq = AssetBundle.LoadFromMemoryAsync(bytes);
                yield return loadReq;
                bundle = loadReq.assetBundle;
                if (bundle == null) { Plugin.Log.LogWarning($"lobby plinth bundle wouldn't load: {info.file}"); yield break; }
                bundle = GetOrRegisterBundle(info.file, bundle);
            }
            yield return ApplyToSlotCoroutine(slot, info, bundle).WrapToIl2Cpp();
        }

        public void RemovePlinth()
        {
            if (_appliedPlinth != null)
            {
                Destroy(_appliedPlinth);
                _appliedPlinth = null;
            }

            var mesh = GameObject.Find(PLINTH_MESH_PATH);
            if (mesh != null) mesh.SetActive(true);

            foreach (var kvp in _extraApplied)
                if (kvp.Value != null) Destroy(kvp.Value);
            _extraApplied.Clear();

            foreach (var slot in BeanMonitorService.GetTrackedPlinths())
            {
                if (slot.meshGO == null) continue;
                slot.meshGO.SetActive(true);
            }
            _extraOrigActive.Clear();

            foreach (var kvp in _bundles)
                if (kvp.Value != null) kvp.Value.Unload(false);
            _bundles.Clear();

            _lastInfo = null;
            _lastBundle = null;
            _appliedFile = null;
            _origActive = true;

        }

        public bool HasPlinthApplied => _appliedPlinth != null || _lastInfo != null;
        public string ActiveFile => _appliedFile;

        // ── Coroutines ────────────────────────────────────────────────────────

        private IEnumerator ApplyToMainMenuCoroutine(SkinInfo info, AssetBundle bundle)
        {
            var holder = GameObject.Find(PLINTH_PATH);
            var mesh = GameObject.Find(PLINTH_MESH_PATH);

            if (holder == null || mesh == null)
            {
                Plugin.Log.LogWarning("no plinth holder/mesh in the main menu, skipping");
                OnStatus?.Invoke("Plinth: mesh not found");
                yield break;
            }

            if (_appliedPlinth != null)
            {
                Destroy(_appliedPlinth);
                _appliedPlinth = null;
            }

            _origActive = mesh.activeSelf;
            mesh.SetActive(false);

            string prefabName = FindPrefabName(bundle);
            if (prefabName == null)
            {
                Plugin.Log.LogWarning("plinth bundle has no prefab");
                OnStatus?.Invoke("Plinth: bad bundle");
                mesh.SetActive(_origActive);
                yield break;
            }

            var req = bundle.LoadAssetAsync<GameObject>(prefabName);
            yield return req;

            var prefab = req.asset?.Cast<GameObject>();
            if (prefab == null)
            {
                Plugin.Log.LogWarning("plinth prefab cast failed");
                OnStatus?.Invoke("Plinth: load failed");
                mesh.SetActive(_origActive);
                yield break;
            }

            var clone = Instantiate(prefab, holder.transform);
            clone.transform.localPosition = internaloffset;
            clone.transform.localRotation = Quaternion.identity;
            clone.layer = LayerMask.NameToLayer("PlayerUI");
            clone.name = "BetterFG_Plinth";

            yield return null;

            SkinApplicationService.SetRenderQueue(clone, 3000);

            _appliedPlinth = clone;
            _appliedFile = info.file;

            Plugin.Log.LogInfo($"plinth {info.name} on the main menu");
            OnStatus?.Invoke($"Plinth: {info.name}");
        }

        private IEnumerator ApplyToSlotCoroutine(PlinthSlot slot, SkinInfo info, AssetBundle bundle)
        {
            if (slot?.holderGO == null || slot.meshGO == null) yield break;

            int id = slot.holderGO.GetInstanceID();

            if (_extraApplied.TryGetValue(id, out var existing) && existing != null)
            {
                Destroy(existing);
                _extraApplied.Remove(id);
            }

            if (!_extraOrigActive.ContainsKey(id))
                _extraOrigActive[id] = slot.meshGO.activeSelf;

            slot.meshGO.SetActive(false);

            string prefabName = FindPrefabName(bundle);
            if (prefabName == null)
            {
                Plugin.Log.LogWarning($"plinth bundle has no prefab, slot {slot.type}");
                slot.meshGO.SetActive(_extraOrigActive[id]);
                yield break;
            }

            var req = bundle.LoadAssetAsync<GameObject>(prefabName);
            yield return req;

            var prefab = req.asset?.Cast<GameObject>();
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"plinth prefab cast failed, slot {slot.type}");
                slot.meshGO.SetActive(_extraOrigActive[id]);
                yield break;
            }

            var clone = Instantiate(prefab, slot.holderGO.transform);
            clone.transform.localPosition = slot.type == PlinthType.Victory ? internaloffsetVictory : internaloffset;
            clone.transform.localRotation = Quaternion.identity;
            clone.layer = LayerMask.NameToLayer("PlayerUI");
            clone.name = "BetterFG_Plinth";

            yield return null;

            SkinApplicationService.SetRenderQueue(clone, 3000);

            _extraApplied[id] = clone;
            Plugin.Log.LogInfo($"plinth {info.name} -> slot {slot.type}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string FindPrefabName(AssetBundle bundle)
        {
            foreach (var name in bundle.GetAllAssetNames())
                if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    return name;
            return null;
        }
    }
}
