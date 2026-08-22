using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tabs
{
    // material properties belong to a MATERIAL, not a texture slot - this is its own tab (reached
    // from the wizard's Png step, "‹ back" returns there) instead of a step or an in-wizard overlay,
    // so it isn't forced on anyone and isn't gated on which texture row happens to be selected.
    // the wizard hands over its whole WIP entry as plain fields below before switching here, and
    // "‹ back" hands the (possibly edited) fields right back so the wizard resumes mid-edit.
    public class SkinTextureMaterialPropsTab : SwitchTab
    {
        public SkinTextureMaterialPropsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Skin Texture - Material Props";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";
        protected override string SwitchLabel => "‹ back";

        protected override Tab MakeSwitchTarget()
        {
            var wizard = BetterFGTabRegistry.NewTab<SkinTextureWizardTab>();
            wizard.ResumeSource = this;
            return wizard;
        }

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS_SM => UIScale.FS_SM;
        private static float ROW_H => 24f * UIScale.S;

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color LABEL = new Color(1f, 1f, 1f, 0.72f);
        private static readonly Color WHITE = Color.white;

        // handed over from the wizard before SwitchSlotTab; read once in BuildContent
        public int EditIndex = -1;
        public string CostumeName = "";
        public readonly List<Material> Mats = new List<Material>();
        public readonly List<string> MatNames = new List<string>();
        public readonly Dictionary<string, string> OverridePaths = new Dictionary<string, string>();
        public readonly Dictionary<string, MatPropOverride> MatProps = new Dictionary<string, MatPropOverride>();
        public int MatIdx = -1;
        public string EntryName = "";

        // the row width actually visible inside a ScrollView built at TabWidth - PAD*2 wide -
        // the viewport (and its scrollbar) eats SCROLLBAR_INSET off both sides
        private float RowW => TabWidth - PAD * 2f - UGUIShip.SCROLLBAR_INSET * 2f;

        private RectTransform _matDropdownArea;
        private GameObject _matDropdownGo;
        private RectTransform _propContent;
        private Text _statusLbl;

        private readonly List<Material> _distinctMats = new List<Material>();
        private readonly List<string> _distinctMatNames = new List<string>();
        private int _propMatIdx = -1;

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "Material", FS_SM, LABEL);
            y += LH + SH;

            var areaGo = new GameObject("MatDropdownArea");
            areaGo.transform.SetParent(contentRoot, false);
            _matDropdownArea = areaGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_matDropdownArea, new Rect(PAD, y, w, BTN_H));
            y += BTN_H + SH;

            float statusH = LH;
            float scrollH = TabHeight - y - VPAD - statusH - SH;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, scrollH));
            _propContent = scroll.content;
            var layout = _propContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 3f;
            _propContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += scrollH + SH;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, statusH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            _distinctMats.Clear();
            _distinctMatNames.Clear();
            for (int i = 0; i < Mats.Count; i++)
            {
                string mn = SkinApplicationService.CleanMatName(Mats[i].name);
                if (string.IsNullOrEmpty(mn) || _distinctMatNames.Contains(mn)) continue;
                _distinctMats.Add(Mats[i]);
                _distinctMatNames.Add(mn);
            }

            // convenience: default to whichever material owns the texture row selected on the wizard
            _propMatIdx = MatIdx >= 0 && MatIdx < Mats.Count ? _distinctMats.IndexOf(Mats[MatIdx]) : -1;
            if (_propMatIdx < 0) _propMatIdx = 0;

            RebuildMatDropdown();
            RebuildPropRows();
            PositionSwitchLink();
        }

        private void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }

        private void RebuildMatDropdown()
        {
            if (_matDropdownGo != null) GameObject.Destroy(_matDropdownGo);
            float w = TabWidth - PAD * 2f;
            var dd = UGUIShip.CreateDropdown(_matDropdownArea, new Rect(0f, 0f, w, BTN_H),
                _distinctMatNames, _propMatIdx, new Action<int>(idx => { _propMatIdx = idx; RebuildPropRows(); }), FS_SM);
            _matDropdownGo = dd.gameObject;
        }

        private void RebuildPropRows()
        {
            for (int i = _propContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_propContent.GetChild(i).gameObject);

            if (_propMatIdx < 0 || _propMatIdx >= _distinctMats.Count)
            {
                UGUIShip.CreateLabel(_propContent, new Rect(6f, 0f, RowW, ROW_H), "pick a material above", FS_SM, HINT);
                return;
            }

            var mat = _distinctMats[_propMatIdx];
            string matName = _distinctMatNames[_propMatIdx];

            bool any = false;
            foreach (var (propName, type, rangeMin, rangeMax) in SkinApplicationService.GetEditableProps(mat))
            {
                any = true;
                if (type == UnityEngine.Rendering.ShaderPropertyType.Color)
                    BuildColorPropRow(mat, matName, propName);
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Vector)
                    BuildVectorPropRow(mat, matName, propName);
                else
                {
                    bool isRange = type == UnityEngine.Rendering.ShaderPropertyType.Range;
                    bool isInt = type == UnityEngine.Rendering.ShaderPropertyType.Int;
                    float min = isRange ? rangeMin : -1000f;
                    float max = isRange ? rangeMax : 1000f;
                    string key = matName + "|" + propName;
                    float current = MatProps.TryGetValue(key, out var existing) ? existing.f : mat.GetFloat(propName);
                    BuildFloatPropRow(matName, propName, current, min, max, !isInt);
                }
            }

            if (!any)
                UGUIShip.CreateLabel(_propContent, new Rect(6f, 0f, RowW, ROW_H),
                    "this material has no editable properties", FS_SM, HINT);
        }

        private void BuildFloatPropRow(string matName, string propName, float current, float min, float max, bool isFloat)
        {
            var rowGo = new GameObject("Prop_" + propName);
            rowGo.transform.SetParent(_propContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;

            float incW = 130f * UIScale.S;
            float labelW = RowW - incW - 4f;
            UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, labelW, ROW_H), propName, FS_SM - 1, WHITE, TextAnchor.MiddleLeft);

            UGUIShip.CreateIncrement(rowGo.transform, new Rect(labelW + 4f, 0f, incW, ROW_H),
                min, max, () => current, v =>
                {
                    current = v;
                    string key = matName + "|" + propName;
                    MatProps[key] = new MatPropOverride { matName = matName, prop = propName, kind = "float", f = v };
                    ApplyLivePreview();
                    SetStatus(propName + " = " + v);
                }, isFloat ? 0.1f : 1f, isFloat, wrap: false, fontSize: FS_SM - 1);
        }

        // uses the same RGB-sliders + hex widget as the foreground/banner colour tabs, plus one
        // more slider for alpha (MatPropOverride carries all four channels)
        private void BuildColorPropRow(Material mat, string matName, string propName)
        {
            string key = matName + "|" + propName;
            Color cur = MatProps.TryGetValue(key, out var existing)
                ? new Color(existing.x, existing.y, existing.z, existing.w)
                : mat.GetColor(propName);
            Color initial = cur;

            var rowGo = new GameObject("Prop_" + propName);
            rowGo.transform.SetParent(_propContent, false);
            var rowRt = rowGo.AddComponent<RectTransform>();
            var le = rowGo.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            float w = RowW;
            float cy = 0f;
            UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, cy, w, LH), propName, FS_SM - 1, WHITE, TextAnchor.MiddleLeft);
            cy += LH + SH;

            void Push()
            {
                MatProps[key] = new MatPropOverride
                {
                    matName = matName,
                    prop = propName,
                    kind = "color",
                    x = cur.r,
                    y = cur.g,
                    z = cur.b,
                    w = cur.a
                };
                ApplyLivePreview();
                SetStatus(propName + " updated");
            }

            UGUIShip.CreateColorControls(rowGo.transform, 0f, ref cy, w,
                () => cur.r, () => cur.g, () => cur.b,
                v => cur.r = v, v => cur.g = v, v => cur.b = v, Push,
                out _, out _, out _, initial);

            UGUIShip.CreateSlider(rowGo.transform, 0f, cy, w, "A", cur.a, LH, PAD, FS_SM,
                v => { cur.a = v; Push(); },
                new Color(0.8f, 0.8f, 0.8f), new Color(0.8f, 0.8f, 0.8f), true, initial.a);
            cy += LH + SH;

            rowRt.sizeDelta = new Vector2(0f, cy);
            le.preferredHeight = cy;
        }

        private void BuildVectorPropRow(Material mat, string matName, string propName)
        {
            string key = matName + "|" + propName;
            Vector4 cur = MatProps.TryGetValue(key, out var existing)
                ? new Vector4(existing.x, existing.y, existing.z, existing.w)
                : mat.GetVector(propName);

            float rowH2 = ROW_H * 2f + 2f;
            var rowGo = new GameObject("Prop_" + propName);
            rowGo.transform.SetParent(_propContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, rowH2);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowH2;
            le.flexibleWidth = 1f;

            UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, ROW_H, RowW, ROW_H),
                propName + " (x,y,z,w)", FS_SM - 1, WHITE, TextAnchor.MiddleLeft);

            float gap = 3f;
            float compW = (RowW - gap * 3f) / 4f;

            void Comp(int idx, float x)
            {
                UGUIShip.CreateIncrement(rowGo.transform, new Rect(x, 0f, compW, ROW_H),
                    -1000f, 1000f, () => cur[idx], v =>
                    {
                        cur[idx] = v;
                        MatProps[key] = new MatPropOverride
                        {
                            matName = matName,
                            prop = propName,
                            kind = "vector",
                            x = cur.x,
                            y = cur.y,
                            z = cur.z,
                            w = cur.w
                        };
                        ApplyLivePreview();
                        SetStatus(propName + " updated");
                    }, 0.1f, true, wrap: false, fontSize: FS_SM - 2);
            }

            Comp(0, 0f);
            Comp(1, compW + gap);
            Comp(2, (compW + gap) * 2f);
            Comp(3, (compW + gap) * 3f);
        }

        // pushes every prop tweak made so far straight onto the live bean(s), so sliders show their
        // effect immediately. reverted (or committed) once the wizard is actually saved/cancelled.
        private void ApplyLivePreview()
        {
            if (SkinApplicationService.Instance == null) return;
            var props = new List<MatPropOverride>(MatProps.Values);
            foreach (var bean in SkinApplicationService.GatherBeans())
                SkinApplicationService.Instance.ApplyMatProps(bean, props);
        }
    }
}
