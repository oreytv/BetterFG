using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BetterFG.Features.UnityRound.Editor;
using FG.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.CreativeGroups
{
    public static class CreativeGroups
    {
        public const string ScaleParamName = "Group Scale";
        public const string ActiveParamName = "Group Enabled";
        public const string PickParamName = "Group";
        private static readonly Color OutlineColour = new Color(0.35f, 0.85f, 1f, 1f);

        private sealed class Group
        {
            public int Id;
            public float Scale = 1f;
            public string Name;
            public bool Active = true;
            public readonly List<string> Guids = new List<string>();
        }

        private static readonly Dictionary<int, Group> _groups = new Dictionary<int, Group>();
        private static readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        private static string _code;
        private static int _nextId = 1;

        private static readonly Dictionary<string, LevelEditorPlaceableObject> _byGuid
            = new Dictionary<string, LevelEditorPlaceableObject>();
        private static int _indexedCount = -1;

        public static string GuidOf(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            string g = obj.GetGuid().ToString();
            if (string.IsNullOrEmpty(g) || g == "00000000-0000-0000-0000-000000000000") return null;
            return g;
        }

        private static LevelEditorPlaceableObject Resolve(string guid)
        {
            if (guid == null) return null;
            var all = LevelEditorPlaceableObject.Collection;
            if (all == null) return null;

            if (all.Count != _indexedCount) Reindex(all);
            if (!_byGuid.TryGetValue(guid, out var hit)) return null;
            if (hit != null) return hit;

            Reindex(all);
            _byGuid.TryGetValue(guid, out hit);
            return hit;
        }

        private static void Reindex(Il2CppSystem.Collections.Generic.List<LevelEditorPlaceableObject> all)
        {
            _byGuid.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                string g = GuidOf(all[i]);
                if (g != null) _byGuid[g] = all[i];
            }
            _indexedCount = all.Count;
        }

        private static string LevelFile(string code) => Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "BettrFG", "Settings", "creative_groups", code + ".txt");

        private static void EnsureLevel()
        {
            string code = CreativeRoundMemory.GetCurrentShareCode();
            if (code == _code) return;

            if (string.IsNullOrEmpty(code))
            {
                if (UnityRoundLoader.InLevelEditor) return;
                _code = null;
                Forget();
                return;
            }

            if (string.IsNullOrEmpty(_code) && _groups.Count > 0)
            {
                _code = code;
                Save();
                Plugin.Log.LogInfo($"level finally has a share code, bound {_groups.Count} group(s) to {code}");
                return;
            }

            _code = code;
            Forget();
            Load();
        }

        private static void Forget()
        {
            _moves.Clear();
            _ourRows.Clear();
            _groups.Clear();
            _index.Clear();
            _byGuid.Clear();
            _indexedCount = -1;
            _nextId = 1;
        }

        private static void Load()
        {
            string path = LevelFile(_code);
            if (!File.Exists(path)) return;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    if (!int.TryParse(parts[0], out int id)) continue;

                    var g = new Group { Id = id };
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out g.Scale);
                    if (g.Scale <= 0f) g.Scale = 1f;

                    string members = parts[parts.Length - 1];
                    if (parts.Length >= 4) g.Name = CleanName(parts[2]);
                    if (parts.Length >= 5) g.Active = parts[3] != "0";
                    if (string.IsNullOrEmpty(g.Name)) g.Name = "Group " + id;

                    foreach (var guid in members.Split(','))
                    {
                        if (guid.Length == 0) continue;
                        g.Guids.Add(guid);
                        _index[guid] = id;
                    }
                    if (g.Guids.Count == 0) continue;

                    _groups[id] = g;
                    if (id >= _nextId) _nextId = id + 1;
                }
                Plugin.Log.LogInfo($"{_groups.Count} group(s) back for level {_code}");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't read the group file for {_code}: {ex.Message}");
            }
        }

        private static void Save()
        {
            if (string.IsNullOrEmpty(_code)) return;
            string path = LevelFile(_code);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (_groups.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.Append("# BettrFG object groups for ").Append(_code).Append('\n');
                foreach (var g in _groups.Values)
                {
                    sb.Append(g.Id).Append('|')
                      .Append(g.Scale.ToString(CultureInfo.InvariantCulture)).Append('|')
                      .Append(g.Name).Append('|')
                      .Append(g.Active ? '1' : '0').Append('|')
                      .Append(string.Join(",", g.Guids)).Append('\n');
                }
                File.WriteAllText(path, sb.ToString());
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"group file for {_code} wouldn't write: {ex.Message}");
            }
        }

        public static int GroupIdOf(LevelEditorPlaceableObject obj)
        {
            EnsureLevel();
            string g = GuidOf(obj);
            if (g == null) return 0;
            return _index.TryGetValue(g, out int id) ? id : 0;
        }

        public static float ScaleOf(int id)
        {
            EnsureLevel();
            return _groups.TryGetValue(id, out var g) ? g.Scale : 1f;
        }

        public static int MemberCount(int id)
        {
            EnsureLevel();
            return _groups.TryGetValue(id, out var g) ? g.Guids.Count : 0;
        }

        public static string NameOf(int id)
        {
            EnsureLevel();
            return _groups.TryGetValue(id, out var g) ? g.Name : null;
        }

        public static string Label(int id)
        {
            EnsureLevel();
            if (!_groups.TryGetValue(id, out var g)) return null;
            return $"{g.Name} ({g.Guids.Count} objects{(g.Active ? "" : ", off")})";
        }

        public static bool IsActive(int id)
        {
            EnsureLevel();
            return _groups.TryGetValue(id, out var g) && g.Active;
        }

        public static void SetActive(int id, bool active)
        {
            EnsureLevel();
            if (!_groups.TryGetValue(id, out var g) || g.Active == active) return;
            g.Active = active;
            Save();
            Plugin.Log.LogInfo($"group {id} ({g.Name}) is {(active ? "back on" : "off, its objects move on their own now")}");
        }

        public static int ActiveGroupIdOf(LevelEditorPlaceableObject obj)
        {
            int id = GroupIdOf(obj);
            return id != 0 && IsActive(id) ? id : 0;
        }

        public static void Rename(int id, string name)
        {
            EnsureLevel();
            if (!_groups.TryGetValue(id, out var g)) return;
            string clean = CleanName(name);
            g.Name = string.IsNullOrEmpty(clean) ? "Group " + id : clean;
            Save();
            Plugin.Log.LogInfo($"group {id} is called {g.Name} now");
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == '|' || c == ',' || c == '\r' || c == '\n') continue;
                sb.Append(c);
                if (sb.Length >= 32) break;
            }
            return sb.ToString().Trim();
        }

        public static int SelectionGroupId()
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return 0;
            int id = 0;
            foreach (var obj in sel)
            {
                int g = GroupIdOf(obj);
                if (g == 0) return 0;
                if (id == 0) id = g;
                else if (id != g) return 0;
            }
            return id;
        }

        public static List<LevelEditorPlaceableObject> Members(int id)
        {
            var list = new List<LevelEditorPlaceableObject>();
            EnsureLevel();
            if (!_groups.TryGetValue(id, out var g)) return list;
            foreach (var guid in g.Guids)
            {
                var o = Resolve(guid);
                if (o != null) list.Add(o);
            }
            return list;
        }

        private static void DetachGuid(string guid)
        {
            if (!_index.TryGetValue(guid, out int id)) return;
            _index.Remove(guid);
            if (!_groups.TryGetValue(id, out var g)) return;
            g.Guids.Remove(guid);
            if (g.Guids.Count == 0) _groups.Remove(id);
        }

        private static readonly List<LevelEditorPlaceableObject> _outlined = new List<LevelEditorPlaceableObject>();

        public static void OnHover(LevelEditorPlaceableObject obj)
        {
            ClearOutline();
            int id = GroupIdOf(obj);
            if (id == 0) return;

            var settings = new OutlineSettings { OutlineColor = OutlineColour, Animate = false };
            var sel = LevelEditorMultiSelectionHandler.Selection();
            foreach (var m in Members(id))
            {
                if (m.Pointer == obj.Pointer) continue;
                if (sel != null && sel.Contains(m)) continue;
                m.SetOutlined(true, settings, false, false);
                _outlined.Add(m);
            }
        }

        public static void ClearOutline()
        {
            if (_outlined.Count == 0) return;
            var settings = new OutlineSettings { OutlineColor = OutlineColour, Animate = false };
            foreach (var m in _outlined)
            {
                if (m == null) continue;
                m.SetOutlined(false, settings, false, false);
            }
            _outlined.Clear();
        }

        private static bool _expanding;

        public static void ExpandAdd(LevelEditorMultiSelectionHandler handler,
            LevelEditorPlaceableObject obj, int options, bool record, bool unselect)
        {
            if (_expanding) return;
            int id = ActiveGroupIdOf(obj);
            if (id == 0) return;

            var sel = LevelEditorMultiSelectionHandler.Selection();
            int added = 0, capped = 0;
            _expanding = true;
            try
            {
                foreach (var m in Members(id))
                {
                    if (m.Pointer == obj.Pointer) continue;
                    if (sel != null && sel.Contains(m)) continue;
                    if (!handler.CanSelectMore) { capped++; continue; }
                    handler.AddToSelection(m, options, record, unselect);
                    added++;
                }
            }
            finally { _expanding = false; }

            if (added > 0) ClearOutline();
            if (capped > 0) Plugin.Log.LogWarning($"group {id} doesn't fit in a multi-selection, {capped} member(s) left out");
            else if (added > 0) Plugin.Log.LogDebug($"group {id} pulled in {added} more");
        }

        public static void ExpandRemove(LevelEditorMultiSelectionHandler handler,
            LevelEditorPlaceableObject obj, int options, bool record, bool unselect)
        {
            if (_expanding) return;
            int id = ActiveGroupIdOf(obj);
            if (id == 0) return;

            var sel = LevelEditorMultiSelectionHandler.Selection();
            _expanding = true;
            try
            {
                foreach (var m in Members(id))
                {
                    if (m.Pointer == obj.Pointer) continue;
                    if (sel != null && !sel.Contains(m)) continue;
                    handler.RemoveFromSelection(m, options, record, unselect);
                }
            }
            finally { _expanding = false; }
        }

        private static LevelEditorPlaceableObject _dragObj;
        private static GameObject _dragHolder;
        private static readonly List<LevelEditorPlaceableObject> _dragRiders = new List<LevelEditorPlaceableObject>();
        private static readonly List<Transform> _dragParents = new List<Transform>();
        private static readonly List<Collider> _dragMuted = new List<Collider>();
        private static readonly List<Vector3> _dragRiderStartPos = new List<Vector3>();
        private static readonly List<Quaternion> _dragRiderStartRot = new List<Quaternion>();
        private static Vector3 _dragHeldStartPos;
        private static Quaternion _dragHeldStartRot;
        private static Vector3 _dragHeldStartWorld;

        private static readonly bool DragTrace = false;
        private static int _dragMissing;
        private static int _dragFrame;

        public static void TickGroupDrag()
        {
            if (LevelEditorMultiSelectionHandler.MultiSelectActive) { EndDrag("multiselect took over"); return; }

            var held = LevelEditorManager.Instance?.GetReticleBase()?.SelectedObject;
            if (held == null)
            {
                if (_dragObj != null && ++_dragMissing < 4) return;
                EndDrag("nothing in hand");
                return;
            }
            _dragMissing = 0;

            int id = ActiveGroupIdOf(held);
            if (id == 0) { EndDrag("no live group on it"); return; }

            var ht = held.transform;
            if (_dragObj == null || _dragObj.Pointer != held.Pointer)
            {
                EndDrag("different object picked up");
                StartDrag(held, ht, id);
                return;
            }
            if (_dragHolder == null) return;

            _dragHolder.transform.SetPositionAndRotation(ht.position, ht.rotation);

            if (!DragTrace) return;
            if (++_dragFrame % 4 != 0) return;
            var r0 = _dragRiders[0];
            if (r0 == null) return;
            var rt = r0.transform;
            Plugin.Log.LogInfo($"drag f{_dragFrame} held {ht.position} rider tf {rt.position} lepo {r0.Position} "
                + $"parent {(rt.parent != null ? rt.parent.name : "none")} snapped {r0.IsSnapped}");
        }

        private static void StartDrag(LevelEditorPlaceableObject held, Transform ht, int id)
        {
            _dragObj = held;
            _dragMissing = 0;
            _dragFrame = 0;
            foreach (var m in Members(id))
                if (m.Pointer != held.Pointer) _dragRiders.Add(m);
            if (_dragRiders.Count == 0) return;

            _dragHolder = new GameObject("BettrFG_GroupDrag");
            _dragHolder.transform.SetPositionAndRotation(ht.position, ht.rotation);
            _dragHeldStartPos = held.Position;
            _dragHeldStartRot = ht.rotation;
            _dragHeldStartWorld = ht.position;

            foreach (var m in _dragRiders)
            {
                var t = m.transform;
                _dragParents.Add(t.parent);
                _dragRiderStartPos.Add(t.position);
                _dragRiderStartRot.Add(t.rotation);
                t.SetParent(_dragHolder.transform, true);

                var cols = m.UnityColliders;
                if (cols == null) continue;
                for (int i = 0; i < cols.Count; i++)
                {
                    var c = cols[i];
                    if (c == null || !c.enabled) continue;
                    c.enabled = false;
                    _dragMuted.Add(c);
                }
            }
            Plugin.Log.LogInfo($"group {id} picked up, {_dragRiders.Count} riding along, {_dragMuted.Count} collider(s) out of the reticle's way");
        }

        private static void EndDrag(string why)
        {
            var held = _dragObj;
            _dragObj = null;
            _dragMissing = 0;
            if (_dragRiders.Count == 0 && _dragHolder == null) return;

            bool reverted = held != null
                && (held.Position - _dragHeldStartPos).sqrMagnitude < 0.0001f
                && Quaternion.Angle(held.transform.rotation, _dragHeldStartRot) < 0.05f;

            Plugin.Log.LogInfo($"group drag done ({why}), putting {_dragRiders.Count} back"
                + (reverted ? " where they started, the move got cancelled" : ""));

            GroupMove move = null;
            if (!reverted && held != null)
            {
                var hat = held.transform;
                move = new GroupMove
                {
                    Anchor = held,
                    BeforePos = _dragHeldStartWorld,
                    BeforeRot = _dragHeldStartRot,
                    AfterPos = hat.position,
                    AfterRot = hat.rotation
                };
            }

            for (int i = 0; i < _dragRiders.Count; i++)
            {
                var m = _dragRiders[i];
                if (m == null) continue;
                var t = m.transform;
                t.SetParent(i < _dragParents.Count ? _dragParents[i] : null, true);
                if (reverted && i < _dragRiderStartPos.Count)
                    t.SetPositionAndRotation(_dragRiderStartPos[i], _dragRiderStartRot[i]);
                m.Position = t.position;
                var rd = m.RotationData;
                if (rd != null) rd.CurrentRotation = t.rotation.eulerAngles;

                if (move != null)
                {
                    move.Riders.Add(m);
                    move.RiderBeforePos.Add(_dragRiderStartPos[i]);
                    move.RiderBeforeRot.Add(_dragRiderStartRot[i]);
                    move.RiderAfterPos.Add(t.position);
                    move.RiderAfterRot.Add(t.rotation);
                }
            }

            if (move != null && move.Riders.Count > 0)
            {
                _moves.Add(move);
                if (_moves.Count > MoveMemory) _moves.RemoveAt(0);
            }

            foreach (var c in _dragMuted)
                if (c != null) c.enabled = true;

            _dragMuted.Clear();
            _dragRiders.Clear();
            _dragParents.Clear();
            _dragRiderStartPos.Clear();
            _dragRiderStartRot.Clear();
            if (_dragHolder != null) { UnityEngine.Object.Destroy(_dragHolder); _dragHolder = null; }
        }

        private sealed class GroupMove
        {
            public LevelEditorPlaceableObject Anchor;
            public Vector3 BeforePos, AfterPos;
            public Quaternion BeforeRot, AfterRot;
            public readonly List<LevelEditorPlaceableObject> Riders = new List<LevelEditorPlaceableObject>();
            public readonly List<Vector3> RiderBeforePos = new List<Vector3>();
            public readonly List<Quaternion> RiderBeforeRot = new List<Quaternion>();
            public readonly List<Vector3> RiderAfterPos = new List<Vector3>();
            public readonly List<Quaternion> RiderAfterRot = new List<Quaternion>();
        }

        private const int MoveMemory = 64;
        private static readonly List<GroupMove> _moves = new List<GroupMove>();

        private static bool SittingAt(Transform t, Vector3 pos, Quaternion rot) =>
            (t.position - pos).sqrMagnitude < 0.0001f && Quaternion.Angle(t.rotation, rot) < 0.05f;

        public static void OnUndoRedoSettled()
        {
            if (_moves.Count == 0) return;

            int replayed = 0, back = 0;
            for (int i = _moves.Count - 1; i >= 0; i--)
            {
                var mv = _moves[i];
                if (mv.Anchor == null) { _moves.RemoveAt(i); continue; }

                var at = mv.Anchor.transform;
                bool undone = SittingAt(at, mv.BeforePos, mv.BeforeRot);
                bool redone = SittingAt(at, mv.AfterPos, mv.AfterRot);
                if (undone == redone) continue;

                var pos = undone ? mv.RiderBeforePos : mv.RiderAfterPos;
                var rot = undone ? mv.RiderBeforeRot : mv.RiderAfterRot;
                bool moved = false;

                for (int r = 0; r < mv.Riders.Count; r++)
                {
                    var m = mv.Riders[r];
                    if (m == null) continue;
                    var t = m.transform;
                    if (SittingAt(t, pos[r], rot[r])) continue;
                    t.SetPositionAndRotation(pos[r], rot[r]);
                    m.Position = pos[r];
                    var rd = m.RotationData;
                    if (rd != null) rd.CurrentRotation = rot[r].eulerAngles;
                    moved = true;
                }

                if (!moved) continue;
                replayed++;
                if (undone) back++;
            }

            if (replayed > 0)
                Plugin.Log.LogInfo($"undo/redo dragged {replayed} group move(s) along, {back} of them backwards");
            else
                Plugin.Log.LogInfo($"undo/redo settled, none of the {_moves.Count} group move(s) on record matched where their anchor landed");
        }

        public static void DecorateHistoryName(LevelEditorPlaceableObject obj, ref string result)
        {
            int id = ActiveGroupIdOf(obj);
            if (id == 0) return;
            string name = NameOf(id);
            if (!string.IsNullOrEmpty(name)) result = name;
        }

        private static LevelEditorObjectInfoViewModel _infoVm;
        private static readonly Il2CppStringArray _infoProps =
            new Il2CppStringArray(new[] { "ObjectName", "ObjectsSelectedText" });

        public static void DecorateName(LevelEditorObjectInfoViewModel vm, ref string result)
        {
            _infoVm = vm;

            int id = GroupIdOf(vm._lepo);
            if (id == 0 && LevelEditorMultiSelectionHandler.MultiSelectActive) id = SelectionGroupId();
            if (id == 0) return;

            string label = Label(id);
            if (label != null) result = label;
        }

        public static void DecorateCount(LevelEditorObjectInfoViewModel vm, ref string result)
        {
            if (LevelEditorMultiSelectionHandler.MultiSelectActive) return;

            int id = GroupIdOf(vm._lepo);
            if (id == 0) return;
            result = MemberCount(id) + " in group";
        }

        private static void RefreshInfoPanel()
        {
            if (_infoVm == null) return;
            _infoVm.RaisePropertiesChangedDeferred(_infoProps);
        }

        private static int _paramGroup;
        private static Il2CppSystem.Action<float> _scaleCallback;
        private static Il2CppSystem.Action<bool> _activeCallback;
        private static ParameterChangedIndex _pickCallback;

        private static LevelEditorPlaceableObject _pickTarget;
        private static readonly List<int> _pickIds = new List<int>();
        private static int _pickNewGroup;

        private static readonly HashSet<System.IntPtr> _ourRows = new HashSet<System.IntPtr>();

        public static void AddGroupRows(LevelEditorPlaceableObject target)
        {
            _paramGroup = 0;
            _pickTarget = null;
            _pickIds.Clear();

            if (target == null) { Plugin.Log.LogDebug("param menu built with no target placeable yet, no group rows"); return; }

            try
            {
                var existing = target.CustomParameters;
                if (existing != null && _ourRows.Count > 0)
                {
                    for (int i = existing.Count - 1; i >= 0; i--)
                    {
                        var e = existing[i].ParameterEntry;
                        if (e == null || !_ourRows.Remove(e.Pointer)) continue;
                        existing.RemoveAt(i);
                    }
                }

                EnsureLevel();
                if (GuidOf(target) == null) return;

                _pickTarget = target;
                int id = GroupIdOf(target);
                _paramGroup = id;

                if (_activeCallback == null)
                    _activeCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(
                        new System.Action<bool>(OnActiveParamChanged));
                if (_scaleCallback == null)
                    _scaleCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(
                        new System.Action<float>(OnScaleParamChanged));
                if (_pickCallback == null)
                    _pickCallback = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(
                        new System.Action<int>(OnGroupPicked));

                bool active = id != 0 && IsActive(id);

                if (id != 0)
                {
                    var scale = ParameterUtils.CreateFloatEntry(ScaleParamName, ScaleOf(id), 0.1f, 10f, 0.1f,
                        ParameterWrapMode.NoWrap, _scaleCallback, "{0}", "F2", null, null, false, 1f, null, !active, false);
                    if (scale != null) { target.AddParameter(scale, 0); _ourRows.Add(scale.Pointer); }

                    var toggle = ParameterUtils.CreateBoolEntry(ActiveParamName, active,
                        ParameterWrapMode.NoWrap, _activeCallback, null, null, false, 1f, null, false, false);
                    if (toggle != null) { target.AddParameter(toggle, 0); _ourRows.Add(toggle.Pointer); }
                }

                var items = new Il2CppSystem.Collections.ArrayList();
                int selected = 0;
                _pickIds.Add(0);
                items.Add((Il2CppSystem.String)"None");
                foreach (var g in _groups.Values)
                {
                    if (g.Id == id) selected = _pickIds.Count;
                    _pickIds.Add(g.Id);
                    items.Add((Il2CppSystem.String)PickLabel(g));
                }
                _pickIds.Add(-1);
                items.Add((Il2CppSystem.String)"New group");

                var pick = ParameterUtils.CreateStringEntry(PickParamName, selected,
                    items.Cast<Il2CppSystem.Collections.ICollection>(), ParameterWrapMode.NoWrap,
                    _pickCallback, null, null, false, 1f, null, false, false);
                if (pick != null) { target.AddParameter(pick, 0); _ourRows.Add(pick.Pointer); }

                Plugin.Log.LogInfo($"group rows on {target.name}, in group {id}, picker has {_pickIds.Count} option(s)"
                    + (id != 0 && !active ? ", group is switched off" : ""));
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"group rows didn't make it into the param menu: {ex.Message}");
            }
        }

        private static string PickLabel(Group g) => $"{g.Name} ({g.Guids.Count})";

        public static bool HasParamGroup => _groups.ContainsKey(_paramGroup);

        public static bool RenameParamGroup(string name, out string display)
        {
            display = null;
            if (!_groups.TryGetValue(_paramGroup, out var g)) return false;
            Rename(g.Id, name);
            display = PickLabel(g);
            RefreshInfoPanel();
            return true;
        }

        private static void OnGroupPicked(int index)
        {
            var target = _pickTarget;
            if (target == null || index < 0 || index >= _pickIds.Count) return;

            int want = _pickIds[index];
            if (want == -1)
            {
                if (_pickNewGroup == 0 || !_groups.ContainsKey(_pickNewGroup))
                {
                    var fresh = new Group { Id = _nextId++ };
                    fresh.Name = "Group " + fresh.Id;
                    _groups[fresh.Id] = fresh;
                    _pickNewGroup = fresh.Id;
                }
                want = _pickNewGroup;
            }
            if (want == _paramGroup) return;

            var guids = new List<string>();
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel != null && sel.Count > 0 && sel.Contains(target))
            {
                foreach (var o in sel)
                {
                    string g = GuidOf(o);
                    if (g != null && !guids.Contains(g)) guids.Add(g);
                }
            }
            else guids.Add(GuidOf(target));

            _groups.TryGetValue(want, out var dest);
            foreach (var g in guids) DetachGuid(g);

            if (dest != null)
            {
                foreach (var g in guids) { dest.Guids.Add(g); _index[g] = dest.Id; }
                _groups[dest.Id] = dest;
            }

            _paramGroup = dest != null ? dest.Id : 0;
            OnHover(target);
            RefreshInfoPanel();
            Save();

            if (dest != null) Plugin.Log.LogInfo($"{guids.Count} object(s) into {dest.Name}, {dest.Guids.Count} in there now");
            else Plugin.Log.LogInfo($"{guids.Count} object(s) out of their group");
        }

        private static void OnActiveParamChanged(bool on)
        {
            int id = _paramGroup;
            if (id == 0) return;
            SetActive(id, on);
            if (!on) EndDrag("group switched off");
        }

        private static void OnScaleParamChanged(float value)
        {
            int id = _paramGroup;
            if (id == 0 || value <= 0f || !IsActive(id)) return;

            float current = ScaleOf(id);
            if (current <= 0f) current = 1f;
            float rel = value / current;
            if (Mathf.Abs(rel - 1f) < 0.0001f) return;

            var members = Members(id);
            if (members.Count == 0) { Plugin.Log.LogWarning($"group {id} scale asked for but none of its objects are in the level"); return; }

            Vector3 pivot = Vector3.zero;
            foreach (var m in members) pivot += m.Position;
            pivot /= members.Count;

            foreach (var m in members)
            {
                var sp = m._levelEditorScaleParameter;
                if (sp != null) sp.SetScale(sp.CurrentScale * rel, true);
                m.Position = pivot + (m.Position - pivot) * rel;
            }

            if (_groups.TryGetValue(id, out var g)) { g.Scale = value; Save(); }
            Plugin.Log.LogInfo($"group {id} x{rel:0.###} (now {value:0.##}), {members.Count} objects around {pivot}");
        }
    }
}
