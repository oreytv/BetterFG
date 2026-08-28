using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using Il2CppInterop.Runtime;
using Levels.Invisibeans;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    public static class FeatureCustomizeFallGuys
    {
        public static readonly BfgFeature feature = new BfgFeature(
            "customizefallguys",
            "Customize Fall Guys",
            false,
            new List<FeatureSetting>
            {
                new FeatureSetting { id = "blink", label = "Blinking", defaultOn = true },
                new FeatureSetting { id = "look", label = "Look left and right", defaultOn = true },
                new FeatureSetting { id = "squint", label = "Light squinting", defaultOn = false },
                new FeatureSetting { id = "localonly", label = "Only my Fall Guy", defaultOn = false },
            },
            onOpen: OnToggled,
            onClosed: OnToggled,
            onSettingChanged: OnSettingChanged,
            ranges: new List<FeatureRange>
            {
                new FeatureRange { id = "eyedist", label = "Eye distance", min = -0.1f, max = 0.12f, step = 0.01f, defaultValue = 0f, hint = "Pushes the eyes apart, or together. At -0.1 they nearly meet in the middle of the face." },
                new FeatureRange { id = "eyeheight", label = "Eye height", min = -0.1f, max = 0.1f, step = 0.01f, defaultValue = 0f, hint = "Slides both eyes up or down the face." },
                new FeatureRange { id = "eyerot", label = "Eye rotation", min = -45f, max = 45f, step = 1f, defaultValue = 0f, hint = "Tilts the two eyes in opposite directions, in degrees. 0 is stock." },
                new FeatureRange { id = "eyescale", label = "Eye scale", min = 0.1f, max = 4f, step = 0.05f, defaultValue = 1f, hint = "Multiplies eye size. 1 is stock." },
                new FeatureRange { id = "eyeyscale", label = "Eye Y scale", min = 0.1f, max = 4f, step = 0.05f, defaultValue = 1f, hint = "Height only, on top of eye scale. Low squashes the eyes into slits, high stretches them tall." },
            },
            onRangeChanged: OnRangeChanged,
            choices: new List<FeatureChoice>
            {
                new FeatureChoice
                {
                    id = "eyemat",
                    label = "Eye material",
                    optionIds = new List<string> { "none", "crownjam" },
                    optionLabels = new List<string> { "None", "Crown Jam" },
                    defaultId = "none",
                    hint = "Draws a custom eye pass over the bean. Its tint follows that bean's faceplate eye colour.",
                },
            },
            onChoiceChanged: OnChoiceChanged);

        const float BakeFps = 30f;
        const int MaxBakeFrames = 512;

        const float LookOffset = 0.03f;
        const float EyeDrawDistanceSqr = 30f * 30f;
        static bool _inRound;

        public static void SetInRound(bool on)
        {
            if (_inRound == on) return;
            _inRound = on;
            // leaving a round with beans culled would strand them hidden
            if (!on)
                for (int i = 0; i < _count; i++) { _eyes[i].hiddenSet = false; _eyes[i].invisHidden = false; _eyes[i].cullSet = false; }
        }

        internal static bool On;

        static bool _blink, _look, _squint, _localOnly, _work;
        static float _dist, _height, _rot, _scale, _yscale;
        static string _eyeMat;
        static GameObject _previewPanel, _previewBean;

        static void ReadSettings()
        {
            _blink = feature.Get("blink");
            _look = feature.Get("look");
            _squint = feature.Get("squint");
            _localOnly = feature.Get("localonly");
            _dist = feature.GetRange("eyedist");
            _height = feature.GetRange("eyeheight");
            _rot = feature.GetRange("eyerot");
            _scale = feature.GetRange("eyescale");
            _yscale = feature.GetRange("eyeyscale");
            On = feature.enabled;
            Utilities.PatchGate.Request(Customization.Player.InvisibilitySyncComponent.GateKey, "eyes", On);
            _eyeMat = On ? feature.GetChoice("eyemat") : "none";
            _work = On && (_blink || _look || _squint
                || _eyeMat != "none"
                || !Mathf.Approximately(_dist, 0f)
                || !Mathf.Approximately(_height, 0f)
                || !Mathf.Approximately(_rot, 0f)
                || !Mathf.Approximately(_scale, 1f)
                || !Mathf.Approximately(_yscale, 1f));
        }

        struct Eyes
        {
            public Transform l;
            public Transform r;
            public float blinkCursor;
            public float blinkSpeed;
            public float lookFrom;
            public float lookTo;
            public float lookT;
            public float lookRate;
            public float lookHold;
            public float squintPhase;
            public GameObject eyeGo;
            public SkinnedMeshRenderer eyeRenderer;
            public bool hiddenSet;
            public bool hiddenValue;
            public bool cullSet;
            public bool culled;
            public bool invisHidden;
            public SkinnedMeshRenderer body;
            public Material wantMat;
            public InvisibilityVisualsController invis;
            public GameObject root;
            // the preview clone is parked a thousand units under the map, so the distance cull and the
            // isVisible skip would both throw its eyes away every frame
            public bool preview;
            // each bean's own rig can be a different scale (PlayerScaleService, differently-built
            // clones, etc), so the rest pose has to be captured per-bean, not shared off whichever
            // bean happened to get tracked first.
            public Vector3 restPosL, restPosR, restScaleL, restScaleR;
            public Quaternion restRotL, restRotR;
        }

        static Eyes[] _eyes = new Eyes[64];
        static int _count;
        static readonly List<GameObject> _pending = new List<GameObject>();

        static int _bakedFrames;
        static float _playbackRate;
        static Vector3[] _lPos, _lScale, _rPos, _rScale;
        static Quaternion[] _lRot, _rRot;

        static void OnToggled() => Refresh(true);

        static void OnSettingChanged(string id, bool on) => Refresh(true);

        static void OnRangeChanged(string id, float value) => Refresh(true);

        static void OnChoiceChanged(string id, string optionId) => Refresh(true);

        public static Texture PreviewTexture => EyePreview.Ensure();

        internal static bool PreviewWanted
        {
            get
            {
                if (!feature.enabled || _previewPanel == null) return false;
                var ui = BetterFG.UI.BetterFGUIMan.Instance;
                return ui != null && ui.IsVisible;
            }
        }

        public static void SetPreviewPanel(GameObject panel)
        {
            _previewPanel = panel;
            Refresh();
            FallGuyEyeDriver.EnsurePreviewLoop();
        }

        public static void Refresh(bool rebuild = false, float delay = 0f)
        {
            if (delay > 0f)
            {
                FallGuyEyeDriver.Instance.StartCoroutine(RefreshLater(rebuild, delay).WrapToIl2Cpp());
                return;
            }

            if (rebuild) RestoreAll();
            ReadSettings();

            var local = BeanMonitorService.LocalPlayerBean;
            var mm = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            // the preview shows a stock Fall Guy, not the live player - PB_FallGuyBot is always
            // resident (HideAndDontSave), so unlike the local/menu bean it never leaves the preview
            // blank
            _previewBean = GameObjectHelper.FindDefaultBotBean();

            // the clone isn't in the scene sweep below (that's the point of it), so re-queue it by hand
            Apply(EyePreview.Clone);
            if (!_work) return;

            if (_localOnly)
            {
                Apply(local);
                if (mm != null)
                {
                    Apply(mm._menuFallGuy);
                    Apply(mm._lobbyFallGuy);
                }
                return;
            }

            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FallguyCustomisationHandler>());
            for (int i = 0; all != null && i < all.Length; i++)
            {
                var handler = all[i].TryCast<FallguyCustomisationHandler>();
                if (handler == null) continue;
                var go = handler.gameObject;
                if (go.scene.IsValid()) Apply(go);
            }
        }

        public static void Apply(GameObject bean, bool isLocalPlayer = true)
        {
            if (bean == null || !_work) return;
            if (_localOnly && !isLocalPlayer) return;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i] == bean) return;
            _pending.Add(bean);
        }

        public static void ApplyLater(GameObject bean, float delay, bool isLocalPlayer = true)
        {
            if (bean == null || !_work) return;
            if (_localOnly && !isLocalPlayer) return;
            var driver = FallGuyEyeDriver.Instance;
            if (driver == null) { Apply(bean, isLocalPlayer); return; }
            driver.StartCoroutine(ApplyAfter(bean, delay, isLocalPlayer).WrapToIl2Cpp());
        }

        static IEnumerator ApplyAfter(GameObject bean, float delay, bool isLocalPlayer)
        {
            yield return new WaitForSeconds(delay);
            Apply(bean, isLocalPlayer);
        }

        internal static void OnInvisibilityVisuals(Levels.Invisibeans.InvisibilityVisualsController controller, bool hidden)
        {
            if (!_work) return;
            for (int i = 0; i < _count; i++)
            {
                var invis = _eyes[i].invis;
                if (invis is null || invis.m_CachedPtr != controller.m_CachedPtr) continue;
                if (_eyes[i].invisHidden == hidden) return;
                _eyes[i].invisHidden = hidden;
                if (hidden) return;
                Apply(_eyes[i].root);
                ApplyLater(_eyes[i].root, 0.5f);
                return;
            }
        }

        public static void ReassertOn(CellBehaviour cell)
        {
            var fg = cell != null ? cell._fallGuy : null;
            if (fg != null) ReassertOn(fg.gameObject);
        }

        public static void ReassertOn(GameObject bean)
        {
            if (!_work || _eyeMat == "none" || bean == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (_eyes[i].root != bean) continue;
                EyeGeometry.Reassert(_eyes[i].eyeRenderer, _eyes[i].body, _eyes[i].wantMat);
                return;
            }
        }

        public static void OnFaceplateChanged(GameObject bean, FaceplateOption option)
        {
            if (!_work || _eyeMat == "none" || bean == null || option == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (_eyes[i].root != bean) continue;
                EyeGeometry.SetTint(_eyes[i].eyeRenderer, option.eyesColour);
                _eyes[i].wantMat = _eyes[i].eyeRenderer.sharedMaterial;
                var fgch = bean.GetComponent<FallguyCustomisationHandler>();
                BlankStockEyes(fgch == null ? null : fgch._matInstance, option.eyesColour);
                return;
            }
        }

        static IEnumerator RefreshLater(bool rebuild, float delay)
        {
            yield return new WaitForSeconds(delay);
            Refresh(rebuild);
        }

        static void RestoreAll()
        {
            for (int i = 0; i < _count; i++)
            {
                var l = _eyes[i].l;
                var r = _eyes[i].r;
                if (l.m_CachedPtr != IntPtr.Zero && r.m_CachedPtr != IntPtr.Zero)
                {
                    l.SetLocalPositionAndRotation(_eyes[i].restPosL, _eyes[i].restRotL);
                    l.localScale = _eyes[i].restScaleL;
                    r.SetLocalPositionAndRotation(_eyes[i].restPosR, _eyes[i].restRotR);
                    r.localScale = _eyes[i].restScaleR;
                }
                EyeGeometry.Detach(_eyes[i].eyeGo);
                _eyes[i].eyeGo = null;
                _eyes[i].eyeRenderer = null;
                _eyes[i].body = null;
                _eyes[i].wantMat = null;
                _eyes[i].invis = null;
                _eyes[i].root = null;
                _eyes[i].l = null;
                _eyes[i].r = null;
            }
            _count = 0;
            _pending.Clear();
            RestoreStockEyes();
        }

        static GameObject AttachEyes(GameObject bean, out SkinnedMeshRenderer body)
        {
            body = null;
            if (_eyeMat == "none") return null;

            var shared = AssetManager.GetMaterial("bettrfg_mat_eyes_" + _eyeMat);
            if (shared == null)
            {
                Plugin.Log.LogWarning($"eye material '{_eyeMat}' isn't in any loaded bundle, leaving that bean's face alone");
                return null;
            }

            var attached = EyeGeometry.Attach(bean, shared, Color.white, out body);
            if (attached == null) return null;

            // the preview clone has no customisation handler by design, but it shares the bean's
            // material instance, so the body renderer is the honest place to read the eye colour from
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            var mat = fgch == null ? body.sharedMaterial : fgch._matInstance;
            int eyes = FallguyCustomisationHandler.ShaderEyesColor;
            if (mat != null && mat.HasProperty(eyes))
            {
                var tint = StockEyes(mat, eyes);
                EyeGeometry.SetTint(attached.GetComponent<SkinnedMeshRenderer>(), tint);
                BlankStockEyes(mat, tint);
            }
            return attached;
        }

        static readonly List<Material> _stockMats = new List<Material>();
        static readonly List<Color> _stockEyes = new List<Color>();

        static Color StockEyes(Material mat, int eyes)
        {
            for (int i = 0; i < _stockMats.Count; i++)
                if (_stockMats[i] != null && _stockMats[i].m_CachedPtr == mat.m_CachedPtr) return _stockEyes[i];
            return mat.GetColor(eyes);
        }

        static void BlankStockEyes(Material mat, Color stock)
        {
            if (mat == null) return;
            int eyes = FallguyCustomisationHandler.ShaderEyesColor;
            int face = FallguyCustomisationHandler.ShaderFaceColor;
            if (!mat.HasProperty(eyes) || !mat.HasProperty(face)) return;

            for (int i = 0; i < _stockMats.Count; i++)
                if (_stockMats[i] != null && _stockMats[i].m_CachedPtr == mat.m_CachedPtr)
                {
                    _stockEyes[i] = stock;
                    mat.SetColor(eyes, mat.GetColor(face));
                    return;
                }

            _stockMats.Add(mat);
            _stockEyes.Add(stock);
            mat.SetColor(eyes, mat.GetColor(face));
            Plugin.Log.LogInfo($"sank the painted eye into the face colour on {mat.name}, was {stock}");
        }

        static void RestoreStockEyes()
        {
            int eyes = FallguyCustomisationHandler.ShaderEyesColor;
            for (int i = 0; i < _stockMats.Count; i++)
                if (_stockMats[i] != null) _stockMats[i].SetColor(eyes, _stockEyes[i]);
            _stockMats.Clear();
            _stockEyes.Clear();
        }

        static void Track(GameObject root)
        {
            if (root == null) return;

            var eyeL = GameObjectHelper.FindBoneOnBean(root, "Eye_L_jnt");
            var eyeR = GameObjectHelper.FindBoneOnBean(root, "Eye_R_jnt");
            if (eyeL == null || eyeR == null) return;

            for (int i = 0; i < _count; i++)
            {
                if (_eyes[i].l.m_CachedPtr != eyeL.m_CachedPtr) continue;
                if (_eyeMat == "none") return;
                var fresh = AttachEyes(root, out var freshBody);
                if (fresh == null) return;
                EyeGeometry.Detach(_eyes[i].eyeGo);
                _eyes[i].eyeGo = fresh;
                _eyes[i].eyeRenderer = fresh.GetComponent<SkinnedMeshRenderer>();
                _eyes[i].wantMat = _eyes[i].eyeRenderer != null ? _eyes[i].eyeRenderer.sharedMaterial : null;
                _eyes[i].body = freshBody;
                _eyes[i].hiddenSet = false;
                _eyes[i].cullSet = false;
                return;
            }

            Vector3 restPosL, restPosR, restScaleL, restScaleR;
            Quaternion restRotL, restRotR;
            eyeL.GetLocalPositionAndRotation(out restPosL, out restRotL);
            eyeR.GetLocalPositionAndRotation(out restPosR, out restRotR);
            restScaleL = eyeL.localScale;
            restScaleR = eyeR.localScale;

            if (_bakedFrames == 0 && AssetManager.Instance != null
                && AssetManager.Instance.animClips.TryGetValue("bettrfg_anim_eyes", out var clip) && clip != null)
            {
                var skel = eyeL;
                while (skel != null && skel.name != "SKELETON") skel = skel.parent;
                if (skel != null && skel.parent != null) Bake(clip, skel.parent.gameObject, eyeL, eyeR);
            }

            if (_count == _eyes.Length) Array.Resize(ref _eyes, _count * 2);
            _eyes[_count].eyeGo = AttachEyes(root, out var newBody);
            _eyes[_count].eyeRenderer = _eyes[_count].eyeGo != null ? _eyes[_count].eyeGo.GetComponent<SkinnedMeshRenderer>() : null;
            _eyes[_count].wantMat = _eyes[_count].eyeRenderer != null ? _eyes[_count].eyeRenderer.sharedMaterial : null;
            _eyes[_count].body = newBody;
            _eyes[_count].hiddenSet = false;
            _eyes[_count].cullSet = false;
            _eyes[_count].invisHidden = false;
            _eyes[_count].invis = root.GetComponentInChildren<InvisibilityVisualsController>();
            _eyes[_count].root = root;
            // preview beans sit parked miles from any game camera - without this the distance/visibility
            // cull in ApplyEyes disables the overlay renderer and freezes its blink/look, so the pet
            // tab's render-texture shot shows only the blanked stock (faceplate-coloured) eyes
            _eyes[_count].preview = root == EyePreview.Clone || root == BetterFG.Customization.Pets.PetPreview.Clone;
            _eyes[_count].l = eyeL;
            _eyes[_count].r = eyeR;
            _eyes[_count].restPosL = restPosL; _eyes[_count].restRotL = restRotL; _eyes[_count].restScaleL = restScaleL;
            _eyes[_count].restPosR = restPosR; _eyes[_count].restRotR = restRotR; _eyes[_count].restScaleR = restScaleR;
            _eyes[_count].blinkSpeed = UnityEngine.Random.Range(0.8f, 1.25f) * _playbackRate;
            _eyes[_count].blinkCursor = UnityEngine.Random.Range(0f, Mathf.Max(1, _bakedFrames));
            _eyes[_count].lookFrom = 0f;
            _eyes[_count].lookTo = 0f;
            _eyes[_count].lookT = 1f;
            _eyes[_count].lookRate = 6f;
            _eyes[_count].lookHold = UnityEngine.Random.Range(0.2f, 2f);
            _eyes[_count].squintPhase = UnityEngine.Random.Range(0f, 6.28f);
            _count++;
        }

        static void Bake(AnimationClip clip, GameObject host, Transform eyeL, Transform eyeR)
        {
            float len = clip.length;
            if (len <= 0f) return;

            int frames = Mathf.Clamp(Mathf.CeilToInt(len * BakeFps), 2, MaxBakeFrames);
            _lPos = new Vector3[frames]; _lRot = new Quaternion[frames]; _lScale = new Vector3[frames];
            _rPos = new Vector3[frames]; _rRot = new Quaternion[frames]; _rScale = new Vector3[frames];

            var t = host.transform;
            t.GetLocalPositionAndRotation(out var hostPos, out var hostRot);
            var hostScale = t.localScale;

            for (int i = 0; i < frames; i++)
            {
                clip.SampleAnimation(host, len * i / frames);
                eyeL.GetLocalPositionAndRotation(out _lPos[i], out _lRot[i]);
                eyeR.GetLocalPositionAndRotation(out _rPos[i], out _rRot[i]);
                _lScale[i] = eyeL.localScale;
                _rScale[i] = eyeR.localScale;
            }

            t.SetLocalPositionAndRotation(hostPos, hostRot);
            t.localScale = hostScale;

            _bakedFrames = frames;
            _playbackRate = frames / len;
            Plugin.Log.LogInfo($"blink clip baked once, {frames} frames off {len:0.00}s, no more per-bean SampleAnimation");
        }

        public static void Tick(float dt)
        {
            if (_pending.Count > 0)
            {
                int last = _pending.Count - 1;
                var next = _pending[last];
                _pending.RemoveAt(last);
                if (next != null && _work) Track(next);
            }

            ApplyEyes(dt);
        }

        public static void TickPreview()
        {
            if (!feature.enabled) return;
            var ui = BetterFG.UI.BetterFGUIMan.Instance;
            if (ui == null || !ui.IsVisible) return;
            if (_previewPanel == null || !_previewPanel.activeInHierarchy || _previewBean == null) return;
            EyePreview.SetBean(_previewBean);
            EyePreview.Render();
        }

        static void ApplyEyes(float dt)
        {
            if (_count == 0 || !_work) return;

            bool blink = _blink && _bakedFrames > 0;
            float now = Time.time;

            // eyes are a couple of centimetres across. past ~30m they're subpixel, but the renderer was
            // still costing a draw call and a skinning pass for every bean on the course — and in a
            // 30-player round most of them are miles away. one camera fetch a tick, one distance test
            // per bean, and everything out of range stops rendering entirely.
            //
            // ROUND ONLY. in the menu/lobby Camera.main isn't the camera framing the plinth bean, so the
            // distance came out huge and the eyes vanished — the menu has at most a couple of beans and
            // nothing to save anyway.
            var cam = _inRound ? Camera.main : null;
            bool haveCam = cam != null;
            Vector3 camPos = haveCam ? cam.transform.position : Vector3.zero;
            bool hasRot = !Mathf.Approximately(_rot, 0f);
            var rotL = hasRot ? Quaternion.Euler(0f, 0f, _rot) : Quaternion.identity;
            var rotR = hasRot ? Quaternion.Euler(0f, 0f, -_rot) : Quaternion.identity;
            int frameStagger = Time.frameCount;

            for (int i = 0; i < _count; i++)
            {
                var l = _eyes[i].l;
                var r = _eyes[i].r;
                if (l.m_CachedPtr == IntPtr.Zero || r.m_CachedPtr == IntPtr.Zero)
                {
                    EyeGeometry.Detach(_eyes[i].eyeGo);
                    _eyes[i].eyeGo = null;
                    _count--;
                    _eyes[i] = _eyes[_count];
                    _eyes[_count].l = null;
                    _eyes[_count].r = null;
                    i--;
                    continue;
                }

                var eyeRenderer = _eyes[i].eyeRenderer;
                bool hasRenderer = eyeRenderer is not null && eyeRenderer.m_CachedPtr != IntPtr.Zero;
                if (hasRenderer)
                {
                    bool hidden = _eyes[i].invisHidden;

                    // the range test only has to keep up with a bean walking towards the camera, so it
                    // runs on one bean in eight per frame (staggered, so the work is spread evenly) and
                    // reuses the last answer in between. get_position boxes its return through il2cpp;
                    // the paired getter writes straight into our locals.
                    if (!hidden && !_eyes[i].preview)
                    {
                        if (((i + frameStagger) & 7) == 0 || !_eyes[i].cullSet)
                        {
                            bool cull = BaseHidden(_eyes[i].body);
                            if (!cull && haveCam)
                            {
                                l.GetPositionAndRotation(out var eyePos, out _);
                                float dx = eyePos.x - camPos.x, dy = eyePos.y - camPos.y, dz = eyePos.z - camPos.z;
                                cull = dx * dx + dy * dy + dz * dz > EyeDrawDistanceSqr;
                            }
                            _eyes[i].culled = cull;
                            _eyes[i].cullSet = true;
                        }
                        hidden = _eyes[i].culled;
                    }
                    // re-writing .enabled every frame was a renderer setter per bean for a value that
                    // only moves when a powerup does
                    if (!_eyes[i].hiddenSet || _eyes[i].hiddenValue != hidden)
                    {
                        _eyes[i].hiddenSet = true;
                        _eyes[i].hiddenValue = hidden;
                        eyeRenderer.enabled = !hidden;
                    }
                }

                Vector3 pl = _eyes[i].restPosL, pr = _eyes[i].restPosR, sl = _eyes[i].restScaleL, sr = _eyes[i].restScaleR;
                Quaternion ql = _eyes[i].restRotL, qr = _eyes[i].restRotR;

                if (blink)
                {
                    float c = _eyes[i].blinkCursor + dt * _eyes[i].blinkSpeed;
                    _eyes[i].blinkCursor = c;

                    int idx = (int)Mathf.PingPong(c, _bakedFrames - 1);
                    pl = _lPos[idx]; ql = _lRot[idx]; sl = _lScale[idx];
                    pr = _rPos[idx]; qr = _rRot[idx]; sr = _rScale[idx];
                }

                float glance = _look ? StepLook(ref _eyes[i], dt) * LookOffset : 0f;
                float squint = _squint
                    ? 1f - 0.2f * (0.55f + 0.45f * Mathf.Sin(now * 0.7f + _eyes[i].squintPhase))
                    : 1f;

                // blink/look/squint state above is plain managed maths and keeps running, so a bean
                // that comes back into view is already where it should be. the four writes below are
                // the expensive part — every one boxes a struct through il2cpp — and nobody can see
                // the eyes of a bean that isn't on screen.
                if (hasRenderer && !_eyes[i].preview && !eyeRenderer.isVisible) continue;

                pl.x += glance - _dist;
                pr.x += glance + _dist;
                pl.y += _height;
                pr.y += _height;

                float yl = sl.y * _scale * _yscale * squint;
                float yr = sr.y * _scale * _yscale * squint;

                l.SetLocalPositionAndRotation(pl, hasRot ? ql * rotL : ql);
                r.SetLocalPositionAndRotation(pr, hasRot ? qr * rotR : qr);

                l.localScale = new Vector3(sl.x * _scale, yl, sl.z * _scale);
                r.localScale = new Vector3(sr.x * _scale, yr, sr.z * _scale);
            }
        }

        static bool BaseHidden(SkinnedMeshRenderer body)
        {
            if (body is null || body.m_CachedPtr == IntPtr.Zero) return false;
            if (!body.gameObject.activeInHierarchy || !body.enabled) return true;
            var invis = Customization.Player.CostumePollerComponent.PeekInvisibleMat();
            if (invis is null) return false;
            var mat = body.sharedMaterial;
            return mat is not null && mat.m_CachedPtr == invis.m_CachedPtr;
        }

        static float StepLook(ref Eyes e, float dt)
        {
            if (e.lookT < 1f)
            {
                e.lookT = Mathf.Min(1f, e.lookT + dt * e.lookRate);
                if (e.lookT >= 1f) e.lookHold = UnityEngine.Random.Range(0.5f, 2.8f);
                return Mathf.Lerp(e.lookFrom, e.lookTo, Mathf.SmoothStep(0f, 1f, e.lookT));
            }

            float held = e.lookTo;
            e.lookHold -= dt;
            if (e.lookHold <= 0f)
            {
                e.lookFrom = held;
                e.lookTo = UnityEngine.Random.Range(-1f, 1f);
                e.lookT = 0f;
                e.lookRate = 1f / UnityEngine.Random.Range(0.12f, 0.24f);
            }
            return held;
        }
    }

    public class FallGuyEyeDriver : MonoBehaviour
    {
        public FallGuyEyeDriver(IntPtr ptr) : base(ptr) { }

        public static FallGuyEyeDriver Instance { get; private set; }

        void Awake() => Instance = this;

        static bool _previewLoopRunning;

        // was started once in Awake and span forever on WaitForEndOfFrame, whether or not the feature
        // was on and whether or not the panel existed — a per-frame il2cpp coroutine resume for the
        // entire session, menus included, with TickPreview early-outing every time. now it only lives
        // while the BettrFG panel is actually open in front of the preview.
        public static void EnsurePreviewLoop()
        {
            if (_previewLoopRunning || Instance == null) return;
            if (!FeatureCustomizeFallGuys.PreviewWanted) return;
            _previewLoopRunning = true;
            Instance.StartCoroutine(PreviewLoop().WrapToIl2Cpp());
        }

        // runs EVERY frame, deliberately. ticking the eye pass at 30Hz to save the transform writes made
        // the eyes visibly jitter against a body rendering at 165+ — the blink source frames are baked at
        // 30fps but the look/squint drifts and the interpolation between them are not. tried 2026-08-21.
        void LateUpdate()
        {
            if (!FeatureCustomizeFallGuys.On) return;
            FeatureCustomizeFallGuys.Tick(Time.deltaTime);
        }

        static IEnumerator PreviewLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (FeatureCustomizeFallGuys.PreviewWanted)
            {
                yield return endOfFrame;
                FeatureCustomizeFallGuys.TickPreview();
            }
            // panel's gone, so is the reason to keep a whole spare bean skinning off-screen
            EyePreview.Invalidate();
            _previewLoopRunning = false;
        }
    }
}
