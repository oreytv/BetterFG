using System;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    // shared shell for a "linear multi-step form ending in Save" tab (skin texture override, font
    // override, ...). subclasses supply the step titles, build each step's panel content, say whether
    // the current step can advance, and do the actual save. everything else — panel switching, the
    // step header, Back/Next button behaviour and labels, the status line — lives here once so a new
    // wizard is just: list the steps, build them, validate, save.
    // NOT abstract, on purpose — IL2Cpp class injection can't build a vtable through an abstract
    // method in the hierarchy ("VTable method was null even though base type isn't abstract" kills
    // plugin load entirely). every "abstract" member below is virtual with a stub instead, matching
    // how Tab/TexturedTab/SwitchTab already do it.
    public class WizardTab : TexturedTab
    {
        public WizardTab(IntPtr ptr) : base(ptr) { }

        public int EditIndex = -1;

        protected static float PAD => UIScale.PAD;
        protected static float VPAD => UIScale.VPAD;
        protected static float SH => UIScale.SH;
        protected static float LH => UIScale.LH;
        protected static float BTN_H => UIScale.BTN_H;
        protected static int FS_SM => UIScale.FS_SM;
        protected static float ROW_H => 24f * UIScale.S;

        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color LABEL = new Color(1f, 1f, 1f, 0.72f);
        protected static readonly Color WHITE = Color.white;
        protected static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        protected static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        protected static readonly Color ROW_IDLE = new Color(0.12f, 0.12f, 0.12f, 1f);
        protected static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);

        protected virtual string[] StepTitles => Array.Empty<string>();
        protected int Step { get; private set; }

        private GameObject[] _panels;
        private Text _stepHeader, _status;
        private Button _backBtn, _nextBtn;

        // build step `step`'s content into root, sized w x bodyH — called once per step, in order
        protected virtual void BuildStep(int step, RectTransform root, float w, float bodyH) { }
        // EditIndex >= 0: restore saved state and return the step to open on, or -1 to stay on step 0
        protected virtual int LoadEditedEntry() => -1;
        protected virtual bool CanAdvance(int step) => true;
        protected virtual void RefreshSummary() { }
        // validate + persist; return true to leave back to the list, false (with a SetStatus) to stay
        protected virtual bool Save() => false;
        protected virtual Tab MakeListTarget() => null;

        protected void SetStatus(string msg) { if (_status != null) _status.text = msg; }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            _stepHeader = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, VPAD, w, LH), "", FS_SM, LABEL);

            float bodyY = VPAD + LH + SH;
            float bodyH = TabHeight - bodyY - BTN_H - LH - VPAD - SH * 2f;

            var titles = StepTitles;
            _panels = new GameObject[titles.Length];
            for (int i = 0; i < titles.Length; i++)
            {
                _panels[i] = MakePanel(contentRoot, bodyY, bodyH);
                BuildStep(i, _panels[i].GetComponent<RectTransform>(), w, bodyH);
            }

            float navY = bodyY + bodyH + SH;
            float bw = (w - PAD) / 2f;
            _backBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, navY, bw, BTN_H),
                "< BACK", BTN_DARK, WHITE, FS_SM, new Action(OnBack));
            _nextBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + bw + PAD * 0.5f, navY, bw, BTN_H),
                "NEXT >", BTN_BLUE, WHITE, FS_SM, new Action(OnNext));

            _status = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, navY + BTN_H + SH, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            if (EditIndex >= 0)
            {
                int start = LoadEditedEntry();
                if (start >= 0) Step = start;
            }
            RefreshStep();
        }

        private GameObject MakePanel(RectTransform parent, float y, float h)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(0f, y, TabWidth, h));
            return go;
        }

        private void OnBack()
        {
            if (Step == 0) { LeaveToList(); return; }
            Step--;
            RefreshStep();
        }

        private void OnNext()
        {
            if (Step == StepTitles.Length - 1) { if (Save()) LeaveToList(); return; }
            Step++;
            RefreshStep();
        }

        protected void LeaveToList() => BetterFGUIMan.Instance?.SwitchSlotTab(this, MakeListTarget());

        protected void RefreshStep()
        {
            for (int i = 0; i < _panels.Length; i++) _panels[i].SetActive(i == Step);

            var titles = StepTitles;
            _stepHeader.text = $"Step {Step + 1} of {titles.Length}  -  {titles[Step]}";

            bool last = Step == titles.Length - 1;
            var nlbl = _nextBtn.GetComponentInChildren<Text>();
            if (nlbl != null) nlbl.text = last ? (EditIndex >= 0 ? "SAVE CHANGES" : "SAVE") : "NEXT >";

            bool can = CanAdvance(Step);
            _nextBtn.interactable = can;
            if (nlbl != null) nlbl.color = can ? WHITE : HINT;

            var blbl = _backBtn.GetComponentInChildren<Text>();
            if (blbl != null) blbl.text = Step == 0 ? "< CANCEL" : "< BACK";

            if (last) RefreshSummary();
        }
    }
}
