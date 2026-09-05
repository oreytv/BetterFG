using System;
using System.Collections;
using System.IO;
using BetterFG.Core;
using BetterFG.UI;
using FallGuysLib.UI;
using FG.Common.UGCNetworking;
using FGClient;
using FGClient.UI;
using Il2CppInterop.Runtime;
using MPG.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using BettrFG.uGUI;
using LB = Wushu.LevelEditor.Runtime.UI.LevelBrowser;

namespace BetterFG.Features.LevelPort
{
    // Reuses the game's own UGC "report level" popup (Generic_UI_UgcReportPopup_Prefab /
    // ReportUGCPopupViewModel) as the Import / Export chooser. Opened via ReportManager; we relabel
    // the first two report-reason rows to IMPORT / EXPORT, hide the rest, and point them at file
    // dialogs. Nothing is ever sent to moderation.
    //
    // Export writes the level's real JSON (LevelEditorLevel._levelJSON, fetched if needed) to a
    // .bfglevel file. Import reads a .bfglevel file and queues it via LevelPortImport; the level
    // then loads from it on every open until the game restarts (or the user saves it for real).
    internal static class LevelPortReportMenu
    {
        internal static bool AnyOpen => _popupVm != null && _popupVm.gameObject.activeInHierarchy;

        // single-shot latch: ignore the row that auto-selects on open, and stop a second
        // select/press opening a second dialog. armed one frame after the rows are wired.
        private static bool _armed;
        private static ReportUGCPopupViewModel _popupVm;

        private static LevelEditorLevel _exportLevel;
        private static ReportUGCConfigurationElementViewModel _importElem, _exportElem;
        private static Action _importAct, _exportAct;

        internal static IEnumerator OpenRoutine(LB.TileData tile)
        {
            var rm = SingletonBehaviour<ReportManager>.Instance;
            if (rm == null) yield break;

            string name = string.IsNullOrEmpty(tile?.Name) ? "level" : tile.Name;
            string code = tile?.LevelCode ?? "";
            _exportLevel = tile?.level;

            _armed = false;
            rm.OpenVisualReportUGCPopup("Import / Export", code, 0, false);

            ReportUGCPopupViewModel vm = null;
            for (int i = 0; i < 12 && vm == null; i++) { yield return null; vm = FindLivePopup(); }
            if (vm == null) yield break;
            _popupVm = vm;
            yield return null; // let Setup() build the rows

            Apply(vm, name, code);

            yield return null;
            _armed = true;
        }

        private static ReportUGCPopupViewModel FindLivePopup()
        {
            foreach (var v in Resources.FindObjectsOfTypeAll<ReportUGCPopupViewModel>())
                if (v != null && v.gameObject.scene.IsValid()) return v;
            return null;
        }

        private static void Apply(ReportUGCPopupViewModel vm, string name, string code)
        {
            var ih = vm._popupInputHandler;
            var elems = ih != null ? ih._settingElements : null;
            if (elems == null || elems.Count < 2) return;

            _importElem = elems[0];
            _exportElem = elems[1];
            _importAct = () => StartImport(name, code);
            _exportAct = () => StartExport(name);

            var content = elems[0].transform.parent;
            for (int i = 0; i < content.childCount; i++)
            {
                var c = content.GetChild(i);
                if (c.GetComponent<ReportUGCConfigurationElementViewModel>() == null)
                    c.gameObject.SetActive(false); // the "Offensive ..." section headers
            }

            // strip the report wording — retitle, rebody, relabel the buttons
            var variant = vm.transform.Find("Generic_UI_LE_ReportRoundPopup_Prefab_Variant");
            if (variant != null)
            {
                SetText(variant, "TitleContainer/TitleText", "ui.level_import_export");
                SetText(variant, "BodyText", $"Choose an action for \"{name}\"" + (string.IsNullOrEmpty(code) ? "." : $" [{code}]."));
                SetText(variant, "ButtonContainer/RightButton/Content/Text", "ui.confirm");
                SetText(variant, "ButtonContainer/LeftButton/Content/Text", "ui.close");
            }

            WireRow(_importElem, "ui.import_from_file", _importAct);
            WireRow(_exportElem, "ui.export_to_file", _exportAct);
            for (int i = 2; i < elems.Count; i++) elems[i].gameObject.SetActive(false);

            // also drive off the popup's own confirm button, in case the controller flow is
            // select-row-then-confirm rather than confirm-on-the-row.
            if (vm._acceptButton != null)
            {
                vm._acceptButton.onClick.RemoveAllListeners();
                vm._acceptButton.onClick.AddListener((UnityAction)(() =>
                {
                    var sel = ih.GetCurrentSelected();
                    Fire(sel != null && sel == _exportElem ? _exportAct : _importAct);
                }));
            }
        }

        private static void Fire(Action act)
        {
            if (!_armed || act == null) return;
            _armed = false;
            act();
        }

