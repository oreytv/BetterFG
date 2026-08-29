using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    // sits on a Stylized legacy Text. its Text stays alive only as a layout/measure driver and must
    // never render; the alpha-0 CanvasRenderer multiplier that hides it is wiped whenever the object
    // is re-enabled (tab/window toggles, list rebuilds), so re-assert it here on every OnEnable.
    // event-driven, no per-frame polling.
    public class StylizeGuard : MonoBehaviour
    {
        public StylizeGuard(IntPtr ptr) : base(ptr) { }
        private CanvasRenderer _cr;
        void OnEnable()
        {
            if (_cr == null) _cr = GetComponent<CanvasRenderer>();
            if (_cr != null) _cr.SetColor(new Color(1f, 1f, 1f, 0f));
        }
    }
}
