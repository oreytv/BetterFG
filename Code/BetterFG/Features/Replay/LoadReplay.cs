using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;

namespace BetterFG.Features.Replay
{
    internal class ReplayMeta
    {
        public string path;
        public string label;
        public string shareCode;
        public bool isUgc;
        public bool isFav;
        public float duration;
        public int players;
        public DateTime when;
        public string haystack;
    }

    internal static class LoadReplay
    {
        const string KNOWN_KEY = "replay.known";
        const string FAV_KEY = "replay.favourites";
        const float FRAME_RATE = 60f;

        static float RoundUpFrame(float t) => Mathf.Ceil(t * FRAME_RATE - 0.0001f) / FRAME_RATE;

        static List<string> Known()
        {
            var list = new List<string>();
            foreach (string entry in SettingsService.Get(KNOWN_KEY).Split('|'))
                if (!string.IsNullOrEmpty(entry)) list.Add(entry);
            return list;
        }

        static List<string> Favourites()
        {
            var list = new List<string>();
            foreach (string entry in SettingsService.Get(FAV_KEY).Split('|'))
                if (!string.IsNullOrEmpty(entry)) list.Add(entry);
            return list;
        }

        public static bool IsFavourite(string path)
        {
            foreach (string entry in Favourites())
                if (string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool ToggleFavourite(string path)
        {
            var favs = Favourites();
            for (int i = 0; i < favs.Count; i++)
                if (string.Equals(favs[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    favs.RemoveAt(i);
                    SettingsService.Set(FAV_KEY, string.Join("|", favs));
                    return false;
                }

            favs.Add(path);
            SettingsService.Set(FAV_KEY, string.Join("|", favs));
            return true;
        }

        public static bool Delete(string path)
        {
            try
            {
                File.Delete(path);

                string thumb = ReplayThumbnail.PathFor(path);
                if (File.Exists(thumb)) File.Delete(thumb);

                string frames = SaveReplay.FramesPathFor(path);
                if (File.Exists(frames)) File.Delete(frames);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't delete {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }

            var known = Known();
            known.RemoveAll(e => string.Equals(e, path, StringComparison.OrdinalIgnoreCase));
            SettingsService.Set(KNOWN_KEY, string.Join("|", known));
            if (IsFavourite(path)) ToggleFavourite(path);
            _metaCache.Remove(path);
            ReplayThumbnail.Forget(path);

            Plugin.Log.LogInfo($"binned {Path.GetFileName(path)}");
            return true;
        }

        public static void Remember(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            string dir = (Path.GetDirectoryName(path) ?? "").TrimEnd('\\');
            if (string.Equals(dir, SaveReplay.ReplayDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;

            var known = Known();
            foreach (string entry in known)
                if (string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)) return;

            known.Add(path);
            SettingsService.Set(KNOWN_KEY, string.Join("|", known));
            Plugin.Log.LogInfo($"replay lives outside the usual folder, keeping a pointer to it: {path}");
        }

        public static List<string> ListFiles()
        {
            var paths = new List<string>();
            try
            {
                if (Directory.Exists(SaveReplay.ReplayDir))
                    paths.AddRange(Directory.GetFiles(SaveReplay.ReplayDir, "*" + SaveReplay.Extension));

                foreach (string entry in Known())
                    if (File.Exists(entry)) paths.Add(entry);

                paths.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't list replays: {ex.Message}"); }
            return paths;
        }

        static readonly Dictionary<string, ReplayMeta> _metaCache = new Dictionary<string, ReplayMeta>();

        public static ReplayMeta ReadMeta(string path)
        {
            var stamp = File.GetLastWriteTime(path);
            if (_metaCache.TryGetValue(path, out var hit) && hit.when == stamp) return hit;

            var meta = new ReplayMeta { path = path, when = stamp, isFav = IsFavourite(path) };
            string json = null;
            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                    if (fs.Length >= 8 && br.ReadInt32() == SaveReplay.ContainerMagic)
                        json = System.Text.Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));

                if (json == null) json = File.ReadAllText(path);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"header of {Path.GetFileName(path)} wouldn't read: {ex.Message}"); }

            string file = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(json))
            {
                meta.label = JsonUtil.GetValue(json, "replayName");
                if (string.IsNullOrEmpty(meta.label)) meta.label = JsonUtil.GetValue(json, "roundName");
                meta.shareCode = JsonUtil.GetValue(json, "shareCode");
                meta.isUgc = JsonUtil.GetBool(json, "isUgc");
                meta.duration = JsonUtil.GetFloat(json, "duration");
                meta.players = JsonUtil.GetArray(json, "players").Count;
            }

            if (string.IsNullOrEmpty(meta.label))
            {
                int cut = file.IndexOf('_', file.IndexOf('_') + 1);
                meta.label = cut > 0 ? file.Substring(cut + 1) : file;
            }

            meta.haystack = (meta.label + " " + meta.shareCode + " " + file).ToLowerInvariant();
            _metaCache[path] = meta;
            return meta;
        }

        static int ReadFrameList(BinaryReader br, List<ReplayFrame> into, int version)
        {
            int n = br.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var frame = new ReplayFrame
                {
                    t = br.ReadSingle(),
                    pos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    rot = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    stateHash = br.ReadInt32(),
                    animTime = br.ReadSingle(),
                };

                if (version >= 12)
                {
                    frame.ragdoll = br.ReadBoolean();
                    frame.upperBody = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    frame.armLeft = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    frame.armRight = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                }
                else if (version >= 10) br.ReadBytes(36);

                into?.Add(frame);
            }
            return n;
        }

        static void ReadAudio(BinaryReader br, ReplayRecording rec)
        {
            int keys = br.ReadInt32();
            for (int i = 0; i < keys; i++) rec.audioKeys.Add(br.ReadString());

            int names = br.ReadInt32();
            for (int i = 0; i < names; i++) rec.audioParamNames.Add(br.ReadString());

            int parameters = br.ReadInt32();
            for (int i = 0; i < parameters; i++)
                rec.audioParams.Add(new ReplayAudioParam { name = br.ReadInt32(), value = br.ReadSingle() });

            int events = br.ReadInt32();
            for (int i = 0; i < events; i++)
            {
                float t = br.ReadSingle();
                float end = rec.version >= 14 ? br.ReadSingle() : -1f;
                rec.audioEvents.Add(new ReplayAudioEvent
                {
                    t = t,
                    end = end,
                    playerId = br.ReadUInt32(),
                    pos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    key = br.ReadInt32(),
                    paramStart = br.ReadInt32(),
                    paramCount = br.ReadInt32(),
                });
            }
        }

        static void ReadGhosts(BinaryReader br, ReplayRecording rec)
        {
            int count = br.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var ghost = new ReplayGhost { name = br.ReadString() };
                ReadFrameList(br, ghost.frames, rec.version);
                rec.ghosts.Add(ghost);
            }
        }

