using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using FG.Common;
using LevelEditor;
using TMPro;
using UnityEngine;

namespace BetterFG.Features.CreativeText
{
    public static class CreativeText
    {
        public const int MinQuality = 20;
        public const int MaxQuality = 300;
        public const int MaxObjects = 2000;

        public const int MinHeight = 4;
        public const int MaxHeight = 120;

        private const int RasterLayer = 31;
        private static int _squareShape = -1;
        private static bool _probedScale;

        private const string KEY_TEXT = "creative.text.value";
        private const string KEY_FONT = "creative.text.font";
        private const string KEY_STICKER = "creative.text.sticker";
        private const string KEY_QUALITY = "creative.text.quality";
        private const string KEY_HEIGHT = "creative.text.height";

        public static string Text
        {
            get => SettingsService.Get(KEY_TEXT, "TEXT");
            set => SettingsService.Set(KEY_TEXT, value);
        }

        public static string FontChoice
        {
            get => SettingsService.Get(KEY_FONT, "");
            set => SettingsService.Set(KEY_FONT, value);
        }

        public static bool Sticker
        {
            get => SettingsService.Get(KEY_STICKER, "true") != "false";
            set => SettingsService.Set(KEY_STICKER, value ? "true" : "false");
        }

        public static int Quality
        {
            get
            {
                int.TryParse(SettingsService.Get(KEY_QUALITY, "40"), out int q);
                return Mathf.Clamp(q, MinQuality, MaxQuality);
            }
            set => SettingsService.Set(KEY_QUALITY, Mathf.Clamp(value, MinQuality, MaxQuality).ToString(CultureInfo.InvariantCulture));
        }

        public static int Height
        {
            get
            {
                int.TryParse(SettingsService.Get(KEY_HEIGHT, "12"), out int h);
                return Mathf.Clamp(h, MinHeight, MaxHeight);
            }
            set => SettingsService.Set(KEY_HEIGHT, Mathf.Clamp(value, MinHeight, MaxHeight).ToString(CultureInfo.InvariantCulture));
        }

        private static List<SystemFont> _fontChoices;

        public static List<SystemFont> FontOptions()
        {
            if (_fontChoices != null) return _fontChoices;

            var list = new List<SystemFont>();
            foreach (var name in FontReplacementService.GetAllFontAssetNames())
                list.Add(new SystemFont { Path = name, Display = name, Family = name });
            list.AddRange(SystemFonts.All());

            _fontChoices = list;
            return list;
        }

