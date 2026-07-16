using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Lingmai.RedMist
{
    public sealed class MinigameSuite : MonoBehaviour
    {
        private enum ActiveGame
        {
            None,
            Flight,
            Scan,
            Herb,
            Formation
        }

        private sealed class FlightEntity
        {
            public RectTransform rect;
            public Image warningHalo;
            public bool danger;
            public bool nearMissCredited;
            public float speed;
            public float spin;
        }

        private sealed class ScanEntity
        {
            public RectTransform rect;
            public RawImage art;
            public Image halo;
            public Image progress;
            public Vector2 position;
            public Vector2 velocity;
            public bool harmful;
            public float age;
            public float lifetime;
            public float phase;
            public float lockProgress;
        }

        private sealed class HerbEntity
        {
            public RectTransform rect;
            public RawImage blossom;
            public Image halo;
            public Image progress;
            public Vector2 basePosition;
            public bool stable;
            public float phase;
            public float gatherProgress;
        }

        private sealed class FormationNode
        {
            public string label;
            public RectTransform rect;
            public RawImage art;
            public Image halo;
            public Image innerRing;
            public Image reactionRing;
            public Vector2 basePosition;
            public Color baseColor;
            public float angle;
            public float phase;
            public bool decoy;
            public bool locked;
            public bool feedbackError;
            public float feedbackUntil;
        }

        private static readonly string[] FormationExpected = { "水面", "旧铜", "药叶" };
        private static readonly string[] FormationClues = { "潮声回映", "旧铜低鸣", "药叶承光" };
        private static Sprite _ringSprite;
        private static Sprite _discSprite;

        private GameObject _root;
        private RectTransform _playfield;
        private Text _title;
        private Text _instructions;
        private Text _status;
        private Text _timerText;
        private Image _feedbackFlash;
        private ActiveGame _active;
        private Action<MinigameResult> _callback;
        private Coroutine _finishRoutine;
        private bool _finishing;
        private int _sessionId;
        private float _remaining;
        private float _spawnClock;
        private int _score;
        private int _errors;
        private int _clicks;
        private Vector2 _lastMousePosition;

        private RectTransform _flightPlayer;
        private RectTransform _playfieldBackdrop;
        private VideoPlayer _flightVideoPlayer;
        private RenderTexture _flightVideoTexture;
        private RawImage _flightVideoImage;
        private Image _flightEnergyFill;
        private Image _flightStormWarning;
        private Text _flightBoostLabel;
        private Text _flightWarningText;
        private float _flightEnergy;
        private float _flightSpeedFactor;
        private float _flightImpactShake;
        private float _flightCombo;
        private int _flightNearMisses;
        private RectTransform _scanReticle;
        private Image _scanReticleRing;
        private Image _scanPulse;
        private float _scanExposure;
        private float _scanOverloadCooldown;
        private RectTransform _herbLens;
        private Image _herbLensRing;
        private Image _herbPulse;
        private RectTransform _formationBeam;
        private RawImage _formationOrb;
        private float _formationAngle;
        private float _formationBeamLength;
        private int _formationStep;
        private Image _formationSource;
        private Image _formationSourceHalo;

        private Texture2D _flightScene;
        private Texture2D _herbScene;
        private Texture2D _vaultScene;
        private Texture2D _flyingSword;
        private Texture2D _sealBoulder;
        private Texture2D _thornArch;
        private Texture2D _spiritCrystal;

        private readonly List<FlightEntity> _flightEntities = new List<FlightEntity>();
        private readonly List<RectTransform> _flightSpeedLines = new List<RectTransform>();
        private readonly List<ScanEntity> _scanEntities = new List<ScanEntity>();
        private readonly List<HerbEntity> _herbEntities = new List<HerbEntity>();
        private readonly List<FormationNode> _formationNodes = new List<FormationNode>();
        private readonly List<string> _formationSequence = new List<string>();
        private readonly List<RectTransform> _formationRings = new List<RectTransform>();
        private readonly List<Image> _formationLinks = new List<Image>();

        public bool IsActive => _active != ActiveGame.None;

        public void Initialize(Transform parent)
        {
            _flightScene = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.CelestialStormRoute);
            _herbScene = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.HerbCourtyard);
            _vaultScene = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.SwordSealVault);
            _flyingSword = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.FlyingSword);
            _sealBoulder = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.SealBoulder);
            _thornArch = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.ThornArch);
            _spiritCrystal = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.SpiritCrystal);

            Image shade = UIFactory.Panel("MinigameOverlay", parent, new Color(0.015f, 0.025f, 0.035f, 1f));
            _root = shade.gameObject;
            UIFactory.Stretch(shade.rectTransform);

            _title = UIFactory.Label("Title", _root.transform, "小游戏", 38, TextAnchor.MiddleCenter, UIFactory.Hex("#f0e6c8"), FontStyle.Bold);
            UIFactory.Anchor(_title.rectTransform, new Vector2(0.15f, 0.89f), new Vector2(0.85f, 0.98f), Vector2.zero, Vector2.zero);

            _instructions = UIFactory.Label("Instructions", _root.transform, "", 21, TextAnchor.MiddleCenter, UIFactory.Hex("#c9d8d0"));
            UIFactory.Anchor(_instructions.rectTransform, new Vector2(0.10f, 0.80f), new Vector2(0.90f, 0.89f), Vector2.zero, Vector2.zero);

            _timerText = UIFactory.Label("Timer", _root.transform, "", 25, TextAnchor.MiddleRight, UIFactory.Hex("#f0c879"), FontStyle.Bold);
            UIFactory.Anchor(_timerText.rectTransform, new Vector2(0.77f, 0.90f), new Vector2(0.95f, 0.97f), Vector2.zero, Vector2.zero);

            Image field = UIFactory.Panel("Playfield", _root.transform, new Color(0.008f, 0.014f, 0.020f, 1f));
            _playfield = field.rectTransform;
            UIFactory.Anchor(_playfield, new Vector2(0.045f, 0.16f), new Vector2(0.955f, 0.81f), Vector2.zero, Vector2.zero);

            _status = UIFactory.Label("Status", _root.transform, "", 24, TextAnchor.MiddleCenter, UIFactory.Hex("#dfe9df"), FontStyle.Bold);
            UIFactory.Anchor(_status.rectTransform, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.16f), Vector2.zero, Vector2.zero);

            _feedbackFlash = UIFactory.Panel("InteractionFlash", _root.transform, Color.clear);
            _feedbackFlash.raycastTarget = false;
            UIFactory.Stretch(_feedbackFlash.rectTransform);
            _root.SetActive(false);
        }

        public void BeginFlight(Action<MinigameResult> callback)
        {
            Begin(ActiveGame.Flight, "御剑破云", "WASD／方向键或按住鼠标移动；按住 Shift／空格消耗灵力冲刺。避开雷障，擦身而过可积累御风连击。", 24f, callback);
            AddPlayfieldBackdrop(_flightScene, new Color(0.48f, 0.58f, 0.70f, 0.11f));
            AddFlightVideoBackdrop();
            CreateFlightMotionLayer();
            RawImage player = UIFactory.Raw("FlyingSword", _playfield, Color.white);
            player.texture = _flyingSword;
            player.raycastTarget = false;
            _flightPlayer = player.rectTransform;
            _flightPlayer.sizeDelta = new Vector2(300, 118);
            _flightPlayer.anchoredPosition = new Vector2(-500, 0);
            _flightPlayer.localRotation = Quaternion.Euler(0, 0, -4);
            _flightEnergy = 1f;
            _flightSpeedFactor = 1f;
            _flightImpactShake = 0f;
            _flightCombo = 0f;
            _flightNearMisses = 0;
            _status.text = "灵气 0　受击 0　御风连击 0";
        }

        public void BeginScan(Action<MinigameResult> callback)
        {
            Begin(ActiveGame.Scan, "神识侦察", "移动准星，按住鼠标左键／空格锁定稳定灵息；避开抖动、泛赤的反噬回波，并控制暴露。", 20f, callback);
            AddPlayfieldBackdrop(_vaultScene, new Color(0.03f, 0.08f, 0.10f, 0.42f));
            _scanReticle = CreateReticle("DivineSenseReticle", UIFactory.Hex("#6fe8ce"), 126f, out _scanReticleRing, out _scanPulse);
            _scanReticle.anchoredPosition = Vector2.zero;
            _scanExposure = 0f;
            _scanOverloadCooldown = 0f;
            SpawnScanEntity(false);
            SpawnScanEntity(false);
            SpawnScanEntity(false);
            SpawnScanEntity(true);
            SpawnScanEntity(true);
            _spawnClock = 1.2f;
            _status.text = "已确认 0／5　神识暴露 0%";
        }

        public void BeginHerb(Action<MinigameResult> callback)
        {
            Begin(ActiveGame.Herb, "灵药采集", "移动观察镜，按住鼠标左键／空格采集呼吸稳定的灵植；避开急促抖动、泛赤雾气的赝品。", 25f, callback);
            AddPlayfieldBackdrop(_herbScene, new Color(0.04f, 0.10f, 0.07f, 0.30f));
            _herbLens = CreateReticle("HerbObservationLens", UIFactory.Hex("#b5ef88"), 142f, out _herbLensRing, out _herbPulse);
            _herbLens.anchoredPosition = Vector2.zero;
            CreateHerbEntity(true, new Vector2(-0.58f, 0.24f), 0.4f);
            CreateHerbEntity(true, new Vector2(-0.16f, -0.18f), 2.1f);
            CreateHerbEntity(true, new Vector2(0.38f, 0.20f), 4.2f);
            CreateHerbEntity(false, new Vector2(-0.46f, -0.38f), 1.2f);
            CreateHerbEntity(false, new Vector2(0.58f, -0.28f), 3.3f);
            _status.text = "完整灵药 0／3　误触 0";
        }

        public void BeginFormation(Action<MinigameResult> callback)
        {
            Begin(ActiveGame.Formation, "四象星枢阵", "A／D旋转月阳光束，也可移动鼠标瞄准阵枢。悬停会唤醒阵纹；空格／左键注入灵力，依回响点亮三枢，青石必须沉寂。", 32f, callback);
            Texture2D formationScene = Resources.Load<Texture2D>(GeneratedArtCatalog.CelestialFormationHall);
            AddPlayfieldBackdrop(formationScene != null ? formationScene : _vaultScene, new Color(0.025f, 0.055f, 0.095f, 0.24f));
            CreateFormationBoard();
            _status.text = "第一重回响：" + FormationClues[0] + "　光路 — → — → —";
        }

        private void Begin(ActiveGame game, string title, string instructions, float time, Action<MinigameResult> callback)
        {
            _sessionId++;
            if (_finishRoutine != null)
            {
                StopCoroutine(_finishRoutine);
                _finishRoutine = null;
            }
            ClearPlayfield();
            _active = game;
            _callback = callback;
            _remaining = time;
            _score = 0;
            _errors = 0;
            _clicks = 0;
            _spawnClock = 0f;
            _finishing = false;
            _formationSequence.Clear();
            _title.text = title;
            _instructions.text = instructions;
            _timerText.text = time > 0 ? Mathf.CeilToInt(time) + " 秒" : "";
            _status.fontSize = 24;
            _feedbackFlash.color = Color.clear;
            _lastMousePosition = (Vector2)Input.mousePosition;
            _root.SetActive(true);
        }

        private void AddPlayfieldBackdrop(Texture2D texture, Color veilColor)
        {
            RawImage backdrop = UIFactory.Raw("EnvironmentPlate", _playfield, Color.white);
            backdrop.texture = texture;
            backdrop.raycastTarget = false;
            UIFactory.Stretch(backdrop.rectTransform);
            AspectCropRawImage crop = backdrop.gameObject.AddComponent<AspectCropRawImage>();
            crop.Refresh(true);
            backdrop.transform.SetAsFirstSibling();
            _playfieldBackdrop = backdrop.rectTransform;

            Image veil = UIFactory.Panel("GameplayVeil", _playfield, veilColor);
            veil.raycastTarget = false;
            UIFactory.Stretch(veil.rectTransform);
        }

        private void AddFlightVideoBackdrop()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Media", "fmv_celestial_flight.mp4");
            if (!File.Exists(path)) return;

            _flightVideoImage = UIFactory.Raw("FlightVideoPlate", _playfield, new Color(1f, 1f, 1f, 0f));
            _flightVideoImage.raycastTarget = false;
            UIFactory.Stretch(_flightVideoImage.rectTransform);
            _flightVideoImage.transform.SetSiblingIndex(Mathf.Min(1, _playfield.childCount - 1));
            AspectCropRawImage crop = _flightVideoImage.gameObject.AddComponent<AspectCropRawImage>();

            _flightVideoTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
            {
                name = "FlightVideoTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _flightVideoTexture.Create();
            _flightVideoImage.texture = _flightVideoTexture;
            crop.Refresh(true);

            _flightVideoPlayer = _flightVideoImage.gameObject.AddComponent<VideoPlayer>();
            _flightVideoPlayer.playOnAwake = false;
            _flightVideoPlayer.waitForFirstFrame = true;
            _flightVideoPlayer.skipOnDrop = true;
            _flightVideoPlayer.isLooping = true;
            _flightVideoPlayer.source = VideoSource.Url;
            _flightVideoPlayer.url = path;
            _flightVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _flightVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _flightVideoPlayer.targetTexture = _flightVideoTexture;
            _flightVideoPlayer.prepareCompleted += OnFlightVideoPrepared;
            _flightVideoPlayer.errorReceived += OnFlightVideoError;
            _flightVideoPlayer.Prepare();
        }

        private void OnFlightVideoPrepared(VideoPlayer player)
        {
            if (player != _flightVideoPlayer || _active != ActiveGame.Flight) return;
            _flightVideoImage.color = Color.white;
            player.Play();
        }

        private void OnFlightVideoError(VideoPlayer player, string message)
        {
            if (player != _flightVideoPlayer) return;
            if (_flightVideoImage != null) _flightVideoImage.color = Color.clear;
            Debug.LogWarning("Flight video fallback activated: " + message);
        }

        private void CreateFlightMotionLayer()
        {
            _flightSpeedLines.Clear();
            for (int i = 0; i < 18; i++)
            {
                Image line = UIFactory.Panel("WindPressureLine", _playfield, new Color(0.70f, 0.92f, 1f, UnityEngine.Random.Range(0.10f, 0.30f)));
                line.raycastTarget = false;
                RectTransform rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(UnityEngine.Random.Range(80f, 260f), UnityEngine.Random.Range(1.5f, 4f));
                rt.anchoredPosition = RandomPointInPlayfield(0f, 8f);
                _flightSpeedLines.Add(rt);
            }

            Image energyTrack = UIFactory.Panel("SwordQiTrack", _playfield, new Color(0.015f, 0.035f, 0.055f, 0.82f));
            energyTrack.raycastTarget = false;
            RectTransform trackRt = energyTrack.rectTransform;
            trackRt.anchorMin = trackRt.anchorMax = new Vector2(0f, 1f);
            trackRt.pivot = new Vector2(0f, 1f);
            trackRt.anchoredPosition = new Vector2(24f, -22f);
            trackRt.sizeDelta = new Vector2(300f, 20f);

            _flightEnergyFill = UIFactory.Panel("SwordQiFill", energyTrack.transform, UIFactory.Hex("#67e9dc"));
            _flightEnergyFill.raycastTarget = false;
            _flightEnergyFill.type = Image.Type.Filled;
            _flightEnergyFill.fillMethod = Image.FillMethod.Horizontal;
            _flightEnergyFill.fillOrigin = 0;
            _flightEnergyFill.fillAmount = 1f;
            UIFactory.Stretch(_flightEnergyFill.rectTransform);

            _flightBoostLabel = UIFactory.Label("BoostLabel", _playfield, "剑气推进", 16, TextAnchor.MiddleLeft, new Color(0.86f, 0.98f, 1f, 0.95f), FontStyle.Bold);
            RectTransform labelRt = _flightBoostLabel.rectTransform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0f, 1f);
            labelRt.pivot = new Vector2(0f, 1f);
            labelRt.anchoredPosition = new Vector2(26f, -44f);
            labelRt.sizeDelta = new Vector2(300f, 26f);

            _flightStormWarning = UIFactory.Panel("StormWarning", _playfield, new Color(1f, 0.45f, 0.18f, 0f));
            _flightStormWarning.raycastTarget = false;
            RectTransform warningRt = _flightStormWarning.rectTransform;
            warningRt.anchorMin = new Vector2(0.985f, 0.08f);
            warningRt.anchorMax = new Vector2(1f, 0.92f);
            warningRt.offsetMin = warningRt.offsetMax = Vector2.zero;
            _flightWarningText = UIFactory.Label("StormWarningText", _playfield, "雷脉逼近", 18, TextAnchor.MiddleCenter, new Color(1f, 0.78f, 0.42f, 0f), FontStyle.Bold);
            RectTransform warningTextRt = _flightWarningText.rectTransform;
            warningTextRt.anchorMin = warningTextRt.anchorMax = new Vector2(0.91f, 0.92f);
            warningTextRt.sizeDelta = new Vector2(150f, 30f);
            warningTextRt.anchoredPosition = Vector2.zero;
        }

        private void ReleaseFlightVideo()
        {
            if (_flightVideoPlayer != null)
            {
                _flightVideoPlayer.prepareCompleted -= OnFlightVideoPrepared;
                _flightVideoPlayer.errorReceived -= OnFlightVideoError;
                _flightVideoPlayer.Stop();
                _flightVideoPlayer.targetTexture = null;
            }
            if (_flightVideoImage != null) _flightVideoImage.texture = null;
            if (_flightVideoTexture != null)
            {
                _flightVideoTexture.Release();
                Destroy(_flightVideoTexture);
            }
            _flightVideoPlayer = null;
            _flightVideoTexture = null;
            _flightVideoImage = null;
        }

        private void Update()
        {
            if (_active == ActiveGame.None || _finishing) return;
            AnimateBackdrop();
            switch (_active)
            {
                case ActiveGame.Flight: UpdateFlight(); break;
                case ActiveGame.Scan: UpdateScan(); break;
                case ActiveGame.Herb: UpdateHerb(); break;
                case ActiveGame.Formation: UpdateFormation(); break;
            }
        }

        private void AnimateBackdrop()
        {
            if (_playfieldBackdrop == null) return;
            float t = Time.unscaledTime;
            float shake = _active == ActiveGame.Flight ? _flightImpactShake * 12f : 0f;
            _playfieldBackdrop.anchoredPosition = new Vector2(
                Mathf.Sin(t * 0.11f) * 12f + Mathf.Sin(t * 37f) * shake,
                Mathf.Cos(t * 0.08f) * 7f + Mathf.Cos(t * 43f) * shake * 0.55f);
            float flightPush = _active == ActiveGame.Flight ? (_flightSpeedFactor - 1f) * 0.025f : 0f;
            float scale = 1.025f + flightPush + Mathf.Sin(t * 0.10f) * 0.008f;
            _playfieldBackdrop.localScale = new Vector3(scale, scale, 1f);
            if (_flightVideoImage != null)
            {
                float videoScale = 1.012f + flightPush;
                _flightVideoImage.rectTransform.localScale = new Vector3(videoScale, videoScale, 1f);
                _flightVideoImage.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(t * 33f) * shake * 0.35f, Mathf.Cos(t * 39f) * shake * 0.22f);
            }
        }

        private void UpdateFlight()
        {
            float dt = Time.unscaledDeltaTime;
            _remaining -= dt;
            _spawnClock -= dt;
            UpdateTimer();

            bool wantsBoost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.Space);
            bool boosting = wantsBoost && _flightEnergy > 0.025f;
            _flightEnergy = Mathf.Clamp01(_flightEnergy + (boosting ? -0.25f : 0.12f) * dt);
            _flightSpeedFactor = Mathf.MoveTowards(_flightSpeedFactor, boosting ? 1.72f : 1f, dt * (boosting ? 3.4f : 2.1f));
            _flightImpactShake = Mathf.Max(0f, _flightImpactShake - dt * 2.8f);
            _flightCombo = Mathf.Max(0f, _flightCombo - dt * 0.11f);
            if (_flightEnergyFill != null)
            {
                _flightEnergyFill.fillAmount = _flightEnergy;
                _flightEnergyFill.color = Color.Lerp(UIFactory.Hex("#ef8b45"), UIFactory.Hex("#67e9dc"), _flightEnergy);
            }
            if (_flightBoostLabel != null)
            {
                _flightBoostLabel.text = boosting ? "剑气推进 ×" + _flightSpeedFactor.ToString("0.0") : _flightEnergy < 0.2f ? "剑气回流中" : "剑气推进（Shift／空格）";
                _flightBoostLabel.color = boosting ? new Color(1f, 0.86f, 0.46f, 1f) : new Color(0.86f, 0.98f, 1f, 0.95f);
            }

            if (_spawnClock <= 0f)
            {
                _spawnClock = Mathf.Lerp(0.70f, 0.48f, (_flightSpeedFactor - 1f) / 0.72f);
                SpawnFlightEntity(UnityEngine.Random.value < 0.62f);
            }

            Vector2 movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (Input.GetMouseButton(0))
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_playfield, Input.mousePosition, null, out Vector2 local);
                movement = (local - _flightPlayer.anchoredPosition).normalized;
            }
            _flightPlayer.anchoredPosition += movement.normalized * (Mathf.Lerp(430f, 610f, (_flightSpeedFactor - 1f) / 0.72f) * dt);
            Vector2 p = ClampToPlayfield(_flightPlayer.anchoredPosition, 115f, 50f);
            _flightPlayer.anchoredPosition = p;
            float bank = Mathf.Clamp(movement.y * 13f, -13f, 13f) - (boosting ? 3f : 0f);
            _flightPlayer.localRotation = Quaternion.Lerp(_flightPlayer.localRotation, Quaternion.Euler(0f, 0f, bank), dt * 7f);
            float swordScale = 1f + (_flightSpeedFactor - 1f) * 0.12f + Mathf.Sin(Time.unscaledTime * 16f) * 0.015f;
            _flightPlayer.localScale = new Vector3(swordScale, swordScale, 1f);

            for (int i = 0; i < _flightSpeedLines.Count; i++)
            {
                RectTransform line = _flightSpeedLines[i];
                if (line == null) continue;
                line.anchoredPosition += Vector2.left * (620f + i * 17f) * _flightSpeedFactor * dt;
                line.sizeDelta = new Vector2(Mathf.Lerp(90f, 330f, (_flightSpeedFactor - 1f) / 0.72f), line.sizeDelta.y);
                Image lineImage = line.GetComponent<Image>();
                if (lineImage != null)
                {
                    float alpha = Mathf.Lerp(0.10f, 0.48f, (_flightSpeedFactor - 1f) / 0.72f) * (0.7f + (i % 4) * 0.1f);
                    lineImage.color = new Color(0.67f, 0.92f, 1f, alpha);
                }
                if (line.anchoredPosition.x < PlayfieldRect.xMin - line.sizeDelta.x)
                {
                    line.anchoredPosition = new Vector2(PlayfieldRect.xMax + UnityEngine.Random.Range(20f, 260f), UnityEngine.Random.Range(PlayfieldRect.yMin + 8f, PlayfieldRect.yMax - 8f));
                }
            }

            bool stormThreat = false;
            for (int i = _flightEntities.Count - 1; i >= 0; i--)
            {
                FlightEntity entity = _flightEntities[i];
                entity.rect.anchoredPosition += Vector2.left * entity.speed * _flightSpeedFactor * dt;
                entity.rect.Rotate(0f, 0f, entity.spin * dt);
                float forwardDistance = entity.rect.anchoredPosition.x - _flightPlayer.anchoredPosition.x;
                float laneDistance = Mathf.Abs(entity.rect.anchoredPosition.y - _flightPlayer.anchoredPosition.y);
                if (entity.danger && forwardDistance > 0f && forwardDistance < 520f && laneDistance < 150f) stormThreat = true;
                if (entity.warningHalo != null)
                {
                    float warning = entity.danger ? Mathf.Clamp01(1f - Mathf.Max(0f, forwardDistance) / 520f) : 0f;
                    entity.warningHalo.color = new Color(1f, 0.34f, 0.15f, warning * (0.24f + Mathf.PingPong(Time.unscaledTime * 4.5f, 0.56f)));
                    entity.warningHalo.rectTransform.localScale = Vector3.one * (1f + warning * (0.12f + Mathf.PingPong(Time.unscaledTime * 1.8f, 0.22f)));
                }
                if (Vector2.Distance(entity.rect.anchoredPosition, _flightPlayer.anchoredPosition) < (entity.danger ? 82f : 58f))
                {
                    if (entity.danger)
                    {
                        _errors++;
                        _flightCombo = 0f;
                        _flightImpactShake = 1f;
                        _flightEnergy = Mathf.Max(0f, _flightEnergy - 0.20f);
                        PlayImpactFlash(UIFactory.Hex("#b9362f"), 0.24f);
                    }
                    else
                    {
                        _score++;
                        _flightCombo = Mathf.Min(9f, _flightCombo + 0.7f);
                        _flightEnergy = Mathf.Min(1f, _flightEnergy + 0.10f);
                        PlayImpactFlash(UIFactory.Hex("#56d6b1"), 0.16f);
                    }
                    Destroy(entity.rect.gameObject);
                    _flightEntities.RemoveAt(i);
                    continue;
                }
                if (entity.danger && !entity.nearMissCredited && forwardDistance < -88f && laneDistance < 132f)
                {
                    entity.nearMissCredited = true;
                    _flightNearMisses++;
                    _flightCombo = Mathf.Min(9f, _flightCombo + 1f);
                    _flightEnergy = Mathf.Min(1f, _flightEnergy + 0.16f);
                    PlayImpactFlash(UIFactory.Hex("#f0c95f"), 0.17f);
                }
                if (entity.rect.anchoredPosition.x < PlayfieldRect.xMin - 120f)
                {
                    Destroy(entity.rect.gameObject);
                    _flightEntities.RemoveAt(i);
                }
            }

            float warningAlpha = stormThreat ? 0.30f + Mathf.PingPong(Time.unscaledTime * 3.8f, 0.55f) : 0f;
            if (_flightStormWarning != null) _flightStormWarning.color = new Color(1f, 0.38f, 0.12f, warningAlpha);
            if (_flightWarningText != null) _flightWarningText.color = new Color(1f, 0.78f, 0.42f, warningAlpha);
            _status.text = "灵气 " + _score + "　受击 " + _errors + "　御风连击 " + Mathf.FloorToInt(_flightCombo) + "　擦身 " + _flightNearMisses;

            if (_remaining <= 0f)
            {
                bool success = _errors <= 4;
                bool perfect = success && _errors == 0 && _score >= 7 && _flightNearMisses >= 2;
                FinishWithFeedback(new MinigameResult(success, perfect, _score * 10 + _flightNearMisses * 6 - _errors * 8,
                    perfect ? "剑光贴着雷脉穿云而过，御风连击未断。" : success ? "成功御剑越过天隘，惊险避开了大部分雷障。" : "雷锁连续命中，只能压低剑光强行落地。"));
            }
        }

        private void SpawnFlightEntity(bool danger)
        {
            bool useThorns = danger && UnityEngine.Random.value < 0.45f;
            Texture2D texture = danger ? (useThorns ? _thornArch : _sealBoulder) : _spiritCrystal;
            RawImage image = UIFactory.Raw(danger ? (useThorns ? "ThornArch" : "SealBoulder") : "SpiritCrystal", _playfield, Color.white);
            image.texture = texture;
            image.raycastTarget = false;
            RectTransform rt = image.rectTransform;
            if (danger)
            {
                float scale = UnityEngine.Random.Range(0.82f, 1.18f);
                rt.sizeDelta = useThorns ? new Vector2(105f, 175f) * scale : new Vector2(145f, 145f) * scale;
            }
            else rt.sizeDelta = new Vector2(72f, 72f);
            Image warningHalo = null;
            if (danger)
            {
                warningHalo = CreateImage("LightningWarningHalo", rt, RingSprite, new Color(1f, 0.34f, 0.15f, 0f), Mathf.Max(rt.sizeDelta.x, rt.sizeDelta.y) * 1.18f);
                warningHalo.transform.SetAsFirstSibling();
            }
            rt.anchoredPosition = new Vector2(PlayfieldRect.xMax + 100f, UnityEngine.Random.Range(PlayfieldRect.yMin + 35f, PlayfieldRect.yMax - 35f));
            rt.localRotation = danger ? Quaternion.Euler(0, 0, UnityEngine.Random.Range(-9f, 9f)) : Quaternion.identity;
            _flightEntities.Add(new FlightEntity
            {
                rect = rt,
                warningHalo = warningHalo,
                danger = danger,
                speed = UnityEngine.Random.Range(390, 570),
                spin = danger ? UnityEngine.Random.Range(-5f, 5f) : UnityEngine.Random.Range(12f, 22f)
            });
        }

        private void UpdateScan()
        {
            float dt = Time.unscaledDeltaTime;
            _remaining -= dt;
            _spawnClock -= dt;
            _scanOverloadCooldown = Mathf.Max(0f, _scanOverloadCooldown - dt);
            UpdateTimer();
            MoveReticle(_scanReticle, 440f, dt, 62f);

            bool channeling = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);
            _scanPulse.color = new Color(0.35f, 1f, 0.83f, channeling ? 0.52f : 0.10f);
            float pulseScale = channeling ? 1.1f + Mathf.PingPong(Time.unscaledTime * 1.8f, 0.42f) : 1f;
            _scanPulse.rectTransform.localScale = Vector3.one * pulseScale;
            _scanReticleRing.color = Color.Lerp(UIFactory.Hex("#6fe8ce"), UIFactory.Hex("#ff6158"), _scanExposure);

            ScanEntity focused = FindFocusedScanEntity();
            if (channeling)
            {
                _scanExposure += dt * (focused != null && focused.harmful ? 0.50f : focused != null ? 0.16f : 0.10f);
            }
            else _scanExposure = Mathf.Max(0f, _scanExposure - dt * 0.20f);

            Rect bounds = PlayfieldRect;
            for (int i = _scanEntities.Count - 1; i >= 0; i--)
            {
                ScanEntity entity = _scanEntities[i];
                entity.age += dt;
                entity.position += entity.velocity * dt;
                BounceWithin(ref entity.position, ref entity.velocity, bounds, 58f);
                Vector2 jitter = entity.harmful
                    ? new Vector2(Mathf.Sin(Time.unscaledTime * 19f + entity.phase), Mathf.Cos(Time.unscaledTime * 23f + entity.phase)) * 8f
                    : new Vector2(Mathf.Sin(Time.unscaledTime * 0.9f + entity.phase), Mathf.Cos(Time.unscaledTime * 0.7f + entity.phase)) * 3f;
                entity.rect.anchoredPosition = entity.position + jitter;
                float breathe = entity.harmful
                    ? 0.92f + Mathf.Sin(Time.unscaledTime * 8f + entity.phase) * 0.13f
                    : 1f + Mathf.Sin(Time.unscaledTime * 1.8f + entity.phase) * 0.07f;
                entity.rect.localScale = Vector3.one * breathe;
                bool isFocused = entity == focused;
                entity.halo.color = entity.harmful
                    ? new Color(1f, 0.22f, 0.16f, isFocused ? 0.72f : 0.26f)
                    : new Color(0.31f, 1f, 0.79f, isFocused ? 0.78f : 0.28f);

                if (channeling && isFocused)
                {
                    entity.lockProgress += dt / (entity.harmful ? 0.42f : 0.86f);
                }
                else entity.lockProgress = Mathf.Max(0f, entity.lockProgress - dt * 1.4f);
                entity.progress.fillAmount = Mathf.Clamp01(entity.lockProgress);

                if (entity.lockProgress >= 1f)
                {
                    if (entity.harmful)
                    {
                        _errors++;
                        _scanExposure = Mathf.Min(1f, _scanExposure + 0.28f);
                        PlayImpactFlash(UIFactory.Hex("#c63c35"), 0.32f);
                    }
                    else
                    {
                        _score++;
                        _clicks++;
                        _scanExposure = Mathf.Max(0f, _scanExposure - 0.16f);
                        PlayImpactFlash(UIFactory.Hex("#4bd5ae"), 0.18f);
                    }
                    RemoveScanEntity(i);
                    _spawnClock = Mathf.Min(_spawnClock, 0.35f);
                    continue;
                }

                if (entity.age >= entity.lifetime)
                {
                    RemoveScanEntity(i);
                    continue;
                }
            }

            if (_spawnClock <= 0f && _scanEntities.Count < 6)
            {
                _spawnClock = UnityEngine.Random.Range(0.75f, 1.15f);
                SpawnScanEntity(UnityEngine.Random.value < 0.34f);
            }

            if (_scanExposure >= 1f && _scanOverloadCooldown <= 0f)
            {
                _errors++;
                _scanExposure = 0.42f;
                _scanOverloadCooldown = 1.3f;
                PlayImpactFlash(UIFactory.Hex("#df302d"), 0.42f);
            }
            _status.text = "已确认 " + _score + "／5　反噬 " + _errors + "　神识暴露 " + Mathf.RoundToInt(_scanExposure * 100f) + "%";

            if (_remaining <= 0f)
            {
                bool success = _score >= 3;
                bool perfect = _score >= 5 && _errors == 0;
                FinishWithFeedback(new MinigameResult(success, perfect, _score * 18 - _errors * 12,
                    perfect ? "确认袭击方向、残留火性与第二层陷阱，且未留下神识特征。" : success ? "找到主要伏击方向，但探查者可能察觉到微弱神识。" : "线索不足，只能把前方视为高风险区域。"));
            }
        }

        private void SpawnScanEntity(bool harmful)
        {
            GameObject root = UIFactory.CreateUIObject(harmful ? "BacklashEcho" : "StableBreath", _playfield);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(harmful ? 118f : 96f, harmful ? 118f : 96f);
            Vector2 position = RandomPointInPlayfield(100f, 78f);
            rt.anchoredPosition = position;

            Image halo = CreateImage("SignalHalo", rt, RingSprite,
                harmful ? new Color(1f, 0.2f, 0.16f, 0.28f) : new Color(0.3f, 1f, 0.78f, 0.28f),
                harmful ? 118f : 104f);
            RawImage art = UIFactory.Raw("SignalMatter", rt, harmful ? new Color(1f, 0.35f, 0.28f, 0.96f) : new Color(0.55f, 1f, 0.82f, 0.96f));
            art.texture = harmful ? _sealBoulder : _spiritCrystal;
            art.raycastTarget = false;
            art.rectTransform.sizeDelta = new Vector2(harmful ? 82f : 68f, harmful ? 82f : 68f);
            Image progress = CreateProgressRing("LockProgress", rt, harmful ? UIFactory.Hex("#ff564d") : UIFactory.Hex("#a9ffe1"), harmful ? 126f : 112f);

            Vector2 velocity = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(harmful ? 60f : 28f, harmful ? 105f : 58f);
            _scanEntities.Add(new ScanEntity
            {
                rect = rt,
                art = art,
                halo = halo,
                progress = progress,
                position = position,
                velocity = velocity,
                harmful = harmful,
                lifetime = UnityEngine.Random.Range(5.5f, 8.5f),
                phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
            });
        }

        private ScanEntity FindFocusedScanEntity()
        {
            ScanEntity result = null;
            float best = 84f;
            for (int i = 0; i < _scanEntities.Count; i++)
            {
                float distance = Vector2.Distance(_scanReticle.anchoredPosition, _scanEntities[i].rect.anchoredPosition);
                if (distance < best)
                {
                    best = distance;
                    result = _scanEntities[i];
                }
            }
            return result;
        }

        private void RemoveScanEntity(int index)
        {
            if (index < 0 || index >= _scanEntities.Count) return;
            Destroy(_scanEntities[index].rect.gameObject);
            _scanEntities.RemoveAt(index);
        }

        private void UpdateHerb()
        {
            float dt = Time.unscaledDeltaTime;
            _remaining -= dt;
            UpdateTimer();
            MoveReticle(_herbLens, 410f, dt, 70f);
            bool gathering = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);
            _herbPulse.color = new Color(0.63f, 1f, 0.46f, gathering ? 0.50f : 0.10f);
            _herbPulse.rectTransform.localScale = Vector3.one * (gathering ? 1.06f + Mathf.PingPong(Time.unscaledTime * 1.4f, 0.38f) : 1f);

            HerbEntity focused = FindFocusedHerb();
            _herbLensRing.color = focused != null && !focused.stable ? UIFactory.Hex("#ff6655") : UIFactory.Hex("#b5ef88");
            for (int i = _herbEntities.Count - 1; i >= 0; i--)
            {
                HerbEntity herb = _herbEntities[i];
                float speed = herb.stable ? 1.5f : 8.5f;
                Vector2 sway = herb.stable
                    ? new Vector2(Mathf.Sin(Time.unscaledTime * speed + herb.phase) * 7f, Mathf.Sin(Time.unscaledTime * 1.1f + herb.phase) * 4f)
                    : new Vector2(Mathf.Sin(Time.unscaledTime * speed + herb.phase) * 14f, Mathf.Cos(Time.unscaledTime * 10.5f + herb.phase) * 8f);
                herb.rect.anchoredPosition = herb.basePosition + sway;
                float breathe = herb.stable
                    ? 1f + Mathf.Sin(Time.unscaledTime * 1.7f + herb.phase) * 0.08f
                    : 0.92f + Mathf.Sin(Time.unscaledTime * 9f + herb.phase) * 0.14f;
                herb.rect.localScale = Vector3.one * breathe;
                bool isFocused = herb == focused;
                herb.halo.color = herb.stable
                    ? new Color(0.55f, 1f, 0.38f, isFocused ? 0.82f : 0.30f)
                    : new Color(1f, 0.25f, 0.16f, isFocused ? 0.82f : 0.34f);

                if (gathering && isFocused) herb.gatherProgress += dt / (herb.stable ? 1.12f : 0.48f);
                else herb.gatherProgress = Mathf.Max(0f, herb.gatherProgress - dt * 1.25f);
                herb.progress.fillAmount = Mathf.Clamp01(herb.gatherProgress);

                if (herb.gatherProgress >= 1f)
                {
                    _clicks++;
                    if (herb.stable)
                    {
                        _score++;
                        PlayImpactFlash(UIFactory.Hex("#72d75b"), 0.24f);
                        Destroy(herb.rect.gameObject);
                        _herbEntities.RemoveAt(i);
                        if (_score >= 3)
                        {
                            bool perfect = _errors == 0;
                            FinishWithFeedback(new MinigameResult(true, perfect, _score * 30 - _errors * 15,
                                perfect ? "辨出真药并完整收束根须，采集没有破坏药性。" : "采得完整灵药，但途中触发过引兽粉回响。"));
                            return;
                        }
                    }
                    else
                    {
                        _errors++;
                        herb.gatherProgress = 0f;
                        herb.phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                        herb.basePosition = RandomPointInPlayfield(110f, 90f);
                        PlayImpactFlash(UIFactory.Hex("#d43b30"), 0.38f);
                    }
                }
            }
            _status.text = "完整灵药 " + _score + "／3　误触 " + _errors + (focused == null ? "　观察镜未对准" : focused.stable ? "　呼吸稳定" : "　灵息紊乱");

            if (_remaining <= 0f)
            {
                bool success = _score >= 2;
                bool perfect = _score >= 3 && _errors == 0;
                FinishWithFeedback(new MinigameResult(success, perfect, _score * 30 - _errors * 15,
                    perfect ? "辨出真药并完整收束根须，采集没有破坏药性。" : success ? "采到可用灵药，但留下气味或损伤了部分药性。" : "误触赝品与引兽粉，只抢救出少量材料。"));
            }
        }

        private void CreateHerbEntity(bool stable, Vector2 normalizedPosition, float phase)
        {
            GameObject root = UIFactory.CreateUIObject(stable ? "BreathingSpiritHerb" : "TurbulentFalseHerb", _playfield);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120f, 150f);
            Vector2 half = new Vector2(PlayfieldRect.width * 0.5f, PlayfieldRect.height * 0.5f);
            Vector2 position = new Vector2(normalizedPosition.x * half.x, normalizedPosition.y * half.y);
            rt.anchoredPosition = position;

            Image halo = CreateImage("HerbHalo", rt, RingSprite,
                stable ? new Color(0.55f, 1f, 0.38f, 0.30f) : new Color(1f, 0.25f, 0.16f, 0.34f), 116f);
            halo.rectTransform.anchoredPosition = new Vector2(0f, 16f);
            CreatePlantStem(rt, stable);
            RawImage blossom = UIFactory.Raw("LivingBlossom", rt, stable ? new Color(0.67f, 1f, 0.56f, 0.98f) : new Color(1f, 0.40f, 0.27f, 0.97f));
            blossom.texture = _spiritCrystal;
            blossom.raycastTarget = false;
            blossom.rectTransform.sizeDelta = new Vector2(72f, 72f);
            blossom.rectTransform.anchoredPosition = new Vector2(0f, 33f);
            Image progress = CreateProgressRing("GatherProgress", rt, stable ? UIFactory.Hex("#c9ff8b") : UIFactory.Hex("#ff5a4e"), 130f);
            progress.rectTransform.anchoredPosition = new Vector2(0f, 16f);

            _herbEntities.Add(new HerbEntity
            {
                rect = rt,
                blossom = blossom,
                halo = halo,
                progress = progress,
                basePosition = position,
                stable = stable,
                phase = phase
            });
        }

        private void CreatePlantStem(RectTransform parent, bool stable)
        {
            Color stemColor = stable ? UIFactory.Hex("#4e9a52") : UIFactory.Hex("#7d4937");
            Image stem = UIFactory.Panel("Stem", parent, stemColor);
            stem.raycastTarget = false;
            stem.rectTransform.sizeDelta = new Vector2(8f, 76f);
            stem.rectTransform.anchoredPosition = new Vector2(0f, -13f);
            for (int side = -1; side <= 1; side += 2)
            {
                Image leaf = CreateImage("Leaf", parent, DiscSprite, stemColor, 42f);
                leaf.rectTransform.sizeDelta = new Vector2(42f, 20f);
                leaf.rectTransform.anchoredPosition = new Vector2(side * 21f, side > 0 ? -2f : -23f);
                leaf.rectTransform.localRotation = Quaternion.Euler(0f, 0f, side * 28f);
            }
        }

        private HerbEntity FindFocusedHerb()
        {
            HerbEntity result = null;
            float best = 94f;
            for (int i = 0; i < _herbEntities.Count; i++)
            {
                float distance = Vector2.Distance(_herbLens.anchoredPosition, _herbEntities[i].rect.anchoredPosition + new Vector2(0f, 16f));
                if (distance < best)
                {
                    best = distance;
                    result = _herbEntities[i];
                }
            }
            return result;
        }

        private void CreateFormationBoard()
        {
            _formationStep = 0;
            _formationAngle = -22f;
            _formationBeamLength = Mathf.Min(PlayfieldRect.width, PlayfieldRect.height) * 0.39f;

            float[] ringSizes = { 184f, 328f, 486f, 628f };
            for (int i = 0; i < ringSizes.Length; i++)
            {
                Color ringColor = i % 2 == 0
                    ? new Color(0.35f, 0.90f, 1f, 0.20f - i * 0.025f)
                    : new Color(1f, 0.76f, 0.30f, 0.18f - i * 0.02f);
                Image ritualRing = CreateImage("CelestialOrbit_" + i, _playfield, RingSprite, ringColor, ringSizes[i]);
                ritualRing.rectTransform.anchoredPosition = Vector2.zero;
                ritualRing.transform.SetSiblingIndex(Mathf.Min(2 + i, _playfield.childCount - 1));
                _formationRings.Add(ritualRing.rectTransform);
            }

            _formationSource = CreateImage("MoonSunSource", _playfield, DiscSprite, new Color(0.90f, 0.84f, 0.54f, 0.94f), 64f);
            _formationSource.rectTransform.anchoredPosition = Vector2.zero;
            _formationSourceHalo = CreateImage("MoonSunHalo", _formationSource.rectTransform, RingSprite, new Color(1f, 0.84f, 0.38f, 0.72f), 112f);
            _formationSourceHalo.raycastTarget = false;
            Image sourceInner = CreateImage("MoonSunInnerOrbit", _formationSource.rectTransform, RingSprite, new Color(0.45f, 0.95f, 1f, 0.70f), 82f);
            sourceInner.raycastTarget = false;

            Image beam = UIFactory.Panel("LiveMoonBeam", _playfield, new Color(0.75f, 0.93f, 1f, 0.74f));
            beam.raycastTarget = false;
            _formationBeam = beam.rectTransform;
            _formationBeam.anchorMin = _formationBeam.anchorMax = new Vector2(0.5f, 0.5f);
            _formationBeam.pivot = new Vector2(0f, 0.5f);
            _formationBeam.sizeDelta = new Vector2(_formationBeamLength, 10f);
            _formationBeam.anchoredPosition = Vector2.zero;

            _formationOrb = UIFactory.Raw("TravellingLight", _playfield, new Color(0.86f, 1f, 0.94f, 1f));
            _formationOrb.texture = _spiritCrystal;
            _formationOrb.raycastTarget = false;
            _formationOrb.rectTransform.sizeDelta = new Vector2(48f, 48f);

            CreateFormationNode("水面", -22f, _spiritCrystal, UIFactory.Hex("#75cfe6"), false, 0.2f);
            CreateFormationNode("旧铜", 42f, _sealBoulder, UIFactory.Hex("#c69558"), false, 1.5f);
            CreateFormationNode("药叶", 134f, _thornArch, UIFactory.Hex("#6dbd70"), false, 3.1f);
            CreateFormationNode("青石", -142f, _sealBoulder, UIFactory.Hex("#66818d"), true, 4.6f);
        }

        private void CreateFormationNode(string label, float angle, Texture2D texture, Color color, bool decoy, float phase)
        {
            GameObject root = UIFactory.CreateUIObject("FormationNode_" + label, _playfield);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(174f, 184f);
            Vector2 position = Direction(angle) * _formationBeamLength;
            rt.anchoredPosition = position;

            Image halo = CreateImage("NodeHalo", rt, RingSprite, new Color(color.r, color.g, color.b, 0.35f), 166f);
            Image reactionRing = CreateImage("ReactionRing", rt, RingSprite, new Color(color.r, color.g, color.b, 0f), 146f);
            Image innerRing = CreateImage("InnerRuneRing", rt, RingSprite, new Color(color.r, color.g, color.b, 0.52f), 112f);
            Image runeDisc = CreateImage("RuneDisc", rt, DiscSprite, new Color(0.015f, 0.035f, 0.055f, 0.78f), 102f);
            RawImage art = UIFactory.Raw("NodeMatter", rt, color);
            art.texture = texture;
            art.raycastTarget = false;
            art.rectTransform.sizeDelta = label == "药叶" ? new Vector2(72f, 104f) : new Vector2(82f, 82f);
            Image namePlate = UIFactory.Panel("NodeNamePlate", rt, new Color(0.01f, 0.025f, 0.045f, 0.88f));
            namePlate.raycastTarget = false;
            namePlate.rectTransform.sizeDelta = new Vector2(106f, 32f);
            namePlate.rectTransform.anchoredPosition = new Vector2(0f, -75f);
            Text nodeLabel = UIFactory.Label("NodeName", namePlate.transform, label, 18, TextAnchor.MiddleCenter, new Color(0.96f, 0.92f, 0.72f, 0.98f), FontStyle.Bold);
            UIFactory.Stretch(nodeLabel.rectTransform);

            _formationNodes.Add(new FormationNode
            {
                label = label,
                rect = rt,
                art = art,
                halo = halo,
                innerRing = innerRing,
                reactionRing = reactionRing,
                basePosition = position,
                baseColor = color,
                angle = angle,
                phase = phase,
                decoy = decoy
            });
        }

        private void UpdateFormation()
        {
            float dt = Time.unscaledDeltaTime;
            _remaining -= dt;
            UpdateTimer();

            float ritualTime = Time.unscaledTime;
            for (int i = 0; i < _formationRings.Count; i++)
            {
                RectTransform ring = _formationRings[i];
                if (ring == null) continue;
                ring.Rotate(0f, 0f, (i % 2 == 0 ? 5f : -7f) * (1f + i * 0.22f) * dt);
                float breathe = 1f + Mathf.Sin(ritualTime * (0.65f + i * 0.14f) + i) * 0.014f;
                ring.localScale = Vector3.one * breathe;
            }
            if (_formationSource != null)
            {
                float sourcePulse = 1f + Mathf.Sin(ritualTime * 4.5f) * 0.075f;
                _formationSource.rectTransform.localScale = Vector3.one * sourcePulse;
            }
            if (_formationSourceHalo != null)
            {
                _formationSourceHalo.rectTransform.Rotate(0f, 0f, 18f * dt);
                _formationSourceHalo.color = new Color(1f, 0.84f, 0.38f, 0.54f + Mathf.Sin(ritualTime * 5f) * 0.20f);
            }
            for (int i = 0; i < _formationLinks.Count; i++)
            {
                Image link = _formationLinks[i];
                if (link == null) continue;
                float alpha = 0.50f + Mathf.Sin(ritualTime * 6f + i * 1.7f) * 0.28f;
                link.color = new Color(1f, 0.78f, 0.27f, alpha);
            }

            float turn = Input.GetAxisRaw("Horizontal");
            _formationAngle += turn * 92f * dt;
            Vector2 mouse = (Vector2)Input.mousePosition;
            if ((mouse - _lastMousePosition).sqrMagnitude > 1f &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_playfield, Input.mousePosition, null, out Vector2 local) && local.sqrMagnitude > 900f)
            {
                _formationAngle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            }
            _lastMousePosition = mouse;
            _formationAngle = Mathf.Repeat(_formationAngle + 180f, 360f) - 180f;
            _formationBeam.localRotation = Quaternion.Euler(0f, 0f, _formationAngle);
            Image beamImage = _formationBeam.GetComponent<Image>();
            float beamPulse = 0.60f + Mathf.Sin(Time.unscaledTime * 8f) * 0.24f;
            beamImage.color = new Color(0.72f, 0.94f, 1f, beamPulse);
            _formationBeam.sizeDelta = new Vector2(_formationBeamLength, 8f + Mathf.PingPong(Time.unscaledTime * 4f, 7f));
            float travel = Mathf.PingPong(Time.unscaledTime * 190f, _formationBeamLength - 24f) + 12f;
            _formationOrb.rectTransform.anchoredPosition = Direction(_formationAngle) * travel;
            _formationOrb.rectTransform.Rotate(0f, 0f, 60f * dt);

            FormationNode aimed = FindAimedFormationNode();
            string expected = _formationStep < FormationExpected.Length ? FormationExpected[_formationStep] : "";
            for (int i = 0; i < _formationNodes.Count; i++)
            {
                FormationNode node = _formationNodes[i];
                bool isAimed = node == aimed;
                bool isExpected = node.label == expected;
                float pulseSpeed = node.decoy ? 10f : isExpected ? 2.2f : 1.3f;
                bool reacting = Time.unscaledTime < node.feedbackUntil;
                float reactionStrength = reacting ? Mathf.Clamp01((node.feedbackUntil - Time.unscaledTime) / 0.8f) : 0f;
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed + node.phase) * (node.decoy ? 0.08f : isExpected ? 0.08f : 0.035f);
                if (isAimed) pulse += 0.12f + Mathf.PingPong(Time.unscaledTime * 2.5f, 0.08f);
                if (reacting) pulse += Mathf.Sin(reactionStrength * Mathf.PI) * (node.feedbackError ? 0.16f : 0.24f);
                node.rect.localScale = Vector3.one * pulse;
                Vector2 jitter = node.decoy
                    ? new Vector2(Mathf.Sin(Time.unscaledTime * 16f), Mathf.Cos(Time.unscaledTime * 19f)) * 5f
                    : Vector2.zero;
                if (reacting && node.feedbackError)
                {
                    jitter += new Vector2(Mathf.Sin(Time.unscaledTime * 48f), Mathf.Cos(Time.unscaledTime * 41f)) * 9f * reactionStrength;
                }
                node.rect.anchoredPosition = node.basePosition + jitter;
                Color reactionColor = node.feedbackError ? UIFactory.Hex("#ff352f") : UIFactory.Hex("#ffe379");
                node.halo.color = reacting
                    ? new Color(reactionColor.r, reactionColor.g, reactionColor.b, 0.70f + reactionStrength * 0.28f)
                    : node.locked
                        ? new Color(1f, 0.83f, 0.34f, 0.94f)
                        : new Color(node.baseColor.r, node.baseColor.g, node.baseColor.b, isAimed ? 0.94f : isExpected ? 0.60f : 0.28f);
                node.innerRing.color = reacting
                    ? new Color(reactionColor.r, reactionColor.g, reactionColor.b, 0.92f)
                    : new Color(node.baseColor.r, node.baseColor.g, node.baseColor.b, isAimed ? 0.92f : node.locked ? 0.86f : 0.46f);
                node.innerRing.rectTransform.Rotate(0f, 0f, (node.decoy ? -34f : 22f) * dt);
                node.reactionRing.color = new Color(reactionColor.r, reactionColor.g, reactionColor.b, reacting ? reactionStrength * 0.88f : isAimed ? 0.32f : 0f);
                node.reactionRing.rectTransform.localScale = Vector3.one * (reacting ? 1f + (1f - reactionStrength) * 0.58f : isAimed ? 1.05f + Mathf.PingPong(Time.unscaledTime, 0.14f) : 1f);
                node.art.color = reacting
                    ? Color.Lerp(node.baseColor, reactionColor, 0.62f)
                    : node.locked ? Color.Lerp(node.baseColor, Color.white, 0.62f) : isAimed ? Color.Lerp(node.baseColor, Color.white, 0.42f) : node.baseColor;
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)) LockFormationNode(aimed);

            if (_remaining <= 0f)
            {
                bool success = _score >= 2;
                bool perfect = _score >= 3 && _errors == 0;
                FinishWithFeedback(new MinigameResult(success, perfect, _score * 30 - _errors * 20,
                    perfect ? "光路闭合，青石阵眼保持沉寂，撤退缺口仍在。" : success ? "铜门开启，但禁制记录了你的灵力特征。" : "阵眼反噬，只能以法力强行撑开铜门。"));
            }
        }

        private FormationNode FindAimedFormationNode()
        {
            FormationNode result = null;
            float best = 16f;
            for (int i = 0; i < _formationNodes.Count; i++)
            {
                FormationNode node = _formationNodes[i];
                if (node.locked) continue;
                float delta = Mathf.Abs(Mathf.DeltaAngle(_formationAngle, node.angle));
                if (delta < best)
                {
                    best = delta;
                    result = node;
                }
            }
            return result;
        }

        private void LockFormationNode(FormationNode node)
        {
            if (node == null)
            {
                _errors++;
                PlayImpactFlash(UIFactory.Hex("#9f302d"), 0.30f);
                _status.text = "灵力落空，阵图泛起赤色反噬。请将光束对准一处阵枢。";
                return;
            }
            string expected = _formationStep < FormationExpected.Length ? FormationExpected[_formationStep] : "";
            if (node.label != expected)
            {
                _errors++;
                node.feedbackUntil = Time.unscaledTime + 0.85f;
                node.feedbackError = true;
                CreateFormationRipple(node, UIFactory.Hex("#ff342f"), false);
                PlayImpactFlash(node.decoy ? UIFactory.Hex("#dd302c") : UIFactory.Hex("#a33a31"), node.decoy ? 0.46f : 0.28f);
                _status.text = node.decoy ? "青石阵眼反噬：它必须保持沉寂。" : "回响不合，月阳光路被弹回。";
                return;
            }

            node.locked = true;
            node.feedbackUntil = Time.unscaledTime + 1.15f;
            node.feedbackError = false;
            _score++;
            _formationSequence.Add(node.label);
            CreatePermanentBeam(node.angle);
            CreateFormationRipple(node, UIFactory.Hex("#ffe47b"), true);
            _formationStep++;
            PlayImpactFlash(UIFactory.Hex("#e7c96b"), 0.30f);
            if (_formationStep >= FormationExpected.Length)
            {
                bool perfect = _errors == 0;
                FinishWithFeedback(new MinigameResult(true, perfect, _score * 30 - _errors * 20,
                    perfect ? "光路闭合，青石阵眼保持沉寂，撤退缺口仍在。" : "铜门开启，但禁制记录了你的灵力特征。"));
                return;
            }
            string route = string.Join(" → ", _formationSequence) + " → " + new string('—', FormationExpected.Length - _formationStep);
            _status.text = "第" + (_formationStep + 1) + "重回响：" + FormationClues[_formationStep] + "　光路 " + route;
        }

        private void CreatePermanentBeam(float angle)
        {
            Image link = UIFactory.Panel("ClosedLightPath", _playfield, new Color(0.96f, 0.79f, 0.30f, 0.72f));
            link.raycastTarget = false;
            RectTransform rt = link.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(_formationBeamLength, 7f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            rt.SetSiblingIndex(Mathf.Min(2, _playfield.childCount - 1));
            _formationLinks.Add(link);

            Image core = UIFactory.Panel("ClosedLightPathCore", link.transform, new Color(1f, 0.96f, 0.72f, 0.92f));
            core.raycastTarget = false;
            RectTransform coreRt = core.rectTransform;
            coreRt.anchorMin = new Vector2(0f, 0.5f);
            coreRt.anchorMax = new Vector2(1f, 0.5f);
            coreRt.pivot = new Vector2(0.5f, 0.5f);
            coreRt.offsetMin = new Vector2(0f, -1.5f);
            coreRt.offsetMax = new Vector2(0f, 1.5f);
        }

        private void CreateFormationRipple(FormationNode node, Color color, bool success)
        {
            Image ripple = CreateImage(success ? "AwakenedHubRipple" : "BacklashRipple", _playfield, RingSprite,
                new Color(color.r, color.g, color.b, 0.92f), success ? 126f : 142f);
            ripple.rectTransform.anchoredPosition = node.basePosition;
            ripple.transform.SetAsLastSibling();
            StartCoroutine(FormationRippleRoutine(ripple, color, success, _sessionId));
        }

        private IEnumerator FormationRippleRoutine(Image ripple, Color color, bool success, int session)
        {
            const float duration = 0.78f;
            Vector2 startSize = ripple.rectTransform.sizeDelta;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                if (session != _sessionId || ripple == null) yield break;
                float p = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - p, 3f);
                float scale = Mathf.Lerp(1f, success ? 2.35f : 1.82f, eased);
                ripple.rectTransform.sizeDelta = startSize * scale;
                ripple.rectTransform.Rotate(0f, 0f, (success ? 72f : -105f) * Time.unscaledDeltaTime);
                ripple.color = new Color(color.r, color.g, color.b, (1f - p) * (success ? 0.92f : 0.78f));
                yield return null;
            }
            if (ripple != null) Destroy(ripple.gameObject);
        }

        private RectTransform CreateReticle(string name, Color color, float size, out Image ring, out Image pulse)
        {
            GameObject root = UIFactory.CreateUIObject(name, _playfield);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);
            pulse = CreateImage("Pulse", rt, RingSprite, new Color(color.r, color.g, color.b, 0.12f), size * 0.90f);
            ring = CreateImage("Ring", rt, RingSprite, new Color(color.r, color.g, color.b, 0.92f), size * 0.72f);
            Image horizontal = UIFactory.Panel("CrossHorizontal", rt, new Color(color.r, color.g, color.b, 0.88f));
            horizontal.raycastTarget = false;
            horizontal.rectTransform.sizeDelta = new Vector2(size * 0.88f, 3f);
            Image vertical = UIFactory.Panel("CrossVertical", rt, new Color(color.r, color.g, color.b, 0.88f));
            vertical.raycastTarget = false;
            vertical.rectTransform.sizeDelta = new Vector2(3f, size * 0.88f);
            Image center = CreateImage("Center", rt, DiscSprite, Color.white, 12f);
            center.raycastTarget = false;
            return rt;
        }

        private void MoveReticle(RectTransform reticle, float speed, float dt, float margin)
        {
            Vector2 mouse = (Vector2)Input.mousePosition;
            bool mouseMoved = (mouse - _lastMousePosition).sqrMagnitude > 1f;
            if (mouseMoved && RectTransformUtility.ScreenPointToLocalPointInRectangle(_playfield, Input.mousePosition, null, out Vector2 local))
            {
                reticle.anchoredPosition = ClampToPlayfield(local, margin, margin);
            }
            else
            {
                Vector2 movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                reticle.anchoredPosition = ClampToPlayfield(reticle.anchoredPosition + movement.normalized * speed * dt, margin, margin);
            }
            _lastMousePosition = mouse;
        }

        private Image CreateImage(string name, Transform parent, Sprite sprite, Color color, float size)
        {
            GameObject go = UIFactory.CreateUIObject(name, parent, typeof(Image));
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        private Image CreateProgressRing(string name, Transform parent, Color color, float size)
        {
            Image progress = CreateImage(name, parent, RingSprite, color, size);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Radial360;
            progress.fillOrigin = 2;
            progress.fillClockwise = true;
            progress.fillAmount = 0f;
            return progress;
        }

        private static Sprite RingSprite
        {
            get
            {
                if (_ringSprite == null) _ringSprite = CreateProceduralSprite(true);
                return _ringSprite;
            }
        }

        private static Sprite DiscSprite
        {
            get
            {
                if (_discSprite == null) _discSprite = CreateProceduralSprite(false);
                return _discSprite;
            }
        }

        private static Sprite CreateProceduralSprite(bool ring)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = ring ? "RuntimeRing" : "RuntimeDisc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (size * 0.5f);
                    float outer = 1f - Mathf.SmoothStep(0.90f, 1f, distance);
                    float alpha = ring ? outer * Mathf.SmoothStep(0.68f, 0.78f, distance) : outer;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        }

        private Rect PlayfieldRect
        {
            get
            {
                Rect rect = _playfield != null ? _playfield.rect : new Rect(-700f, -340f, 1400f, 680f);
                return rect.width > 20f && rect.height > 20f ? rect : new Rect(-700f, -340f, 1400f, 680f);
            }
        }

        private Vector2 ClampToPlayfield(Vector2 point, float horizontalMargin, float verticalMargin)
        {
            Rect rect = PlayfieldRect;
            point.x = Mathf.Clamp(point.x, rect.xMin + horizontalMargin, rect.xMax - horizontalMargin);
            point.y = Mathf.Clamp(point.y, rect.yMin + verticalMargin, rect.yMax - verticalMargin);
            return point;
        }

        private Vector2 RandomPointInPlayfield(float horizontalMargin, float verticalMargin)
        {
            Rect rect = PlayfieldRect;
            return new Vector2(
                UnityEngine.Random.Range(rect.xMin + horizontalMargin, rect.xMax - horizontalMargin),
                UnityEngine.Random.Range(rect.yMin + verticalMargin, rect.yMax - verticalMargin));
        }

        private static void BounceWithin(ref Vector2 position, ref Vector2 velocity, Rect bounds, float margin)
        {
            if (position.x < bounds.xMin + margin || position.x > bounds.xMax - margin)
            {
                velocity.x *= -1f;
                position.x = Mathf.Clamp(position.x, bounds.xMin + margin, bounds.xMax - margin);
            }
            if (position.y < bounds.yMin + margin || position.y > bounds.yMax - margin)
            {
                velocity.y *= -1f;
                position.y = Mathf.Clamp(position.y, bounds.yMin + margin, bounds.yMax - margin);
            }
        }

        private static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private void UpdateTimer()
        {
            _timerText.text = Mathf.CeilToInt(Mathf.Max(0f, _remaining)) + " 秒";
        }

        private void PlayImpactFlash(Color color, float strength)
        {
            StartCoroutine(ImpactFlashRoutine(color, strength, _sessionId));
        }

        private IEnumerator ImpactFlashRoutine(Color color, float strength, int session)
        {
            const float duration = 0.34f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                if (session != _sessionId) yield break;
                float alpha = Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI) * strength;
                _feedbackFlash.color = new Color(color.r, color.g, color.b, alpha);
                yield return null;
            }
            if (session == _sessionId) _feedbackFlash.color = Color.clear;
        }

        private void FinishWithFeedback(MinigameResult result)
        {
            if (_finishing || _active == ActiveGame.None) return;
            _finishing = true;
            _timerText.text = "";
            _instructions.text = result.perfect ? "完美完成" : result.success ? "完成，但留下了代价" : "失败后继续";
            _status.fontSize = 22;
            _status.text = result.summary;
            Color color = result.success ? UIFactory.Hex("#58c995") : UIFactory.Hex("#bd4038");
            _finishRoutine = StartCoroutine(FinishRoutine(result, color, _sessionId));
        }

        private IEnumerator FinishRoutine(MinigameResult result, Color color, int session)
        {
            Vector3 originalScale = _playfield.localScale;
            const float duration = 1.45f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                if (session != _sessionId) yield break;
                float p = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Sin(p * Mathf.PI) * 0.24f;
                _feedbackFlash.color = new Color(color.r, color.g, color.b, alpha);
                _playfield.localScale = originalScale * (1f + Mathf.Sin(p * Mathf.PI) * 0.018f);
                yield return null;
            }
            if (session != _sessionId) yield break;
            _feedbackFlash.color = Color.clear;
            _playfield.localScale = originalScale;
            _finishRoutine = null;
            CompleteEnd(result);
        }

        private void CompleteEnd(MinigameResult result)
        {
            Action<MinigameResult> callback = _callback;
            _callback = null;
            _active = ActiveGame.None;
            _finishing = false;
            _sessionId++;
            _root.SetActive(false);
            ClearPlayfield();
            callback?.Invoke(result);
        }

        private void ClearPlayfield()
        {
            ReleaseFlightVideo();
            _flightEntities.Clear();
            _flightSpeedLines.Clear();
            _scanEntities.Clear();
            _herbEntities.Clear();
            _formationNodes.Clear();
            _formationSequence.Clear();
            _formationRings.Clear();
            _formationLinks.Clear();
            _flightPlayer = null;
            _playfieldBackdrop = null;
            _flightEnergyFill = null;
            _flightStormWarning = null;
            _flightBoostLabel = null;
            _flightWarningText = null;
            _scanReticle = null;
            _scanReticleRing = null;
            _scanPulse = null;
            _herbLens = null;
            _herbLensRing = null;
            _herbPulse = null;
            _formationBeam = null;
            _formationOrb = null;
            _formationSource = null;
            _formationSourceHalo = null;
            if (_playfield == null) return;
            _playfield.localScale = Vector3.one;
            for (int i = _playfield.childCount - 1; i >= 0; i--) Destroy(_playfield.GetChild(i).gameObject);
        }
    }
}