        static void ReadStarchart(BinaryReader br, ReplayRecording rec)
        {
            int paths = br.ReadInt32();
            for (int i = 0; i < paths; i++) rec.starchartPaths.Add(br.ReadString());

            int events = br.ReadInt32();
            for (int i = 0; i < events; i++)
                rec.starchartEvents.Add(new ReplayStarchartEvent
                {
                    t = br.ReadSingle(),
                    pathStart = br.ReadInt32(),
                    pathCount = br.ReadInt32(),
                });
        }

        static void ReadVfx(BinaryReader br, ReplayRecording rec)
        {
            int events = br.ReadInt32();
            for (int i = 0; i < events; i++)
                rec.diveSlideVfxEvents.Add(new ReplayVfxEvent
                {
                    t = br.ReadSingle(),
                    playerId = br.ReadUInt32(),
                });
        }

        static void ReadLevel(BinaryReader br, ReplayRecording rec)
        {
            int packedLength = br.ReadInt32();
            if (packedLength <= 0) return;

            using (var ms = new MemoryStream(br.ReadBytes(packedLength)))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var sr = new StreamReader(gz, System.Text.Encoding.UTF8))
                rec.levelJson = sr.ReadToEnd();
        }

        static void ReadTextures(BinaryReader br, ReplayRecording rec)
        {
            foreach (var player in rec.players)
                foreach (var tex in player.bfgTextures)
                {
                    int length = br.ReadInt32();
                    if (length <= 0) continue;
                    tex.texData = br.ReadBytes(length);
                    tex.texPath = "";
                }
        }