        public static int FontIndex()
        {
            string choice = FontChoice;
            var options = FontOptions();
            for (int i = 0; i < options.Count; i++)
                if (string.Equals(options[i].Path, choice, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        public static string FontLabel(string choice)
        {
            if (string.IsNullOrEmpty(choice)) return "(default)";
            if (!IsFontFile(choice)) return choice;
            foreach (var font in SystemFonts.All())
                if (string.Equals(font.Path, choice, StringComparison.OrdinalIgnoreCase)) return font.Display;
            return Path.GetFileNameWithoutExtension(choice);
        }

        private static bool IsFontFile(string choice) =>
            !string.IsNullOrEmpty(choice) && (choice.IndexOf('\\') >= 0 || choice.IndexOf('/') >= 0);

        private static TMP_FontAsset ResolveFont()
        {
            string choice = FontChoice;
            if (IsFontFile(choice))
            {
                var built = FontReplacementService.BuildPreview(new FontOverride { fontPath = choice });
                if (built != null) return built;
                Plugin.Log.LogWarning($"{Path.GetFileName(choice)} wouldn't build into a font asset");
            }
            else
            {
                var named = FontReplacementService.GetFontAssetByName(choice);
                if (named != null) return named;
            }

            var all = FontReplacementService.GetAllFontAssetNames();
            for (int i = 0; i < all.Count; i++)
            {
                var fa = FontReplacementService.GetFontAssetByName(all[i]);
                if (fa != null) return fa;
            }
            return null;
        }

        private const string BlockPod = "POD_Rectangle_Vanilla";
        private const string StickerPod = "POD_Decal_1_Common";

        private static GameObject _stickerPrefab;
        private static GameObject _blockPrefab;

        public static GameObject TemplatePrefab()
        {
            bool sticker = Sticker;
            var cached = sticker ? _stickerPrefab : _blockPrefab;
            if (cached != null) return cached;

            var pods = Resources.FindObjectsOfTypeAll<ScriptableObjects.PlaceableObjectData>();
            if (pods == null || pods.Length == 0)
            {
                Plugin.Log.LogWarning("no placeable object data loaded, can't pick a text template");
                return null;
            }

            string want = sticker ? StickerPod : BlockPod;
            for (int i = 0; i < pods.Length; i++)
            {
                var pod = pods[i];
                if (pod == null || pod.name != want) continue;
                var prefab = pod.GetDefaultObject();
                if (prefab == null) continue;

                if (sticker) _stickerPrefab = prefab; else _blockPrefab = prefab;
                Plugin.Log.LogInfo($"text {(sticker ? "stickers" : "blocks")} are {prefab.name}");
                return prefab;
            }

            Plugin.Log.LogWarning($"{want} isn't in this theme's object list, nothing to build text out of");
            return null;
        }

        private struct Piece
        {
            public Vector2 Centre;
            public Vector2 Size;
            public float Angle;
        }

        private sealed class TextPlan
        {
            public string Key;
            public string Problem;
            public int Cols;
            public int Rows;
            public bool Squares;
            public readonly List<Piece> Pieces = new List<Piece>();
            public bool Capped;
            public float Coverage;
        }

        private static TextPlan _plan;

        private static string PlanKey() => $"{Text}|{FontChoice}|{Quality}|{(Sticker ? "s" : "f")}";

        private static TextPlan CurrentPlan()
        {
            string key = PlanKey();
            if (_plan != null && _plan.Key == key) return _plan;

            var plan = new TextPlan { Key = key };
            _plan = plan;

            string text = Text;
            if (string.IsNullOrEmpty(text)) { plan.Problem = "no text"; return plan; }

            var font = ResolveFont();
            if (font == null) { plan.Problem = "no font"; return plan; }

            var prefab = TemplatePrefab();
            if (prefab == null) { plan.Problem = Sticker ? "no sticker object" : "no block object"; return plan; }
            plan.Squares = prefab.GetComponentInChildren<LevelEditorScaleParameter>(true) == null;

            if (!Rasterise(font, text, Quality, out bool[] cells, out int cols, out int rows))
            {
                plan.Problem = "nothing rendered";
                return plan;
            }

            plan.Cols = cols;
            plan.Rows = rows;
            Decompose(cells, cols, rows, plan);
            if (plan.Pieces.Count == 0) plan.Problem = "came out blank";
            return plan;
        }

        public static int PlannedObjects(out string note)
        {
            var plan = CurrentPlan();
            note = plan.Problem;
            if (plan.Capped) note = $"over {MaxObjects}";
            return plan.Pieces.Count;
        }

        public static void Invalidate() => _plan = null;

        private static float _smallestPiece = -1f;

        public static int UsableQuality()
        {
            if (_smallestPiece <= 0f) return 0;
            return Mathf.Max(1, Mathf.FloorToInt(Height / _smallestPiece));
        }

        public static int Place(out string status) => Place(out status, out _);

        public static int Place(out string status, out LevelEditorPlaceableObject middle)
        {
            status = "";
            middle = null;
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) { status = "not in the editor"; return 0; }

            var plan = CurrentPlan();
            if (plan.Problem != null) { status = plan.Problem; return 0; }
            if (plan.Capped)
            {
                status = $"{plan.Pieces.Count}+ objects, over the {MaxObjects} cap";
                return 0;
            }

            var prefab = TemplatePrefab();
            if (prefab == null)
            {
                status = Sticker ? "no sticker object found" : "no block object found";
                return 0;
            }

            var probe = UnityEngine.Object.Instantiate(prefab);
            probe.transform.SetPositionAndRotation(new Vector3(0f, 9000f, 0f), Quaternion.identity);
            var probeLepo = probe.GetComponent<LevelEditorPlaceableObject>();
            var vectorScales = probe.GetComponentsInChildren<LevelEditorScaleParameter>(true);
            var decalScales = probe.GetComponentsInChildren<LevelEditorDecalScaleParameter>(true);
            int vectorCount = vectorScales != null ? vectorScales.Length : 0;
            int decalCount = decalScales != null ? decalScales.Length : 0;
            bool canStretch = !plan.Squares && vectorCount > 0;
            bool hasDecalScale = decalCount > 0;

            // the object's real footprint is what its colliders say, not its renderers: prefabs carry
            // ghost/outline/invisible-variant meshes that blow the renderer bounds up, and dividing by an
            // inflated size is what left the long axis short.
            Vector3 size = Vector3.zero;
            var minScale = Vector3.zero;
            bool measured = false;
            if (probeLepo != null)
            {
                probeLepo.FindPlacementCollidersAndAddBaseColliders();
                probeLepo.BuildColliderBounds();
                var bounds = probeLepo.ColliderBounds;
                if (bounds.size.sqrMagnitude > 0.0001f)
                {
                    size = bounds.size;
                    measured = true;
                }

                var probeScale = probeLepo._levelEditorScaleParameter;
                var probeValues = probeScale != null ? probeScale._values : null;
                if (probeValues != null) minScale = probeValues.GetMinimunScale();
            }

            Vector3 rendererSize = Vector3.zero;
            var rends = probe.GetComponentsInChildren<Renderer>(false);
            if (rends != null)
            {
                Bounds b = default;
                bool any = false;
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null || !r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (any) rendererSize = b.size;
            }
            if (!measured && rendererSize.sqrMagnitude > 0.0001f)
            {
                size = rendererSize;
                measured = true;
            }

            UnityEngine.Object.Destroy(probe);

            if (probeLepo == null)
            {
                status = "template isn't a placeable";
                Plugin.Log.LogWarning($"{prefab.name} has no LevelEditorPlaceableObject on it, nothing to place");
                return 0;
            }
            if (!measured || size.x + size.y + size.z <= 0.0001f)
            {
                status = "couldn't measure the template";
                Plugin.Log.LogWarning($"{prefab.name} has no renderer bounds to go on, giving up on the text");
                return 0;
            }

            int normal = Sticker ? (size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2)) : 1;
            int right = normal == 0 ? 2 : 0;
            int up = normal == 1 ? 2 : 1;

            float height = Height;
            float cell = height / plan.Rows;
            float smallestPiece = Mathf.Max(size[right] * minScale[right], size[up] * minScale[up]);
            _smallestPiece = smallestPiece;
            if (smallestPiece > cell + 0.0001f)
                Plugin.Log.LogWarning($"{prefab.name} won't go under {smallestPiece:0.##} a piece but the cells are {cell:0.###}, "
                    + $"so {plan.Rows} rows won't fit in {height} units and the strokes will run fat. "
                    + $"{Mathf.FloorToInt(height / smallestPiece)} rows is all this size can actually draw");
            var rightAxis = Vector3.zero; rightAxis[right] = 1f;
            var upAxis = Vector3.zero; upAxis[up] = 1f;

            var reticle = mgr.GetReticleGameObject();
            var origin = reticle != null ? reticle.transform.position : Vector3.zero;

            var spinAxis = Vector3.Cross(rightAxis, upAxis);
            var placed = new List<LevelEditorPlaceableObject>();
            bool ranOut = false;
            int clamped = 0;

            float flat = Mathf.Max(size[right], size[up]);

            for (int i = 0; i < plan.Pieces.Count && !ranOut; i++)
            {
                var piece = plan.Pieces[i];
                float cx = piece.Centre.x - plan.Cols * 0.5f;
                float cy = piece.Centre.y - plan.Rows * 0.5f;
                var anchor = origin + rightAxis * (cx * cell) + upAxis * (cy * cell);
                var rot = Quaternion.AngleAxis(piece.Angle, spinAxis);
                var along = rot * rightAxis;

                float wantLong = cell * piece.Size.x;
                float wantThick = cell * piece.Size.y;

                var go = UnityEngine.Object.Instantiate(prefab);
                go.transform.SetPositionAndRotation(anchor, rot);
                var lepo = go.GetComponent<LevelEditorPlaceableObject>();

                if (mgr.CanAffordObjectCostAndStock(lepo, false, true) != LevelEditorCostManager.ObjectAffordability.CanAfford)
                {
                    UnityEngine.Object.Destroy(go);
                    ranOut = true;
                    break;
                }

                lepo.RegenerateGuid();
                mgr.RegisterObject(lepo, true, false, false);

                var rd = lepo.RotationData;
                if (rd != null) rd.CurrentRotation = rot.eulerAngles;

                var range = BatchTargets.GetRangeParam(lepo);
                if (range != null) range.SetRangeIndex(SquareShape(range));

                if (canStretch)
                {
                    var sp = lepo._levelEditorScaleParameter;
                    if (sp != null)
                    {
                        var before = sp.CurrentScale;
                        var want = before;
                        want[right] *= wantLong / size[right];
                        want[up] *= wantThick / size[up];

                        var values = sp._values;
                        if (values != null)
                        {
                            // the scale param moves in fixed steps, so anything in between gets snapped
                            // on the way back out of a save. land on a step here instead.
                            float step = values.ParameterScaleToUnitsMultiplier;
                            if (step > 0.0001f)
                            {
                                var lo = values.GetMinimunScale();
                                want[right] = Mathf.Max(lo[right], Mathf.Round(want[right] / step) * step);
                                want[up] = Mathf.Max(lo[up], Mathf.Round(want[up] / step) * step);
                            }
                        }
                        sp.SetScale(want, true);

                        if (!_probedScale)
                        {
                            _probedScale = true;
                            Plugin.Log.LogInfo($"scale probe on {prefab.name}: collider size {size}, renderer size {rendererSize}, "
                                + $"want {wantLong:0.##} long, before {before} -> asked {want} -> param {sp.CurrentScale}, "
                                + $"transform {go.transform.localScale}, "
                                + (values == null
                                    ? "_values is null"
                                    : $"values {values.GetCurrentScale()}, min {values.GetMinimunScale()} max {values.GetMaximumScale()}, units x{values.ParameterScaleToUnitsMultiplier}"));
                        }
                    }
                }
                else if (hasDecalScale)
                {
                    var dp = BatchTargets.GetDecalScaleParam(lepo);
                    if (dp != null)
                    {
                        float want = dp.CurrentVal * wantLong / flat;
                        float got = Mathf.Clamp(want, dp._minVal, dp._maxVal);
                        if (Mathf.Abs(want - got) > 0.0001f) clamped++;
                        dp.SetValue(got);
                    }
                }

                lepo.Position = anchor;

                // the hover raycast goes collider -> LookupFromCollider, so a clone that never lands
                // in that static map can't be picked no matter where you point at it. CorrectColliders
                // is the game's own catch-up after a scale change, before the bounds get cached.
                lepo.FindPlacementCollidersAndAddBaseColliders();
                LevelEditorPlaceableObject.RegisterAllCollidersForPlaceable(lepo);
                lepo.CorrectColliders();
                lepo.BuildColliderBounds();

                placed.Add(lepo);
            }

            if (placed.Count == 0)
            {
                status = ranOut ? "out of budget" : "nothing placed";
                return 0;
            }

            var centre = Vector3.zero;
            foreach (var p in placed) centre += p.Position;
            centre /= placed.Count;
            float nearest = float.MaxValue;
            foreach (var p in placed)
            {
                float d = (p.Position - centre).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d;
                middle = p;
            }

            int group = BetterFG.Features.CreativeGroups.CreativeGroups.CreateGroup(Text, placed);
            status = $"{placed.Count} placed" + (ranOut ? ", budget ran out" : "");

            Plugin.Log.LogInfo($"'{Text}' at quality {plan.Rows} -> {placed.Count} object(s) from {plan.Pieces.Count} "
                + $"{(plan.Squares ? "squares" : "strokes")} of {prefab.name}, {plan.Coverage * 100f:0.#}% of the glyphs covered, "
                + $"cell {cell:0.###}, axes {right}/{up} spin {spinAxis}, group {group}"
                + (clamped > 0 ? $", {clamped} came back shorter than asked" : "")
                + (ranOut ? ", budget ran dry partway" : ""));
            return placed.Count;
        }

        private const int AngleSteps = 12;
        private const int SpanSamples = 5;
        private const float LeaveUncovered = 0.005f;
        private const float Bleed = 1f;

        private static int SquareShape(LevelEditorRangeParameter range)
        {
            if (_squareShape >= 0) return _squareShape;
            var names = range.RangeSettingNames;
            for (int i = 0; names != null && i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name)) continue;
                name = name.ToLowerInvariant();
                if (name.Contains("square") || name.Contains("cube")) return _squareShape = i;
            }
            return _squareShape = 0;
        }

