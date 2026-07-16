using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lingmai.RedMist
{
    public sealed class SanctuaryPreview : MonoBehaviour
    {
        private readonly List<Material> _materials = new List<Material>();
        private Material _cinematicVistaMaterial;
        private GameObject _root;
        private Camera _camera;
        private Action _onExit;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private float _previousFov;
        private CameraClearFlags _previousClearFlags;
        private bool _ending;
        private bool _captureRequested;
        private bool _captureWritten;
        private float _captureClock;
        private bool _embedded;
        private StoryStageCamera _storyCamera;
        private GameObject _maleActor;
        private GameObject _femaleActor;
        private readonly List<GameObject> _supportingActors = new List<GameObject>();
        private readonly List<ActorSnapshot> _explorationActorSnapshots = new List<ActorSnapshot>();
        private readonly List<ExplorationCharacterBillboard> _explorationBillboards = new List<ExplorationCharacterBillboard>();
        private ThirdPersonExplorationController _explorationController;
        private bool _isExploring;
        private string _explorationNodeId;
        private PlayerRoute _explorationRoute;
        private Action<string> _onExplorationInteraction;
        private bool _explorationExitDelivered;
        private Vector3 _preExplorationCameraPosition;
        private Quaternion _preExplorationCameraRotation;
        private float _preExplorationCameraFov;

        private struct ActorSnapshot
        {
            public GameObject actor;
            public bool active;
            public Vector3 position;
            public Quaternion rotation;
        }

        public bool IsEmbedded => _embedded;
        public bool IsExploring => _isExploring;

        public void BeginExploration(
            string nodeId,
            PlayerRoute route,
            Action<string> onInteraction,
            Action<string> onPrompt,
            Action onExit)
        {
            if (_root == null || _camera == null)
            {
                Debug.LogError("RED_MIST_EXPLORATION_UNAVAILABLE node=" + nodeId);
                onPrompt?.Invoke("探索场景尚未准备完成");
                onExit?.Invoke();
                return;
            }

            if (_isExploring) EndExploration();

            _isExploring = true;
            _explorationNodeId = string.IsNullOrEmpty(nodeId) ? "camp" : nodeId;
            _explorationRoute = route == PlayerRoute.ChuMingqi ? PlayerRoute.ChuMingqi : PlayerRoute.ShenYan;
            _onExplorationInteraction = onInteraction;
            _explorationExitDelivered = false;
            UpdateExplorationVista(_explorationNodeId);

            _preExplorationCameraPosition = _camera.transform.position;
            _preExplorationCameraRotation = _camera.transform.rotation;
            _preExplorationCameraFov = _camera.fieldOfView;
            CaptureExplorationActors();

            GameObject player = _explorationRoute == PlayerRoute.ChuMingqi ? _femaleActor : _maleActor;
            if (player == null) player = _maleActor != null ? _maleActor : _femaleActor;
            if (player == null)
            {
                Debug.LogError("RED_MIST_EXPLORATION_PLAYER_MISSING node=" + _explorationNodeId);
                EndExploration();
                onPrompt?.Invoke("当前角色模型无法进入探索");
                onExit?.Invoke();
                return;
            }

            if (_storyCamera != null) _storyCamera.enabled = false;
            PlaceExplorationActors(_explorationNodeId, player);
            SetupExplorationCharacterBillboards();

            _explorationController = GetComponent<ThirdPersonExplorationController>();
            if (_explorationController == null)
                _explorationController = gameObject.AddComponent<ThirdPersonExplorationController>();
            _explorationController.Initialize(
                _camera,
                player,
                _explorationNodeId,
                HandleExplorationInteraction,
                onPrompt,
                () => HandleExplorationExitRequest(onExit));
            _explorationController.SetInputEnabled(true);
            Debug.Log("RED_MIST_EXPLORATION_BEGIN node=" + _explorationNodeId + " route=" + _explorationRoute);
        }

        public void SetExplorationInput(bool enabled)
        {
            if (!_isExploring || _explorationController == null) return;
            _explorationController.SetInputEnabled(enabled);
        }

        public void EndExploration()
        {
            if (!_isExploring) return;
            _isExploring = false;

            string completedNodeId = _explorationNodeId;
            PlayerRoute completedRoute = _explorationRoute;

            if (_explorationController != null) _explorationController.Shutdown();
            TeardownExplorationCharacterBillboards();
            if (_storyCamera != null)
            {
                _storyCamera.enabled = true;
                FocusStoryNode(completedNodeId, completedRoute, true);
            }
            else if (_camera != null)
            {
                _camera.transform.position = _preExplorationCameraPosition;
                _camera.transform.rotation = _preExplorationCameraRotation;
                _camera.fieldOfView = _preExplorationCameraFov;
            }
            RestoreExplorationActors();

            Debug.Log("RED_MIST_EXPLORATION_END node=" + completedNodeId + " route=" + completedRoute);
            _explorationNodeId = null;
            _explorationRoute = PlayerRoute.None;
            _onExplorationInteraction = null;
        }

        private void SetupExplorationCharacterBillboards()
        {
            TeardownExplorationCharacterBillboards();
            Texture2D maleFront = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.ExplorerShenYanFront);
            Texture2D maleBack = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.ExplorerShenYanBack);
            Texture2D femaleFront = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.ExplorerChuMingqiFront);
            Texture2D femaleBack = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.ExplorerChuMingqiBack);
            AddExplorationCharacterBillboard(_maleActor, maleFront, maleBack, 0f);
            AddExplorationCharacterBillboard(_femaleActor, femaleFront, femaleBack, 1.7f);
        }

        private void AddExplorationCharacterBillboard(GameObject actor, Texture2D front, Texture2D back, float phase)
        {
            if (actor == null || !actor.activeSelf || front == null || back == null) return;
            ExplorationCharacterBillboard billboard = actor.GetComponent<ExplorationCharacterBillboard>();
            if (billboard == null) billboard = actor.AddComponent<ExplorationCharacterBillboard>();
            billboard.Initialize(_camera, front, back, phase);
            _explorationBillboards.Add(billboard);
        }

        private void TeardownExplorationCharacterBillboards()
        {
            foreach (ExplorationCharacterBillboard billboard in _explorationBillboards)
            {
                if (billboard != null) billboard.Shutdown();
            }
            _explorationBillboards.Clear();
        }

        public void Begin(Action onExit)
        {
            _onExit = onExit;
            _captureRequested = Array.IndexOf(Environment.GetCommandLineArgs(), "-captureSanctuary") >= 0;
            BuildScene();
        }

        public void BeginEmbedded()
        {
            _embedded = true;
            _captureRequested = false;
            BuildScene();
            FocusStoryNode("title", PlayerRoute.None, true);
        }

        private void CaptureExplorationActors()
        {
            _explorationActorSnapshots.Clear();
            CaptureExplorationActor(_maleActor);
            CaptureExplorationActor(_femaleActor);
            foreach (GameObject actor in _supportingActors) CaptureExplorationActor(actor);
        }

        private void CaptureExplorationActor(GameObject actor)
        {
            if (actor == null) return;
            _explorationActorSnapshots.Add(new ActorSnapshot
            {
                actor = actor,
                active = actor.activeSelf,
                position = actor.transform.position,
                rotation = actor.transform.rotation
            });
        }

        private void RestoreExplorationActors()
        {
            foreach (ActorSnapshot snapshot in _explorationActorSnapshots)
            {
                if (snapshot.actor == null) continue;
                snapshot.actor.transform.position = snapshot.position;
                snapshot.actor.transform.rotation = snapshot.rotation;
                snapshot.actor.SetActive(snapshot.active);
            }
            _explorationActorSnapshots.Clear();
        }

        private void PlaceExplorationActors(string nodeId, GameObject player)
        {
            GetExplorationSpawn(nodeId, out float playerX, out float playerZ, out float playerYaw);
            PlaceActor(player, playerX, playerZ, 0.15f, playerYaw);
            player.SetActive(true);

            GameObject companion = player == _maleActor ? _femaleActor : _maleActor;
            if (companion != null)
            {
                GetCompanionPosition(nodeId, out float companionX, out float companionZ, out float companionYaw);
                PlaceActor(companion, companionX, companionZ, 0.15f, companionYaw);
                companion.SetActive(true);
            }

            for (int i = 0; i < _supportingActors.Count; i++)
            {
                GameObject actor = _supportingActors[i];
                if (actor == null) continue;
                // Hide placeholder disciples in the controllable view until they receive their
                // own authored presentation skins. The companion protagonist remains visible.
                actor.SetActive(false);
            }
        }

        private static void GetExplorationSpawn(string nodeId, out float x, out float z, out float yaw)
        {
            switch (nodeId)
            {
                case "herb_route":
                case "corpse_signs":
                case "rescue":
                    x = -4.0f;
                    z = 13.0f;
                    yaw = 18f;
                    break;
                case "shijun":
                case "formation":
                    x = -9.0f;
                    z = 51.0f;
                    yaw = 4f;
                    break;
                case "underground":
                case "combat_one":
                case "combat_two":
                case "aftermath":
                    x = -9.0f;
                    z = 60.0f;
                    yaw = 4f;
                    break;
                default:
                    x = -11.5f;
                    z = 44.0f;
                    yaw = -42f;
                    break;
            }
        }

        private static void GetCompanionPosition(string nodeId, out float x, out float z, out float yaw)
        {
            if (nodeId == "herb_route" || nodeId == "corpse_signs" || nodeId == "rescue")
            {
                x = 0.3f;
                z = 22.5f;
                yaw = 182f;
            }
            else if (nodeId == "shijun" || nodeId == "formation")
            {
                x = -5.7f;
                z = 57.0f;
                yaw = 205f;
            }
            else
            {
                x = -8.0f;
                z = 52.0f;
                yaw = 190f;
            }
        }

        private void HandleExplorationInteraction(string interactionId)
        {
            if (!_isExploring) return;
            _onExplorationInteraction?.Invoke(interactionId);
        }

        private void HandleExplorationExitRequest(Action callback)
        {
            if (_explorationExitDelivered) return;
            _explorationExitDelivered = true;
            if (_isExploring) EndExploration();
            callback?.Invoke();
        }

        private void Update()
        {
            if (_captureRequested)
            {
                _captureClock += Time.unscaledDeltaTime;
                if (!_captureWritten && _captureClock >= 3f)
                {
                    _captureWritten = true;
                    ScreenCapture.CaptureScreenshot(@"E:\fanren\Builds\RedMistSanctuaryPreview\sanctuary-preview.png");
                    Debug.Log("RED_MIST_SANCTUARY_CAPTURE_REQUESTED");
                }
                if (_captureClock >= 5f) Application.Quit();
            }
            if (!_embedded && Input.GetKeyDown(KeyCode.Escape)) EndPreview();
        }

        private void OnGUI()
        {
            if (_embedded) return;
            GUIStyle title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 23,
                fontStyle = FontStyle.Bold
            };
            title.normal.textColor = new Color(0.92f, 0.94f, 0.87f);
            GUIStyle body = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            body.normal.textColor = new Color(0.80f, 0.86f, 0.82f);
            GUI.Box(new Rect(22, 20, 520, 104), string.Empty);
            GUI.Label(new Rect(40, 30, 470, 30), "赤雾秘苑 · 实时3D视觉样板", title);
            GUI.Label(new Rect(40, 62, 480, 54), "WASD移动  ·  鼠标右键观察  ·  Q/E升降  ·  Shift加速\nC切换自动电影镜头  ·  Esc返回主菜单", body);
        }

        private void BuildScene()
        {
            _root = new GameObject("SanctuaryPreviewRoot");
            _camera = Camera.main;
            if (_camera == null)
            {
                GameObject cameraObject = new GameObject("SanctuaryCamera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                _camera = cameraObject.GetComponent<Camera>();
                cameraObject.transform.SetParent(_root.transform);
            }

            _previousPosition = _camera.transform.position;
            _previousRotation = _camera.transform.rotation;
            _previousFov = _camera.fieldOfView;
            _previousClearFlags = _camera.clearFlags;

            ConfigureRendering();
            // Keep the wet textures, but do not multiply their already-dark albedo into black.
            // The authored plates and character skins both contain deep shadows, so a readable
            // midtone floor is important when the player can freely orbit the camera.
            Material rock = CreateNaturalMaterial("Wet moss rock", "mossy_rock", new Color(0.56f, 0.60f, 0.55f), 0.08f);
            Material cobble = CreateNaturalMaterial("Moss cobblestone", "mossy_cobblestone", new Color(0.72f, 0.74f, 0.69f), 0.14f);
            Material darkStone = CreateNaturalMaterial("Ancient dark stone", "mossy_rock", new Color(0.43f, 0.47f, 0.44f), 0.07f);
            Material bamboo = CreateColorMaterial("Bamboo", new Color(0.12f, 0.29f, 0.18f), 0.22f);
            Material leaf = CreateColorMaterial("Leaves", new Color(0.055f, 0.18f, 0.105f), 0.10f);
            Material bark = CreateColorMaterial("Wet bark", new Color(0.12f, 0.095f, 0.065f), 0.06f);
            Material bronze = CreateColorMaterial("Aged bronze", new Color(0.18f, 0.23f, 0.18f), 0.42f, 0.72f);

            CreateGround(rock);
            CreateStream();
            CreatePath(cobble);
            CreateCliffs(rock);
            CreateBoulders(rock);
            CreateGate(darkStone, bronze, cobble);
            CreateCinematicVista();
            CreateCamp(bark, bronze);
            CreateBambooGrove(bamboo, leaf);
            CreatePineGrove(bark, leaf);
            CreateAncientTrees();
            CreateShrubs(leaf);
            CreateMist();
            CreateSpiritMotes();
            _maleActor = CreateNpc(new Vector3(-10.6f, GroundHeight(-10.6f, 49f) + 0.15f, 49f), new Color(0.12f, 0.22f, 0.25f), false, "沈砚");
            _femaleActor = CreateNpc(new Vector3(-7.8f, GroundHeight(-7.8f, 52f) + 0.15f, 52f), new Color(0.56f, 0.58f, 0.52f), true, "楚明绮");
            _supportingActors.Add(CreateNpc(new Vector3(-16.2f, GroundHeight(-16.2f, 45f) + 0.15f, 45f), new Color(0.28f, 0.31f, 0.29f), false, "营地弟子甲"));
            _supportingActors.Add(CreateNpc(new Vector3(-14.2f, GroundHeight(-14.2f, 43f) + 0.15f, 43f), new Color(0.44f, 0.46f, 0.43f), true, "营地弟子乙"));
            CreateLighting();

            _camera.transform.position = new Vector3(-13f, 5.4f, -38f);
            _camera.transform.LookAt(new Vector3(-7f, 10f, 66f));
            _camera.fieldOfView = 52f;
            _camera.nearClipPlane = 0.08f;
            _camera.farClipPlane = 320f;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            _camera.clearFlags = CameraClearFlags.Skybox;
            if (_embedded)
            {
                _storyCamera = _camera.gameObject.AddComponent<StoryStageCamera>();
            }
            else
            {
                _camera.gameObject.AddComponent<SanctuaryFlyCamera>();
            }
            _camera.gameObject.AddComponent<CinematicPostFx>();
        }

        private void ConfigureRendering()
        {
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowDistance = 180f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadows = ShadowQuality.All;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0034f;
            RenderSettings.fogColor = new Color(0.13f, 0.19f, 0.20f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 0.84f;
            RenderSettings.ambientSkyColor = new Color(0.40f, 0.46f, 0.47f);
            RenderSettings.ambientEquatorColor = new Color(0.27f, 0.32f, 0.30f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.14f, 0.13f);

            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                Material sky = new Material(skyShader) { name = "Sanctuary dawn sky" };
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 5f);
                sky.SetFloat("_AtmosphereThickness", 1.25f);
                sky.SetColor("_SkyTint", new Color(0.36f, 0.48f, 0.53f));
                sky.SetColor("_GroundColor", new Color(0.12f, 0.14f, 0.13f));
                sky.SetFloat("_Exposure", 0.60f);
                RenderSettings.skybox = sky;
                _materials.Add(sky);
            }
            DynamicGI.UpdateEnvironment();
        }

        private Material CreateNaturalMaterial(string name, string prefix, Color tint, float smoothness)
        {
            Shader shader = LoadLocalResource<Shader>("Shaders/NaturalPBR", true);
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name };
            material.SetTexture("_MainTex", LoadLocalResource<Texture2D>("Environment/" + prefix + "_diff_1k", true));
            material.SetTexture("_NormalMap", LoadLocalResource<Texture2D>("Environment/" + prefix + "_nor_gl_1k", true));
            material.SetTexture("_RoughnessMap", LoadLocalResource<Texture2D>("Environment/" + prefix + "_rough_1k", true));
            material.SetColor("_Tint", tint);
            material.SetFloat("_FallbackSmoothness", smoothness);
            _materials.Add(material);
            return material;
        }

        private Material CreateColorMaterial(string name, Color color, float smoothness, float metallic = 0f)
        {
            Material material = new Material(Shader.Find("Standard")) { name = name, color = color };
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", metallic);
            _materials.Add(material);
            return material;
        }

        private void CreateGround(Material material)
        {
            const int xCount = 41;
            const int zCount = 91;
            const float width = 84f;
            const float length = 180f;
            Vector3[] vertices = new Vector3[xCount * zCount];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(xCount - 1) * (zCount - 1) * 6];
            for (int z = 0; z < zCount; z++)
            {
                float worldZ = -length * 0.5f + z * length / (zCount - 1);
                for (int x = 0; x < xCount; x++)
                {
                    float worldX = -width * 0.5f + x * width / (xCount - 1);
                    int index = z * xCount + x;
                    vertices[index] = new Vector3(worldX, GroundHeight(worldX, worldZ), worldZ);
                    uv[index] = new Vector2(worldX / 3.2f, worldZ / 3.2f);
                }
            }
            int ti = 0;
            for (int z = 0; z < zCount - 1; z++)
            {
                for (int x = 0; x < xCount - 1; x++)
                {
                    int i = z * xCount + x;
                    triangles[ti++] = i;
                    triangles[ti++] = i + xCount;
                    triangles[ti++] = i + 1;
                    triangles[ti++] = i + 1;
                    triangles[ti++] = i + xCount;
                    triangles[ti++] = i + xCount + 1;
                }
            }
            Mesh mesh = new Mesh { name = "Sculpted valley terrain", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject ground = new GameObject("Sculpted Valley Terrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            ground.transform.SetParent(_root.transform);
            ground.GetComponent<MeshFilter>().sharedMesh = mesh;
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            ground.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        private void CreateStream()
        {
            const int segments = 91;
            const float length = 180f;
            Vector3[] vertices = new Vector3[segments * 2];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(segments - 1) * 6];
            for (int i = 0; i < segments; i++)
            {
                float z = -length * 0.5f + i * length / (segments - 1);
                float center = StreamX(z);
                float width = 4.3f + Mathf.PerlinNoise(i * 0.11f, 0.2f) * 1.8f;
                float y = GroundHeight(center, z) + 0.23f;
                vertices[i * 2] = new Vector3(center - width, y, z);
                vertices[i * 2 + 1] = new Vector3(center + width, y, z);
                uv[i * 2] = new Vector2(0f, i * 0.22f);
                uv[i * 2 + 1] = new Vector2(1f, i * 0.22f);
            }
            int ti = 0;
            for (int i = 0; i < segments - 1; i++)
            {
                int index = i * 2;
                triangles[ti++] = index;
                triangles[ti++] = index + 2;
                triangles[ti++] = index + 1;
                triangles[ti++] = index + 1;
                triangles[ti++] = index + 2;
                triangles[ti++] = index + 3;
            }
            Mesh mesh = new Mesh { name = "Winding stream", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            GameObject stream = new GameObject("Living Stream", typeof(MeshFilter), typeof(MeshRenderer));
            stream.transform.SetParent(_root.transform);
            stream.GetComponent<MeshFilter>().sharedMesh = mesh;
            Shader waterShader = LoadLocalResource<Shader>("Shaders/SanctuaryWater", true);
            Material water = new Material(waterShader != null ? waterShader : Shader.Find("Standard")) { name = "Moving clear water" };
            _materials.Add(water);
            stream.GetComponent<MeshRenderer>().sharedMaterial = water;
        }

        private void CreatePath(Material material)
        {
            System.Random random = new System.Random(9041);
            for (int i = 0; i < 53; i++)
            {
                float z = -76f + i * 2.55f;
                float x = -10.8f + Mathf.Sin(z * 0.045f) * 2.2f;
                float y = GroundHeight(x, z) + 0.25f;
                GameObject slab = CreatePrimitive(PrimitiveType.Cube, "Wet stone path", new Vector3(x, y, z), new Vector3(4.2f + Range(random, -0.5f, 0.5f), 0.28f, 2.35f), material);
                slab.transform.rotation = Quaternion.Euler(Range(random, -2f, 2f), Range(random, -3.5f, 3.5f), Range(random, -1.5f, 1.5f));
            }
        }

        private void CreateCliffs(Material material)
        {
            CreateCliffWall(-1, material);
            CreateCliffWall(1, material);
        }

        private void CreateCliffRockFormations(Material material)
        {
            GameObject prefab = LoadLocalResource<GameObject>("Environment/Models/rock_moss_set_02/rock_moss_set_02_1k", false);
            if (prefab == null) return;
            System.Random random = new System.Random(5502);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 18; i++)
                {
                    float z = -88f + i * 10.3f + Range(random, -3f, 3f);
                    float x = side * Range(random, 29f, 36f);
                    float y = GroundHeight(x, z) + Range(random, 2f, 15f);
                    GameObject rocks = Instantiate(prefab, new Vector3(x, y, z), Quaternion.Euler(Range(random, -18f, 18f), Range(random, 0f, 360f), Range(random, -15f, 15f)), _root.transform);
                    rocks.name = "CC0 cliff rock outcrop";
                    float scale = Range(random, 1.8f, 4.2f);
                    rocks.transform.localScale = new Vector3(scale, scale * Range(random, 1f, 1.7f), scale);
                    foreach (Renderer renderer in rocks.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
                }
            }
        }

        private void CreateCliffWall(int side, Material material)
        {
            const int zSegments = 41;
            const int ySegments = 9;
            Vector3[] vertices = new Vector3[zSegments * ySegments];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(zSegments - 1) * (ySegments - 1) * 6];
            for (int zIndex = 0; zIndex < zSegments; zIndex++)
            {
                float z = -96f + zIndex * 4.8f;
                float ridge = Mathf.PerlinNoise(zIndex * 0.19f, side > 0 ? 0.73f : 0.19f);
                float baseX = side * (35f + ridge * 6f + Mathf.Sin(zIndex * 0.54f) * 1.5f);
                float bottom = GroundHeight(baseX, z) - 4f;
                float height = 27f + ridge * 23f + Mathf.PerlinNoise(zIndex * 0.37f, 0.52f) * 8f;
                for (int yIndex = 0; yIndex < ySegments; yIndex++)
                {
                    float fraction = yIndex / (float)(ySegments - 1);
                    float fracture = (Mathf.PerlinNoise(zIndex * 0.31f + yIndex * 0.23f, side > 0 ? 0.37f : 0.81f) - 0.5f) * 4.8f;
                    float ledge = Mathf.Sin(fraction * Mathf.PI * 5f + zIndex * 0.7f) * 0.9f;
                    int index = zIndex * ySegments + yIndex;
                    vertices[index] = new Vector3(baseX + side * (fraction * 2.8f + fracture + ledge), bottom + height * fraction, z);
                    uv[index] = new Vector2(z / 6f, (bottom + height * fraction) / 6f);
                }
            }
            int ti = 0;
            for (int zIndex = 0; zIndex < zSegments - 1; zIndex++)
            {
                for (int yIndex = 0; yIndex < ySegments - 1; yIndex++)
                {
                    int index = zIndex * ySegments + yIndex;
                    if (side < 0)
                    {
                        triangles[ti++] = index;
                        triangles[ti++] = index + 1;
                        triangles[ti++] = index + ySegments;
                        triangles[ti++] = index + ySegments;
                        triangles[ti++] = index + 1;
                        triangles[ti++] = index + ySegments + 1;
                    }
                    else
                    {
                        triangles[ti++] = index;
                        triangles[ti++] = index + ySegments;
                        triangles[ti++] = index + 1;
                        triangles[ti++] = index + ySegments;
                        triangles[ti++] = index + ySegments + 1;
                        triangles[ti++] = index + 1;
                    }
                }
            }
            Mesh mesh = new Mesh { name = side < 0 ? "Left natural cliff" : "Right natural cliff", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject wall = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            wall.transform.SetParent(_root.transform);
            wall.GetComponent<MeshFilter>().sharedMesh = mesh;
            wall.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void CreateBoulders(Material material)
        {
            GameObject prefab = LoadLocalResource<GameObject>("Environment/Models/rock_moss_set_02/rock_moss_set_02_1k", false);
            if (prefab != null)
            {
                System.Random modelRandom = new System.Random(2307);
                for (int i = 0; i < 24; i++)
                {
                    float z = Range(modelRandom, -88f, 88f);
                    bool left = modelRandom.NextDouble() > 0.5;
                    float x = left ? Range(modelRandom, -34f, -17f) : Range(modelRandom, 14f, 34f);
                    GameObject rocks = Instantiate(prefab, new Vector3(x, GroundHeight(x, z), z), Quaternion.Euler(0f, Range(modelRandom, 0f, 360f), 0f), _root.transform);
                    rocks.name = "CC0 moss rock formation";
                    float scale = Range(modelRandom, 0.7f, 1.9f);
                    rocks.transform.localScale = Vector3.one * scale;
                    foreach (Renderer renderer in rocks.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
                }
                return;
            }

            System.Random random = new System.Random(2307);
            for (int i = 0; i < 92; i++)
            {
                float z = Range(random, -88f, 88f);
                bool left = random.NextDouble() > 0.5;
                float x = left ? Range(random, -35f, -16f) : Range(random, 13f, 35f);
                float size = Range(random, 0.8f, 4.7f);
                float y = GroundHeight(x, z) + size * 0.25f;
                GameObject rock = CreatePrimitive(PrimitiveType.Sphere, "Weathered boulder", new Vector3(x, y, z), new Vector3(size * Range(random, 0.7f, 1.4f), size * Range(random, 0.55f, 0.95f), size * Range(random, 0.8f, 1.5f)), material);
                rock.transform.rotation = Quaternion.Euler(Range(random, 0f, 180f), Range(random, 0f, 180f), Range(random, 0f, 180f));
            }
        }

        private void CreateGate(Material stone, Material bronze, Material path)
        {
            float z = 69f;
            float baseY = GroundHeight(-8f, z) + 1.0f;
            for (int step = 0; step < 8; step++)
                CreatePrimitive(PrimitiveType.Cube, "Gate stair", new Vector3(-8f, baseY + step * 0.34f, z - 8f + step * 1.2f), new Vector3(12f - step * 0.35f, 0.55f, 1.4f), path);
            baseY += 2.7f;
            CreatePrimitive(PrimitiveType.Cube, "Left monolith", new Vector3(-13f, baseY + 6.5f, z), new Vector3(3.5f, 13f, 3.2f), stone);
            CreatePrimitive(PrimitiveType.Cube, "Right monolith", new Vector3(-3f, baseY + 6.5f, z), new Vector3(3.5f, 13f, 3.2f), stone);
            CreatePrimitive(PrimitiveType.Cube, "Carved lintel", new Vector3(-8f, baseY + 13.1f, z), new Vector3(14.5f, 2.4f, 3.8f), stone);
            CreatePrimitive(PrimitiveType.Cube, "Upper roof", new Vector3(-8f, baseY + 15.2f, z), new Vector3(18f, 0.9f, 5.2f), bronze).transform.rotation = Quaternion.Euler(0f, 0f, 2f);
            CreatePrimitive(PrimitiveType.Cube, "Bronze sealed door", new Vector3(-8f, baseY + 5.9f, z + 0.7f), new Vector3(6.6f, 11.2f, 0.5f), bronze);
            for (int i = 0; i < 3; i++)
            {
                GameObject ring = CreatePrimitive(PrimitiveType.Cylinder, "Gate seal", new Vector3(-8f, baseY + 4.5f + i * 2.1f, z + 1.1f), new Vector3(1.0f + i * 0.23f, 0.18f, 1.0f + i * 0.23f), bronze);
                ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            Light glow = new GameObject("Restrained red gate light", typeof(Light)).GetComponent<Light>();
            glow.transform.SetParent(_root.transform);
            glow.transform.position = new Vector3(-8f, baseY + 5.5f, z - 4f);
            glow.type = LightType.Point;
            glow.range = 19f;
            glow.intensity = 2.2f;
            glow.color = new Color(0.72f, 0.19f, 0.15f);
        }

        private void CreateCinematicVista()
        {
            // The nearby terrain remains fully 3D and walkable. A large authored vista replaces
            // the visibly blockout-quality far gate, giving the exploration camera a detailed
            // architectural destination while retaining real foreground parallax and collision.
            string[] blockoutNames =
            {
                "Left monolith", "Right monolith", "Carved lintel", "Upper roof",
                "Bronze sealed door", "Gate seal"
            };
            foreach (Transform child in _root.transform)
            {
                if (Array.IndexOf(blockoutNames, child.name) >= 0) child.gameObject.SetActive(false);
            }

            Texture2D vistaTexture = GeneratedArtCatalog.LoadRequired(GeneratedArtCatalog.SanctuaryEntrance);
            Shader shader = LoadLocalResource<Shader>("Shaders/CinematicVistaBlend", true);
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Standard");
            _cinematicVistaMaterial = new Material(shader) { name = "Authored sanctuary gate vista" };
            _cinematicVistaMaterial.mainTexture = vistaTexture;
            _cinematicVistaMaterial.color = Color.white;
            if (_cinematicVistaMaterial.HasProperty("_Exposure")) _cinematicVistaMaterial.SetFloat("_Exposure", 1.08f);
            if (_cinematicVistaMaterial.HasProperty("_ShadowLift")) _cinematicVistaMaterial.SetFloat("_ShadowLift", 0.01f);
            if (_cinematicVistaMaterial.HasProperty("_EdgeFeather")) _cinematicVistaMaterial.SetFloat("_EdgeFeather", 0.045f);
            if (_cinematicVistaMaterial.HasProperty("_BottomFeather")) _cinematicVistaMaterial.SetFloat("_BottomFeather", 0.08f);
            _materials.Add(_cinematicVistaMaterial);

            GameObject vista = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vista.name = "Cinematic sanctuary vista";
            vista.transform.SetParent(_root.transform);
            // The previous 39 x 26 quad sat only a few metres behind the seal. Its hard left
            // edge crossed the centre of the screen during herb-route exploration. Treat this
            // as a genuine distant matte instead: the large 2:1 plate sits beyond the terrain,
            // changes apparent scale very little while walking, and its shader feathers into fog.
            vista.transform.position = new Vector3(-8f, 54f, 135f);
            vista.transform.rotation = Quaternion.identity;
            vista.transform.localScale = new Vector3(228f, 114f, 1f);
            Renderer vistaRenderer = vista.GetComponent<Renderer>();
            vistaRenderer.sharedMaterial = _cinematicVistaMaterial;
            vistaRenderer.shadowCastingMode = ShadowCastingMode.Off;
            vistaRenderer.receiveShadows = false;
            Collider vistaCollider = vista.GetComponent<Collider>();
            if (vistaCollider != null) Destroy(vistaCollider);

            GameObject boundary = new GameObject("Cinematic vista boundary", typeof(BoxCollider));
            boundary.transform.SetParent(_root.transform);
            boundary.transform.position = new Vector3(-8f, 8.0f, 66.6f);
            boundary.transform.localScale = new Vector3(39f, 16f, 0.55f);
        }

        private void UpdateExplorationVista(string nodeId)
        {
            if (_cinematicVistaMaterial == null) return;
            string texturePath = nodeId == "herb_route" || nodeId == "corpse_signs" || nodeId == "rescue"
                ? GeneratedArtCatalog.HerbCourtyard
                : nodeId == "formation"
                    ? GeneratedArtCatalog.CelestialFormationHall
                    : nodeId == "underground" || nodeId.StartsWith("combat", StringComparison.Ordinal)
                        ? GeneratedArtCatalog.SwordSealVault
                        : GeneratedArtCatalog.SanctuaryEntrance;
            Texture2D texture = GeneratedArtCatalog.LoadRequired(texturePath);
            if (texture != null) _cinematicVistaMaterial.mainTexture = texture;
        }

        private void CreateBambooGrove(Material trunk, Material leaf)
        {
            System.Random random = new System.Random(4207);
            for (int i = 0; i < 58; i++)
            {
                float z = Range(random, -76f, 76f);
                float x = Range(random, 18f, 34f);
                CreateBamboo(new Vector3(x, GroundHeight(x, z), z), Range(random, 6f, 12f), Range(random, 0.10f, 0.19f), trunk, leaf, random);
            }
        }

        private void CreateBamboo(Vector3 position, float height, float radius, Material trunk, Material leaf, System.Random random)
        {
            GameObject root = new GameObject("Bamboo cluster");
            root.transform.SetParent(_root.transform);
            root.transform.position = position;
            GameObject culm = CreatePrimitive(PrimitiveType.Cylinder, "Bamboo culm", position + Vector3.up * height * 0.5f, new Vector3(radius, height * 0.5f, radius), trunk);
            culm.transform.SetParent(root.transform, true);
            culm.transform.rotation = Quaternion.Euler(Range(random, -2f, 2f), Range(random, 0f, 360f), Range(random, -2f, 2f));
            for (int n = 1; n < 6; n++)
            {
                float y = height * n / 6.3f;
                GameObject node = CreatePrimitive(PrimitiveType.Cylinder, "Bamboo node", position + Vector3.up * y, new Vector3(radius * 1.35f, 0.055f, radius * 1.35f), trunk);
                node.transform.SetParent(root.transform, true);
            }
            for (int n = 0; n < 13; n++)
            {
                float angle = Range(random, 0f, 360f);
                float y = Range(random, height * 0.48f, height * 0.97f);
                float distance = Range(random, 0.25f, 1.2f);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                GameObject blade = CreatePrimitive(PrimitiveType.Cube, "Bamboo leaf", position + Vector3.up * y + offset, new Vector3(Range(random, 0.45f, 1.1f), 0.025f, Range(random, 0.08f, 0.18f)), leaf, false);
                blade.transform.SetParent(root.transform, true);
                blade.transform.rotation = Quaternion.Euler(Range(random, -35f, 35f), angle, Range(random, -20f, 20f));
            }
        }

        private void CreatePineGrove(Material bark, Material foliage)
        {
            GameObject prefab = LoadLocalResource<GameObject>("Environment/Models/pine_sapling_small/pine_sapling_small_1k", false);
            if (prefab != null)
            {
                Material pineBark = CreatePineBarkMaterial();
                Material pineNeedles = CreatePineNeedleMaterial();
                System.Random modelRandom = new System.Random(7812);
                for (int i = 0; i < 20; i++)
                {
                    float z = Range(modelRandom, -80f, 83f);
                    float x = i < 15 ? Range(modelRandom, -34f, -18f) : Range(modelRandom, 18f, 31f);
                    float ground = GroundHeight(x, z);
                    GameObject pine = Instantiate(prefab, new Vector3(x, ground, z), Quaternion.Euler(0f, Range(modelRandom, 0f, 360f), 0f), _root.transform);
                    pine.name = "CC0 pine sapling";
                    foreach (Renderer renderer in pine.GetComponentsInChildren<Renderer>())
                    {
                        Material[] source = renderer.sharedMaterials;
                        Material[] replacements = new Material[source.Length];
                        for (int m = 0; m < source.Length; m++)
                        {
                            string materialName = source[m] != null ? source[m].name.ToLowerInvariant() : string.Empty;
                            replacements[m] = materialName.Contains("twig") || materialName.Contains("needle") || materialName.Contains("leaf") ? pineNeedles : pineBark;
                        }
                        renderer.sharedMaterials = replacements;
                    }
                    FitModelToHeight(pine, Range(modelRandom, 7.5f, 12.5f), ground);
                }
                return;
            }

            System.Random random = new System.Random(7812);
            for (int i = 0; i < 24; i++)
            {
                float z = Range(random, -80f, 83f);
                float x = Range(random, -34f, -20f);
                float ground = GroundHeight(x, z);
                float height = Range(random, 8f, 17f);
                CreatePrimitive(PrimitiveType.Cylinder, "Pine trunk", new Vector3(x, ground + height * 0.45f, z), new Vector3(0.42f, height * 0.45f, 0.42f), bark);
                for (int layer = 0; layer < 4; layer++)
                {
                    float radius = 3.5f - layer * 0.65f;
                    GameObject crown = CreatePrimitive(PrimitiveType.Sphere, "Pine crown", new Vector3(x, ground + height * (0.53f + layer * 0.11f), z), new Vector3(radius, 1.15f, radius), foliage, false);
                    crown.transform.rotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
                }
            }
        }

        private void CreateAncientTrees()
        {
            GameObject prefab = LoadLocalResource<GameObject>("Environment/Models/island_tree_01/island_tree_01_1k", false);
            if (prefab == null) return;
            Material trunk = CreateNaturalMaterial("Ancient tree trunk", "Models/island_tree_01/textures/island_tree_01", new Color(0.70f, 0.64f, 0.52f), 0.08f);
            Material leaves = CreateFoliageMaterial("Ancient tree leaves", "Models/island_tree_01/textures/island_tree_01_leaves", new Color(0.46f, 0.66f, 0.42f), 0.34f);
            Material branches = CreateNaturalMaterial("Ancient tree branches", "Models/island_tree_01/textures/island_tree_01_branches", new Color(0.64f, 0.58f, 0.48f), 0.06f);
            Vector3[] positions =
            {
                new Vector3(-18f, 0f, 59f),
                new Vector3(7f, 0f, 58f),
                new Vector3(19f, 0f, 34f),
                new Vector3(-28f, 0f, 24f)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                p.y = GroundHeight(p.x, p.z);
                GameObject tree = Instantiate(prefab, p, Quaternion.Euler(0f, 37f + i * 83f, 0f), _root.transform);
                tree.name = "CC0 ancient valley tree";
                foreach (Renderer renderer in tree.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] source = renderer.sharedMaterials;
                    Material[] replacement = new Material[source.Length];
                    for (int m = 0; m < source.Length; m++)
                    {
                        string name = source[m] != null ? source[m].name.ToLowerInvariant() : string.Empty;
                        replacement[m] = name.Contains("leaves") ? leaves : name.Contains("branches") ? branches : trunk;
                    }
                    renderer.sharedMaterials = replacement;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                FitModelToHeight(tree, 11.5f + i * 1.35f, p.y);
            }
        }

        private Material CreatePineBarkMaterial()
        {
            Shader shader = LoadLocalResource<Shader>("Shaders/NaturalPBR", true);
            Material material = new Material(shader) { name = "Pine bark PBR" };
            material.SetTexture("_MainTex", LoadLocalResource<Texture2D>("Environment/Models/pine_sapling_small/textures/pine_sapling_small_bark_diff_1k", true));
            material.SetTexture("_NormalMap", LoadLocalResource<Texture2D>("Environment/Models/pine_sapling_small/textures/pine_sapling_small_bark_nor_gl_1k", true));
            material.SetTexture("_RoughnessMap", LoadLocalResource<Texture2D>("Environment/Models/pine_sapling_small/textures/pine_sapling_small_bark_rough_1k", true));
            material.SetColor("_Tint", new Color(0.62f, 0.58f, 0.50f));
            material.SetFloat("_FallbackSmoothness", 0.06f);
            _materials.Add(material);
            return material;
        }

        private Material CreatePineNeedleMaterial()
        {
            Shader shader = LoadLocalResource<Shader>("Shaders/FoliageCutout", true);
            Material material = new Material(shader) { name = "Pine needles cutout" };
            material.SetTexture("_MainTex", LoadLocalResource<Texture2D>("Environment/Models/pine_sapling_small/textures/pine_sapling_small_twig_diff_1k", true));
            material.SetTexture("_AlphaTex", LoadLocalResource<Texture2D>("Environment/Models/pine_sapling_small/textures/pine_sapling_small_twig_alpha_1k", true));
            material.SetTexture("_NormalMap", LoadLocalResource<Texture2D>("Environment/Models/pine_sapling_small/textures/pine_sapling_small_twig_nor_gl_1k", true));
            material.SetTexture("_RoughnessMap", LoadLocalResource<Texture2D>("Environment/Models/pine_sapling_small/textures/pine_sapling_small_twig_rough_1k", true));
            material.SetColor("_Tint", new Color(0.68f, 0.82f, 0.68f));
            material.SetFloat("_Cutoff", 0.32f);
            _materials.Add(material);
            return material;
        }

        private void CreateShrubs(Material foliage)
        {
            GameObject fernPrefab = LoadLocalResource<GameObject>("Environment/Models/fern_02/fern_02_1k", false);
            GameObject shrubPrefab = LoadLocalResource<GameObject>("Environment/Models/shrub_02/shrub_02_1k", false);
            if (fernPrefab != null && shrubPrefab != null)
            {
                Material fernMaterial = CreateFoliageMaterial("CC0 fern foliage", "Models/fern_02/textures/fern_02", new Color(0.48f, 0.72f, 0.47f), 0.28f);
                Material shrubMaterial = CreateFoliageMaterial("CC0 forest shrub", "Models/shrub_02/textures/shrub_02", new Color(0.54f, 0.70f, 0.49f), 0.30f);
                System.Random realRandom = new System.Random(6721);
                for (int i = 0; i < 58; i++)
                {
                    bool campCluster = i < 26;
                    float z = campCluster ? Range(realRandom, 42f, 61f) : Range(realRandom, -82f, 84f);
                    float x = campCluster
                        ? Range(realRandom, -18f, 7f)
                        : realRandom.NextDouble() > 0.5 ? Range(realRandom, -30f, -13f) : Range(realRandom, 11f, 30f);
                    GameObject source = i % 3 == 0 ? shrubPrefab : fernPrefab;
                    GameObject plant = Instantiate(source, new Vector3(x, GroundHeight(x, z), z), Quaternion.Euler(0f, Range(realRandom, 0f, 360f), 0f), _root.transform);
                    plant.name = source == fernPrefab ? "CC0 fern undergrowth" : "CC0 layered shrub";
                    foreach (Renderer renderer in plant.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterial = source == fernPrefab ? fernMaterial : shrubMaterial;
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                    }
                    float targetHeight = source == fernPrefab ? Range(realRandom, 0.45f, 0.90f) : Range(realRandom, 0.85f, 1.55f);
                    FitModelToHeight(plant, targetHeight, GroundHeight(x, z));
                }
                return;
            }

            System.Random random = new System.Random(6721);
            for (int i = 0; i < 115; i++)
            {
                float z = Range(random, -88f, 86f);
                float x = random.NextDouble() > 0.5 ? Range(random, -30f, -14f) : Range(random, 12f, 31f);
                float y = GroundHeight(x, z) + 0.35f;
                CreatePrimitive(PrimitiveType.Sphere, "Fern and shrub mass", new Vector3(x, y, z), new Vector3(Range(random, 0.5f, 1.7f), Range(random, 0.18f, 0.55f), Range(random, 0.5f, 1.7f)), foliage, false);
            }
        }

        private Material CreateFoliageMaterial(string name, string prefix, Color tint, float cutoff)
        {
            Shader shader = LoadLocalResource<Shader>("Shaders/FoliageCutout", true);
            Material material = new Material(shader != null ? shader : Shader.Find("Standard")) { name = name };
            material.SetTexture("_MainTex", LoadLocalResource<Texture2D>("Environment/" + prefix + "_diff_1k", true));
            material.SetTexture("_AlphaTex", LoadLocalResource<Texture2D>("Environment/" + prefix + "_alpha_1k", true));
            material.SetTexture("_NormalMap", LoadLocalResource<Texture2D>("Environment/" + prefix + "_nor_gl_1k", true));
            material.SetTexture("_RoughnessMap", LoadLocalResource<Texture2D>("Environment/" + prefix + "_rough_1k", true));
            material.SetColor("_Tint", tint);
            material.SetFloat("_Cutoff", cutoff);
            _materials.Add(material);
            return material;
        }

        private void CreateMist()
        {
            Material mistMaterial = CreateParticleMaterial("Crimson ground mist", new Color(0.62f, 0.12f, 0.16f, 0.15f), false);
            GameObject objectRoot = new GameObject("Layered crimson mist", typeof(ParticleSystem));
            objectRoot.transform.SetParent(_root.transform);
            objectRoot.transform.position = new Vector3(-3f, 1.7f, 35f);
            ParticleSystem particles = objectRoot.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 15f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.08f, 0.12f, 0.07f), new Color(0.80f, 0.22f, 0.25f, 0.16f));
            main.prewarm = true;
            main.maxParticles = 170;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 7f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(32f, 2.2f, 110f);
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.22f, 0.18f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.01f, 0.09f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.12f);
            ParticleSystemRenderer renderer = objectRoot.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mistMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingFudge = 1f;
            particles.Simulate(12f, true, true, true);
            particles.Play();
        }

        private void CreateSpiritMotes()
        {
            Material material = CreateParticleMaterial("Spirit motes", new Color(0.54f, 0.93f, 0.84f, 0.8f), true);
            GameObject objectRoot = new GameObject("Spirit motes", typeof(ParticleSystem));
            objectRoot.transform.SetParent(_root.transform);
            objectRoot.transform.position = new Vector3(-4f, 8f, 18f);
            ParticleSystem particles = objectRoot.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.12f);
            main.startColor = new Color(0.62f, 0.95f, 0.83f, 0.9f);
            main.maxParticles = 170;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 16f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(55f, 14f, 130f);
            objectRoot.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            particles.Simulate(8f, true, true, true);
            particles.Play();
        }

        private Material CreateParticleMaterial(string name, Color color, bool additive)
        {
            Shader shader = LoadLocalResource<Shader>("Shaders/SanctuaryParticle", true);
            Material material = new Material(shader) { name = name, color = new Color(color.r, color.g, color.b, 1f) };
            material.SetFloat("_AdditiveBoost", additive ? 1.7f : 1f);
            Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = name + " soft particle" };
            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = (x - 31.5f) / 31.5f;
                    float dy = (y - 31.5f) / 31.5f;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
                    pixels[y * 64 + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            material.mainTexture = texture;
            _materials.Add(material);
            return material;
        }

        private void CreateCamp(Material wood, Material bronze)
        {
            Material canvas = CreateColorMaterial("Weathered tent cloth", new Color(0.36f, 0.34f, 0.28f), 0.08f);
            CreateTent(new Vector3(-20.5f, GroundHeight(-20.5f, 39f), 39f), 1.0f, canvas, wood);
            CreateTent(new Vector3(-17.2f, GroundHeight(-17.2f, 45f), 45f), 0.82f, canvas, wood);

            Vector3 firePosition = new Vector3(-15.4f, GroundHeight(-15.4f, 48f) + 0.12f, 48f);
            Material ember = CreateColorMaterial("Camp ember", new Color(1f, 0.23f, 0.045f), 0.25f);
            GameObject fire = CreatePrimitive(PrimitiveType.Sphere, "Low camp fire", firePosition, new Vector3(0.22f, 0.10f, 0.22f), ember, false);
            Light light = fire.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 2.4f;
            light.color = new Color(1f, 0.38f, 0.12f);
            light.shadows = LightShadows.Soft;

            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.54f;
                GameObject stone = CreatePrimitive(PrimitiveType.Sphere, "Fire ring stone", firePosition + offset, Vector3.one * 0.16f, bronze, false);
                stone.transform.localScale = new Vector3(0.26f, 0.14f, 0.20f);
            }
        }

        private void CreateTent(Vector3 position, float scale, Material canvas, Material wood)
        {
            GameObject root = new GameObject("Expedition tent");
            root.transform.SetParent(_root.transform);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, -12f, 0f);

            GameObject left = CreatePrimitive(PrimitiveType.Cube, "Tent cloth left", position + Vector3.up * 1.15f, new Vector3(1.75f, 0.055f, 2.0f) * scale, canvas, false);
            left.transform.rotation = root.transform.rotation * Quaternion.Euler(0f, 0f, 42f);
            left.transform.SetParent(root.transform, true);
            GameObject right = CreatePrimitive(PrimitiveType.Cube, "Tent cloth right", position + Vector3.up * 1.15f, new Vector3(1.75f, 0.055f, 2.0f) * scale, canvas, false);
            right.transform.rotation = root.transform.rotation * Quaternion.Euler(0f, 0f, -42f);
            right.transform.SetParent(root.transform, true);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject pole = CreatePrimitive(PrimitiveType.Cylinder, "Tent support", position + new Vector3(0f, 0.95f * scale, side * 1.75f * scale), new Vector3(0.045f, 0.95f, 0.045f) * scale, wood, false);
                pole.transform.SetParent(root.transform, true);
            }
        }

        private GameObject CreateNpc(Vector3 position, Color robeColor, bool female, string displayName)
        {
            bool usePeopleSansPeople = !female;
            string modelKey = female ? "DefaultFemale" : "DefaultMale";
            GameObject prefab = usePeopleSansPeople
                ? LoadLocalResource<GameObject>("Characters/PeopleSansPeople/PSP_Person", false)
                : LoadLocalResource<GameObject>("Characters/UnityStandard/" + modelKey, false);
            if (prefab != null)
            {
                GameObject actor = Instantiate(prefab, position, Quaternion.Euler(0f, 165f, 0f), _root.transform);
                actor.name = displayName + (usePeopleSansPeople ? " · PeopleSansPeople" : " · Unity Humanoid");
                actor.transform.localScale = Vector3.one;

                Animator animator = actor.GetComponent<Animator>();
                if (animator == null) animator = actor.AddComponent<Animator>();
                animator.runtimeAnimatorController = LoadLocalResource<RuntimeAnimatorController>("Characters/UnityStandard/" + (female ? "FemaleIdle" : "MaleIdle"), true);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                if (usePeopleSansPeople) ApplyPeopleSansPeopleMaterials(actor);
                else ApplyCharacterMaterials(actor, female);
                FitModelToHeight(actor, female ? 1.70f : 1.80f, position.y - 0.15f);
                AddCharacterHair(actor, female, displayName);
                return actor;
            }

            Material robe = CreateColorMaterial(female ? "Pale travelling robe" : "Blue travelling robe", robeColor, 0.12f);
            Material skin = CreateColorMaterial("Natural skin", new Color(0.56f, 0.40f, 0.31f), 0.22f);
            Material hair = CreateColorMaterial("Black hair", new Color(0.018f, 0.014f, 0.012f), 0.15f);
            GameObject fallbackActor = new GameObject(displayName + " · fallback mannequin");
            fallbackActor.transform.SetParent(_root.transform);
            fallbackActor.transform.position = position;
            fallbackActor.transform.rotation = Quaternion.Euler(0f, 165f, 0f);
            GameObject robeBody = CreatePrimitive(PrimitiveType.Capsule, "Layered robe", position + Vector3.up * 1.05f, new Vector3(0.48f, 0.92f, 0.38f), robe, false);
            robeBody.transform.SetParent(fallbackActor.transform, true);
            GameObject head = CreatePrimitive(PrimitiveType.Sphere, "Head", position + Vector3.up * 2.12f, Vector3.one * 0.42f, skin, false);
            head.transform.SetParent(fallbackActor.transform, true);
            GameObject hairCap = CreatePrimitive(PrimitiveType.Sphere, "Hair", position + Vector3.up * 2.27f, new Vector3(0.44f, 0.28f, 0.44f), hair, false);
            hairCap.transform.SetParent(fallbackActor.transform, true);
            GameObject knot = CreatePrimitive(PrimitiveType.Sphere, "Hair knot", position + Vector3.up * 2.55f, Vector3.one * (female ? 0.17f : 0.14f), hair, false);
            knot.transform.SetParent(fallbackActor.transform, true);
            return fallbackActor;
        }

        private void ApplyPeopleSansPeopleMaterials(GameObject actor)
        {
            Material material = new Material(Shader.Find("Standard")) { name = "PeopleSansPeople runtime PBR" };
            material.SetTexture("_MainTex", LoadLocalResource<Texture2D>("Characters/PeopleSansPeople/PSPManV2_Color1", true));
            Texture2D normal = LoadLocalResource<Texture2D>("Characters/PeopleSansPeople/PSPManV2_normal_1", true);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_Glossiness", 0.24f);
            _materials.Add(material);
            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                Material[] assigned = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < assigned.Length; i++) assigned[i] = material;
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private void AddCharacterHair(GameObject actor, bool female, string displayName)
        {
            Animator animator = actor.GetComponent<Animator>();
            Transform head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Material hair = CreateColorMaterial(displayName + " hair", new Color(0.012f, 0.010f, 0.009f), 0.20f);
            if (head != null)
            {
                Vector3 behind = -actor.transform.forward;
                CreateAttachedWorldPrimitive(PrimitiveType.Sphere, "Hair cap", head, head.position + Vector3.up * 0.060f, new Vector3(0.125f, 0.090f, 0.120f), hair);
                CreateAttachedWorldPrimitive(PrimitiveType.Sphere, female ? "High hair coil" : "Hair knot", head, head.position + Vector3.up * 0.170f + behind * 0.015f, Vector3.one * (female ? 0.064f : 0.052f), hair);
                if (female)
                    CreateAttachedWorldPrimitive(PrimitiveType.Capsule, "Long tied hair", head, head.position - Vector3.up * 0.105f + behind * 0.070f, new Vector3(0.045f, 0.18f, 0.045f), hair);
            }
        }

        private static GameObject CreateAttachedWorldPrimitive(PrimitiveType type, string name, Transform parent, Vector3 worldPosition, Vector3 worldScale, Material material)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.position = worldPosition;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = worldScale;
            instance.transform.SetParent(parent, true);
            instance.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return instance;
        }

        private static void FitModelToHeight(GameObject instance, float targetHeight, float groundY)
        {
            if (instance == null || targetHeight <= 0f) return;
            if (!TryGetRendererBounds(instance, out Bounds before) || before.size.y < 0.001f)
            {
                Debug.LogWarning("RED_MIST_RESOURCE_BOUNDS_MISSING object=" + instance.name);
                return;
            }

            float scale = targetHeight / before.size.y;
            instance.transform.localScale *= scale;
            if (!TryGetRendererBounds(instance, out Bounds after)) return;
            Vector3 position = instance.transform.position;
            position.y += groundY - after.min.y;
            instance.transform.position = position;
        }

        private static bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = new Bounds(instance.transform.position, Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static T LoadLocalResource<T>(string path, bool required) where T : UnityEngine.Object
        {
            T resource = Resources.Load<T>(path);
            if (resource == null)
            {
                string message = "RED_MIST_RESOURCE_MISSING type=" + typeof(T).Name + " path=" + path;
                if (required) Debug.LogError(message);
                else Debug.LogWarning(message + " fallback=enabled");
            }
            return resource;
        }

        private void ApplyCharacterMaterials(GameObject actor, bool female)
        {
            string prefix = female ? "DefaultFemale" : "DefaultMale";
            Material material = new Material(Shader.Find("Standard")) { name = prefix + " runtime PBR" };
            material.SetTexture("_MainTex", LoadLocalResource<Texture2D>("Characters/UnityStandard/" + prefix + "_Albedo", true));
            Texture2D normal = LoadLocalResource<Texture2D>("Characters/UnityStandard/" + prefix + "_Normal", true);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetTexture("_OcclusionMap", LoadLocalResource<Texture2D>("Characters/UnityStandard/" + prefix + "_Occlusion", true));
            Texture2D specular = LoadLocalResource<Texture2D>("Characters/UnityStandard/" + prefix + "_SpecSmooth", true);
            if (specular != null)
            {
                material.SetTexture("_SpecGlossMap", specular);
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            material.SetFloat("_Glossiness", 0.28f);
            _materials.Add(material);

            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                Material[] assigned = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < assigned.Length; i++) assigned[i] = material;
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        public void FocusStoryNode(string nodeId, PlayerRoute route, bool immediate = false)
        {
            if (!_embedded || _storyCamera == null) return;

            Vector3 cameraPosition;
            Vector3 lookAt;
            float fov;
            bool campVisible = false;
            bool flight = false;

            switch (nodeId)
            {
                case "title":
                    cameraPosition = new Vector3(-13f, 5.4f, -38f);
                    lookAt = new Vector3(-7f, 7.8f, 64f);
                    fov = 50f;
                    campVisible = true;
                    PlaceActor(_maleActor, -10.6f, 49f, 0.15f, 165f);
                    PlaceActor(_femaleActor, -7.8f, 52f, 0.15f, 165f);
                    break;
                case "prologue":
                    cameraPosition = new Vector3(-0.4f, 2.25f, 42.8f);
                    lookAt = new Vector3(-7.7f, 1.36f, 49.1f);
                    fov = 33f;
                    campVisible = true;
                    PlaceActor(_maleActor, -8.7f, 48.2f, 0.15f, 52f);
                    PlaceActor(_femaleActor, -6.3f, 50.0f, 0.15f, 228f);
                    break;
                case "camp":
                    cameraPosition = new Vector3(-25f, 3.6f, 33f);
                    lookAt = new Vector3(-12f, 1.7f, 48f);
                    fov = 46f;
                    campVisible = true;
                    PlaceActor(_maleActor, -10.0f, 49f, 0.15f, 220f);
                    PlaceActor(_femaleActor, -7.4f, 51f, 0.15f, 205f);
                    break;
                case "alliance":
                    cameraPosition = new Vector3(-1.5f, 3.0f, 41f);
                    lookAt = new Vector3(-9.0f, 1.55f, 50f);
                    fov = 43f;
                    campVisible = true;
                    PlaceActor(_maleActor, -10.4f, 49.6f, 0.15f, 35f);
                    PlaceActor(_femaleActor, -7.9f, 51f, 0.15f, 205f);
                    break;
                case "flight":
                    cameraPosition = new Vector3(10f, 8.5f, -17f);
                    lookAt = new Vector3(-1f, 5.0f, 22f);
                    fov = 55f;
                    flight = true;
                    PlaceActor(_maleActor, -2.2f, 21f, 3.1f, 8f);
                    PlaceActor(_femaleActor, 1.2f, 25f, 3.7f, 8f);
                    break;
                case "herb_route":
                case "corpse_signs":
                case "rescue":
                    cameraPosition = new Vector3(-13f, 3.4f, 4f);
                    lookAt = new Vector3(-2f, 1.4f, 22f);
                    fov = 48f;
                    PlaceActor(_maleActor, -3.2f, 20f, 0.15f, 198f);
                    PlaceActor(_femaleActor, 0.3f, 22.5f, 0.15f, 175f);
                    break;
                case "shijun":
                case "formation":
                    cameraPosition = new Vector3(14f, 3.8f, 34f);
                    lookAt = new Vector3(-7f, 2.1f, 58f);
                    fov = 44f;
                    PlaceActor(_maleActor, -8.8f, 54f, 0.15f, 35f);
                    PlaceActor(_femaleActor, -5.7f, 57f, 0.15f, 205f);
                    break;
                case "underground":
                case "combat_one":
                case "combat_two":
                case "aftermath":
                    cameraPosition = new Vector3(4.5f, 3.0f, 59f);
                    lookAt = new Vector3(-6.8f, 2.2f, 72f);
                    fov = 47f;
                    PlaceActor(_maleActor, -8.5f, 66f, 0.15f, 35f);
                    PlaceActor(_femaleActor, -4.9f, 68f, 0.15f, 210f);
                    break;
                case "ending":
                default:
                    cameraPosition = new Vector3(-18f, 4.6f, 27f);
                    lookAt = new Vector3(-6f, 2.2f, 57f);
                    fov = 49f;
                    PlaceActor(_maleActor, -9.3f, 53f, 0.15f, 172f);
                    PlaceActor(_femaleActor, -6.1f, 55f, 0.15f, 185f);
                    break;
            }

            foreach (GameObject actor in _supportingActors) if (actor != null) actor.SetActive(campVisible);
            if (_maleActor != null) _maleActor.SetActive(route != PlayerRoute.ChuMingqi || nodeId == "alliance" || nodeId == "combat_one" || nodeId == "combat_two" || nodeId == "ending" || nodeId == "title" || nodeId == "prologue");
            if (_femaleActor != null) _femaleActor.SetActive(route != PlayerRoute.ShenYan || nodeId == "alliance" || nodeId == "combat_one" || nodeId == "combat_two" || nodeId == "ending" || nodeId == "title" || nodeId == "prologue");

            RenderSettings.fogDensity = nodeId == "underground" || nodeId.StartsWith("combat", StringComparison.Ordinal) ? 0.0095f : flight ? 0.0026f : 0.0034f;
            RenderSettings.fogColor = nodeId == "underground" || nodeId.StartsWith("combat", StringComparison.Ordinal)
                ? new Color(0.16f, 0.20f, 0.21f)
                : new Color(0.13f, 0.19f, 0.20f);
            _storyCamera.SetShot(cameraPosition, lookAt, fov, immediate);
            Debug.Log("RED_MIST_3D_STORY_SHOT node=" + nodeId + " route=" + route);
        }

        private static void PlaceActor(GameObject actor, float x, float z, float heightOffset, float yaw)
        {
            if (actor == null) return;
            actor.transform.position = new Vector3(x, GroundHeight(x, z) + heightOffset, z);
            actor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private void CreateLighting()
        {
            GameObject sunObject = new GameObject("Dawn sun", typeof(Light));
            sunObject.transform.SetParent(_root.transform);
            sunObject.transform.rotation = Quaternion.Euler(37f, -42f, 0f);
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.08f;
            sun.color = new Color(1f, 0.89f, 0.72f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.shadowBias = 0.045f;
            sun.shadowNormalBias = 0.35f;

            GameObject fillObject = new GameObject("Cool valley fill", typeof(Light));
            fillObject.transform.SetParent(_root.transform);
            fillObject.transform.rotation = Quaternion.Euler(58f, 132f, 0f);
            Light fill = fillObject.GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.64f;
            fill.color = new Color(0.58f, 0.70f, 0.76f);
            fill.shadows = LightShadows.None;

            GameObject rimObject = new GameObject("Warm valley rim", typeof(Light));
            rimObject.transform.SetParent(_root.transform);
            rimObject.transform.rotation = Quaternion.Euler(28f, 36f, 0f);
            Light rim = rimObject.GetComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.30f;
            rim.color = new Color(0.88f, 0.58f, 0.34f);
            rim.shadows = LightShadows.None;

            GameObject windObject = new GameObject("Mountain wind", typeof(WindZone));
            windObject.transform.SetParent(_root.transform);
            WindZone wind = windObject.GetComponent<WindZone>();
            wind.mode = WindZoneMode.Directional;
            wind.windMain = 0.45f;
            wind.windTurbulence = 0.32f;
            wind.windPulseMagnitude = 0.18f;
            wind.windPulseFrequency = 0.12f;
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(_root.transform);
            instance.transform.position = position;
            instance.transform.localScale = scale;
            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            if (!collider)
            {
                Collider existing = instance.GetComponent<Collider>();
                if (existing != null) Destroy(existing);
            }
            return instance;
        }

        private static float GroundHeight(float x, float z)
        {
            float side = Mathf.Pow(Mathf.Abs(x) / 42f, 2.35f) * 15f;
            float broad = (Mathf.PerlinNoise((x + 80f) * 0.035f, (z + 110f) * 0.032f) - 0.5f) * 2.4f;
            float detail = (Mathf.PerlinNoise((x + 20f) * 0.12f, (z + 30f) * 0.11f) - 0.5f) * 0.55f;
            float streamCut = Mathf.Exp(-Mathf.Pow((x - StreamX(z)) / 6.2f, 2f)) * 1.15f;
            return side + broad + detail - streamCut;
        }

        private static float StreamX(float z)
        {
            return 2.7f + Mathf.Sin(z * 0.042f) * 3.2f + Mathf.Sin(z * 0.013f) * 1.4f;
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private void EndPreview()
        {
            if (_ending) return;
            _ending = true;
            SanctuaryFlyCamera controller = _camera != null ? _camera.GetComponent<SanctuaryFlyCamera>() : null;
            CinematicPostFx postFx = _camera != null ? _camera.GetComponent<CinematicPostFx>() : null;
            if (controller != null) Destroy(controller);
            if (postFx != null) Destroy(postFx);
            if (_camera != null)
            {
                _camera.transform.position = _previousPosition;
                _camera.transform.rotation = _previousRotation;
                _camera.fieldOfView = _previousFov;
                _camera.clearFlags = _previousClearFlags;
            }
            RenderSettings.fog = false;
            if (_root != null) Destroy(_root);
            foreach (Material material in _materials) if (material != null) Destroy(material);
            _materials.Clear();
            Action callback = _onExit;
            _onExit = null;
            if (callback != null) callback();
            Destroy(this);
        }
    }
}