        static void ReadSpeech(BinaryReader br, ReplayRecording rec)
        {
            int options = br.ReadInt32();
            for (int i = 0; i < options; i++)
            {
                var option = new ReplaySpeechOption
                {
                    itemId = br.ReadString(),
                    speechId = br.ReadInt32(),
                    duration = br.ReadSingle(),
                };

                if (rec.version >= 9)
                {
                    option.text = br.ReadString();
                    option.hasText = br.ReadBoolean();
                    option.shiny = br.ReadBoolean();
                    int length = br.ReadInt32();
                    if (length > 0) option.image = br.ReadBytes(length);
                }

                rec.speechOptions.Add(option);
            }

            int events = br.ReadInt32();
            for (int i = 0; i < events; i++)
                rec.speechEvents.Add(new ReplaySpeechEvent
                {
                    t = br.ReadSingle(),
                    end = br.ReadSingle(),
                    playerId = br.ReadUInt32(),
                    option = br.ReadInt32(),
                });
        }

        static void ReadWorld(BinaryReader br, ReplayRecording rec, bool withStates)
        {
            int count = br.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var obj = new ReplayObject
                {
                    path = br.ReadString(),
                    guid = br.ReadString(),
                    owner = withStates ? br.ReadString() : "",
                    prefab = br.ReadString(),
                    spawnTime = br.ReadSingle(),
                    despawnTime = br.ReadSingle(),
                };

                int frames = br.ReadInt32();
                for (int f = 0; f < frames; f++)
                    obj.frames.Add(new ReplayObjectFrame
                    {
                        t = br.ReadSingle(),
                        pos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        rot = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        scale = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        stateHash = br.ReadInt32(),
                        animTime = br.ReadSingle(),
                        active = br.ReadBoolean(),
                    });

                if (withStates)
                {
                    int states = br.ReadInt32();
                    for (int s = 0; s < states; s++)
                        obj.states.Add(new ReplayObjectState { t = br.ReadSingle(), state = br.ReadInt32() });
                }

                rec.worldObjects.Add(obj);
            }
        }

        static int ReadFrames(ReplayRecording rec, string jsonPath)
        {
            string framePath = SaveReplay.FramesPathFor(jsonPath);
            if (!File.Exists(framePath)) return 0;

            int total = 0;
            try
            {
                using (var br = new BinaryReader(File.OpenRead(framePath)))
                {
                    int magic = br.ReadInt32();
                    int playerCount = br.ReadInt32();
                    for (int i = 0; i < playerCount; i++)
                    {
                        uint id = br.ReadUInt32();
                        ReplayPlayer target = null;
                        for (int j = 0; j < rec.players.Count; j++)
                            if (rec.players[j].playerId == id) { target = rec.players[j]; break; }
                        total += ReadFrameList(br, target?.frames, 1);
                    }
                    if (magic == unchecked((int)0xBF8E0002))
                        ReadFrameList(br, rec.cameraFrames, 1);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"frame sidecar for {Path.GetFileName(jsonPath)} wouldn't read: {ex.Message}"); }
            return total;
        }

        public static ReplayRecording Read(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length >= 8 && br.ReadInt32() == SaveReplay.ContainerMagic)
                    {
                        try
                        {
                            int jsonLength = br.ReadInt32();
                            string containerJson = System.Text.Encoding.UTF8.GetString(br.ReadBytes(jsonLength));
                            var container = Parse(containerJson);

                            int playerCount = br.ReadInt32();
                            int packed = 0;
                            for (int i = 0; i < playerCount; i++)
                            {
                                uint id = br.ReadUInt32();
                                ReplayPlayer target = null;
                                for (int j = 0; j < container.players.Count; j++)
                                    if (container.players[j].playerId == id) { target = container.players[j]; break; }
                                packed += ReadFrameList(br, target?.frames, container.version);
                            }
                            ReadFrameList(br, container.cameraFrames, container.version);
                            if (container.version >= 3) ReadAudio(br, container);
                            if (container.version >= 15) ReadStarchart(br, container);
                            if (container.version >= 17) ReadVfx(br, container);
                            if (container.version >= 4) ReadLevel(br, container);
                            if (container.version >= 6) ReadWorld(br, container, container.version >= 11);
                            if (container.version >= 7) ReadTextures(br, container);
                            if (container.version >= 8) ReadSpeech(br, container);
                            if (container.version >= 16) ReadGhosts(br, container);

                            container.sourcePath = path;
                            Remember(path);
                            Plugin.Log.LogInfo($"loaded replay {Path.GetFileName(path)}: {container.roundName}, {container.players.Count} players, {container.keyframes.Count} camera keys, {container.visibilityKeyframes.Count} visibility keys, {packed} frames, {container.audioEvents.Count} sounds, {container.speechEvents.Count} bubbles, {container.worldObjects.Count} world objects");
                            return container;
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"replay {Path.GetFileName(path)} wouldn't parse: {ex.Message} (stopped at byte {fs.Position} of {fs.Length})");
                            return null;
                        }
                    }
                }

                string json = File.ReadAllText(path);
                var rec = Parse(json);
                rec.sourcePath = path;
                Remember(path);
                int legacy = ReadFrames(rec, path);
                Plugin.Log.LogInfo($"loaded replay {Path.GetFileName(path)} (old two-file layout): {rec.roundName}, {rec.players.Count} players, {legacy} frames");
                return rec;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"replay {Path.GetFileName(path)} wouldn't parse: {ex.Message}");
                return null;
            }
        }

        static ReplayRecording Parse(string json)
        {
            var rec = new ReplayRecording
            {
                version = JsonUtil.GetInt(json, "version", 1),
                name = JsonUtil.GetValue(json, "replayName"),
                recordedAt = JsonUtil.GetValue(json, "recordedAt"),
                buildHash = JsonUtil.GetValue(json, "buildHash"),
                roundId = JsonUtil.GetValue(json, "roundId"),
                roundName = JsonUtil.GetValue(json, "roundName"),
                sceneName = JsonUtil.GetValue(json, "sceneName"),
                archetypeId = JsonUtil.GetValue(json, "archetypeId"),
                shareCode = JsonUtil.GetValue(json, "shareCode"),
                levelVersion = JsonUtil.GetInt(json, "levelVersion"),
                backgroundScene = JsonUtil.GetValue(json, "backgroundScene"),
                isUgc = JsonUtil.GetBool(json, "isUgc"),
                isFinal = JsonUtil.GetBool(json, "isFinal"),
                squadSize = (uint)JsonUtil.GetInt(json, "squadSize"),
                duration = RoundUpFrame(JsonUtil.GetFloat(json, "duration")),
                trimStart = RoundUpFrame(JsonUtil.GetFloat(json, "trimStart")),
                trimEnd = RoundUpFrame(JsonUtil.GetFloat(json, "trimEnd")),
            };

            foreach (var entry in JsonUtil.GetArray(json, "sets"))
                rec.sets.Add(new ReplaySet
                {
                    key = JsonUtil.GetValue(entry, "key"),
                    chosen = JsonUtil.GetValue(entry, "chosen"),
                    path = JsonUtil.GetValue(entry, "path"),
                });

            foreach (var entry in JsonUtil.GetArray(json, "keyframes"))
            {
                var keyframe = new ReplayKeyframe
                {
                    time = RoundUpFrame(JsonUtil.GetFloat(entry, "time")),
                    cameraType = (ReplayCameraType)JsonUtil.GetInt(entry, "cameraType"),
                    lookAt = (ReplayLookAt)JsonUtil.GetInt(entry, "lookAt"),
                    easingCurve = (ReplayEasingCurve)JsonUtil.GetInt(entry, "easingCurve"),
                    easingDirection = (ReplayEasingDirection)JsonUtil.GetInt(entry, "easingDirection"),
                    position = new Vector3(JsonUtil.GetFloat(entry, "px"), JsonUtil.GetFloat(entry, "py"), JsonUtil.GetFloat(entry, "pz")),
                    rotation = new Quaternion(JsonUtil.GetFloat(entry, "rx"), JsonUtil.GetFloat(entry, "ry"), JsonUtil.GetFloat(entry, "rz"), JsonUtil.GetFloat(entry, "rw")),
                    fov = JsonUtil.GetFloat(entry, "fov"),
                    targetPlayerId = (uint)JsonUtil.GetInt(entry, "targetPlayerId"),
                    lookAtPlayerId = (uint)JsonUtil.GetInt(entry, "lookAtPlayerId"),
                    targetObject = JsonUtil.GetInt(entry, "targetObject", -1),
                    lookAtObject = JsonUtil.GetInt(entry, "lookAtObject", -1),
                    speed = JsonUtil.GetFloat(entry, "speed"),
                    cut = JsonUtil.GetInt(entry, "cut") != 0,
                    cutToNext = JsonUtil.GetInt(entry, "cutToNext") != 0,
                    shakeKind = (ReplayShakeKind)JsonUtil.GetInt(entry, "shakeKind"),
                    shakeTier = JsonUtil.GetInt(entry, "shakeTier", 2),
                };
                if (keyframe.speed <= 0f) keyframe.speed = 1f;
                rec.keyframes.Add(keyframe);
            }

            foreach (var entry in JsonUtil.GetArray(json, "visibilityKeyframes"))
            {
                var vis = new ReplayVisibilityKeyframe
                {
                    time = RoundUpFrame(JsonUtil.GetFloat(entry, "time")),
                    showPhrases = JsonUtil.GetBool(entry, "showPhrases"),
                    names = (ReplayVisibilityMode)JsonUtil.GetInt(entry, "names"),
                    players = (ReplayVisibilityMode)JsonUtil.GetInt(entry, "playersMode", JsonUtil.GetInt(entry, "players")),
                    showGhosts = JsonUtil.GetBool(entry, "showGhosts", true),
                };
                foreach (string id in JsonUtil.GetValue(entry, "nameOnlyPlayers").Split('|'))
                    if (uint.TryParse(id, out uint pid)) vis.nameOnlyPlayers.Add(pid);
                foreach (string id in JsonUtil.GetValue(entry, "onlyPlayers").Split('|'))
                    if (uint.TryParse(id, out uint pid)) vis.onlyPlayers.Add(pid);
                rec.visibilityKeyframes.Add(vis);
            }

            foreach (var entry in JsonUtil.GetArray(json, "postFxKeyframes"))
            {
                rec.postFxKeyframes.Add(new ReplayPostFxKeyframe
                {
                    time = RoundUpFrame(JsonUtil.GetFloat(entry, "time")),
                    exposure = JsonUtil.GetFloat(entry, "exposure"),
                    contrast = JsonUtil.GetFloat(entry, "contrast"),
                    saturation = JsonUtil.GetFloat(entry, "saturation"),
                    temperature = JsonUtil.GetFloat(entry, "temperature"),
                    tint = JsonUtil.GetFloat(entry, "tint"),
                    vignette = JsonUtil.GetFloat(entry, "vignette"),
                    chromaticAberration = JsonUtil.GetFloat(entry, "chromaticAberration"),
                    bloomIntensity = JsonUtil.GetFloat(entry, "bloomIntensity"),
                    bloomThreshold = JsonUtil.GetFloat(entry, "bloomThreshold", 1f),
                    sharpenAmount = JsonUtil.GetFloat(entry, "sharpenAmount"),
                    sharpenRadius = JsonUtil.GetFloat(entry, "sharpenRadius", 1f),
                });
            }

            foreach (var entry in JsonUtil.GetArray(json, "players"))
            {
                var player = new ReplayPlayer
                {
                    playerId = (uint)JsonUtil.GetInt(entry, "playerId"),
                    name = JsonUtil.GetValue(entry, "name"),
                    generatedName = JsonUtil.GetValue(entry, "generatedName"),
                    accountId = JsonUtil.GetValue(entry, "accountId"),
                    platformId = JsonUtil.GetValue(entry, "platformId"),
                    teamId = JsonUtil.GetInt(entry, "teamId"),
                    squadId = (uint)JsonUtil.GetInt(entry, "squadId"),
                    partyId = JsonUtil.GetValue(entry, "partyId"),
                    isLocal = JsonUtil.GetBool(entry, "isLocal"),
                    isBot = JsonUtil.GetBool(entry, "isBot"),
                    colour = JsonUtil.GetValue(entry, "colour"),
                    pattern = JsonUtil.GetValue(entry, "pattern"),
                    costumeTop = JsonUtil.GetValue(entry, "costumeTop"),
                    costumeBottom = JsonUtil.GetValue(entry, "costumeBottom"),
                    costumeFull = JsonUtil.GetValue(entry, "costumeFull"),
                    faceplate = JsonUtil.GetValue(entry, "faceplate"),
                    victoryPose = JsonUtil.GetValue(entry, "victoryPose"),
                    nickname = JsonUtil.GetValue(entry, "nickname"),
                    nameplate = JsonUtil.GetValue(entry, "nameplate"),
                    fameEarnedBadge = JsonUtil.GetInt(entry, "fameEarnedBadge"),
                    fameUpdatedAt = DateTime.TryParse(JsonUtil.GetValue(entry, "fameUpdatedAt"),
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var fameAt) ? fameAt : default,
                    bfgScale = JsonUtil.GetFloat(entry, "bfgScale"),
                    outTime = JsonUtil.GetFloat(entry, "outTime", -1f),
                    bfgCosmetics = JsonUtil.GetValue(entry, "bfgCosmetics"),
                    bfgColour = JsonUtil.GetValue(entry, "bfgColour"),
                    bfgPattern = JsonUtil.GetValue(entry, "bfgPattern"),
                    bfgFaceplate = JsonUtil.GetValue(entry, "bfgFaceplate"),
                };

                if (JsonUtil.GetBool(entry, "hasNametag"))
                {
                    player.nametag = new BetterFG.Network.RemoteNametagInfo
                    {
                        r = JsonUtil.GetFloat(entry, "ntR"),
                        g = JsonUtil.GetFloat(entry, "ntG"),
                        b = JsonUtil.GetFloat(entry, "ntB"),
                        bold = JsonUtil.GetBool(entry, "ntBold"),
                        italic = JsonUtil.GetBool(entry, "ntItalic"),
                        customName = JsonUtil.GetValue(entry, "ntCustomName"),
                        iconMode = JsonUtil.GetValue(entry, "ntIconMode"),
                        iconCountry = JsonUtil.GetValue(entry, "ntIconCountry"),
                        iconPath = JsonUtil.GetValue(entry, "ntIconPath"),
                        iconScale = JsonUtil.GetFloat(entry, "ntIconScale"),
                        iconOffX = JsonUtil.GetFloat(entry, "ntIconOffX"),
                        iconOffY = JsonUtil.GetFloat(entry, "ntIconOffY"),
                        platformHide = JsonUtil.GetValue(entry, "ntPlatformHide"),
                        platformCustom = JsonUtil.GetValue(entry, "ntPlatformCustom"),
                        nameStyle = JsonUtil.GetValue(entry, "ntNameStyle"),
                        backingEnabled = JsonUtil.GetBool(entry, "ntBackingEnabled"),
                        backingPath = JsonUtil.GetValue(entry, "ntBackingPath"),
                        backingOffX = JsonUtil.GetFloat(entry, "ntBackingOffX"),
                        backingOffY = JsonUtil.GetFloat(entry, "ntBackingOffY"),
                        backingScale = JsonUtil.GetFloat(entry, "ntBackingScale"),
                        nickname = JsonUtil.GetValue(entry, "ntNickname"),
                    };
                }

                foreach (var skin in JsonUtil.GetArray(entry, "bfgSkins"))
                    player.bfgSkins.Add(new BetterFG.Network.RemoteSkinEntry
                    {
                        file = JsonUtil.GetValue(skin, "file"),
                        type = JsonUtil.GetValue(skin, "type"),
                        source = JsonUtil.GetValue(skin, "source"),
                        localPath = JsonUtil.GetValue(skin, "localPath"),
                        repoUrl = JsonUtil.GetValue(skin, "repoUrl"),
                    });

                foreach (var tex in JsonUtil.GetArray(entry, "bfgTextures"))
                {
                    var texEntry = new BetterFG.Customization.Player.SkinTexEntry
                    {
                        entryName = JsonUtil.GetValue(tex, "entryName"),
                        texPath = JsonUtil.GetValue(tex, "texPath"),
                        matIdx = JsonUtil.GetInt(tex, "matIdx"),
                        enabled = true,
                    };

                    foreach (var name in JsonUtil.GetValue(tex, "matNames").Split('|'))
                        if (!string.IsNullOrEmpty(name)) texEntry.matNames.Add(name);

                    player.bfgTextures.Add(texEntry);
                }

                rec.players.Add(player);
            }

            return rec;
        }
    }
}