        private static void Decompose(bool[] cells, int cols, int rows, TextPlan plan)
        {
            int n = cells.Length;
            var dist = new float[n];
            int ink = 0;
            for (int i = 0; i < n; i++)
            {
                dist[i] = cells[i] ? 1e9f : 0f;
                if (cells[i]) ink++;
            }
            if (ink == 0) return;

            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int i = y * cols + x;
                    if (dist[i] == 0f) continue;
                    float d = dist[i];
                    if (x > 0) d = Mathf.Min(d, dist[i - 1] + 1f);
                    if (y > 0) d = Mathf.Min(d, dist[i - cols] + 1f);
                    if (x > 0 && y > 0) d = Mathf.Min(d, dist[i - cols - 1] + 1.414f);
                    if (x + 1 < cols && y > 0) d = Mathf.Min(d, dist[i - cols + 1] + 1.414f);
                    dist[i] = d;
                }
            for (int y = rows - 1; y >= 0; y--)
                for (int x = cols - 1; x >= 0; x--)
                {
                    int i = y * cols + x;
                    if (dist[i] == 0f) continue;
                    float d = dist[i];
                    if (x + 1 < cols) d = Mathf.Min(d, dist[i + 1] + 1f);
                    if (y + 1 < rows) d = Mathf.Min(d, dist[i + cols] + 1f);
                    if (x + 1 < cols && y + 1 < rows) d = Mathf.Min(d, dist[i + cols + 1] + 1.414f);
                    if (x > 0 && y + 1 < rows) d = Mathf.Min(d, dist[i + cols - 1] + 1.414f);
                    dist[i] = d;
                }

