using System.Collections.Generic;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public sealed class IntroRoute
    {
        public Vector3[] Points;
        public float[] Ceiling;
        public float Length;
        public string How;
    }

    public sealed class CreativeIntroRoute
    {
        const float PreferredCell = 5f;
        const int MaxCells = 7000;
        const float LateralPad = 12f;
        const float ProbeAbove = 45f;
        const float ProbeBelow = 12f;
        const float MaxClimb = 11f;
        const float CeilingRadius = 14f;
        const int RoutePoints = 14;
        const float ClusterRadius = 22f;
        const int MaxStops = 6;

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        float _cell;
        int _nx, _nz;
        float _ox, _oz;
        float[] _height;
        bool[] _solid;
        int[] _heap;
        float[] _key;
        int _heapN;

        public IntroRoute PlanRace(Vector3 start, Vector3 finish)
        {
            var seeds = new List<Vector3> { start, finish };
            if (!BuildGrid(seeds)) return null;

            int a = NearestCell(start);
            int b = NearestCell(finish);
            if (a < 0 || b < 0) return null;

            var cells = Walk(a, b);
            if (cells == null) return null;

            var poly = Simplify(cells);
            poly[0] = new Vector3(start.x, poly[0].y, start.z);
            poly[poly.Count - 1] = new Vector3(finish.x, poly[poly.Count - 1].y, finish.z);
            return Build(poly, $"traced {poly.Count} corners of the actual course");
        }

        public IntroRoute PlanTour(Vector3 start, List<Vector3> targets)
        {
            var seeds = new List<Vector3>(targets) { start };
            if (!BuildGrid(seeds)) return null;

            var stops = Order(start, Cluster(targets));
            if (stops.Count == 0) return null;

            var poly = new List<Vector3> { start };
            Vector3 from = start;
            int walked = 0;
            foreach (var stop in stops)
            {
                int a = NearestCell(from);
                int b = NearestCell(stop);
                List<int> cells = a >= 0 && b >= 0 ? Walk(a, b) : null;
                if (cells != null)
                {
                    var leg = Simplify(cells);
                    for (int i = 1; i < leg.Count; i++) poly.Add(leg[i]);
                    walked++;
                }
                else poly.Add(stop);
                from = stop;
            }

            return Build(poly, $"scoring tour, {stops.Count} stops, {walked} of them reachable on foot");
        }

        bool BuildGrid(List<Vector3> seeds)
        {
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            int placed = 0;

            foreach (var po in UnityEngine.Object.FindObjectsOfType<LevelEditorPlaceableObject>())
            {
                if (po == null || !po.gameObject.activeInHierarchy) continue;
                Vector3 p = po.transform.position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                placed++;
            }
            if (placed < 4) return false;

            foreach (var s in seeds)
            {
                min = Vector3.Min(min, s);
                max = Vector3.Max(max, s);
            }

            float w = max.x - min.x + LateralPad * 2f;
            float d = max.z - min.z + LateralPad * 2f;
            _cell = PreferredCell;
            while ((w / _cell + 2f) * (d / _cell + 2f) > MaxCells) _cell *= 1.3f;

            _ox = min.x - LateralPad;
            _oz = min.z - LateralPad;
            _nx = Mathf.CeilToInt(w / _cell) + 1;
            _nz = Mathf.CeilToInt(d / _cell) + 1;

            int n = _nx * _nz;
            _height = new float[n];
            _solid = new bool[n];

            float top = max.y + ProbeAbove;
            float span = top - (min.y - ProbeBelow);
            int hits = 0;

            for (int iz = 0; iz < _nz; iz++)
            {
                for (int ix = 0; ix < _nx; ix++)
                {
                    var origin = new Vector3(_ox + ix * _cell, top, _oz + iz * _cell);
                    RaycastHit hit;
                    if (!Physics.Raycast(origin, Vector3.down, out hit, span, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    int i = iz * _nx + ix;
                    _solid[i] = true;
                    _height[i] = hit.point.y;
                    hits++;
                }
            }

            Plugin.Log.LogInfo($"level scan: {_nx}x{_nz} cells at {_cell:0.#}u, {hits} of them have ground under them ({placed} placeables)");
            return hits > 12;
        }

        int NearestCell(Vector3 p)
        {
            int cx = Mathf.RoundToInt((p.x - _ox) / _cell);
            int cz = Mathf.RoundToInt((p.z - _oz) / _cell);
            int best = -1;
            float bestScore = float.MaxValue;

            for (int r = 0; r <= 8; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= _nx || z >= _nz) continue;
                        int i = z * _nx + x;
                        if (!_solid[i]) continue;
                        float score = Mathf.Abs(_height[i] - p.y) + r * _cell * 0.35f;
                        if (score < bestScore) { bestScore = score; best = i; }
                    }
                }
                if (best >= 0 && r >= 2) break;
            }
            return best;
        }

        List<int> Walk(int from, int to)
        {
            int n = _nx * _nz;
            var g = new float[n];
            var prev = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; prev[i] = -1; }

            _heap = new int[n * 8];
            _key = new float[n * 8];
            _heapN = 0;

            g[from] = 0f;
            Push(from, Heuristic(from, to));

            while (_heapN > 0)
            {
                int cur = Pop();
                if (cur == to) break;
                if (closed[cur]) continue;
                closed[cur] = true;

                int cx = cur % _nx, cz = cur / _nx;
                for (int k = 0; k < 8; k++)
                {
                    int x = cx + DX[k], z = cz + DZ[k];
                    if (x < 0 || z < 0 || x >= _nx || z >= _nz) continue;
                    int nb = z * _nx + x;
                    if (!_solid[nb] || closed[nb]) continue;

                    bool diag = k >= 4;
                    if (diag && (!_solid[cz * _nx + x] || !_solid[z * _nx + cx])) continue;

                    float dh = _height[nb] - _height[cur];
                    if (Mathf.Abs(dh) > MaxClimb) continue;

                    float step = _cell * (diag ? 1.414f : 1f) + Mathf.Abs(dh) * 1.5f;
                    float cost = g[cur] + step;
                    if (cost >= g[nb]) continue;

                    g[nb] = cost;
                    prev[nb] = cur;
                    Push(nb, cost + Heuristic(nb, to));
                }
            }

            if (prev[to] < 0 && to != from) return null;

            var cells = new List<int>();
            for (int c = to; c >= 0; c = prev[c]) cells.Add(c);
            cells.Reverse();
            return cells;
        }

        float Heuristic(int a, int b)
        {
            float dx = (a % _nx - b % _nx) * _cell;
            float dz = (a / _nx - b / _nx) * _cell;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void Push(int idx, float k)
        {
            if (_heapN >= _heap.Length) return;
            int i = _heapN++;
            _heap[i] = idx;
            _key[i] = k;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_key[p] <= _key[i]) break;
                int ti = _heap[p]; float tk = _key[p];
                _heap[p] = _heap[i]; _key[p] = _key[i];
                _heap[i] = ti; _key[i] = tk;
                i = p;
            }
        }

        int Pop()
        {
            int top = _heap[0];
            _heapN--;
            _heap[0] = _heap[_heapN];
            _key[0] = _key[_heapN];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < _heapN && _key[l] < _key[m]) m = l;
                if (r < _heapN && _key[r] < _key[m]) m = r;
                if (m == i) break;
                int ti = _heap[m]; float tk = _key[m];
                _heap[m] = _heap[i]; _key[m] = _key[i];
                _heap[i] = ti; _key[i] = tk;
                i = m;
            }
            return top;
        }

        List<Vector3> Simplify(List<int> cells)
        {
            var pts = new List<Vector3> { CellPos(cells[0]) };
            int anchor = 0;
            for (int i = 2; i < cells.Count; i++)
            {
                if (Visible(cells[anchor], cells[i])) continue;
                anchor = i - 1;
                pts.Add(CellPos(cells[anchor]));
            }
            pts.Add(CellPos(cells[cells.Count - 1]));
            return pts;
        }

        bool Visible(int a, int b)
        {
            Vector3 pa = CellPos(a), pb = CellPos(b);
            float dist = Vector2.Distance(new Vector2(pa.x, pa.z), new Vector2(pb.x, pb.z));
            int steps = Mathf.CeilToInt(dist / (_cell * 0.5f));
            for (int s = 1; s < steps; s++)
            {
                Vector3 p = Vector3.Lerp(pa, pb, s / (float)steps);
                int ix = Mathf.RoundToInt((p.x - _ox) / _cell);
                int iz = Mathf.RoundToInt((p.z - _oz) / _cell);
                if (ix < 0 || iz < 0 || ix >= _nx || iz >= _nz) return false;
                int i = iz * _nx + ix;
                if (!_solid[i] || Mathf.Abs(_height[i] - p.y) > MaxClimb) return false;
            }
            return true;
        }

        Vector3 CellPos(int i) => new Vector3(_ox + i % _nx * _cell, _height[i], _oz + i / _nx * _cell);

        List<Vector3> Cluster(List<Vector3> pts)
        {
            var sums = new List<Vector3>();
            var counts = new List<int>();
            foreach (var p in pts)
            {
                int best = -1;
                float bd = ClusterRadius;
                for (int i = 0; i < sums.Count; i++)
                {
                    float d = Vector3.Distance(sums[i] / counts[i], p);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0) { sums.Add(p); counts.Add(1); }
                else { sums[best] += p; counts[best]++; }
            }

            var order = new List<int>();
            for (int i = 0; i < sums.Count; i++) order.Add(i);
            order.Sort((x, y) => counts[y].CompareTo(counts[x]));

            var centroids = new List<Vector3>();
            for (int i = 0; i < order.Count && i < MaxStops; i++) centroids.Add(sums[order[i]] / counts[order[i]]);
            return centroids;
        }

        static List<Vector3> Order(Vector3 start, List<Vector3> stops)
        {
            var left = new List<Vector3>(stops);
            var route = new List<Vector3>();
            Vector3 cur = start;
            while (left.Count > 0)
            {
                int bi = 0;
                float bd = float.MaxValue;
                for (int i = 0; i < left.Count; i++)
                {
                    float d = Vector3.Distance(cur, left[i]);
                    if (d < bd) { bd = d; bi = i; }
                }
                cur = left[bi];
                route.Add(cur);
                left.RemoveAt(bi);
            }
            return route;
        }

        IntroRoute Build(List<Vector3> poly, string how)
        {
            if (poly.Count < 2) return null;

            float total = 0f;
            for (int i = 1; i < poly.Count; i++) total += Vector3.Distance(poly[i - 1], poly[i]);
            if (total < 10f) return null;

            var pts = new Vector3[RoutePoints];
            float step = total / (RoutePoints - 1);
            int seg = 0;
            float segStart = 0f;

            for (int i = 0; i < RoutePoints; i++)
            {
                float target = step * i;
                while (seg < poly.Count - 2 && segStart + Vector3.Distance(poly[seg], poly[seg + 1]) < target)
                {
                    segStart += Vector3.Distance(poly[seg], poly[seg + 1]);
                    seg++;
                }
                float segLen = Vector3.Distance(poly[seg], poly[seg + 1]);
                float f = segLen > 0.001f ? (target - segStart) / segLen : 0f;
                pts[i] = Vector3.Lerp(poly[seg], poly[seg + 1], Mathf.Clamp01(f));
            }

            var ceiling = new float[RoutePoints];
            int r = Mathf.CeilToInt(CeilingRadius / _cell);
            for (int i = 0; i < RoutePoints; i++)
            {
                int cx = Mathf.RoundToInt((pts[i].x - _ox) / _cell);
                int cz = Mathf.RoundToInt((pts[i].z - _oz) / _cell);
                float hi = pts[i].y;
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= _nx || z >= _nz) continue;
                        int c = z * _nx + x;
                        if (_solid[c] && _height[c] > hi) hi = _height[c];
                    }
                }
                ceiling[i] = hi;
            }

            return new IntroRoute { Points = pts, Ceiling = ceiling, Length = total, How = how };
        }
    }
}
