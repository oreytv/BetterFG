using System;
using UnityEngine;
using UnityEngine.UI;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    public class RowLabelFit : MonoBehaviour
    {
        public RowLabelFit(IntPtr ptr) : base(ptr) { }

        private const int MaxLines = 2;

        private RectTransform _parentRt;
        private LayoutElement _le;
        private Text _label;
        private RectTransform _labelRt;
        private float _baseHeight;
        private string _lastText;
        private int _settle = 6;

        void LateUpdate()
        {
            if (_le == null)
            {
                _le = GetComponent<LayoutElement>();
                if (_le == null) { enabled = false; return; }
                _parentRt = transform.parent != null ? transform.parent.GetComponent<RectTransform>() : null;
                _baseHeight = _le.preferredHeight;
            }

            if (_label == null)
            {
                _label = WidestChildText();
                if (_label == null)
                {
                    if (--_settle <= 0) enabled = false;
                    return;
                }
                _labelRt = _label.rectTransform;
                _label.horizontalOverflow = HorizontalWrapMode.Wrap;
                _label.verticalOverflow = VerticalWrapMode.Overflow;
            }

            bool changed = _label.text != _lastText;
            if (_settle <= 0 && !changed) return;
            if (_settle > 0) _settle--;
            _lastText = _label.text;

            float lineH = _label.fontSize + 4f;
            int lines = Mathf.Clamp(Mathf.RoundToInt(_label.preferredHeight / lineH), 1, MaxLines);
            float want = _baseHeight + (lines - 1) * lineH;
            if (Mathf.Approximately(_le.preferredHeight, want)) return;

            _le.preferredHeight = want;
            if (Mathf.Approximately(_labelRt.anchorMin.y, _labelRt.anchorMax.y))
                _labelRt.sizeDelta = new Vector2(_labelRt.sizeDelta.x, want);
            if (_parentRt != null) LayoutRebuilder.MarkLayoutForRebuild(_parentRt);
        }

        private Text WidestChildText()
        {
            Text best = null;
            float bestW = float.NegativeInfinity;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                var t = child.GetComponent<Text>();
                if (t == null) continue;
                float w = child.GetComponent<RectTransform>().rect.width;
                if (w > bestW) { best = t; bestW = w; }
            }
            return best;
        }
    }
}