            var seeds = new int[ink];
            var keys = new float[ink];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (!cells[i]) continue;
                seeds[k] = i;
                keys[k] = -dist[i];
                k++;
            }
            Array.Sort(keys, seeds);

            var covered = new bool[n];
            int left = ink;
            float leave = LeaveUncovered * MinQuality / Mathf.Max(MinQuality, rows);
            int quit = Mathf.Max(1, Mathf.RoundToInt(ink * leave));

            for (int s = 0; s < seeds.Length && left > quit; s++)
            {
                int seed = seeds[s];
                if (covered[seed]) continue;

                var at = new Vector2(seed % cols + 0.5f, seed / cols + 0.5f);

                float bestArea = 0f, bestAngle = 0f, bestLen = 1f, bestThick = 1f;
                var bestMid = at;
                for (int a = 0; a < AngleSteps; a++)
                {
                    float ang = a * 180f / AngleSteps;
                    float rad = ang * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    var perp = new Vector2(-dir.y, dir.x);

                    float outward = Span(cells, cols, rows, at, perp);
                    float inward = Span(cells, cols, rows, at, -perp);
                    float thick = outward + inward;
                    var spine = at + perp * ((outward - inward) * 0.5f);

                    float fwd = Reach(cells, cols, rows, spine, dir, perp, thick * 0.5f);
                    float back = Reach(cells, cols, rows, spine, -dir, perp, thick * 0.5f);
                    float len = Mathf.Max(1f, fwd + back);
                    float area = len * thick;
                    if (area <= bestArea) continue;

                    bestArea = area;
                    bestAngle = ang;
                    bestLen = len;
                    bestThick = thick;
                    bestMid = spine + dir * ((fwd - back) * 0.5f);
                }

                float radBest = bestAngle * Mathf.Deg2Rad;
                var bestDir = new Vector2(Mathf.Cos(radBest), Mathf.Sin(radBest));
                var size2 = new Vector2(bestLen + Bleed, bestThick + Bleed);

                if (plan.Squares)
                {
                    float side = bestThick + Bleed;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(bestLen / side));
                    float startOff = -(steps - 1) * 0.5f * side;
                    for (int i = 0; i < steps; i++)
                    {
                        var c = bestMid + bestDir * (startOff + i * side);
                        plan.Pieces.Add(new Piece { Centre = c, Size = new Vector2(side, side), Angle = 0f });
                        if (plan.Pieces.Count >= MaxObjects) { plan.Capped = true; break; }
                    }
                }
                else
                {
                    plan.Pieces.Add(new Piece { Centre = bestMid, Size = size2, Angle = bestAngle });
                }

                left -= Claim(cells, covered, cols, rows, bestMid, size2, bestAngle);
                if (plan.Pieces.Count >= MaxObjects) { plan.Capped = true; break; }
            }

            plan.Coverage = ink > 0 ? 1f - (float)left / ink : 0f;
        }

        private static float Span(bool[] cells, int cols, int rows, Vector2 from, Vector2 dir)
        {
            float reach = 0f;
            int limit = (cols + rows) * 2;
            for (int i = 1; i <= limit; i++)
            {
                float t = i * 0.5f;
                var p = from + dir * t;
                int px = Mathf.FloorToInt(p.x);
                int py = Mathf.FloorToInt(p.y);
                if (px < 0 || py < 0 || px >= cols || py >= rows) break;
                if (!cells[py * cols + px]) break;
                reach = t;
            }
            return reach + 0.5f;
        }

        private static float Reach(bool[] cells, int cols, int rows, Vector2 start,
            Vector2 dir, Vector2 perp, float half)
        {
            int limit = cols + rows;
            float reach = 0f;

            for (int step = 1; step <= limit; step++)
            {
                var at = start + dir * step;
                int cx = Mathf.FloorToInt(at.x);
                int cy = Mathf.FloorToInt(at.y);
                if (cx < 0 || cy < 0 || cx >= cols || cy >= rows) break;
                if (!cells[cy * cols + cx]) break;

                int hits = 0;
                for (int j = 0; j < SpanSamples; j++)
                {
                    float t = (j / (SpanSamples - 1f) - 0.5f) * 2f;
                    var p = at + perp * (t * half * 0.8f);
                    int px = Mathf.FloorToInt(p.x);
                    int py = Mathf.FloorToInt(p.y);
                    if (px < 0 || py < 0 || px >= cols || py >= rows) continue;
                    if (cells[py * cols + px]) hits++;
                }
                if (hits < SpanSamples - 1) break;
                reach = step;
            }
            return reach;
        }

        private static int Claim(bool[] cells, bool[] covered, int cols, int rows,
            Vector2 centre, Vector2 size, float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            var perp = new Vector2(-dir.y, dir.x);
            float halfLen = size.x * 0.5f;
            float halfThick = size.y * 0.5f;
            float span = halfLen + halfThick + 1f;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(centre.x - span));
            int x1 = Mathf.Min(cols - 1, Mathf.CeilToInt(centre.x + span));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(centre.y - span));
            int y1 = Mathf.Min(rows - 1, Mathf.CeilToInt(centre.y + span));
            int claimed = 0;

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * cols + x;
                    if (!cells[i] || covered[i]) continue;
                    var d = new Vector2(x + 0.5f, y + 0.5f) - centre;
                    if (Mathf.Abs(Vector2.Dot(d, dir)) > halfLen) continue;
                    if (Mathf.Abs(Vector2.Dot(d, perp)) > halfThick) continue;
                    covered[i] = true;
                    claimed++;
                }
            return claimed;
        }

        private static bool Rasterise(TMP_FontAsset font, string text, int quality, out bool[] cells, out int w, out int h)
        {
            cells = null;
            w = 0;
            h = 0;

            var textGo = new GameObject("BettrFG_TextRaster");
            textGo.layer = RasterLayer;
            textGo.transform.position = new Vector3(0f, 8000f, 0f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.fontSize = 48f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.richText = false;
            tmp.rectTransform.sizeDelta = new Vector2(8000f, 800f);
            tmp.text = text;
            tmp.ForceMeshUpdate(true, true);

            var bounds = tmp.textBounds;
            if (bounds.size.x <= 0.0001f || bounds.size.y <= 0.0001f)
            {
                Plugin.Log.LogWarning($"'{text}' measured {bounds.size} in {font.name}, no glyphs to raster");
                UnityEngine.Object.Destroy(textGo);
                return false;
            }

            var camGo = new GameObject("BettrFG_TextCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << RasterLayer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            camGo.transform.rotation = Quaternion.identity;

            var frame = new Rect(bounds.center.x - bounds.size.x * 0.5f, bounds.center.y - bounds.size.y * 0.5f,
                bounds.size.x, bounds.size.y);

            int scoutH = 96;
            int scoutW = Mathf.Clamp(Mathf.RoundToInt(scoutH * (frame.width / frame.height)), 1, 2048);
            if (!Shoot(cam, textGo.transform.position, frame, scoutW, scoutH, out bool[] scout))
            {
                Plugin.Log.LogWarning($"scout pass on '{text}' came back empty");
                UnityEngine.Object.Destroy(camGo);
                UnityEngine.Object.Destroy(textGo);
                return false;
            }

            int x0 = scoutW, x1 = -1, y0 = scoutH, y1 = -1;
            for (int y = 0; y < scoutH; y++)
                for (int x = 0; x < scoutW; x++)
                {
                    if (!scout[y * scoutW + x]) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }

            if (x1 < x0 || y1 < y0)
            {
                UnityEngine.Object.Destroy(camGo);
                UnityEngine.Object.Destroy(textGo);
                return false;
            }

            float pxW = frame.width / scoutW;
            float pxH = frame.height / scoutH;
            var ink = new Rect(frame.x + x0 * pxW, frame.y + y0 * pxH, (x1 - x0 + 1) * pxW, (y1 - y0 + 1) * pxH);

            h = Mathf.Clamp(quality, 4, MaxQuality);
            w = Mathf.Clamp(Mathf.RoundToInt(h * (ink.width / ink.height)), 1, 4096);
            bool ok = Shoot(cam, textGo.transform.position, ink, w, h, out cells);

            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(textGo);

            if (!ok) Plugin.Log.LogWarning($"rastered '{text}' at {w}x{h} and every cell came back empty");
            return ok;
        }

        private static bool Shoot(Camera cam, Vector3 origin, Rect frame, int w, int h, out bool[] cells)
        {
            cam.orthographicSize = frame.height * 0.5f;
            cam.aspect = (float)w / h;
            cam.transform.position = origin + new Vector3(frame.x + frame.width * 0.5f, frame.y + frame.height * 0.5f, -20f);

            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;

            var px = tex.GetPixels32();
            cells = new bool[w * h];
            int filled = 0;
            for (int i = 0; i < cells.Length && i < px.Length; i++)
            {
                cells[i] = px[i].r > 110;
                if (cells[i]) filled++;
            }

            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);
            return filled > 0;
        }
    }
}
