using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    public static class BatchLink
    {
        public static bool IsController(LevelEditorPlaceableObject obj)
        {
            var ctrl = obj?.LinkController;
            if (ctrl == null) return false;
            return LinkingSystemExtensions.IsMoverType(ctrl.GetTransmitterLinkType());
        }

        public static LevelEditorPlaceableObject Controller() => Controller(out _);

        public static LevelEditorPlaceableObject Controller(out int found)
        {
            found = 0;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return null;

            var pivot = LevelEditorMultiSelectionHandler.SelectionPivot();
            LevelEditorPlaceableObject first = null, onPivot = null;
            foreach (var obj in sel)
            {
                if (obj == null || !IsController(obj)) continue;
                found++;
                if (first == null) first = obj;
                if (pivot != null && obj.Pointer == pivot.Pointer) onPivot = obj;
            }
            return onPivot ?? first;
        }

        public static string TypeName(LevelEditorPlaceableObject controller)
        {
            var ctrl = controller?.LinkController;
            if (ctrl == null) return "?";
            return ctrl.GetTransmitterLinkType() == ObjectLinkController.TransmitterLinkType.Rotation
                ? "rotation" : "movement";
        }

        public static void Survey(LevelEditorPlaceableObject controller,
            out int receivers, out int linked, out int slotsFree)
        {
            receivers = 0; linked = 0; slotsFree = -1;

            var ctrl = controller?.LinkController;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (ctrl == null || sel == null) return;

            foreach (var obj in sel)
            {
                if (obj == null || obj.Pointer == controller.Pointer) continue;
                if (obj.LinkController == null) continue;
                receivers++;
                if (ctrl.IsReceiverLinkedTo(obj)) linked++;
            }

            int max = ctrl.GetMaxConnections();
            if (max > 0) slotsFree = max - ctrl.GetConnectedReceiverCount();
        }

        public static int LinkAll(BatchEditHistory.BatchEntry entry,
            LevelEditorPlaceableObject controller, out string note)
        {
            note = null;
            var ctrl = controller?.LinkController;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (ctrl == null || sel == null)
            {
                Plugin.Log.LogWarning("batch link asked for, but there's no controller in the selection");
                return 0;
            }

            int done = 0, already = 0, full = 0, budget = 0, incompatible = 0, inert = 0;
            foreach (var obj in sel)
            {
                if (obj == null || obj.Pointer == controller.Pointer) continue;

                var rc = obj.LinkController;
                if (rc == null) { inert++; continue; }

                bool ok = ctrl.CanTransmitterLinkToReceiver(rc, out var why);
                if (!ok || why != ObjectLinkController.LinkConnectionCheckResult.ValidConnection)
                {
                    switch (why)
                    {
                        case ObjectLinkController.LinkConnectionCheckResult.AlreadyConnected: already++; break;
                        case ObjectLinkController.LinkConnectionCheckResult.NoAvailableSlots: full++; break;
                        case ObjectLinkController.LinkConnectionCheckResult.LinkBudgetExceeded: budget++; break;
                        default: incompatible++; break;
                    }
                    continue;
                }

                entry?.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, LinkTo = controller, Linked = false });
                ctrl.LinkReceiver(obj, false);
                done++;
            }

            if (full > 0) note = full + " over the controller's limit";
            else if (budget > 0) note = budget + " over the level's link budget";
            else if (incompatible + inert > 0 && done == 0) note = "nothing selected can be driven by this";
            else if (already > 0 && done == 0) note = "all of them are already linked";

            Plugin.Log.LogInfo($"{TypeName(controller)} controller took {done} new link(s)"
                + (already > 0 ? $", {already} already on it" : "")
                + (full > 0 ? $", {full} didn't fit" : "")
                + (budget > 0 ? $", {budget} blocked by the link budget" : "")
                + (incompatible + inert > 0 ? $", {incompatible + inert} can't be linked at all" : ""));
            return done;
        }

        public static int UnlinkAll(BatchEditHistory.BatchEntry entry, LevelEditorPlaceableObject controller)
        {
            var ctrl = controller?.LinkController;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (ctrl == null || sel == null) { Plugin.Log.LogWarning("unlink with no controller selected, ignoring"); return 0; }

            int done = 0;
            foreach (var obj in sel)
            {
                if (obj == null || obj.Pointer == controller.Pointer) continue;
                if (!ctrl.IsReceiverLinkedTo(obj)) continue;

                entry?.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, LinkTo = controller, Linked = true });
                ctrl.UnlinkReceiver(obj, false, true, true, false);
                done++;
            }
            Plugin.Log.LogInfo($"cut {done} link(s) off the {TypeName(controller)} controller");
            return done;
        }

        public static void SetLinked(ObjectLinkController ctrl, LevelEditorPlaceableObject receiver, bool linked)
        {
            if (ctrl.IsReceiverLinkedTo(receiver) == linked) return;

            if (!linked) { ctrl.UnlinkReceiver(receiver, false, true, true, false); return; }

            var rc = receiver.LinkController;
            if (rc == null) return;
            bool ok = ctrl.CanTransmitterLinkToReceiver(rc, out var why);
            if (!ok || why != ObjectLinkController.LinkConnectionCheckResult.ValidConnection)
            {
                Plugin.Log.LogWarning($"couldn't put a link back, editor says {why}");
                return;
            }
            ctrl.LinkReceiver(receiver, false);
        }
    }
}
