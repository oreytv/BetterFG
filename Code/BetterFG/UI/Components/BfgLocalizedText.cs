using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Components
{
    // drop this on any Text alongside a localization id. re-applies the id's current-language string
    // on enable and on every LocalizationService.LanguageChanged. if something else set .text directly
    // since our last apply (a stateful button relabeling itself, a live value display), we back off
    // instead of stomping it — better to leave it untranslated than snap a live value back to the id's
    // stale creation-time text.
    public class BfgLocalizedText : MonoBehaviour
    {
        public BfgLocalizedText(IntPtr ptr) : base(ptr) { }

        public string Key;

        private Text _text;
        private string _lastApplied;

        public void SetKey(string key)
        {
            Key = key;
            Apply();
        }

        void Awake()
        {
            _text = GetComponent<Text>();
        }

        void OnEnable()
        {
            LocalizationService.LanguageChanged += Apply;
            Apply();
        }

        void OnDisable()
        {
            LocalizationService.LanguageChanged -= Apply;
        }

        private void Apply()
        {
            if (_text == null) _text = GetComponent<Text>();
            if (_text == null || string.IsNullOrEmpty(Key)) return;
            if (_lastApplied != null && _text.text != _lastApplied) return;
            _lastApplied = LocalizationService.Get(Key);
            _text.text = _lastApplied;
        }
    }
}
