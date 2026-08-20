using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class PlinthsUgcTab : PlinthsTab
    {
        public PlinthsUgcTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Plinths - UGC";

        protected override string SwitchLabel => "In-game →";
        protected override BetterFGTab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<PlinthsInGameTab>();

        readonly Dictionary<string, Image> _coverImgs = new Dictionary<string, Image>();

        protected override float HeaderY() => RepoSelectorTab.BuildCurrentRepoRow(this, ContentRoot, PAD, ListW);

        protected override void OnCatalogChanged() => Rebuild();

        protected override void OnCoverLoaded(string key, Texture2D tex)
        {
            if (tex == null) return;
            if (_coverImgs.TryGetValue(key, out var img) && img != null) ApplyCover(img, tex);
        }

        protected override void BuildRows()
        {
            _coverImgs.Clear();

            var catalog = Catalog;
            if (catalog == null) { SetStatus("Catalog isn't up yet."); return; }

            var plinths = new List<SkinInfo>();
            foreach (var skin in catalog.AvailableSkins)
                if (SkinTypeParser.FromString(skin.type) == SkinType.Plinth) plinths.Add(skin);

            if (plinths.Count == 0)
            {
                var reg = RepoRegistry.Instance;
                bool fetching = catalog.IsFetching;
                if (!fetching && reg?.Active != null && !catalog.IsFetchedRepo(reg.Active.githubUrl))
                {
                    catalog.FetchSkins(reg.Active);
                    fetching = true;
                }

                var hintGo = new GameObject("Empty");
                hintGo.transform.SetParent(ListContent, false);
                hintGo.AddComponent<RectTransform>();
                hintGo.AddComponent<LayoutElement>().preferredHeight = ROW_H;
                UGUIShip.CreateStretchLabel(hintGo.transform,
                    fetching ? "Fetching..." : "No plinths in this repo. Pick another above.", FS_SM, HINT);
                SetStatus(fetching ? "Fetching..." : "Nothing here yet.");
                return;
            }

            string activeFile = PlinthApp?.ActiveFile;
            foreach (var skin in plinths)
            {
                var captured = skin;
                var img = BuildRow(skin.name, "by " + skin.author, skin.file == activeFile, new Action(() =>
                {
                    PlinthApp?.ApplyPlinthFromSource(captured, new Action<string>(SetStatus));
                    SetStatus($"Applying {captured.name}...");
                }));

                string key = (string.IsNullOrEmpty(skin.sourceRepo) ? "" : skin.sourceRepo) + "|" + skin.file;
                _coverImgs[key] = img;

                if (catalog.TryGetCover(skin, out var tex) && tex != null) ApplyCover(img, tex);
                else
                {
                    catalog.EnsureCover(skin, true);
                    UGUIShip.CreateStretchLabel(img.transform, "No Preview", FS_SM, HINT);
                }
            }

            SetStatus($"{plinths.Count} UGC plinth(s).");
        }
    }
}
