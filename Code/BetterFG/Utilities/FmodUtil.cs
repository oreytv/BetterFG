using System;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace BetterFG.Utilities
{
    internal static class FmodUtil
    {
        public static int StopSnapshots(Func<string, bool> match)
        {
            int stopped = 0;
            try
            {
                var studio = RuntimeManager.StudioSystem;
                if (!studio.isValid()) return 0;
                if (studio.getBankList(out Il2CppStructArray<Bank> banks) != RESULT.OK || banks == null) return 0;

                foreach (var bank in banks)
                {
                    if (!bank.isValid()) continue;
                    if (bank.getEventList(out Il2CppStructArray<EventDescription> descriptions) != RESULT.OK || descriptions == null) continue;

                    foreach (var description in descriptions)
                    {
                        if (!description.isValid()) continue;
                        if (description.getPath(out string path) != RESULT.OK) continue;
                        if (!path.StartsWith("snapshot:/")) continue;
                        if (description.getInstanceList(out Il2CppStructArray<EventInstance> instances) != RESULT.OK || instances == null) continue;

                        foreach (var instance in instances)
                        {
                            if (!instance.isValid()) continue;
                            instance.getPlaybackState(out PLAYBACK_STATE state);
                            if (state == PLAYBACK_STATE.STOPPED) continue;
                            if (match != null && !match(path)) continue;

                            instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                            stopped++;
                        }
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't sweep snapshots: {ex.Message}"); }
            return stopped;
        }

        public static void UnmuteBus(string path)
        {
            try
            {
                var bus = RuntimeManager.GetBus(path);
                if (!bus.isValid()) return;

                bus.getMute(out bool muted);
                bus.getPaused(out bool paused);
                bus.getVolume(out float volume, out float _);

                if (muted) bus.setMute(false);
                if (paused) bus.setPaused(false);
                if (volume <= 0.001f) bus.setVolume(1f);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't unmute {path}: {ex.Message}"); }
        }
    }
}
