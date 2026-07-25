using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Lingmai.RedMist
{
    public sealed class VerticalSliceGame : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.018f, 0.027f, 0.037f, 1f);
        private static readonly Color Jade = new Color(0.18f, 0.48f, 0.39f, 1f);
        private static readonly Color JadeHover = new Color(0.26f, 0.67f, 0.50f, 1f);
        private static readonly Color Vermilion = new Color(0.52f, 0.16f, 0.12f, 1f);
        private static readonly Color Gold = new Color(0.82f, 0.66f, 0.32f, 1f);

        private GameState _state = new GameState();
        private Canvas _canvas;
        private RectTransform _screenRoot;
        private RawImage _backdrop;
        private RawImage _videoLayer;
        private RawImage _portrait;
        private AspectCropRawImage _backdropCrop;
        private AspectCropRawImage _videoCrop;
        private AspectCropRawImage _portraitCrop;
        private Image _cinematicWash;
        private Image _flash;
        private Text _cue;
        private GameObject _titleRoot;
        private GameObject _storyRoot;
        private GameObject _settingsRoot;
        private GameObject _journalRoot;
        private GameObject _explorationRoot;
        private GameObject _fmvRoot;
        private GameObject _errorRoot;
        private GameObject _fmvTopBar;
        private GameObject _fmvBottomBar;
        private Image _dialoguePanel;
        private Text _nodeTitle;
        private Text _location;
        private Text _speaker;
        private Text _body;
        private Text _hud;
        private Text _clock;
        private Text _journalText;
        private RawImage _journalArt;
        private AspectRatioFitter _journalArtFitter;
        private Text _journalArtCaption;
        private Text _explorationObjective;
        private Text _explorationPrompt;
        private Text _explorationNotice;
        private Text _fmvTitle;
        private Text _fmvStatus;
        private Text _fmvHint;
        private Text _errorTitle;
        private Text _errorMessage;
        private Button _errorAction;
        private Button _fmvSkip;
        private int _journalArtIndex;
        private Text _settingsSummary;
        private readonly List<Button> _choiceButtons = new List<Button>();
        private readonly List<bool> _choiceAllowed = new List<bool>();
        private Button _voiceButton;
        private Text _voiceStatus;
        private HoldToTalkButton _holdToTalk;
        private PushToTalkController _pushToTalk;
        private UnityMicrophoneGateway _unityMicrophone;
        private UnityAsrClient _asrClient;
        private UnityVoiceInteractionClient _voiceInteractionClient;
        private VoiceInteractionCoordinator _voiceInteractionCoordinator;
        private bool _voiceAvailable;
        private Coroutine _typewriter;
        private Coroutine _explorationNoticeRoutine;
        private string _fullBody;
        private bool _textComplete;
        private AudioDirector _audio;
        private CinematicDirector _cinematic;
        private MediaDirector _media;
        private GenerationRuntimeBootstrap _generationRuntime;
        private LedgerRuntimeContext _ledgerRuntime;
        private AssociationRuntimeContext _associationRuntime;
        private StoryThreadWeaver _storyThreadWeaver;
        private StoryThread _activeStoryThread;
        private MinigameSuite _minigames;
        private SanctuaryPreview _storyStage;
        private bool _exploring;
        private readonly HashSet<string> _explorationInteractions = new HashSet<string>();
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private int _fmvPresentationToken;
        private bool _qaSaveSnapshotTaken;
        private bool _qaHadSave;
        private string _qaSaveSnapshot;
        private bool _fatalContentError;
        private readonly StoryThreadChapterLifetime _associationChapterLifetime =
            new StoryThreadChapterLifetime();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntime()
        {
            if (UnityEngine.Object.FindObjectOfType<VerticalSliceGame>() != null) return;
            new GameObject("RedMistVerticalSlice").AddComponent<VerticalSliceGame>();
        }

        private void Awake()
        {
            if (FindObjectsOfType<VerticalSliceGame>().Length > 1)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            UIFactory.EnsureEventSystem();
            BuildUI();
            StoryBundleLoadResult bundleResult = StoryCatalog.InitializeFromStreamingAssets();
            if (!bundleResult.Success)
            {
                StoryBundleErrorPageModel page = StoryBundleErrorPageModel.From(bundleResult);
                ShowErrorPage(page.Title, page.Message, false);
                return;
            }
            InitializeVoiceCapture();
            _audio = gameObject.AddComponent<AudioDirector>();
            _audio.Initialize(_state.masterVolume);
            _cinematic = gameObject.AddComponent<CinematicDirector>();
            _cinematic.Initialize(_backdrop, _flash, _cue, _screenRoot);
            _media = gameObject.AddComponent<MediaDirector>();
            _media.Initialize(_videoLayer);
            _generationRuntime = gameObject.AddComponent<GenerationRuntimeBootstrap>();
            _generationRuntime.Initialize(_media);
            InitializeLedgerRuntime();
            InitializeAssociationRuntime(bundleResult.Bundle);
            _media.SetVolume(_state.cinematicVolume);
            _media.SourceDimensionsChanged += (_, __) => _videoCrop?.Refresh(true);
            _minigames = gameObject.AddComponent<MinigameSuite>();
            _minigames.Initialize(_canvas.transform);
            _storyStage = gameObject.AddComponent<SanctuaryPreview>();
            _storyStage.BeginEmbedded();
            ShowTitle();
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument == "-smokeRoute") StartCoroutine(BeginSmokeRoute());
                if (argument == "-sanctuaryPreview") StartCoroutine(BeginSanctuaryPreviewFromCommandLine());
                if (argument == "-captureStoryStage") StartCoroutine(CaptureStoryStage());
                if (argument == "-captureArtTour") StartCoroutine(CaptureArtTour());
                if (argument == "-captureExploreTour") StartCoroutine(CaptureExploreTour());
                if (argument == "-captureScanGame") StartCoroutine(CaptureDynamicMinigame("scan"));
                if (argument == "-captureHerbGame") StartCoroutine(CaptureDynamicMinigame("herb"));
                if (argument == "-captureFormationGame") StartCoroutine(CaptureDynamicMinigame("formation"));
                if (argument == "-captureFmvTour") StartCoroutine(CaptureFmvTour());
                if (argument == "-captureFmvBranch") StartCoroutine(CaptureFmvBranch());
            }
        }

        private void InitializeLedgerRuntime()
        {
            try
            {
                // CX-401 installs only the anonymous identity, durable outbox, and optional
                // uploader. Story actions are intentionally not inferred into ledger events;
                // that requires an approved content mapping in a later content review.
                _ledgerRuntime = LedgerRuntimeBootstrap.Initialize(gameObject);
            }
            catch (Exception error) when (
                error is LedgerRuntimeConfigurationException ||
                error is PlayerIdentityException ||
                error is LedgerOutboxException ||
                error is ArgumentException ||
                error is InvalidOperationException)
            {
                _ledgerRuntime = null;
                Debug.LogWarning(
                    "Ledger runtime is unavailable; offline gameplay remains enabled (" +
                    error.GetType().Name + ").");
            }
        }

        private void InitializeAssociationRuntime(StoryBundle bundle)
        {
            _storyThreadWeaver = new StoryThreadWeaver(new StateEffectAtomicChannel());
            try
            {
                string playerId = _ledgerRuntime != null
                    ? _ledgerRuntime.PlayerId
                    : StoryThreadDefaults.OfflinePlayerId;
                _associationRuntime = AssociationRuntimeBootstrap.Initialize(
                    gameObject,
                    playerId,
                    bundle);
            }
            catch (Exception error) when (
                error is AssociationRuntimeConfigurationException ||
                error is StoryThreadContractException ||
                error is ArgumentException ||
                error is InvalidOperationException ||
                error is IOException ||
                error is UnauthorizedAccessException)
            {
                _associationRuntime = null;
                Debug.LogWarning(
                    "StoryThread runtime is unavailable; baseline offline gameplay remains enabled (" +
                    error.GetType().Name + ").");
            }
        }

        private IEnumerator CaptureExploreTour()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            CaptureQaSaveSnapshot();
            StartRoute(PlayerRoute.ShenYan);
            if (_media != null && _media.IsBusy) _media.Skip();
            ShowNode("herb_route", false, false);
            yield return new WaitForSecondsRealtime(0.8f);
            StartExploration();
            yield return new WaitForSecondsRealtime(4.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\exploration-3d.png");
            Debug.Log("RED_MIST_EXPLORATION_CAPTURED active=" + (_storyStage != null && _storyStage.IsExploring));
            yield return new WaitForSecondsRealtime(1.0f);
            RestoreQaSaveSnapshot();
            Application.Quit();
        }

        private IEnumerator CaptureDynamicMinigame(string game)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            CaptureQaSaveSnapshot();
            StartRoute(PlayerRoute.ShenYan);
            if (_media != null && _media.IsBusy) _media.Skip();
            if (game == "scan") _minigames.BeginScan(_ => { });
            else if (game == "herb") _minigames.BeginHerb(_ => { });
            else _minigames.BeginFormation(_ => { });
            yield return new WaitForSecondsRealtime(3.5f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\minigame-" + game + ".png");
            Debug.Log("RED_MIST_DYNAMIC_MINIGAME_CAPTURED game=" + game);
            yield return new WaitForSecondsRealtime(1.0f);
            RestoreQaSaveSnapshot();
            Application.Quit();
        }

        private IEnumerator CaptureFmvTour()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            CaptureQaSaveSnapshot();
            _state = new GameState { route = PlayerRoute.ShenYan, currentNodeId = StoryCatalog.EntryNodeId };
            string[] cues =
            {
                CinematicCatalog.GateArrival,
                CinematicCatalog.CelestialFlight,
                CinematicCatalog.HerbCourtyard,
                CinematicCatalog.SwordVault
            };

            for (int i = 0; i < cues.Length; i++)
            {
                bool finished = false;
                MediaEndReason endReason = MediaEndReason.Error;
                string cue = cues[i];
                PlayFmvPresentation(cue, "FMV 验收　" + (i + 1) + "／" + cues.Length, "本地视频首帧、声音与宽高比检查", reason =>
                {
                    endReason = reason;
                    finished = true;
                });

                float prepareDeadline = Time.realtimeSinceStartup + 12f;
                while (_media != null && _media.IsPreparing && Time.realtimeSinceStartup < prepareDeadline) yield return null;
                if (_media != null && _media.IsPlaying)
                {
                    yield return new WaitForSecondsRealtime(3.0f);
                    ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\fmv-" + (i + 1).ToString("00") + "-" + cue + ".png");
                    yield return new WaitForSecondsRealtime(0.4f);
                    _media.Skip();
                }

                float finishDeadline = Time.realtimeSinceStartup + 3f;
                while (!finished && Time.realtimeSinceStartup < finishDeadline) yield return null;
                Debug.Log("RED_MIST_FMV_CAPTURE cue=" + cue + " reason=" + endReason);
                yield return new WaitForSecondsRealtime(0.4f);
            }
            Debug.Log("RED_MIST_FMV_TOUR_COMPLETE clips=" + cues.Length);
            RestoreQaSaveSnapshot();
            Application.Quit();
        }

        private IEnumerator CaptureFmvBranch()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            CaptureQaSaveSnapshot();
            StartRoute(PlayerRoute.ShenYan);
            float playbackDeadline = Time.realtimeSinceStartup + 18f;
            while (_media != null && _media.IsBusy && Time.realtimeSinceStartup < playbackDeadline) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            CompleteText();
            yield return new WaitForSecondsRealtime(0.4f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\fmv-branch-choice.png");
            yield return new WaitForSecondsRealtime(0.5f);

            // The second prologue choice is the real branch: it skips camp preparation and
            // reaches alliance with the side-gate ambush clue already recorded.
            if (_choiceButtons.Count > 1 && _choiceButtons[1].gameObject.activeSelf && _choiceButtons[1].interactable)
                _choiceButtons[1].onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.8f);
            CompleteText();
            yield return new WaitForSecondsRealtime(0.4f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\fmv-branch-result.png");
            Debug.Log("RED_MIST_FMV_BRANCH_RESULT node=" + _state.currentNodeId +
                      " ambushKnown=" + _state.ambushKnown +
                      " history=" + string.Join(",", _state.choiceHistory));
            yield return new WaitForSecondsRealtime(0.5f);
            RestoreQaSaveSnapshot();
            Application.Quit();
        }

        private IEnumerator CaptureArtTour()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            CaptureQaSaveSnapshot();
            StartRoute(PlayerRoute.ShenYan);
            if (_media != null && _media.IsBusy) _media.Skip();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-01-prologue.png");

            yield return new WaitForSecondsRealtime(1.0f);
            ShowNode("herb_route", false, false);
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-02-herb.png");

            yield return new WaitForSecondsRealtime(1.0f);
            ShowNode("underground", false, false);
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-03-vault.png");

            yield return new WaitForSecondsRealtime(1.0f);
            ShowNode("formation", false, false);
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-04-warden.png");

            yield return new WaitForSecondsRealtime(1.0f);
            ToggleJournal();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-06-journal-weather.png");
            yield return new WaitForEndOfFrame();
            CycleJournalArt();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-07-journal-environment.png");
            yield return new WaitForEndOfFrame();
            CycleJournalArt();
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-08-journal-cavern.png");
            yield return new WaitForEndOfFrame();
            ToggleJournal();

            yield return new WaitForSecondsRealtime(1.0f);
            _minigames.BeginFlight(_ => { });
            yield return new WaitForSecondsRealtime(3.0f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\art-tour-05-flight.png");
            Debug.Log("RED_MIST_ART_TOUR_CAPTURED scenes=5 archiveBoards=3");
            yield return new WaitForSecondsRealtime(1.0f);
            RestoreQaSaveSnapshot();
            Application.Quit();
        }

        private void CaptureQaSaveSnapshot()
        {
            if (_qaSaveSnapshotTaken) return;
            _qaSaveSnapshotTaken = true;
            try
            {
                _qaHadSave = File.Exists(SaveSystem.SavePath);
                _qaSaveSnapshot = _qaHadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
            }
            catch (Exception exception)
            {
                _qaHadSave = false;
                _qaSaveSnapshot = null;
                Debug.LogWarning("RED_MIST_QA_SAVE_SNAPSHOT_FAILED " + exception.Message);
            }
        }

        private void RestoreQaSaveSnapshot()
        {
            if (!_qaSaveSnapshotTaken) return;
            try
            {
                if (_qaHadSave) File.WriteAllText(SaveSystem.SavePath, _qaSaveSnapshot ?? string.Empty);
                else SaveSystem.Delete();
                Debug.Log("RED_MIST_QA_SAVE_RESTORED hadSave=" + _qaHadSave);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("RED_MIST_QA_SAVE_RESTORE_FAILED " + exception.Message);
            }
            finally
            {
                _qaSaveSnapshotTaken = false;
                _qaHadSave = false;
                _qaSaveSnapshot = null;
            }
        }

        private IEnumerator CaptureStoryStage()
        {
            yield return new WaitForSecondsRealtime(12f);
            ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\story-stage-main.png");
            Debug.Log("RED_MIST_STORY_STAGE_CAPTURE_REQUESTED");
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }

        private IEnumerator BeginSanctuaryPreviewFromCommandLine()
        {
            yield return null;
            _storyStage?.FocusStoryNode("title", PlayerRoute.None, true);
        }

        private IEnumerator BeginSmokeRoute()
        {
            yield return null;
            StartRoute(PlayerRoute.ShenYan);
            Debug.Log("RED_MIST_SMOKE_ROUTE_STARTED");
        }

        private void Update()
        {
            if (_pushToTalk != null &&
                (_pushToTalk.Current.State == VoiceCaptureState.AwaitingPermission ||
                 _pushToTalk.Current.State == VoiceCaptureState.Recording))
            {
                _pushToTalk.Tick();
            }

            if (Input.GetKeyUp(KeyCode.V)) CompleteVoiceCapture();
            else if (Input.GetKeyDown(KeyCode.V) && CanUseVoiceCapture()) BeginVoiceCapture();

            if (_media != null && _media.IsBusy)
            {
                if (_fmvStatus != null)
                    _fmvStatus.text = _media.IsPreparing ? "正在准备本地影视片段……" : "";
                if (Input.GetKeyDown(KeyCode.Escape)) _media.Skip();
                return;
            }
            if (_minigames != null && _minigames.IsActive) return;
            if (_exploring) return;
            if (_storyRoot != null && _storyRoot.activeSelf)
            {
                if (!_textComplete && (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(1))) CompleteText();
                else
                {
                    for (int i = 0; i < Mathf.Min(4, _choiceButtons.Count); i++)
                    {
                        bool numberPressed = Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) ||
                                             Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i));
                        if (numberPressed && _choiceButtons[i].gameObject.activeSelf && _choiceButtons[i].interactable)
                            _choiceButtons[i].onClick.Invoke();
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape) && _storyRoot != null && _storyRoot.activeSelf)
            {
                ToggleSettings();
            }
        }

        private void BuildUI()
        {
            _canvas = UIFactory.CreateCanvas("RedMistCanvas");
            DontDestroyOnLoad(_canvas.gameObject);

            Image screen = UIFactory.Panel("ScreenRoot", _canvas.transform, Color.clear);
            _screenRoot = screen.rectTransform;
            UIFactory.Stretch(_screenRoot);
            screen.raycastTarget = false;

            _backdrop = UIFactory.Raw("Backdrop", _screenRoot, Color.white);
            UIFactory.Stretch(_backdrop.rectTransform, -30, -20, -30, -20);
            _backdropCrop = _backdrop.gameObject.AddComponent<AspectCropRawImage>();

            _videoLayer = UIFactory.Raw("VideoLayer", _screenRoot, Color.white);
            UIFactory.Stretch(_videoLayer.rectTransform);
            _videoCrop = _videoLayer.gameObject.AddComponent<AspectCropRawImage>();
            _videoLayer.gameObject.SetActive(false);

            _cinematicWash = UIFactory.Panel("CinematicWash", _screenRoot, new Color(0.015f, 0.025f, 0.035f, 0.28f));
            UIFactory.Stretch(_cinematicWash.rectTransform);
            _cinematicWash.raycastTarget = false;

            _flash = UIFactory.Panel("Flash", _screenRoot, Color.clear);
            UIFactory.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;

            _cue = UIFactory.Label("CinematicCue", _screenRoot, "", 64, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            UIFactory.Anchor(_cue.rectTransform, new Vector2(0.2f, 0.36f), new Vector2(0.8f, 0.68f), Vector2.zero, Vector2.zero);

            BuildTitleUI();
            BuildStoryUI();
            BuildExplorationUI();
            BuildSettingsUI();
            BuildJournalUI();
            BuildFmvUI();
            BuildErrorUI();
        }

        private void BuildFmvUI()
        {
            _fmvRoot = UIFactory.CreateUIObject("InteractiveFmvScreen", _screenRoot);
            UIFactory.Stretch(_fmvRoot.GetComponent<RectTransform>());

            Image black = UIFactory.Panel("FmvLetterboxBackground", _fmvRoot.transform, Color.black);
            UIFactory.Stretch(black.rectTransform);
            black.raycastTarget = true;

            _videoLayer.transform.SetParent(_fmvRoot.transform, false);
            UIFactory.Stretch(_videoLayer.rectTransform);
            _videoLayer.raycastTarget = false;

            Image top = UIFactory.Panel("FmvTopBar", _fmvRoot.transform, new Color(0.01f, 0.015f, 0.02f, 0.72f));
            _fmvTopBar = top.gameObject;
            UIFactory.Anchor(top.rectTransform, new Vector2(0f, 0.91f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _fmvTitle = UIFactory.Label("FmvTitle", top.transform, "影视剧情", 22, TextAnchor.MiddleLeft, UIFactory.Hex("#f0e5c5"), FontStyle.Bold);
            UIFactory.Anchor(_fmvTitle.rectTransform, new Vector2(0.035f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero);
            _fmvStatus = UIFactory.Label("FmvStatus", _fmvRoot.transform, "正在准备本地影视片段……", 24, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            UIFactory.Anchor(_fmvStatus.rectTransform, new Vector2(0.25f, 0.42f), new Vector2(0.75f, 0.58f), Vector2.zero, Vector2.zero);

            _fmvSkip = UIFactory.Button("FmvSkip", top.transform, "跳过  Esc", () => _media?.Skip(), new Color(0.10f, 0.13f, 0.14f, 0.92f), JadeHover, 16);
            UIFactory.Anchor(_fmvSkip.GetComponent<RectTransform>(), new Vector2(0.84f, 0.18f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero);

            Image bottom = UIFactory.Panel("FmvBottomBar", _fmvRoot.transform, new Color(0.01f, 0.015f, 0.02f, 0.66f));
            _fmvBottomBar = bottom.gameObject;
            UIFactory.Anchor(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.105f), Vector2.zero, Vector2.zero);
            _fmvHint = UIFactory.Label("FmvHint", bottom.transform, "场景建立镜头 · 播放结束后进入互动与分支选择", 18, TextAnchor.MiddleCenter, UIFactory.Hex("#d7ded7"));
            UIFactory.Stretch(_fmvHint.rectTransform, 40f, 0f, 40f, 0f);

            _fmvRoot.SetActive(false);
        }

        private void BuildTitleUI()
        {
            _titleRoot = UIFactory.CreateUIObject("TitleScreen", _screenRoot);
            UIFactory.Stretch(_titleRoot.GetComponent<RectTransform>());
            Image veil = UIFactory.Panel("Veil", _titleRoot.transform, new Color(0.01f, 0.02f, 0.03f, 0.56f));
            UIFactory.Stretch(veil.rectTransform);

            Text eyebrow = UIFactory.Label("Eyebrow", _titleRoot.transform, "《灵脉余烬》AI影视互动游戏 · 可玩垂直切片", 25, TextAnchor.MiddleCenter, Gold);
            UIFactory.Anchor(eyebrow.rectTransform, new Vector2(0.16f, 0.76f), new Vector2(0.84f, 0.84f), Vector2.zero, Vector2.zero);
            Text title = UIFactory.Label("Title", _titleRoot.transform, "赤 雾 秘 苑", 72, TextAnchor.MiddleCenter, new Color(0.94f, 0.95f, 0.90f), FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, new Vector2(0.1f, 0.61f), new Vector2(0.9f, 0.78f), Vector2.zero, Vector2.zero);
            Text subtitle = UIFactory.Label("Subtitle", _titleRoot.transform, "双主角 · 受控分支 · 失败继续 · 预计单次 20–30 分钟", 25, TextAnchor.MiddleCenter, UIFactory.Hex("#c5d4cd"));
            UIFactory.Anchor(subtitle.rectTransform, new Vector2(0.15f, 0.55f), new Vector2(0.85f, 0.63f), Vector2.zero, Vector2.zero);

            Button male = UIFactory.Button("MaleRoute", _titleRoot.transform, "沈砚线\n活着，才有选择", () => StartRoute(PlayerRoute.ShenYan), new Color(0.07f, 0.24f, 0.25f, 0.96f), JadeHover, 26);
            UIFactory.Anchor(male.GetComponent<RectTransform>(), new Vector2(0.24f, 0.34f), new Vector2(0.48f, 0.51f), Vector2.zero, Vector2.zero);
            Button female = UIFactory.Button("FemaleRoute", _titleRoot.transform, "楚明绮线\n命运不属于宗门", () => StartRoute(PlayerRoute.ChuMingqi), new Color(0.30f, 0.14f, 0.14f, 0.96f), new Color(0.62f, 0.25f, 0.18f), 26);
            UIFactory.Anchor(female.GetComponent<RectTransform>(), new Vector2(0.52f, 0.34f), new Vector2(0.76f, 0.51f), Vector2.zero, Vector2.zero);

            Button cont = UIFactory.Button("Continue", _titleRoot.transform, "继续存档", ContinueGame, new Color(0.12f, 0.14f, 0.16f, 0.95f), new Color(0.28f, 0.32f, 0.32f), 22);
            UIFactory.Anchor(cont.GetComponent<RectTransform>(), new Vector2(0.38f, 0.23f), new Vector2(0.62f, 0.30f), Vector2.zero, Vector2.zero);
            cont.interactable = SaveSystem.HasSave;

            Text note = UIFactory.Label("PrototypeNote", _titleRoot.transform, "高精度场景画面、角色立绘、天气与镜头会随剧情节点切换。", 18, TextAnchor.MiddleCenter, new Color(0.78f, 0.83f, 0.79f));
            UIFactory.Anchor(note.rectTransform, new Vector2(0.10f, 0.10f), new Vector2(0.90f, 0.18f), Vector2.zero, Vector2.zero);
        }

        private void BuildErrorUI()
        {
            Image shade = UIFactory.Panel("BlockingError", _screenRoot, new Color(0.01f, 0.018f, 0.025f, 0.98f));
            _errorRoot = shade.gameObject;
            UIFactory.Stretch(shade.rectTransform);

            _errorTitle = UIFactory.Label(
                "ErrorTitle",
                shade.transform,
                "无法继续",
                46,
                TextAnchor.MiddleCenter,
                UIFactory.Hex("#f0d6bf"),
                FontStyle.Bold);
            UIFactory.Anchor(_errorTitle.rectTransform, new Vector2(0.16f, 0.61f), new Vector2(0.84f, 0.74f), Vector2.zero, Vector2.zero);

            _errorMessage = UIFactory.Label(
                "ErrorMessage",
                shade.transform,
                string.Empty,
                24,
                TextAnchor.UpperCenter,
                UIFactory.Hex("#d5ddd8"));
            UIFactory.Anchor(_errorMessage.rectTransform, new Vector2(0.18f, 0.34f), new Vector2(0.82f, 0.60f), Vector2.zero, Vector2.zero);

            _errorAction = UIFactory.Button(
                "ErrorAction",
                shade.transform,
                "返回标题",
                DismissErrorPage,
                new Color(0.12f, 0.23f, 0.21f, 0.96f),
                JadeHover,
                22);
            UIFactory.Anchor(_errorAction.GetComponent<RectTransform>(), new Vector2(0.39f, 0.20f), new Vector2(0.61f, 0.29f), Vector2.zero, Vector2.zero);
            _errorRoot.SetActive(false);
        }

        private void ShowErrorPage(string title, string message, bool canReturnToTitle)
        {
            _fatalContentError = !canReturnToTitle;
            if (_titleRoot != null) _titleRoot.SetActive(false);
            if (_storyRoot != null) _storyRoot.SetActive(false);
            if (_settingsRoot != null) _settingsRoot.SetActive(false);
            if (_journalRoot != null) _journalRoot.SetActive(false);
            if (_explorationRoot != null) _explorationRoot.SetActive(false);
            if (_fmvRoot != null) _fmvRoot.SetActive(false);
            _errorTitle.text = title;
            _errorMessage.text = message;
            Text actionLabel = _errorAction.GetComponentInChildren<Text>();
            if (actionLabel != null) actionLabel.text = canReturnToTitle ? "返回标题" : "退出游戏";
            _errorRoot.SetActive(true);
        }

        private void DismissErrorPage()
        {
            if (_fatalContentError)
            {
                Application.Quit();
                return;
            }

            ShowTitle();
        }

        private void BuildStoryUI()
        {
            _storyRoot = UIFactory.CreateUIObject("StoryScreen", _screenRoot);
            UIFactory.Stretch(_storyRoot.GetComponent<RectTransform>());

            Image top = UIFactory.Panel("TopBar", _storyRoot.transform, new Color(0.015f, 0.028f, 0.035f, 0.76f));
            UIFactory.Anchor(top.rectTransform, new Vector2(0, 0.935f), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            _nodeTitle = UIFactory.Label("NodeTitle", top.transform, "", 22, TextAnchor.MiddleLeft, UIFactory.Hex("#edf0df"), FontStyle.Bold);
            UIFactory.Anchor(_nodeTitle.rectTransform, new Vector2(0.025f, 0), new Vector2(0.30f, 1), Vector2.zero, Vector2.zero);
            _location = UIFactory.Label("Location", top.transform, "", 16, TextAnchor.MiddleLeft, UIFactory.Hex("#adbea9"));
            UIFactory.Anchor(_location.rectTransform, new Vector2(0.30f, 0), new Vector2(0.48f, 1), Vector2.zero, Vector2.zero);
            _hud = UIFactory.Label("Hud", top.transform, "", 15, TextAnchor.MiddleCenter, UIFactory.Hex("#d4ddd3"));
            UIFactory.Anchor(_hud.rectTransform, new Vector2(0.45f, 0), new Vector2(0.725f, 1), Vector2.zero, Vector2.zero);
            _clock = UIFactory.Label("Clock", top.transform, "", 15, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            UIFactory.Anchor(_clock.rectTransform, new Vector2(0.73f, 0), new Vector2(0.79f, 1), Vector2.zero, Vector2.zero);

            Button explore = UIFactory.Button("Explore", top.transform, "探索", StartExploration, new Color(0.16f, 0.28f, 0.18f, 0.92f), JadeHover, 14);
            UIFactory.Anchor(explore.GetComponent<RectTransform>(), new Vector2(0.795f, 0.20f), new Vector2(0.84f, 0.80f), Vector2.zero, Vector2.zero);

            Button save = UIFactory.Button("Save", top.transform, "存档", ManualSave, new Color(0.10f, 0.18f, 0.19f, 0.88f), Jade, 14);
            UIFactory.Anchor(save.GetComponent<RectTransform>(), new Vector2(0.845f, 0.20f), new Vector2(0.89f, 0.80f), Vector2.zero, Vector2.zero);
            Button journal = UIFactory.Button("Journal", top.transform, "志记", ToggleJournal, new Color(0.10f, 0.18f, 0.19f, 0.88f), Jade, 14);
            UIFactory.Anchor(journal.GetComponent<RectTransform>(), new Vector2(0.895f, 0.20f), new Vector2(0.94f, 0.80f), Vector2.zero, Vector2.zero);
            Button settings = UIFactory.Button("Settings", top.transform, "设置", ToggleSettings, new Color(0.10f, 0.18f, 0.19f, 0.88f), Jade, 14);
            UIFactory.Anchor(settings.GetComponent<RectTransform>(), new Vector2(0.945f, 0.20f), new Vector2(0.992f, 0.80f), Vector2.zero, Vector2.zero);

            Image dialogue = UIFactory.Panel("DialoguePanel", _storyRoot.transform, new Color(0.015f, 0.025f, 0.032f, 0.74f));
            _dialoguePanel = dialogue;
            UIFactory.Anchor(dialogue.rectTransform, new Vector2(0.045f, 0.03f), new Vector2(0.955f, 0.285f), Vector2.zero, Vector2.zero);
            Image dialogueAccent = UIFactory.Panel("DialogueAccent", dialogue.transform, new Color(Gold.r, Gold.g, Gold.b, 0.72f));
            UIFactory.Anchor(dialogueAccent.rectTransform, new Vector2(0f, 0.985f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            dialogueAccent.raycastTarget = false;

            _portrait = UIFactory.Raw("Portrait", dialogue.transform, Color.white);
            UIFactory.Anchor(_portrait.rectTransform, new Vector2(0.015f, 0.06f), new Vector2(0.205f, 0.95f), Vector2.zero, Vector2.zero);
            _portraitCrop = _portrait.gameObject.AddComponent<AspectCropRawImage>();
            // The new portraits are individual vertical photographs rather than multi-pose sheets.
            // Keep the upper body and face framed inside the compact dialogue card.
            _portraitCrop.SetSourceRegion(new Rect(0.06f, 0.30f, 0.88f, 0.66f));
            _portrait.gameObject.SetActive(false);

            _speaker = UIFactory.Label("Speaker", dialogue.transform, "", 21, TextAnchor.MiddleLeft, Gold, FontStyle.Bold);
            UIFactory.Anchor(_speaker.rectTransform, new Vector2(0.225f, 0.76f), new Vector2(0.63f, 0.95f), Vector2.zero, Vector2.zero);
            _body = UIFactory.Label("Body", dialogue.transform, "", 19, TextAnchor.UpperLeft, UIFactory.Hex("#e5e8de"));
            _body.lineSpacing = 1.16f;
            UIFactory.Anchor(_body.rectTransform, new Vector2(0.225f, 0.10f), new Vector2(0.63f, 0.77f), Vector2.zero, Vector2.zero);

            _voiceButton = UIFactory.Button("PushToTalk", dialogue.transform, "按住说话　V", null, new Color(0.11f, 0.24f, 0.22f, 0.94f), JadeHover, 15);
            UIFactory.Anchor(_voiceButton.GetComponent<RectTransform>(), new Vector2(0.225f, 0.015f), new Vector2(0.40f, 0.095f), Vector2.zero, Vector2.zero);
            _holdToTalk = _voiceButton.gameObject.AddComponent<HoldToTalkButton>();
            _holdToTalk.Pressed += BeginVoiceCapture;
            _holdToTalk.Released += CompleteVoiceCapture;
            _holdToTalk.Cancelled += CancelVoiceCapture;
            _voiceButton.gameObject.SetActive(false);

            _voiceStatus = UIFactory.Label("VoiceStatus", dialogue.transform, "", 13, TextAnchor.MiddleLeft, UIFactory.Hex("#adbea9"));
            UIFactory.Anchor(_voiceStatus.rectTransform, new Vector2(0.41f, 0.015f), new Vector2(0.63f, 0.095f), Vector2.zero, Vector2.zero);

            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Button button = UIFactory.Button("Choice" + (i + 1), dialogue.transform, "", null, new Color(0.055f, 0.22f, 0.18f, 0.93f), JadeHover, 18);
                float choiceTop = 0.76f - i * 0.22f;
                UIFactory.Anchor(button.GetComponent<RectTransform>(), new Vector2(0.66f, choiceTop), new Vector2(0.965f, choiceTop + 0.18f), Vector2.zero, Vector2.zero);
                button.gameObject.SetActive(false);
                _choiceButtons.Add(button);
                _choiceAllowed.Add(true);
            }
            _storyRoot.SetActive(false);
        }

        private void BuildExplorationUI()
        {
            _explorationRoot = UIFactory.CreateUIObject("ExplorationHUD", _screenRoot);
            UIFactory.Stretch(_explorationRoot.GetComponent<RectTransform>());

            Image objectivePanel = UIFactory.Panel("ObjectivePanel", _explorationRoot.transform, new Color(0.012f, 0.026f, 0.028f, 0.82f));
            UIFactory.Anchor(objectivePanel.rectTransform, new Vector2(0.025f, 0.845f), new Vector2(0.47f, 0.965f), Vector2.zero, Vector2.zero);
            _explorationObjective = UIFactory.Label("Objective", objectivePanel.transform, "", 20, TextAnchor.MiddleLeft, UIFactory.Hex("#e6eadf"), FontStyle.Bold);
            UIFactory.Anchor(_explorationObjective.rectTransform, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero);

            _explorationNotice = UIFactory.Label("Notice", _explorationRoot.transform, "", 26, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            UIFactory.Anchor(_explorationNotice.rectTransform, new Vector2(0.24f, 0.72f), new Vector2(0.76f, 0.82f), Vector2.zero, Vector2.zero);

            Text crosshair = UIFactory.Label("Crosshair", _explorationRoot.transform, "＋", 28, TextAnchor.MiddleCenter, new Color(0.78f, 0.92f, 0.84f, 0.82f), FontStyle.Bold);
            UIFactory.Anchor(crosshair.rectTransform, new Vector2(0.48f, 0.47f), new Vector2(0.52f, 0.53f), Vector2.zero, Vector2.zero);

            Image promptPanel = UIFactory.Panel("PromptPanel", _explorationRoot.transform, new Color(0.01f, 0.02f, 0.025f, 0.84f));
            UIFactory.Anchor(promptPanel.rectTransform, new Vector2(0.21f, 0.035f), new Vector2(0.79f, 0.14f), Vector2.zero, Vector2.zero);
            _explorationPrompt = UIFactory.Label("Prompt", promptPanel.transform, "", 20, TextAnchor.MiddleCenter, UIFactory.Hex("#e3ebdf"), FontStyle.Bold);
            UIFactory.Stretch(_explorationPrompt.rectTransform, 18, 8, 18, 8);

            Button leave = UIFactory.Button("LeaveExploration", _explorationRoot.transform, "返回剧情", EndExploration, new Color(0.08f, 0.18f, 0.18f, 0.90f), JadeHover, 17);
            UIFactory.Anchor(leave.GetComponent<RectTransform>(), new Vector2(0.84f, 0.885f), new Vector2(0.965f, 0.955f), Vector2.zero, Vector2.zero);
            _explorationRoot.SetActive(false);
        }

        private void BuildSettingsUI()
        {
            Image shade = UIFactory.Panel("SettingsOverlay", _screenRoot, new Color(0.01f, 0.02f, 0.025f, 0.96f));
            _settingsRoot = shade.gameObject;
            UIFactory.Anchor(shade.rectTransform, new Vector2(0.27f, 0.17f), new Vector2(0.73f, 0.83f), Vector2.zero, Vector2.zero);
            Text title = UIFactory.Label("Title", shade.transform, "设置与无障碍", 36, TextAnchor.MiddleCenter, UIFactory.Hex("#edf0df"), FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, new Vector2(0.1f, 0.82f), new Vector2(0.9f, 0.96f), Vector2.zero, Vector2.zero);
            _settingsSummary = UIFactory.Label("Summary", shade.transform, "", 23, TextAnchor.UpperCenter, UIFactory.Hex("#cbd8d0"));
            UIFactory.Anchor(_settingsSummary.rectTransform, new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.80f), Vector2.zero, Vector2.zero);

            AddSettingsButton(shade.transform, "音量－", new Vector2(0.10f, 0.44f), () => ChangeVolume(-0.1f));
            AddSettingsButton(shade.transform, "音量＋", new Vector2(0.55f, 0.44f), () => ChangeVolume(0.1f));
            AddSettingsButton(shade.transform, "文字慢", new Vector2(0.10f, 0.30f), () => ChangeTextSpeed(-0.25f));
            AddSettingsButton(shade.transform, "文字快", new Vector2(0.55f, 0.30f), () => ChangeTextSpeed(0.25f));
            AddSettingsButton(shade.transform, "切换减少动态", new Vector2(0.10f, 0.16f), ToggleReducedMotion, 0.80f);
            AddSettingsButton(shade.transform, "关闭", new Vector2(0.33f, 0.03f), ToggleSettings, 0.34f);
            _settingsRoot.SetActive(false);
        }

        private void AddSettingsButton(Transform parent, string label, Vector2 anchor, Action action, float width = 0.35f)
        {
            Button button = UIFactory.Button("Setting_" + label, parent, label, action, new Color(0.09f, 0.20f, 0.20f), Jade, 20);
            UIFactory.Anchor(button.GetComponent<RectTransform>(), anchor, anchor + new Vector2(width, 0.10f), Vector2.zero, Vector2.zero);
        }

        private void BuildJournalUI()
        {
            Image shade = UIFactory.Panel("JournalOverlay", _screenRoot, new Color(0.012f, 0.024f, 0.028f, 0.97f));
            _journalRoot = shade.gameObject;
            UIFactory.Anchor(shade.rectTransform, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.88f), Vector2.zero, Vector2.zero);
            Text title = UIFactory.Label("Title", shade.transform, "行旅志记", 36, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, new Vector2(0.1f, 0.86f), new Vector2(0.9f, 0.97f), Vector2.zero, Vector2.zero);
            _journalText = UIFactory.Label("JournalText", shade.transform, "", 20, TextAnchor.UpperLeft, UIFactory.Hex("#d8e1d8"));
            _journalText.lineSpacing = 1.15f;
            UIFactory.Anchor(_journalText.rectTransform, new Vector2(0.05f, 0.15f), new Vector2(0.53f, 0.85f), Vector2.zero, Vector2.zero);

            Image artFrame = UIFactory.Panel("ArchiveArtFrame", shade.transform, new Color(0.035f, 0.055f, 0.058f, 1f));
            UIFactory.Anchor(artFrame.rectTransform, new Vector2(0.56f, 0.24f), new Vector2(0.95f, 0.83f), Vector2.zero, Vector2.zero);
            artFrame.raycastTarget = false;
            _journalArt = UIFactory.Raw("ArchiveArt", artFrame.transform, Color.white);
            UIFactory.Stretch(_journalArt.rectTransform);
            _journalArt.raycastTarget = false;
            _journalArtFitter = _journalArt.gameObject.AddComponent<AspectRatioFitter>();
            _journalArtFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _journalArtCaption = UIFactory.Label("ArchiveArtCaption", shade.transform, "", 18, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            UIFactory.Anchor(_journalArtCaption.rectTransform, new Vector2(0.56f, 0.17f), new Vector2(0.95f, 0.235f), Vector2.zero, Vector2.zero);

            Button cycleArt = UIFactory.Button("CycleArchiveArt", shade.transform, "切换秘苑图鉴", CycleJournalArt, new Color(0.10f, 0.23f, 0.21f), JadeHover, 18);
            UIFactory.Anchor(cycleArt.GetComponent<RectTransform>(), new Vector2(0.62f, 0.055f), new Vector2(0.89f, 0.135f), Vector2.zero, Vector2.zero);
            Button close = UIFactory.Button("Close", shade.transform, "返回剧情", ToggleJournal, new Color(0.10f, 0.23f, 0.21f), JadeHover, 20);
            UIFactory.Anchor(close.GetComponent<RectTransform>(), new Vector2(0.21f, 0.055f), new Vector2(0.45f, 0.135f), Vector2.zero, Vector2.zero);
            _journalRoot.SetActive(false);
        }

        private void ShowTitle()
        {
            CancelVoiceCapture();
            SetVoiceAvailable(false);
            if (_errorRoot != null) _errorRoot.SetActive(false);
            _fatalContentError = false;
            if (_media != null && _media.IsBusy) _media.Skip();
            ReleaseHeldCinematic();
            if (_exploring) EndExploration();
            if (_fmvRoot != null) _fmvRoot.SetActive(false);
            _titleRoot.SetActive(true);
            _storyRoot.SetActive(false);
            _settingsRoot.SetActive(false);
            _journalRoot.SetActive(false);
            _storyStage?.FocusStoryNode("title", PlayerRoute.None, true);
            SetBackdrop(GeneratedArtCatalog.SceneForNode("title"), Color.white, GeneratedArtCatalog.SceneRegionForNode("title"));
        }

        private void StartRoute(PlayerRoute route)
        {
            _audio.Click();
            _state = new GameState
            {
                route = route,
                currentNodeId = StoryCatalog.EntryNodeId,
                sectDuty = route == PlayerRoute.ChuMingqi ? 2 : 0
            };
            _cinematic.SetReducedMotion(false);
            _titleRoot.SetActive(false);
            _storyRoot.SetActive(true);
            BeginChapterPresentation(StoryCatalog.EntryNodeId, true, true);
        }

        private void ContinueGame()
        {
            SaveLoadResult loaded = SaveSystem.Load();
            if (loaded.Status != SaveLoadStatus.Success)
            {
                if (loaded.Status != SaveLoadStatus.NotFound)
                    ShowErrorPage("无法继续存档", loaded.UserMessage, true);
                return;
            }

            _state = loaded.State;
            _audio.SetVolume(_state.masterVolume);
            _media?.SetVolume(_state.cinematicVolume);
            _cinematic.SetReducedMotion(_state.reducedMotion);
            _titleRoot.SetActive(false);
            _storyRoot.SetActive(true);
            BeginChapterPresentation(
                string.IsNullOrWhiteSpace(_state.currentNodeId)
                    ? StoryCatalog.EntryNodeId
                    : _state.currentNodeId,
                false,
                true);
        }

        private void BeginChapterPresentation(
            string nodeId,
            bool autosave,
            bool allowCinematic)
        {
            long token = _associationChapterLifetime.Begin();
            _activeStoryThread = null;
            if (_associationRuntime == null || _state.route == PlayerRoute.None)
            {
                ShowNode(nodeId, autosave, allowCinematic);
                return;
            }

            _associationRuntime.ExpireChapter();
            try
            {
                _activeStoryThread = _associationRuntime.GetDefault(
                    _state.route,
                    nodeId);
            }
            catch (Exception error) when (
                error is StoryThreadContractException ||
                error is ArgumentException ||
                error is InvalidOperationException ||
                error is IOException ||
                error is UnauthorizedAccessException)
            {
                Debug.LogWarning(
                    "The reviewed default StoryThread is unavailable; baseline node will be shown (" +
                    error.GetType().Name + ").");
                _activeStoryThread = null;
            }

            Task<StoryThread> prefetch;
            try
            {
                prefetch = _associationRuntime.PrefetchAsync(
                    _state.route,
                    nodeId,
                    CancellationToken.None);
            }
            catch (Exception error) when (
                error is AssociationTransportException ||
                error is StoryThreadContractException ||
                error is ArgumentException ||
                error is InvalidOperationException)
            {
                Debug.LogWarning(
                    "StoryThread prefetch could not start; reviewed default remains active (" +
                    error.GetType().Name + ").");
                ShowNode(nodeId, autosave, allowCinematic);
                return;
            }

            // Rendering never waits for the optional network enhancement. The reviewed default
            // is available immediately, while a validated online result may replace it only for
            // nodes the player has not presented yet.
            ShowNode(nodeId, autosave, allowCinematic);
            if (prefetch.IsCompleted)
            {
                CompleteChapterPrefetch(prefetch, token);
                return;
            }
            StartCoroutine(AwaitChapterPrefetch(prefetch, token));
        }

        private IEnumerator AwaitChapterPrefetch(
            Task<StoryThread> prefetch,
            long token)
        {
            while (!prefetch.IsCompleted && _associationChapterLifetime.IsCurrent(token))
                yield return null;
            CompleteChapterPrefetch(prefetch, token);
        }

        private void CompleteChapterPrefetch(
            Task<StoryThread> prefetch,
            long token)
        {
            if (!_associationChapterLifetime.IsCurrent(token)) return;
            try
            {
                StoryThread result = prefetch.GetAwaiter().GetResult();
                if (result != null) _activeStoryThread = result;
            }
            catch (Exception error) when (
                error is AssociationTransportException ||
                error is StoryThreadContractException ||
                error is ArgumentException ||
                error is InvalidOperationException ||
                error is OperationCanceledException)
            {
                Debug.LogWarning(
                    "StoryThread prefetch failed; reviewed default remains active (" +
                    error.GetType().Name + ").");
            }
        }

        private void ShowNode(string id, bool autosave = true, bool allowCinematic = true)
        {
            CancelVoiceCapture();
            // The held frame belongs to the choices for the node that just played it. Crossing a
            // node boundary disposes that GPU texture before another scene or movie is prepared.
            ReleaseHeldCinematic();
            SetStoryCinematicMode(false);
            StoryNode node = StoryCatalog.Get(id);
            if (node == null)
            {
                Debug.LogError("Missing story node: " + id);
                ShowErrorPage("剧情节点错误", "当前剧情节点不存在，已停止继续以保护存档。", true);
                return;
            }
            _state.currentNodeId = id;
            _state.Clamp();
            if (_state.history.Count == 0 || _state.history[_state.history.Count - 1] != id) _state.history.Add(id);
            _nodeTitle.text = node.title;
            _location.text = node.location;
            _speaker.text = node.speaker;
            _storyStage?.FocusStoryNode(node.id, _state.route);
            SetBackdrop(GeneratedArtCatalog.SceneForNode(node.id, _state.route), Color.white, GeneratedArtCatalog.SceneRegionForNode(node.id));
            SetPortrait(node);
            RefreshHud();
            if (allowCinematic && !string.IsNullOrWhiteSpace(node.introMediaRef) &&
                CinematicCatalog.TryGetCue(node.introMediaRef, out CinematicCueDefinition intro) &&
                !_state.watchedCinematics.Contains(intro.CueId))
            {
                BeginNodeCinematic(node, intro);
                return;
            }

            PresentNode(node, autosave);
        }

        private void PresentNode(StoryNode node, bool autosave)
        {
            _titleRoot.SetActive(false);
            bool onRetainedCinematic = _media != null && _media.HasRetainedFrame;
            if (onRetainedCinematic) PlaceRetainedCinematicBehindStory();
            SetStoryCinematicMode(onRetainedCinematic);
            _storyRoot.SetActive(true);
            BuildNodeChoices(node);
            StoryThreadPresentation presentation = _storyThreadWeaver == null
                ? new StoryThreadPresentation(node.TextFor(_state.route), false)
                : _storyThreadWeaver.Present(
                    node,
                    _state.route,
                    _state,
                    _activeStoryThread);
            if (node.kind == NodeKind.Ending)
            {
                _associationChapterLifetime.End();
                _associationRuntime?.ExpireChapter();
                _activeStoryThread = null;
            }
            BeginText(presentation.Narrative);
            if (autosave || presentation.Injected) SaveSystem.Save(_state);
        }

        private void BeginNodeCinematic(StoryNode node, CinematicCueDefinition intro)
        {
            CancelVoiceCapture();
            _state.pendingCinematicId = intro.CueId;
            _state.cinematicCheckpointSeconds = 0f;
            // Persist the pending cue even when resuming an older save. If the process closes
            // mid-clip, Continue reliably re-enters this node and starts the local clip again.
            SaveSystem.Save(_state);
            SetChoicesInteractable(false);

            PlayFmvPresentation(
                intro.CueId,
                node.title + "　·　" + node.location,
                "本地影视场景 · 播放结束后进入剧情互动",
                reason =>
                {
                    if (reason == MediaEndReason.Completed || reason == MediaEndReason.Skipped)
                    {
                        if (!_state.watchedCinematics.Contains(intro.CueId)) _state.watchedCinematics.Add(intro.CueId);
                    }
                    _state.pendingCinematicId = "";
                    _state.cinematicCheckpointSeconds = 0f;
                    SaveSystem.Save(_state);
                    PresentNode(node, false);
                });
        }

        private bool PlayFmvPresentation(string cueId, string title, string hint, Action<MediaEndReason> finished)
        {
            if (_media == null)
            {
                finished?.Invoke(MediaEndReason.Error);
                return false;
            }
            if (_media.IsBusy)
            {
                finished?.Invoke(MediaEndReason.Busy);
                return false;
            }

            ReleaseHeldCinematic();
            SetStoryCinematicMode(false);
            int token = ++_fmvPresentationToken;
            _settingsRoot.SetActive(false);
            _journalRoot.SetActive(false);
            _titleRoot.SetActive(false);
            _storyRoot.SetActive(false);
            _explorationRoot.SetActive(false);
            _fmvTitle.text = title;
            _fmvHint.text = hint;
            _fmvStatus.text = "正在准备本地影视片段……";
            SetFmvChromeVisible(true);
            _fmvRoot.SetActive(true);
            _fmvRoot.transform.SetAsLastSibling();
            _audio?.SetCinematicDuck(true);
            _media.SetVolume(_state.cinematicVolume);

            return _media.Play(cueId, reason =>
            {
                if (token != _fmvPresentationToken) return;
                bool retained = reason == MediaEndReason.Completed && _media.HasRetainedFrame;
                if (retained)
                {
                    SetFmvChromeVisible(false);
                    PlaceRetainedCinematicBehindStory();
                }
                else
                {
                    _fmvRoot.SetActive(false);
                }
                _audio?.SetCinematicDuck(false);
                finished?.Invoke(reason);
            }, true);
        }

        private void SetFmvChromeVisible(bool visible)
        {
            if (_fmvTopBar != null) _fmvTopBar.SetActive(visible);
            if (_fmvBottomBar != null) _fmvBottomBar.SetActive(visible);
            if (_fmvStatus != null) _fmvStatus.gameObject.SetActive(visible);
        }

        private void PlaceRetainedCinematicBehindStory()
        {
            if (_fmvRoot == null || _storyRoot == null) return;
            _fmvRoot.SetActive(true);
            // Insert at the story screen's current index. Unity shifts StoryScreen one slot to
            // the right, leaving settings/journal overlays above both presentation layers.
            int fmvIndex = _fmvRoot.transform.GetSiblingIndex();
            int storyIndex = _storyRoot.transform.GetSiblingIndex();
            int targetIndex = fmvIndex > storyIndex ? storyIndex : Mathf.Max(0, storyIndex - 1);
            if (fmvIndex != targetIndex) _fmvRoot.transform.SetSiblingIndex(targetIndex);
        }

        private void SetStoryCinematicMode(bool enabled)
        {
            if (_dialoguePanel != null)
            {
                _dialoguePanel.color = enabled
                    ? new Color(0.008f, 0.016f, 0.022f, 0.52f)
                    : new Color(0.015f, 0.025f, 0.032f, 0.74f);
            }

            if (enabled && _portrait != null) _portrait.gameObject.SetActive(false);

            // Actor footage can occupy the frame itself. In cinematic mode the text starts at
            // the left edge rather than reserving a second, stretched portrait card.
            float textLeft = enabled ? 0.035f : 0.225f;
            if (_speaker != null)
                UIFactory.Anchor(_speaker.rectTransform, new Vector2(textLeft, 0.76f), new Vector2(0.63f, 0.95f), Vector2.zero, Vector2.zero);
            if (_body != null)
                UIFactory.Anchor(_body.rectTransform, new Vector2(textLeft, 0.10f), new Vector2(0.63f, 0.77f), Vector2.zero, Vector2.zero);
        }

        private void ReleaseHeldCinematic()
        {
            if (_media == null || !_media.HasRetainedFrame) return;
            _media.ReleaseRetainedFrame();
            if (_fmvRoot != null) _fmvRoot.SetActive(false);
        }

        private void BeginFlightMinigame()
        {
            CancelVoiceCapture();
            ReleaseHeldCinematic();
            SetStoryCinematicMode(false);
            _minigames.BeginFlight(OnFlightComplete);
        }

        private void StartExploration()
        {
            if (_exploring || (_media != null && _media.IsBusy) || _state.route == PlayerRoute.None || _storyStage == null) return;
            CancelVoiceCapture();
            _audio.Click();
            ReleaseHeldCinematic();
            SetStoryCinematicMode(false);
            _exploring = true;
            _settingsRoot.SetActive(false);
            _journalRoot.SetActive(false);
            _titleRoot.SetActive(false);
            _storyRoot.SetActive(false);
            _backdrop.gameObject.SetActive(false);
            _videoLayer.gameObject.SetActive(false);
            _cinematicWash.gameObject.SetActive(false);
            _explorationRoot.SetActive(true);
            _explorationPrompt.text = "WASD 移动　Shift 奔跑　按住右键转动镜头　E 互动　Esc 返回";
            _explorationNotice.text = "已进入实时3D探索";
            RefreshExplorationObjective();
            _storyStage.BeginExploration(_state.currentNodeId, _state.route, OnExplorationInteraction, OnExplorationPrompt, EndExploration);
            SaveSystem.Save(_state);
            Debug.Log("RED_MIST_EXPLORATION_BEGIN node=" + _state.currentNodeId + " route=" + _state.route);
        }

        private void EndExploration()
        {
            if (!_exploring) return;
            _exploring = false;
            _storyStage?.EndExploration();
            _explorationRoot.SetActive(false);
            _cinematicWash.gameObject.SetActive(true);
            _storyRoot.SetActive(true);
            StoryNode node = StoryCatalog.Get(_state.currentNodeId);
            if (node != null)
            {
                _storyStage?.FocusStoryNode(node.id, _state.route, true);
                SetBackdrop(GeneratedArtCatalog.SceneForNode(node.id, _state.route), Color.white, GeneratedArtCatalog.SceneRegionForNode(node.id));
                SetPortrait(node);
            }
            SaveSystem.Save(_state);
            Debug.Log("RED_MIST_EXPLORATION_END node=" + _state.currentNodeId);
        }

        private void RefreshExplorationObjective()
        {
            string objective;
            switch (_state.currentNodeId)
            {
                case "herb_route":
                    objective = "走近药圃的青色灵光，按 E 进入实时采药";
                    break;
                case "corpse_signs":
                    objective = "沿林地寻找漂移的神识痕迹，按 E 展开侦察";
                    break;
                case "formation":
                    objective = "抵达古铜门前的阵眼，按 E 操纵月阳光路";
                    break;
                case "camp":
                case "prologue":
                case "alliance":
                    objective = "探索营地：检查营火、与同伴交谈，再沿石路前进";
                    break;
                default:
                    objective = "自由探索赤雾秘苑，靠近发光标记按 E 互动";
                    break;
            }
            _explorationObjective.text = "当前目标：" + objective + "\n<size=15>WASD移动　Shift奔跑　右键观察　滚轮缩放　E互动　Esc返回</size>";
        }

        private void OnExplorationPrompt(string prompt)
        {
            if (_explorationPrompt != null) _explorationPrompt.text = string.IsNullOrEmpty(prompt)
                ? "WASD 移动　Shift 奔跑　按住右键转动镜头　E 互动"
                : prompt;
        }

        private void OnExplorationInteraction(string interactionId)
        {
            if (!_exploring) return;
            switch (interactionId)
            {
                case "campfire":
                    if (_explorationInteractions.Add("campfire") && !_state.discoveries.Contains("营火旁重新整理的匿息符"))
                    {
                        _state.wards += 1;
                        _state.mana += 4;
                        _state.discoveries.Add("营火旁重新整理的匿息符");
                    }
                    ShowExplorationNotice("你拨开余烬，重新温养了一张匿息符。护符 +1");
                    break;
                case "companion":
                    if (_explorationInteractions.Add("companion")) _state.trust += 1;
                    ShowExplorationNotice(_state.route == PlayerRoute.ShenYan
                        ? "楚明绮提醒你：红雾会沿水面反向流动。信任 +1"
                        : "沈砚没有追问你的功法，只指出了安全落脚点。信任 +1");
                    break;
                case "herb":
                    BeginExplorationMinigame("herb");
                    break;
                case "sense":
                    BeginExplorationMinigame("sense");
                    break;
                case "seal":
                    BeginExplorationMinigame("seal");
                    break;
                case "exit":
                    EndExploration();
                    break;
                default:
                    ShowExplorationNotice("这里暂时没有可确认的线索。");
                    break;
            }
            _state.Clamp();
            RefreshHud();
        }

        private void BeginExplorationMinigame(string kind)
        {
            CancelVoiceCapture();
            _storyStage?.SetExplorationInput(false);
            _explorationRoot.SetActive(false);
            if (kind == "herb") _minigames.BeginHerb(result => CompleteExplorationMinigame(kind, result));
            else if (kind == "sense") _minigames.BeginScan(result => CompleteExplorationMinigame(kind, result));
            else _minigames.BeginFormation(result => CompleteExplorationMinigame(kind, result));
        }

        private void CompleteExplorationMinigame(string kind, MinigameResult result)
        {
            bool advancesStory = (kind == "herb" && _state.currentNodeId == "herb_route") ||
                                 (kind == "sense" && _state.currentNodeId == "corpse_signs") ||
                                 (kind == "seal" && _state.currentNodeId == "formation");
            if (advancesStory)
            {
                EndExploration();
                if (kind == "herb") OnHerbComplete(result);
                else if (kind == "sense") OnScanComplete(result);
                else OnFormationComplete(result);
                return;
            }

            ReportResult(result);
            _explorationRoot.SetActive(true);
            _storyStage?.SetExplorationInput(true);
            ShowExplorationNotice(result.summary);
        }

        private void ShowExplorationNotice(string message)
        {
            if (_explorationNoticeRoutine != null) StopCoroutine(_explorationNoticeRoutine);
            _explorationNoticeRoutine = StartCoroutine(ExplorationNoticeRoutine(message));
        }

        private IEnumerator ExplorationNoticeRoutine(string message)
        {
            _explorationNotice.text = message;
            yield return new WaitForSecondsRealtime(3.2f);
            _explorationNotice.text = "";
            _explorationNoticeRoutine = null;
        }

        private void BuildNodeChoices(StoryNode node)
        {
            ClearChoices();
            SetVoiceAvailable(node.voiceIntentRefs.Count > 0 || node.invalidInputRules.Count > 0);
            if (node.kind == NodeKind.Ending)
            {
                string result = DetermineEnding();
                _fullBody = node.TextFor(_state.route) + "\n\n" + result;
                SetChoice(0, "从另一条主线开始", () => StartRoute(_state.route == PlayerRoute.ShenYan ? PlayerRoute.ChuMingqi : PlayerRoute.ShenYan));
                SetChoice(1, "返回标题", ShowTitle);
                SetChoice(2, "删除存档", () => { SaveSystem.Delete(); ShowTitle(); });
                return;
            }

            if (node.kind == NodeKind.Flight)
            {
                SetChoice(0, "开始御器飞行", BeginFlightMinigame);
                return;
            }
            if (node.kind == NodeKind.DivineSense)
            {
                SetChoice(0, "进入3D林地，寻找神识痕迹", StartExploration);
                return;
            }
            if (node.kind == NodeKind.HerbGathering)
            {
                SetChoice(0, "进入3D药圃，亲自观察与采集", StartExploration);
                return;
            }
            if (node.kind == NodeKind.Formation)
            {
                if (_state.formationOpened)
                {
                    SetChoice(0, "使用石峻的阵钉，无损开启铜门", OpenFormationWithSpike);
                    SetChoice(1, "保留阵钉，亲自进入3D阵区校准", StartExploration);
                }
                else
                {
                    SetChoice(0, "进入3D铜门区域，寻找阵眼", StartExploration);
                }
                return;
            }
            if (node.kind == NodeKind.CombatOne)
            {
                BuildCombatChoices(1);
                return;
            }
            if (node.kind == NodeKind.CombatTwo)
            {
                BuildCombatChoices(2);
                return;
            }

            for (int i = 0; i < node.choices.Count && i < 4; i++)
            {
                ChoiceDefinition definition = node.choices[i];
                string label = definition.label;
                if (!string.IsNullOrEmpty(definition.hint)) label += "\n<size=14>" + definition.hint + "</size>";
                SetChoice(i, label, () => ResolveAction(definition));
            }
        }

        private void BuildCombatChoices(int round)
        {
            if (round == 1 && _state.route == PlayerRoute.ShenYan)
            {
                SetChoice(0, "金蜉刃试探鳞隙", () => ResolveCombat("probe_blades", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "probe_blades", 1));
                SetChoice(1, "符箓限制蛟尾", () => ResolveCombat("ward_tail", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "ward_tail", 1));
                SetChoice(2, "保护伤员撤向狭道", () => ResolveCombat("protect_ally", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "protect_ally", 1));
                SetChoice(3, "保留底牌，立即撤退", () => ResolveCombat("retreat", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "retreat", 1));
            }
            else if (round == 1)
            {
                SetChoice(0, "结月轮阵控制黑泥", () => ResolveCombat("moon_control", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "moon_control", 1));
                SetChoice(1, "护送弟子退向玉栏", () => ResolveCombat("protect_ally", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "protect_ally", 1));
                SetChoice(2, "短暂释放赤鸾火", () => ResolveCombat("phoenix_flash", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "phoenix_flash", 1));
                SetChoice(3, "下令全队撤退", () => ResolveCombat("retreat", 1), StoryProgressionRules.IsCombatActionAvailable(_state, "retreat", 1));
            }
            else if (_state.route == PlayerRoute.ShenYan)
            {
                SetChoice(0, "发动金蜉连环刃", () => ResolveCombat("trump_blades", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "trump_blades", 2));
                SetChoice(1, "引爆符阵断其退路", () => ResolveCombat("formation_burst", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "formation_burst", 2));
                SetChoice(2, "借蒸汽与楚明绮合击", () => ResolveCombat("joint_strike", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "joint_strike", 2));
                SetChoice(3, "带伤员从缺口撤退", () => ResolveCombat("retreat", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "retreat", 2));
            }
            else
            {
                SetChoice(0, "公开赤鸾焰环镇压", () => ResolveCombat("phoenix_ring", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "phoenix_ring", 2));
                SetChoice(1, "借符阵伪装火光合击", () => ResolveCombat("joint_strike", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "joint_strike", 2));
                SetChoice(2, "令弟子撤离，独自拖延", () => ResolveCombat("hold_line", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "hold_line", 2));
                SetChoice(3, "保住身份，立即撤退", () => ResolveCombat("retreat", 2), StoryProgressionRules.IsCombatActionAvailable(_state, "retreat", 2));
            }
        }

        private void ResolveAction(ChoiceDefinition definition)
        {
            _audio.Click();
            string action = definition.action;
            RecordChoice(action);
            StoryProgressionRules.ApplyNarrativeChoice(_state, action);
            if (!string.IsNullOrEmpty(definition.nextNodeId)) ShowNode(definition.nextNodeId);
            else ShowErrorPage("剧情转场错误", "当前选项缺少下一节点，已停止继续以保护存档。", true);
        }

        private void ResolveCombat(string action, int round)
        {
            _audio.Danger();
            RecordChoice(action);
            SetChoicesInteractable(false);
            string nextNodeId = TransitionTarget(action);
            if (nextNodeId == null) return;
            CombatProgressionResult resolution = StoryProgressionRules.ApplyCombat(_state, action, round);
            Color cueColor = Vermilion;
            if (resolution.CueTone == CombatCueTone.Jade) cueColor = Jade;
            else if (resolution.CueTone == CombatCueTone.Gold) cueColor = Gold;
            else if (resolution.CueTone == CombatCueTone.Retreat)
                cueColor = new Color(0.25f, 0.48f, 0.45f);
            _cinematic.PlayCue(resolution.CueId, cueColor, true);
            StartCoroutine(ContinueAfterCue(
                nextNodeId,
                resolution.CueTone == CombatCueTone.Retreat ? 1.1f : 1.15f));
        }

        private IEnumerator ContinueAfterCue(string node, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            ShowNode(node);
        }

        private string TransitionTarget(string triggerId)
        {
            if (StoryCatalog.TryGetTransition(_state.currentNodeId, triggerId, out string nextNodeId) &&
                StoryCatalog.Get(nextNodeId) != null)
            {
                return nextNodeId;
            }

            Debug.LogError("Missing story transition: " + _state.currentNodeId + " -> " + triggerId);
            ShowErrorPage("剧情转场错误", "剧情包缺少当前行动的合法转场，已停止继续以保护存档。", true);
            return null;
        }

        private void FollowTransition(string triggerId)
        {
            string nextNodeId = TransitionTarget(triggerId);
            if (nextNodeId != null) ShowNode(nextNodeId);
        }

        private void OnFlightComplete(MinigameResult result)
        {
            string trigger = StoryProgressionRules.ApplyFlight(_state, result);
            RecordChoice(trigger);
            ReportResult(result);
            FollowTransition(trigger);
        }

        private void OnHerbComplete(MinigameResult result)
        {
            string trigger = StoryProgressionRules.ApplyHerb(_state, result);
            RecordChoice(trigger);
            ReportResult(result);
            FollowTransition(trigger);
        }

        private void OnScanComplete(MinigameResult result)
        {
            string trigger = StoryProgressionRules.ApplyScan(_state, result);
            RecordChoice(trigger);
            ReportResult(result);
            FollowTransition(trigger);
        }

        private void OnFormationComplete(MinigameResult result)
        {
            string trigger = StoryProgressionRules.ApplyFormation(_state, result);
            RecordChoice(trigger);
            ReportResult(result);
            FollowTransition(trigger);
        }

        private void OpenFormationWithSpike()
        {
            _audio.Click();
            string trigger = StoryProgressionRules.ApplyFormationSpike(_state);
            RecordChoice(trigger);
            _cinematic.PlayCue("阵钉归位", Jade, false);
            FollowTransition(trigger);
        }

        private void RecordChoice(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return;
            _state.choiceHistory.Add(_state.currentNodeId + "::" + action);
        }

        private void ReportResult(MinigameResult result)
        {
            _state.Clamp();
            if (result.success)
            {
                _audio.Success();
                _cinematic.PlayCue(result.perfect ? "完美判定" : "完成", Jade, false);
            }
            else
            {
                _audio.Danger();
                _cinematic.PlayCue("代价继续", Vermilion, true);
            }
        }

        private string DetermineEnding()
        {
            string title;
            string detail;
            RedMistEndingKind ending = StoryProgressionRules.ClassifyEnding(_state);
            if (ending == RedMistEndingKind.Retreat)
            {
                title = "【失败后继续】雾门之外";
                detail = "你放弃核心战利品并保存了性命。后续筑基资源减少，宗门评价与人情债发生变化，但主线继续。";
            }
            else if (ending == RedMistEndingKind.CostlyVictory)
            {
                title = "【代价胜利】火照玄泥";
                detail = "墨蛟被击败或逐退，但关键底牌、伤势或队伍代价已经无法完全隐藏。敌对势力会据此准备克制。";
            }
            else
            {
                title = "【谨慎同盟】各守一线";
                detail = "双方带伤撤离，分享必要情报，却没有索取彼此全部秘密。尊重比亲近更早建立。";
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("<size=30><b>" + title + "</b></size>");
            summary.AppendLine(detail);
            summary.AppendLine();
            summary.Append("结算：主药 ").Append(_state.herbs)
                .Append("　信任 ").Append(_state.trust)
                .Append("　尊重 ").Append(_state.respect)
                .Append("　暴露 ").Append(_state.exposure)
                .Append("　法力 ").Append(_state.mana)
                .Append("　伤势 ").Append(100 - _state.health).AppendLine();
            summary.Append("关键状态：")
                .Append(_state.discipleRescued ? "救下伤员；" : "伤员未获救；")
                .Append(_state.secretPreserved ? "秘密大体保全；" : "秘密已部分暴露；")
                .Append(_state.formationOpened ? "保留撤退阵眼；" : "禁制被强攻；")
                .Append(_state.dragonDefeated ? "墨蛟败退。" : "墨蛟仍存。 ");
            return summary.ToString();
        }

        private void BeginText(string text)
        {
            if (_state.currentNodeId == "ending") text += "\n\n" + DetermineEnding();
            _fullBody = text;
            _textComplete = false;
            _body.text = "";
            SetChoicesInteractable(false);
            if (_typewriter != null) StopCoroutine(_typewriter);
            _typewriter = StartCoroutine(Typewriter(text));
        }

        private IEnumerator Typewriter(string text)
        {
            float charactersPerSecond = 78f * Mathf.Clamp(_state.textSpeed, 0.5f, 2f);
            float accumulator = 0f;
            int index = 0;
            while (index < text.Length)
            {
                accumulator += Time.unscaledDeltaTime * charactersPerSecond;
                int target = Mathf.Min(text.Length, Mathf.FloorToInt(accumulator));
                if (target > index)
                {
                    index = target;
                    _body.text = text.Substring(0, index);
                }
                yield return null;
            }
            _body.text = text;
            _textComplete = true;
            SetChoicesInteractable(true);
        }

        private void CompleteText()
        {
            if (_typewriter != null) StopCoroutine(_typewriter);
            _typewriter = null;
            _body.text = _fullBody;
            _textComplete = true;
            SetChoicesInteractable(true);
        }

        private void SetChoice(int index, string text, Action action, bool enabled = true)
        {
            if (index < 0 || index >= _choiceButtons.Count) return;
            Button button = _choiceButtons[index];
            _choiceAllowed[index] = enabled;
            button.gameObject.SetActive(true);
            Text label = button.GetComponentInChildren<Text>();
            label.text = (index + 1) + "　" + text;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = enabled;
        }

        private void ClearChoices()
        {
            SetVoiceAvailable(false);
            foreach (Button button in _choiceButtons)
            {
                button.onClick.RemoveAllListeners();
                button.gameObject.SetActive(false);
            }
            for (int i = 0; i < _choiceAllowed.Count; i++) _choiceAllowed[i] = true;
        }

        private void SetChoicesInteractable(bool value)
        {
            for (int i = 0; i < _choiceButtons.Count; i++)
            {
                Button button = _choiceButtons[i];
                if (button.gameObject.activeSelf) button.interactable = value && _choiceAllowed[i];
            }
            if (_voiceButton != null) _voiceButton.interactable = value && _voiceAvailable;
        }

        private void InitializeVoiceCapture()
        {
            _asrClient = gameObject.AddComponent<UnityAsrClient>();
            _voiceInteractionClient = gameObject.AddComponent<UnityVoiceInteractionClient>();
            _voiceInteractionCoordinator = new VoiceInteractionCoordinator();
            _unityMicrophone = new UnityMicrophoneGateway();
            _pushToTalk = new PushToTalkController(
                new UnityVoicePermissionGateway(),
                _unityMicrophone,
                new UnityRealtimeClock(),
                new VoiceCaptureOptions(16000, 8d, 5d, 0.5d, 0.01f));
            _pushToTalk.Changed += OnVoiceCaptureChanged;
            _pushToTalk.Captured += OnVoiceCaptured;
        }

        private bool CanUseVoiceCapture()
        {
            return _pushToTalk != null && _voiceAvailable && _voiceButton != null &&
                   _voiceButton.gameObject.activeInHierarchy && _voiceButton.interactable && _textComplete &&
                   (_settingsRoot == null || !_settingsRoot.activeSelf) &&
                   (_journalRoot == null || !_journalRoot.activeSelf) &&
                   (_media == null || !_media.IsBusy) &&
                   (_minigames == null || !_minigames.IsActive) && !_exploring;
        }

        private void BeginVoiceCapture()
        {
            if (!CanUseVoiceCapture()) return;
            VoiceCaptureBoundary.CancelPending(
                _asrClient,
                _voiceInteractionClient,
                _voiceInteractionCoordinator);
            _pushToTalk.Press();
        }

        private void CompleteVoiceCapture()
        {
            if (_pushToTalk == null) return;
            VoiceCaptureState state = _pushToTalk.Current.State;
            if (state == VoiceCaptureState.AwaitingPermission || state == VoiceCaptureState.Recording)
                _pushToTalk.Release();
        }

        private void CancelVoiceCapture()
        {
            _holdToTalk?.Cancel();
            _asrClient?.Cancel();
            _voiceInteractionClient?.Cancel();
            _voiceInteractionCoordinator?.Cancel();
            if (_pushToTalk == null) return;
            VoiceCaptureState state = _pushToTalk.Current.State;
            if (state == VoiceCaptureState.AwaitingPermission || state == VoiceCaptureState.Recording)
                _pushToTalk.Cancel();
        }

        private void SetVoiceAvailable(bool available)
        {
            _voiceAvailable = available;
            if (!available) CancelVoiceCapture();
            if (_voiceButton != null)
            {
                _voiceButton.gameObject.SetActive(available);
                _voiceButton.interactable = available && _textComplete;
            }
            if (_voiceStatus != null)
                _voiceStatus.text = available ? "按住按钮或 V 录音；固定选项始终可用" : "";
        }

        private void OnVoiceCaptureChanged(VoiceCaptureUpdate update)
        {
            if (_voiceStatus == null || !_voiceAvailable) return;
            switch (update.State)
            {
                case VoiceCaptureState.AwaitingPermission:
                    _voiceStatus.text = "等待麦克风权限……固定选项仍可用";
                    break;
                case VoiceCaptureState.Recording:
                    _voiceStatus.text = "录音中，松开结束　电平 " + Mathf.RoundToInt(update.Level01 * 100f) + "%";
                    break;
                case VoiceCaptureState.Completed:
                    _voiceStatus.text = "录音完成，正在安全理解……固定选项仍可用";
                    break;
                case VoiceCaptureState.Fallback:
                    _voiceStatus.text = VoiceFallbackMessage(update.Error) + "；已保留固定选项";
                    if (update.Error == VoiceCaptureError.Silence) BeginVoiceInteraction(string.Empty);
                    break;
                default:
                    _voiceStatus.text = "按住按钮或 V 录音；固定选项始终可用";
                    break;
            }
        }

        private static string VoiceFallbackMessage(VoiceCaptureError error)
        {
            switch (error)
            {
                case VoiceCaptureError.NoMicrophone: return "未检测到麦克风";
                case VoiceCaptureError.PermissionDenied: return "麦克风权限被拒绝";
                case VoiceCaptureError.DeviceDisconnected: return "麦克风已断开";
                case VoiceCaptureError.Timeout: return "录音已超时";
                case VoiceCaptureError.Silence: return "未检测到有效声音";
                case VoiceCaptureError.Cancelled: return "录音已取消";
                default: return "麦克风暂时不可用";
            }
        }

        private void OnVoiceCaptured(VoiceAudioPayload audio)
        {
            if (audio == null) return;
            if (_asrClient == null || !_asrClient.IsEnabled)
            {
                if (_voiceStatus != null && _voiceAvailable)
                    _voiceStatus.text = "录音完成；在线转写未启用，已保留固定选项";
                return;
            }

            if (_voiceStatus != null && _voiceAvailable)
                _voiceStatus.text = "正在安全转写……固定选项仍可用";
            string capturedNodeId = _state.currentNodeId;
            _asrClient.Transcribe(audio, result => OnAsrCompleted(capturedNodeId, result));
        }

        private void OnAsrCompleted(string capturedNodeId, AsrClientResult result)
        {
            if (_voiceStatus == null || !_voiceAvailable || result == null ||
                !string.Equals(capturedNodeId, _state.currentNodeId, StringComparison.Ordinal)) return;
            if (!result.Success)
            {
                _voiceStatus.text = "在线转写暂时不可用；已保留固定选项";
                return;
            }

            string text = result.Text.Length <= 48 ? result.Text : result.Text.Substring(0, 48) + "…";
            _voiceStatus.text = "识别到：“" + text + "”；正在匹配当前场景";
            BeginVoiceInteraction(result.Text);
        }

        private void BeginVoiceInteraction(string transcript)
        {
            if (!_voiceAvailable || _voiceInteractionClient == null ||
                _voiceInteractionCoordinator == null || !_voiceInteractionClient.IsEnabled)
            {
                if (_voiceStatus != null && _voiceAvailable)
                    _voiceStatus.text = "在线剧情反应未启用；固定选项仍可用";
                return;
            }

            StoryNode node = StoryCatalog.Get(_state.currentNodeId);
            if (node == null || node.npcResponseRefs.Count == 0)
            {
                if (_voiceStatus != null) _voiceStatus.text = "当前场景没有已审核的语音反应；固定选项仍可用";
                return;
            }

            string capturedNodeId = node.id;
            int generation = _voiceInteractionCoordinator.Begin(capturedNodeId, node.npcResponseRefs);
            _voiceInteractionClient.Interact(
                capturedNodeId,
                transcript ?? string.Empty,
                result => OnVoiceInteractionCompleted(generation, capturedNodeId, result));
        }

        private void OnVoiceInteractionCompleted(
            int generation,
            string capturedNodeId,
            VoiceInteractionClientResult result)
        {
            if (_voiceInteractionCoordinator == null) return;
            VoicePresentationDecision decision = _voiceInteractionCoordinator.Complete(
                generation,
                _state.currentNodeId,
                result);
            if (decision.Outcome == VoicePresentationOutcome.IgnoreStale) return;
            if (decision.Outcome != VoicePresentationOutcome.PresentNpcResponse ||
                !StoryCatalog.TryGetNpcResponse(capturedNodeId, decision.NpcResponseId, out StoryNpcResponse response))
            {
                if (_voiceStatus != null && _voiceAvailable)
                    _voiceStatus.text = "未能安全匹配剧情反应；固定选项仍可用";
                return;
            }

            _speaker.text = StoryCatalog.Get(capturedNodeId).speaker + " · " + response.Emotion;
            _fullBody = response.Text;
            _body.text = response.Text;
            _textComplete = true;
            ShowNpcResponseFirstFrame(decision.NpcResponseId);
            if (_voiceStatus != null)
                _voiceStatus.text = result.Resolution == "confirmation_required"
                    ? "请使用固定选项确认；不会自动触发严重后果"
                    : "NPC 已回应；固定选项仍可用";
        }

        private void ShowNpcResponseFirstFrame(string responseId)
        {
            string key = GeneratedArtCatalog.FirstFrameForResponse(responseId);
            if (string.IsNullOrWhiteSpace(key)) return;

            SetBackdrop(key, Color.white, new Rect(0f, 0f, 1f, 1f));
            // The adopted v4 plates already contain the scout and the environment at cinematic
            // scale. Hiding the separate portrait prevents a second, stretched sticker layer.
            if (_portrait != null)
            {
                _portrait.texture = null;
                _portrait.gameObject.SetActive(false);
            }
            SetStoryCinematicMode(true);
            Debug.Log("RED_MIST_SCOUT_FIRST_FRAME response=" + responseId + " resource=" + key);
        }

        private void RefreshHud()
        {
            _state.Clamp();
            _hud.text = _state.RouteName + "　法力 " + _state.mana + "　神识 " + _state.divineSense + "　体魄 " + _state.health + "　灵药 " + _state.herbs + "　暴露 " + _state.exposure;
            int day = 3;
            int total = 5 * 60 + _state.worldMinutes;
            _clock.text = "第" + day + "日 " + (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        private void SetBackdrop(string key, Color tint, Rect? sourceRegion = null)
        {
            _backdrop.gameObject.SetActive(true);
            Texture2D texture = LoadTexture(key);
            _backdropCrop?.SetSourceRegion(sourceRegion ?? new Rect(0f, 0f, 1f, 1f));
            _cinematic?.SetBackdrop(texture, tint);
            _backdropCrop?.Refresh(true);
            if (_cinematic == null)
            {
                _backdrop.texture = texture;
                _backdrop.color = tint;
            }
        }

        private void SetPortrait(StoryNode node)
        {
            // The alliance plates already contain the route-specific actors at full cinematic
            // scale. Adding a second portrait card would duplicate the protagonist and recreate
            // the pasted-sticker look these integrated plates are intended to replace.
            if (node != null && node.id == "alliance")
            {
                _portrait.texture = null;
                _portrait.gameObject.SetActive(false);
                return;
            }

            string key = GeneratedArtCatalog.PortraitForNode(node.id, _state.route);
            _portrait.texture = LoadTexture(key);
            _portrait.color = Color.white;
            _portrait.gameObject.SetActive(_portrait.texture != null);
            _portraitCrop?.SetSourceRegion(GeneratedArtCatalog.PortraitRegionForNode(node.id));
            _portraitCrop?.Refresh(true);
        }

        private Texture2D LoadTexture(string key)
        {
            if (_textures.TryGetValue(key, out Texture2D cached)) return cached;
            Texture2D texture = key.Contains("/")
                ? GeneratedArtCatalog.LoadRequired(key)
                : Resources.Load<Texture2D>("Art/" + key);
            if (texture == null) texture = CreateFallbackTexture(key);
            _textures[key] = texture;
            return texture;
        }

        private static Texture2D CreateFallbackTexture(string key)
        {
            const int width = 320;
            const int height = 180;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false) { name = "Fallback_" + key };
            int seed = key.GetHashCode();
            System.Random random = new System.Random(seed);
            Color a = new Color(0.03f + (float)random.NextDouble() * 0.08f, 0.08f + (float)random.NextDouble() * 0.10f, 0.10f + (float)random.NextDouble() * 0.12f);
            Color b = new Color(0.10f + (float)random.NextDouble() * 0.18f, 0.04f + (float)random.NextDouble() * 0.10f, 0.06f + (float)random.NextDouble() * 0.12f);
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float p = y / (float)(height - 1);
                    float mist = Mathf.PerlinNoise(x * 0.023f, y * 0.031f + seed * 0.001f) * 0.12f;
                    pixels[y * width + x] = Color.Lerp(a, b, p) + new Color(mist, mist, mist, 0);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void ManualSave()
        {
            SaveSystem.Save(_state);
            _audio.Success();
            _cinematic.PlayCue("存档已写入", Jade, false);
        }

        private void ToggleSettings()
        {
            if (_titleRoot.activeSelf) return;
            bool active = !_settingsRoot.activeSelf;
            _settingsRoot.SetActive(active);
            if (active)
            {
                CancelVoiceCapture();
                _journalRoot.SetActive(false);
                RefreshSettingsSummary();
            }
        }

        private void ToggleJournal()
        {
            bool active = !_journalRoot.activeSelf;
            _journalRoot.SetActive(active);
            if (active)
            {
                CancelVoiceCapture();
                _settingsRoot.SetActive(false);
                StringBuilder text = new StringBuilder();
                text.AppendLine("<b>路线：</b>" + _state.RouteName);
                text.AppendLine("<b>当前地点：</b>" + _location.text);
                text.AppendLine("<b>世界时间：</b>" + _clock.text);
                text.AppendLine();
                text.AppendLine("<b>已确认情报</b>");
                if (_state.discoveries.Count == 0) text.AppendLine("暂无可靠情报。猜测不会显示为事实。");
                foreach (string item in _state.discoveries) text.AppendLine("• " + item);
                text.AppendLine();
                text.AppendLine("<b>关系与代价</b>");
                text.AppendLine("信任 " + _state.trust + "　尊重 " + _state.respect + "　猜疑 " + _state.suspicion + "　宗门义务 " + _state.sectDuty);
                text.AppendLine("底牌暴露 " + _state.exposure + "　护符 " + _state.wards + "　灵药 " + _state.herbs);
                text.AppendLine();
                text.AppendLine("<b>行动记录</b>");
                foreach (string id in _state.history)
                {
                    StoryNode node = StoryCatalog.Get(id);
                    if (node != null) text.AppendLine("• " + node.title + "／" + node.location);
                }
                _journalText.text = text.ToString();
                RefreshJournalArt();
            }
        }

        private void CycleJournalArt()
        {
            _journalArtIndex = (_journalArtIndex + 1) % GeneratedArtCatalog.ReferenceBoardCount;
            RefreshJournalArt();
            _audio?.Click();
        }

        private void RefreshJournalArt()
        {
            Texture2D texture = LoadTexture(GeneratedArtCatalog.ReferenceBoardAt(_journalArtIndex));
            _journalArt.texture = texture;
            if (texture != null)
                _journalArtFitter.aspectRatio = (float)texture.width / texture.height;
            _journalArtCaption.text = GeneratedArtCatalog.ReferenceBoardCaptionAt(_journalArtIndex)
                + "　" + (_journalArtIndex + 1) + "／" + GeneratedArtCatalog.ReferenceBoardCount;
        }

        private void ChangeVolume(float delta)
        {
            _state.masterVolume = Mathf.Clamp01(_state.masterVolume + delta);
            _state.cinematicVolume = _state.masterVolume;
            _audio.SetVolume(_state.masterVolume);
            _media?.SetVolume(_state.cinematicVolume);
            RefreshSettingsSummary();
        }

        private void ChangeTextSpeed(float delta)
        {
            _state.textSpeed = Mathf.Clamp(_state.textSpeed + delta, 0.5f, 2f);
            RefreshSettingsSummary();
        }

        private void ToggleReducedMotion()
        {
            _state.reducedMotion = !_state.reducedMotion;
            _cinematic.SetReducedMotion(_state.reducedMotion);
            RefreshSettingsSummary();
        }

        private void RefreshSettingsSummary()
        {
            _settingsSummary.text = "主音量／影视音量　" + Mathf.RoundToInt(_state.masterVolume * 100) + "%\n文字速度　" + _state.textSpeed.ToString("0.00") + "×\n减少动态／镜头震动　" + (_state.reducedMotion ? "开启" : "关闭") + "\n已观看影视片段　" + _state.watchedCinematics.Count + "／4\n\n空格：立即显示全部文字　数字1–4：选择　影视播放时 ESC：跳过";
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) CancelVoiceCapture();
        }

        private void OnDisable()
        {
            CancelVoiceCapture();
        }

        private void OnDestroy()
        {
            CancelVoiceCapture();
            if (_pushToTalk != null)
            {
                _pushToTalk.Changed -= OnVoiceCaptureChanged;
                _pushToTalk.Captured -= OnVoiceCaptured;
            }
            if (_holdToTalk != null)
            {
                _holdToTalk.Pressed -= BeginVoiceCapture;
                _holdToTalk.Released -= CompleteVoiceCapture;
                _holdToTalk.Cancelled -= CancelVoiceCapture;
            }
            _unityMicrophone?.Dispose();
            _unityMicrophone = null;
        }

    }
}
