using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using BetterFG.Core;
using BetterFG.UI;

namespace BetterFG.Services
{
    // fullscreen cover for canvas open/close transitions, spawned from the bettrfg_ui_canvas_loadingscreen
    // bundle. one shared canvas, reused by whatever's transitioning (replay viewer today) so it always
    // sits above everything and nothing pops in underneath it.
    public static class LoadingScreenService
    {
        private static Canvas _canvas;

        public static bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

        public static void Show()
        {
            if (_canvas == null)
            {
                _canvas = UGUIShip.CreateCanvas("BettrFG_LoadingCanvas");
                _canvas.sortingOrder = 1002;

                var go = AssetManager.SpawnPersistent("bettrfg_ui_canvas_loadingscreen");
                if (go != null)
                {
                    go.transform.SetParent(_canvas.transform, false);
                    var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                }
            }
            _canvas.gameObject.SetActive(true);
        }

        // plays the outro on the prefab's own animator instead of just yanking it away,
        // then hides once that's had time to play out
        public static void Hide()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) return;

            Animator anim = _canvas.GetComponentInChildren<Animator>(true);

            if (anim == null || AssetManager.Instance == null)
            {
                _canvas.gameObject.SetActive(false);
                return;
            }

            anim.SetTrigger("end");
            AssetManager.Instance.StartCoroutine(HideAfterStop().WrapToIl2Cpp());
        }

        private static IEnumerator HideAfterStop()
        {
            yield return new WaitForSecondsRealtime(0.7f);
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }
    }
}
