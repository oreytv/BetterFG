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
    // �� GradientImage ���������������������������������������������������������
    public class GradientImage : BaseMeshEffect
    {
        public GradientImage(IntPtr ptr) : base(ptr) { }

        public bool Vertical = false;

        public Color Left = new Color(0f, 1f, 0.1f, 0f);
        public Color LeftMid = new Color(0f, 1f, 0.1f, 0.18f);
        public Color RightMid = new Color(0f, 1f, 0.1f, 0.18f);
        public Color Right = new Color(0f, 1f, 0.1f, 0f);

        public Color TopColor = Color.white;
        public Color BottomColor = Color.black;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            int count = vh.currentVertCount;
            if (count == 0) return;

            var vert = new UIVertex();

            if (Vertical)
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    if (vert.position.y < minY) minY = vert.position.y;
                    if (vert.position.y > maxY) maxY = vert.position.y;
                }
                float h = maxY - minY;
                if (h < 0.001f) return;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    float t = 1f - (vert.position.y - minY) / h;
                    vert.color = Color.Lerp(TopColor, BottomColor, t);
                    vh.SetUIVertex(vert, i);
                }
            }
            else
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    if (vert.position.x < minX) minX = vert.position.x;
                    if (vert.position.x > maxX) maxX = vert.position.x;
                }
                float w = maxX - minX;
                if (w < 0.001f) return;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    float t = (vert.position.x - minX) / w;
                    vert.color = SampleHorizontal(t);
                    vh.SetUIVertex(vert, i);
                }
            }
        }

        private Color32 SampleHorizontal(float t)
        {
            if (t <= 0f) return Left;
            if (t >= 1f) return Right;
            if (t < 0.33f) return Color.Lerp(Left, LeftMid, t / 0.33f);
            if (t < 0.66f) return Color.Lerp(LeftMid, RightMid, (t - 0.33f) / 0.33f);
            return Color.Lerp(RightMid, Right, (t - 0.66f) / 0.34f);
        }
    }
}
