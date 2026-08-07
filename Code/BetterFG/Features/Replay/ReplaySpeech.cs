using System;
using System.Collections.Generic;
using Character;
using FGClient;
using MPG.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Replay
{
    public class ReplaySpeechOption
    {
        public string itemId = "";
        public int speechId;
        public float duration = 3f;
        public string text = "";
        public bool hasText;
        public bool shiny;
        public byte[] image;
    }

    public struct ReplaySpeechEvent
    {
        public float t;
        public float end;
        public uint playerId;
        public int option;
    }

    internal class ReplaySpeechPlayer
    {
        const float POP_IN = 0.18f;
        const float FADE_OUT = 0.22f;

        class Speaker
        {
            public Transform bean;
            public readonly List<ReplaySpeechEvent> events = new List<ReplaySpeechEvent>();
            public GameObject bubble;
            public SpeechBubbleViewModel vm;
            public CanvasGroup group;
            public Vector3 offset;
            public Vector3 baseScale = Vector3.one;
            public bool text;
            public int shown = -1;
        }

        readonly ReplayRecording _rec;
        readonly Transform _root;
        readonly Dictionary<uint, Speaker> _speakers = new Dictionary<uint, Speaker>();
        readonly Sprite[] _sprites;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        SpeechBubbleViewModel _imagePrefab;
        SpeechBubbleViewModel _textPrefab;
        Camera _cam;

        public bool PhrasesVisible = true;
        public Func<uint, bool> PlayerVisible;

        public ReplaySpeechPlayer(ReplayRecording rec, Transform root)
        {
            _rec = rec;
            _root = root;
            _sprites = new Sprite[rec.speechOptions.Count];
        }

        public void Prepare(Dictionary<uint, Transform> beans)
        {
            if (_rec.speechEvents.Count == 0) return;

            foreach (var vm in Resources.FindObjectsOfTypeAll<SpeechBubbleViewModel>())
            {
                if (vm == null || vm.name.EndsWith("(Clone)")) continue;
                if (vm.TryCast<SpeechBubbleActionViewModel>() != null) continue;

                if (vm.name.IndexOf("AndText", StringComparison.OrdinalIgnoreCase) >= 0) _textPrefab = _textPrefab ?? vm;
                else _imagePrefab = _imagePrefab ?? vm;
            }

            var lookup = SingletonBehaviour<SpeechOptionsManager>.Instance?._speechOptionsLookup;
            var usable = new bool[_sprites.Length];
            int packed = 0;
            int live = 0;

            for (int i = 0; i < _sprites.Length; i++)
            {
                var saved = _rec.speechOptions[i];
                if (Unpack(saved, i)) { packed++; usable[i] = true; continue; }

                var option = Find(lookup, saved);
                var image = option?.TryCast<ImageSpeechOption>();
                if (image == null)
                {
                    Plugin.Log.LogWarning($"nothing left to draw '{saved.itemId}' with — no art packed with the replay and it's gone from the wheel, skipping those bubbles");
                    continue;
                }

                if (string.IsNullOrEmpty(saved.text))
                {
                    saved.text = option.DisplayName ?? "";
                    saved.hasText = option.TryCast<TextAndImageSpeechOption>() != null;
                    saved.shiny = option.IsShiny;
                }

                live++;
                usable[i] = true;
                _sprites[i] = image.CurrentSprite;
                int slot = i;
                image.LoadSprite(new Action<Sprite>(sprite => { if (sprite != null) _sprites[slot] = sprite; }));
            }

            int dropped = 0;
            foreach (var speech in _rec.speechEvents)
            {
                if (!usable[speech.option]) { dropped++; continue; }
                if (!beans.TryGetValue(speech.playerId, out var bean) || bean == null) continue;

                if (!_speakers.TryGetValue(speech.playerId, out var speaker))
                {
                    speaker = new Speaker { bean = bean };
                    _speakers[speech.playerId] = speaker;
                }
                speaker.events.Add(speech);
            }

            Plugin.Log.LogInfo($"replay speech: {_rec.speechEvents.Count} bubbles from {_speakers.Count} players, art for {packed} packed in and {live} off the live wheel out of {_sprites.Length}"
                + (dropped > 0 ? $", {dropped} dropped with nothing to draw them" : "")
                + (_imagePrefab == null ? " — no bubble prefab anywhere, none of them will show" : ""));
        }

        bool Unpack(ReplaySpeechOption saved, int slot)
        {
            if (saved.image == null || saved.image.Length == 0) return false;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            if (!tex.LoadImage(saved.image))
            {
                UnityEngine.Object.Destroy(tex);
                Plugin.Log.LogWarning($"the packed art for '{saved.text}' wouldn't decode, falling back to whatever the wheel has");
                return false;
            }

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _sprites[slot] = sprite;
            _owned.Add(tex);
            _owned.Add(sprite);
            return true;
        }

        static SocialOption Find(Il2CppSystem.Collections.Generic.Dictionary<int, SocialOption> lookup, ReplaySpeechOption saved)
        {
            if (lookup == null) return null;

            foreach (var kvp in lookup)
            {
                var option = kvp.Value;
                if (option != null && FeatureReplay.ItemId(option) == saved.itemId) return option;
            }

            return lookup.ContainsKey(saved.speechId) ? lookup[saved.speechId] : null;
        }

        public void Apply(float time, Camera cam)
        {
            _cam = cam;

            foreach (var kvp in _speakers)
            {
                var speaker = kvp.Value;
                int at = At(speaker, time);
                if (at != speaker.shown) Swap(speaker, at);
                if (speaker.shown < 0 || speaker.bubble == null || speaker.bean == null) continue;

                bool visible = PhrasesVisible && (PlayerVisible == null || PlayerVisible(kvp.Key));
                if (speaker.bubble.activeSelf != visible) speaker.bubble.SetActive(visible);
                if (!visible) continue;

                var speech = speaker.events[speaker.shown];
                float age = time - speech.t;
                float left = speech.end - time;

                float pop = ReplayEase.Apply(ReplayEasingCurve.Back, ReplayEasingDirection.Out, age / POP_IN);
                speaker.bubble.transform.localScale = speaker.baseScale * pop;
                speaker.bubble.transform.SetPositionAndRotation(speaker.bean.position + speaker.offset, cam.transform.rotation);
                speaker.group.alpha = Mathf.Clamp01(left / FADE_OUT);
            }
        }

        static int At(Speaker speaker, float time)
        {
            var events = speaker.events;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i].t > time) continue;
                return time < events[i].end ? i : -1;
            }
            return -1;
        }

        void Swap(Speaker speaker, int at)
        {
            speaker.shown = at;

            if (at < 0)
            {
                if (speaker.bubble != null) speaker.bubble.SetActive(false);
                return;
            }

            int slot = speaker.events[at].option;
            var saved = _rec.speechOptions[slot];
            if (speaker.bubble == null || speaker.text != saved.hasText) Build(speaker, saved.hasText);
            if (speaker.bubble == null) { speaker.shown = -1; return; }

            speaker.bubble.SetActive(true);
            speaker.vm.SpeechImageSprite = _sprites[slot];
            speaker.vm.SpeechText = saved.hasText ? saved.text : "";
            speaker.vm.IsShiny = saved.shiny;
            speaker.vm.IsShiny = saved.shiny;
            speaker.vm.Alpha = 1f;
            speaker.vm.RaiseAllPropertiesChanged();

            foreach (var label in speaker.bubble.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate();

            var rects = speaker.bubble.GetComponentsInChildren<RectTransform>(true);
            for (int i = rects.Length - 1; i >= 0; i--) LayoutRebuilder.ForceRebuildLayoutImmediate(rects[i]);
        }

        void Build(Speaker speaker, bool wantsText)
        {
            if (speaker.bubble != null) UnityEngine.Object.Destroy(speaker.bubble);

            var prefab = wantsText && _textPrefab != null ? _textPrefab : _imagePrefab;
            if (prefab == null) { speaker.bubble = null; return; }

            var clone = UnityEngine.Object.Instantiate(prefab.gameObject, _root);
            clone.name = "BettrFG_ReplayBubble";
            clone.SetActive(true);

            int layer = speaker.bean.gameObject.layer;
            foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera = _cam;

            speaker.bubble = clone;
            speaker.text = wantsText;
            speaker.baseScale = clone.transform.localScale;
            speaker.vm = clone.GetComponent<SpeechBubbleViewModel>();
            speaker.offset = speaker.vm._billboardPositionOffset;
            speaker.vm.enabled = false;

            speaker.group = clone.GetComponent<CanvasGroup>();
            if (speaker.group == null) speaker.group = clone.AddComponent<CanvasGroup>();
        }

        public void Release()
        {
            foreach (var kvp in _speakers)
                if (kvp.Value.bubble != null) UnityEngine.Object.Destroy(kvp.Value.bubble);
            _speakers.Clear();

            foreach (var asset in _owned)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            _owned.Clear();
        }
    }
}
