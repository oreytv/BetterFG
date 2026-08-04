using System;
using System.Collections.Generic;
using BetterFG.UI;
using UnityEngine;

namespace BetterFG.Features.Replay
{
    internal class ReplayKeyframeWindow
    {
        public static ReplayKeyframeWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;

        static readonly Vector3 TITLE_POS = new Vector3(-153.6963f, 113.9108f, 0f);

        readonly ReplayRecording _rec;
        readonly ReplayKeyframe _kf;
        readonly List<int> _targets = new List<int>();
        readonly Action _onClose;
        readonly RectTransform _root;
        readonly float _width;
        RectTransform _content;
        float _y;
        int _row;

        public ReplayKeyframe Keyframe => _kf;

        ReplayKeyframeWindow(Transform parent, Rect rect, ReplayRecording rec, ReplayKeyframe kf, Action onClose)
        {
            _rec = rec;
            _kf = kf;
            _onClose = onClose;
            _width = rect.width;
            _root = UGUIShip.CreatePanel(parent, rect, Color.clear, "ReplayKeyframeWindow");
            ReplayWindowKit.EditBackdrop(_root);

            var pick = new Dictionary<string, int>();
            var depth = new Dictionary<string, int>();
            for (int i = 0; i < rec.worldObjects.Count; i++)
            {
                var obj = rec.worldObjects[i];

                string key;
                if (!string.IsNullOrEmpty(obj.prefab)) key = "p" + i;
                else if (!string.IsNullOrEmpty(obj.guid)) key = "g" + obj.guid;
                else
                {
                    int slash = obj.path.IndexOf('/');
                    key = "s" + (slash < 0 ? obj.path : obj.path.Substring(0, slash));
                }

                int deep = 0;
                foreach (char c in obj.path) if (c == '/') deep++;

                if (pick.ContainsKey(key) && depth[key] <= deep) continue;
                pick[key] = i;
                depth[key] = deep;
            }

            foreach (var index in pick.Values) _targets.Add(index);
            _targets.Sort();

            Rebuild();
        }

        public static ReplayKeyframeWindow Show(Transform parent, Rect rect, ReplayRecording rec, ReplayKeyframe kf, Action onClose)
        {
            Instance?.Close();
            Instance = new ReplayKeyframeWindow(parent, rect, rec, kf, onClose);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            ReplayViewer.Instance?.ClearPlayerHighlight();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _onClose?.Invoke();
        }

        void Rebuild()
        {
            if (_content != null) UnityEngine.Object.Destroy(_content.gameObject);

            _content = ReplayWindowKit.Content(_root);

            var stamp = TimeSpan.FromSeconds(_kf.time);
            ReplayWindowKit.Title(_content, _width, string.Format("Camera keyframe  ·  {0:D2}:{1:D2}.{2:D3}",
                stamp.Minutes, stamp.Seconds, stamp.Milliseconds), TITLE_POS);

            _y = ReplayWindowKit.HEAD + 4f;
            _row = 0;

            Line("Camera", ReplayKeyframe.TypeLabel(_kf.cameraType), () => StepType(-1), () => StepType(1));

            if (_kf.cameraType == ReplayCameraType.Fixed)
                Line("Attached to", PlayerLabel(_kf.targetPlayerId), () => StepPlayer(false, -1), () => StepPlayer(false, 1), () => Pick(false, false));
            else if (_kf.cameraType == ReplayCameraType.FixedObject)
                Line("Attached to", ObjectLabel(_kf.targetObject), () => StepObject(false, -1), () => StepObject(false, 1), () => Pick(false, true));

            Line("Look at", ReplayKeyframe.LookAtLabel(_kf.lookAt), () => StepLookAt(-1), () => StepLookAt(1));

            if (_kf.lookAt == ReplayLookAt.Player)
                Line("Look target", PlayerLabel(_kf.lookAtPlayerId), () => StepPlayer(true, -1), () => StepPlayer(true, 1), () => Pick(true, false));
            else if (_kf.lookAt == ReplayLookAt.Object)
                Line("Look target", ObjectLabel(_kf.lookAtObject), () => StepObject(true, -1), () => StepObject(true, 1), () => Pick(true, true));

            Line("Easing", ReplayKeyframe.EasingLabel(_kf.easingCurve), () => StepEasing(-1), () => StepEasing(1));

            if (_kf.easingCurve != ReplayEasingCurve.Linear && _kf.easingCurve != ReplayEasingCurve.Constant)
                Line("Direction", ReplayKeyframe.DirectionLabel(_kf.easingDirection), () => StepDirection(-1), () => StepDirection(1));

            Line("Transition", ReplayKeyframe.CutLabel(_kf.cut), ToggleCut, ToggleCut);
            Line("Game speed", ReplayKeyframe.SpeedLabel(_kf.speed), () => StepSpeed(-1), () => StepSpeed(1));

            _y += 4f;
            UGUIShip.CreateButton(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW + 4f), "Edit camera",
                ReplayWindowKit.BTN_GREEN, Color.white, UIScale.FS_SM, new Action(EditCamera));
            _y += ROW + 6f;