        // set a TMP label under `root` at `path`, killing any localisation / MVVM binding that would
        // otherwise overwrite it.
        private static void SetText(Transform root, string path, string text)
        {
            var t = root.Find(path);
            if (t == null) return;
            var go = t.gameObject;
            var loc = go.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
            if (loc != null) loc.enabled = false;
            var bind = go.GetComponent("TMPTextBinding")?.TryCast<Behaviour>();
            if (bind != null) bind.enabled = false;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null) UGUIShip.RelabelText(tmp, text);
        }

        private static void WireRow(ReportUGCConfigurationElementViewModel e, string label, Action act)
        {
            SetText(e.transform, "SettingsText", label);

            var call = (UnityAction)(() => Fire(act));
            e._OnPress?.RemoveAllListeners();
            e.AddOnPressListener(call);

            var btn = e.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(call);
            }
        }

        private static void CloseMenu()
        {
            SingletonBehaviour<ReportManager>.Instance?.CloseVisualReportUGCPopup();
        }

        // ── export ───────────────────────────────────────────────────────────

        private static void StartExport(string name)
        {
            var lvl = _exportLevel;
            CloseMenu();
            if (lvl == null) { Plugin.Log.LogWarning("export: this tile has no editable level"); return; }

            var fj = lvl._levelJSON;
            if (fj != null && fj.Fetched && !string.IsNullOrEmpty(fj.Resource))
            {
                PromptSaveExport(name, fj.Resource);
                return;
            }

            Plugin.Log.LogInfo("export: fetching level JSON");
            var cb = DelegateSupport.ConvertDelegate<ResponseHandler>(new Action<ResponseHandlerObject>(ro =>
            {
                if (ro != null && ro.Failed) { Plugin.Log.LogError("export: FetchLevelJSON failed"); return; }
                var s = lvl._levelJSON != null ? lvl._levelJSON.Resource : null;
                if (string.IsNullOrEmpty(s)) { Plugin.Log.LogError("export: level JSON came back empty"); return; }
                PromptSaveExport(name, s);
            }));
            lvl.FetchLevelJSON(cb);
        }

        private static void PromptSaveExport(string name, string json)
        {
            WinDialogs.SaveFile("Export level", "bfglevel", Sanitise(name) + ".bfglevel", new Action<string>(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    File.WriteAllText(path, LevelPortCodec.Encode(json));
                    Plugin.Log.LogInfo($"level exported -> {path} ({json.Length} chars of JSON, encoded)");
                }
                catch (Exception ex) { Plugin.Log.LogError("export write failed: " + ex.Message); }
            }));
        }

        // ── import ───────────────────────────────────────────────────────────

        private static void StartImport(string name, string code)
        {
            CloseMenu();
            WinDialogs.PickFile("Import level", new Action<string>(path =>
            {
                if (!string.IsNullOrEmpty(path)) ConfirmImport(path, name, code);
            }), "BettrFG Level\0*.bfglevel\0All Files\0*.*\0");
        }

        private static void ConfirmImport(string path, string levelName, string code)
        {
            NavPromptCore.RegisterCmsString("bfg_levelport_import_title", "Import over this level?");
            NavPromptCore.RegisterCmsString("bfg_levelport_import_body",
                $"\"{levelName}\" will load from the imported file every time you open it, for the rest of this session. Restart the game to undo it, or save the level in the editor to keep it permanently.");
            PopUp.ShowPopup("bfg_levelport_import_title", "bfg_levelport_import_body",
                PopupInteractionType.Query, UIModalMessage.ModalType.MT_OK_CANCEL,
                UIModalMessage.OKButtonType.Disruptive,
                (Action<bool>)(ok => { if (ok) DoImport(path, levelName, code); }));
        }

        private static void DoImport(string path, string levelName, string code)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { Plugin.Log.LogError("import: couldn't read the file: " + ex.Message); return; }

            if (!LevelPortCodec.TryDecode(text, out string json))
            {
                Plugin.Log.LogError("import: not a BettrFG level file — only .bfglevel files exported by BettrFG work here, not raw Fall Guys JSON");
                return;
            }
            if (string.IsNullOrEmpty(code))
            {
                Plugin.Log.LogWarning("import: this level has no share code — save + publish it once so BettrFG can target it");
                return;
            }

            LevelPortImport.Queue(code, json);
            Plugin.Log.LogInfo($"import queued for {levelName} [{code}] ({json.Length} chars)");

            NavPromptCore.RegisterCmsString("bfg_levelport_done_title", "Import queued");
            NavPromptCore.RegisterCmsString("bfg_levelport_done_body",
                $"Open \"{levelName}\" to load it from the imported file. It stays this way until you restart the game; save the level in the editor to keep it.");
            PopUp.ShowPopup("bfg_levelport_done_title", "bfg_levelport_done_body",
                PopupInteractionType.Info, UIModalMessage.ModalType.MT_OK,
                UIModalMessage.OKButtonType.Default, (Action<bool>)(_ => { }));
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "level";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
