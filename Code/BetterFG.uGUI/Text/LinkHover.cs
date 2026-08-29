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
    // recolors a link Text on hover. lives on the link's transparent hit rect so CreateLinkLabel
    // callers don't have to poll hover state themselves.
    public class LinkHover : MonoBehaviour
    {
        public LinkHover(IntPtr ptr) : base(ptr) { }
        public Text Text;
        public Color Idle;
        public Color Hover;
        private RectTransform _rt;
        private Canvas _canvas;
        private bool _over;

        void Awake()
        {
            _rt = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
        }

        void Update()
        {
            if (_rt == null || Text == null) return;
            if (_canvas != null && !_canvas.enabled) return;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(
                _rt, new Vector2(Input.mousePosition.x, Input.mousePosition.y), null);
            if (over == _over) return;
            _over = over;
            Text.color = over ? Hover : Idle;
        }
    }
}