            UGUIShip.CreateLabel(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW),
                "X deletes", UIScale.FS_SM - 1, ReplayWindowKit.HINT);

            _root.sizeDelta = new Vector2(_width, _y + ROW + PAD);
        }

        void Line(string label, string value, Action prev, Action next, Action centre = null)
        {
            ReplayWindowKit.Carousel(_content, _y, _width, _row, label, value, prev, next, centre);
            _y += ROW;
            _row++;
        }

        void Pick(bool lookTarget, bool objects) =>
            ReplayViewer.Instance?.BeginTargetPick(_kf, lookTarget, objects, _targets);

        void EditCamera()
        {
            MarkPlayer(false, 0);
            ReplayViewer.Instance?.BeginCameraEdit(_kf);
        }

        static void MarkPlayer(bool on, uint playerId)
        {
            var viewer = ReplayViewer.Instance;
            if (viewer == null) return;
            if (on) viewer.HighlightPlayer(playerId);
            else viewer.ClearPlayerHighlight();
        }

        string ObjectLabel(int index)
        {
            var list = _rec.worldObjects;
            if (_targets.Count == 0) return "no objects";
            if (index < 0 || index >= list.Count) index = _targets[0];

            var obj = list[index];
            if (!string.IsNullOrEmpty(obj.prefab)) return obj.prefab;

            string path = obj.path;
            int slash = path.LastIndexOf('/');
            if (slash >= 0) path = path.Substring(slash + 1);
            int colon = path.IndexOf(':');
            return colon >= 0 ? path.Substring(colon + 1) : path;
        }

        void StepObject(bool lookTarget, int dir)
        {
            if (_targets.Count == 0) return;

            int at = _targets.IndexOf(lookTarget ? _kf.lookAtObject : _kf.targetObject);
            if (at < 0) at = 0;
            int next = _targets[ReplayWindowKit.Step(at, dir, _targets.Count, true)];

            if (lookTarget) _kf.lookAtObject = next;
            else
            {
                var was = Anchor(_kf.cameraType, _kf.targetPlayerId);
                _kf.targetObject = next;
                _kf.position += was - Anchor(_kf.cameraType, _kf.targetPlayerId);
            }
            ReplayViewer.Instance?.HighlightObject(next);
            Rebuild();
        }

        string PlayerLabel(uint playerId)
        {
            var list = _rec.players;
            if (list.Count == 0) return "no players";
            for (int i = 0; i < list.Count; i++)
                if (list[i].playerId == playerId)
                    return string.IsNullOrEmpty(list[i].name) ? ("Player " + playerId) : list[i].name;
            return string.IsNullOrEmpty(list[0].name) ? ("Player " + list[0].playerId) : list[0].name;
        }

        void StepType(int dir)
        {
            var world = ReplayViewer.Instance != null ? ReplayViewer.Instance.KeyframePosition(_kf) : _kf.position;

            _kf.cameraType = (ReplayCameraType)ReplayWindowKit.Step((int)_kf.cameraType, dir, 4, true);
            if (_kf.cameraType == ReplayCameraType.FixedObject && !_targets.Contains(_kf.targetObject)) _kf.targetObject = _targets.Count > 0 ? _targets[0] : -1;
            if (_kf.cameraType != ReplayCameraType.FixedObject) _kf.targetObject = -1;
            _kf.position = world - Anchor(_kf.cameraType, _kf.targetPlayerId);
            if (_kf.cameraType == ReplayCameraType.FixedObject) ReplayViewer.Instance?.HighlightObject(_kf.targetObject);
            else MarkPlayer(_kf.cameraType == ReplayCameraType.Fixed, _kf.targetPlayerId);
            Rebuild();
        }

        void Rebase(ReplayCameraType wasType, uint wasTarget)
        {
            _kf.position += Anchor(wasType, wasTarget) - Anchor(_kf.cameraType, _kf.targetPlayerId);
        }

        Vector3 Anchor(ReplayCameraType type, uint playerId)
        {
            if (ReplayViewer.Instance == null) return Vector3.zero;
            if (type == ReplayCameraType.Fixed) return ReplayViewer.Instance.PlayerPositionAt(playerId, _kf.time);
            if (type == ReplayCameraType.FixedObject) return ReplayViewer.Instance.TargetPositionAt(_kf.targetObject, 0, _kf.time);
            return Vector3.zero;
        }

        void StepLookAt(int dir)
        {
            _kf.lookAt = (ReplayLookAt)ReplayWindowKit.Step((int)_kf.lookAt, dir, 3, true);
            if (_kf.lookAt == ReplayLookAt.Object && !_targets.Contains(_kf.lookAtObject)) _kf.lookAtObject = _targets.Count > 0 ? _targets[0] : -1;
            if (_kf.lookAt != ReplayLookAt.Object) _kf.lookAtObject = -1;
            if (_kf.lookAt == ReplayLookAt.Object) ReplayViewer.Instance?.HighlightObject(_kf.lookAtObject);
            else MarkPlayer(_kf.lookAt == ReplayLookAt.Player, _kf.lookAtPlayerId);
            Rebuild();
        }

        void StepEasing(int dir)
        {
            _kf.easingCurve = (ReplayEasingCurve)ReplayWindowKit.Step((int)_kf.easingCurve, dir, ReplayEase.CurveCount, true);
            MarkPlayer(false, 0);
            Rebuild();
        }

        void StepDirection(int dir)
        {
            _kf.easingDirection = (ReplayEasingDirection)ReplayWindowKit.Step((int)_kf.easingDirection, dir, ReplayEase.DirectionCount, true);
            MarkPlayer(false, 0);
            Rebuild();
        }

        void ToggleCut()
        {
            _kf.cut = !_kf.cut;
            MarkPlayer(false, 0);
            Rebuild();
        }

        void StepSpeed(int dir)
        {
            var table = ReplayKeyframe.Speeds;
            int index = 0;
            float closest = float.MaxValue;
            for (int i = 0; i < table.Length; i++)
            {
                float delta = Mathf.Abs(table[i] - _kf.speed);
                if (delta < closest) { closest = delta; index = i; }
            }

            _kf.speed = table[ReplayWindowKit.Step(index, dir, table.Length, false)];
            MarkPlayer(false, 0);
            Rebuild();
        }

        void StepPlayer(bool lookTarget, int dir)
        {
            var list = _rec.players;
            if (list.Count == 0) return;

            uint current = lookTarget ? _kf.lookAtPlayerId : _kf.targetPlayerId;
            int index = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].playerId == current) { index = i; break; }

            index = ReplayWindowKit.Step(index, dir, list.Count, true);
            if (lookTarget) _kf.lookAtPlayerId = list[index].playerId;
            else
            {
                uint wasTarget = _kf.targetPlayerId;
                _kf.targetPlayerId = list[index].playerId;
                Rebase(_kf.cameraType, wasTarget);
            }
            MarkPlayer(true, lookTarget ? _kf.lookAtPlayerId : _kf.targetPlayerId);
            Rebuild();
        }
    }
}
