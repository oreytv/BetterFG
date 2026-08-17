using System.Collections.Generic;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    internal static class EyeGeometry
    {
        const float EyeWeight = 0.5f;
        const float SurfaceLift = 0.004f;

        static readonly Dictionary<int, Mesh> _carved = new Dictionary<int, Mesh>();

        public static GameObject Attach(GameObject root, Material shared, Color tint)
        {
            var meshes = root.GetComponent<FG.Common.FallguyCustomisationHandler>()?.SkinnedMeshes;
            if (meshes == null) return null;

            for (int i = 0; i < meshes.Count; i++)
            {
                var body = meshes[i];
                if (body == null || body.sharedMesh == null) continue;

                var eyeMesh = Carve(body);
                if (eyeMesh == null) continue;

                var go = new GameObject("BettrFG_Eyes");
                go.layer = body.gameObject.layer;
                go.transform.SetParent(body.transform, false);

                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = eyeMesh;
                smr.bones = body.bones;
                smr.rootBone = body.rootBone;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.sharedMaterial = shared;

                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_MultiplyColor", tint);
                smr.SetPropertyBlock(mpb);
                return go;
            }

            return null;
        }

        public static void Detach(GameObject spawned)
        {
            if (spawned == null) return;
            Object.Destroy(spawned);
        }

        static Mesh Carve(SkinnedMeshRenderer body)
        {
            var src = body.sharedMesh;
            int key = src.GetInstanceID();
            if (_carved.TryGetValue(key, out var cached)) return cached;

            Mesh built = null;
            int el = -1, er = -1;
            var bones = body.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                if (b == null) continue;
                if (b.name == "Eye_L_jnt") el = i;
                else if (b.name == "Eye_R_jnt") er = i;
            }

            if (el >= 0 && er >= 0 && src.isReadable) built = Extract(src, el, er);
            _carved[key] = built;

            if (built != null)
                Plugin.Log.LogInfo($"carved eyes off {src.name}: {built.vertexCount} verts, {built.triangles.Length / 3} tris (bones {el}/{er})");

            return built;
        }

        static Mesh Extract(Mesh src, int el, int er)
        {
            var weights = src.boneWeights;
            var verts = src.vertices;
            var norms = src.normals;
            var uvs = src.uv;
            var tris = src.triangles;

            var isEye = new bool[verts.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                float total = 0f;
                if (w.boneIndex0 == el || w.boneIndex0 == er) total += w.weight0;
                if (w.boneIndex1 == el || w.boneIndex1 == er) total += w.weight1;
                if (w.boneIndex2 == el || w.boneIndex2 == er) total += w.weight2;
                if (w.boneIndex3 == el || w.boneIndex3 == er) total += w.weight3;
                isEye[i] = total > EyeWeight;
            }

            var remap = new int[verts.Length];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;

            var outVerts = new List<Vector3>();
            var outNorms = new List<Vector3>();
            var outUvs = new List<Vector2>();
            var outWeights = new List<BoneWeight>();
            var outTris = new List<int>();

            bool hasNorms = norms != null && norms.Length == verts.Length;
            bool hasUvs = uvs != null && uvs.Length == verts.Length;

            for (int t = 0; t < tris.Length; t += 3)
            {
                if (!isEye[tris[t]] || !isEye[tris[t + 1]] || !isEye[tris[t + 2]]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t + k];
                    if (remap[v] < 0)
                    {
                        remap[v] = outVerts.Count;
                        var n = hasNorms ? norms[v] : Vector3.zero;
                        outVerts.Add(verts[v] + n * SurfaceLift);
                        outNorms.Add(n);
                        outUvs.Add(hasUvs ? uvs[v] : Vector2.zero);
                        outWeights.Add(weights[v]);
                    }
                    outTris.Add(remap[v]);
                }
            }

            if (outTris.Count == 0) return null;

            var mesh = new Mesh { name = src.name + "_BettrFGEyes", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(outVerts.ToArray());
            if (hasNorms) mesh.SetNormals(outNorms.ToArray());
            if (hasUvs) mesh.SetUVs(0, outUvs.ToArray());
            mesh.SetTriangles(outTris.ToArray(), 0);
            mesh.boneWeights = outWeights.ToArray();
            mesh.bindposes = src.bindposes;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
