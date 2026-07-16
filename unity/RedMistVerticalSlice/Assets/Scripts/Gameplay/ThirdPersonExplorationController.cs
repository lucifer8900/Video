using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Lightweight third-person exploration used by the runtime-built sanctuary scene.
    /// It intentionally depends only on the legacy Input Manager and built-in physics so
    /// the vertical slice does not need Cinemachine, the Input System, or NavMesh packages.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonExplorationController : MonoBehaviour
    {
        private sealed class Hotspot
        {
            public string id;
            public string title;
            public string action;
            public Vector3 position;
            public float radius;
            public bool repeatable;
            public bool consumed;
            public float phase;
            public GameObject marker;
            public Transform orb;
            public Vector3 orbScale;
        }

        private const float WalkSpeed = 3.25f;
        private const float RunSpeed = 6.1f;
        private const float Gravity = -24f;
        private const float MinCameraDistance = 2.35f;
        private const float MaxCameraDistance = 7.2f;

        private readonly List<Hotspot> _hotspots = new List<Hotspot>();
        private readonly List<Material> _runtimeMaterials = new List<Material>();

        private Camera _camera;
        private GameObject _player;
        private GameObject _motorObject;
        private CharacterController _character;
        private GameObject _markerRoot;
        private Animator _animator;

        private Transform _originalParent;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalLocalRotation;
        private Vector3 _originalLocalScale;
        private Vector3 _visualBaseLocalPosition;
        private Quaternion _visualBaseLocalRotation;

        private Transform _hips;
        private Transform _leftUpperLeg;
        private Transform _rightUpperLeg;
        private Transform _leftLowerLeg;
        private Transform _rightLowerLeg;
        private Transform _leftUpperArm;
        private Transform _rightUpperArm;

        private Action<string> _onInteraction;
        private Action<string> _onPrompt;
        private Action _onExit;
        private string _nodeId;
        private string _lastPrompt;

        private Vector3 _cameraVelocity;
        private Vector3 _cameraPreviousPosition;
        private Quaternion _cameraPreviousRotation;
        private float _cameraPreviousFov;
        private Vector3 _lastSafePosition;
        private float _yaw;
        private float _pitch = 18f;
        private float _cameraDistance = 4.8f;
        private float _verticalVelocity = -2f;
        private float _locomotionAmount;
        private float _strideClock;
        private float _lateralInput;
        private bool _initialized;
        private bool _inputEnabled;
        private bool _pointerCaptured;
        private bool _exitRequested;

        public void Initialize(
            Camera camera,
            GameObject player,
            string nodeId,
            Action<string> onInteraction,
            Action<string> onPrompt,
            Action onExit)
        {
            if (camera == null)
            {
                Debug.LogError("RED_MIST_EXPLORATION_INIT_FAILED reason=missing_camera");
                return;
            }
            if (player == null)
            {
                Debug.LogError("RED_MIST_EXPLORATION_INIT_FAILED reason=missing_player");
                return;
            }

            if (_initialized) Shutdown();
            enabled = true;

            _camera = camera;
            _player = player;
            _nodeId = string.IsNullOrWhiteSpace(nodeId) ? "camp" : nodeId;
            _onInteraction = onInteraction;
            _onPrompt = onPrompt;
            _onExit = onExit;
            _exitRequested = false;

            _cameraPreviousPosition = camera.transform.position;
            _cameraPreviousRotation = camera.transform.rotation;
            _cameraPreviousFov = camera.fieldOfView;

            _originalParent = player.transform.parent;
            _originalLocalPosition = player.transform.localPosition;
            _originalLocalRotation = player.transform.localRotation;
            _originalLocalScale = player.transform.localScale;

            CreateMotor();
            CacheHumanoidBones();
            CreateHotspots();

            _yaw = _motorObject.transform.eulerAngles.y;
            _pitch = 18f;
            _cameraDistance = 4.8f;
            _camera.fieldOfView = 53f;
            _lastSafePosition = _motorObject.transform.position;
            _initialized = true;
            _inputEnabled = true;
            _lastPrompt = null;
            UpdateCamera(true);
            UpdatePromptAndInteraction(false);
            Debug.Log("RED_MIST_EXPLORATION_READY node=" + _nodeId + " hotspots=" + _hotspots.Count);
        }

        public void SetInputEnabled(bool enabledValue)
        {
            _inputEnabled = enabledValue && _initialized && !_exitRequested;
            if (!_inputEnabled)
            {
                ReleasePointer();
                PublishPrompt("互动进行中……");
            }
            else
            {
                _lastPrompt = null;
                UpdatePromptAndInteraction(false);
            }
        }

        public void Shutdown()
        {
            if (!_initialized && _motorObject == null && _markerRoot == null) return;

            ReleasePointer();
            Action<string> promptCallback = _onPrompt;
            _initialized = false;
            _inputEnabled = false;

            if (_player != null)
            {
                _player.transform.SetParent(_originalParent, false);
                _player.transform.localPosition = _originalLocalPosition;
                _player.transform.localRotation = _originalLocalRotation;
                _player.transform.localScale = _originalLocalScale;
            }

            if (_camera != null)
            {
                _camera.transform.position = _cameraPreviousPosition;
                _camera.transform.rotation = _cameraPreviousRotation;
                _camera.fieldOfView = _cameraPreviousFov;
            }

            if (_markerRoot != null) Destroy(_markerRoot);
            if (_motorObject != null) Destroy(_motorObject);
            foreach (Material material in _runtimeMaterials)
            {
                if (material != null) Destroy(material);
            }

            _runtimeMaterials.Clear();
            _hotspots.Clear();
            _markerRoot = null;
            _motorObject = null;
            _character = null;
            _animator = null;
            _player = null;
            _camera = null;
            _onInteraction = null;
            _onPrompt = null;
            _onExit = null;
            _lastPrompt = null;
            promptCallback?.Invoke(string.Empty);
            enabled = false;
            Debug.Log("RED_MIST_EXPLORATION_SHUTDOWN");
        }

        private void CreateMotor()
        {
            Vector3 worldPosition = _player.transform.position;
            float worldYaw = _player.transform.eulerAngles.y;

            _motorObject = new GameObject("ThirdPersonExplorationMotor");
            _motorObject.transform.SetParent(_originalParent, true);
            _motorObject.transform.position = worldPosition;
            _motorObject.transform.rotation = Quaternion.Euler(0f, worldYaw, 0f);

            _character = _motorObject.AddComponent<CharacterController>();
            _character.height = 1.78f;
            _character.radius = 0.30f;
            _character.center = new Vector3(0f, 0.90f, 0f);
            _character.stepOffset = 0.34f;
            _character.slopeLimit = 48f;
            _character.skinWidth = 0.045f;
            _character.minMoveDistance = 0f;

            _player.transform.SetParent(_motorObject.transform, true);
            _player.transform.localPosition = Vector3.zero;
            _player.transform.localRotation = Quaternion.identity;
            _visualBaseLocalPosition = _player.transform.localPosition;
            _visualBaseLocalRotation = _player.transform.localRotation;

            _animator = _player.GetComponentInChildren<Animator>(true);
            if (_animator != null)
            {
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private void CacheHumanoidBones()
        {
            if (_animator == null || !_animator.isHuman) return;
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _leftUpperLeg = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _rightUpperLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _leftLowerLeg = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _rightLowerLeg = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }

        private void CreateHotspots()
        {
            _markerRoot = new GameObject("ExplorationHotspotMarkers");
            _markerRoot.transform.SetParent(_originalParent, true);

            Vector3 companionPosition = CompanionPositionForNode(_nodeId);
            AddHotspot("campfire", "营火余烬", "检查营火与残留气息", new Vector3(-15.4f, 0f, 48f), 2.8f, false, new Color(1f, 0.34f, 0.10f));
            AddHotspot("companion", "同行者", "与同行者交谈", companionPosition, 2.65f, true, new Color(0.30f, 0.92f, 0.78f));
            AddHotspot("herb", "灵药生长区", "辨识并采集灵药", new Vector3(-2.5f, 0f, 18f), 2.9f, false, new Color(0.45f, 1f, 0.38f));
            AddHotspot("sense", "神识残痕", "凝神追踪残留灵息", new Vector3(3.5f, 0f, 34f), 3.0f, false, new Color(0.40f, 0.72f, 1f));
            AddHotspot("seal", "秘苑铜门", "触碰并辨认铜门封印", new Vector3(-8f, 0f, 65f), 3.2f, false, new Color(0.95f, 0.62f, 0.18f));

            Vector3 exitPosition = _motorObject.transform.position - _motorObject.transform.forward * 4.5f;
            AddHotspot("exit", "返回剧情", "结束自由探索并返回剧情", exitPosition, 2.6f, true, new Color(0.88f, 0.80f, 0.56f));
        }

        private void AddHotspot(string id, string title, string action, Vector3 approximatePosition, float radius, bool repeatable, Color color)
        {
            Hotspot hotspot = new Hotspot
            {
                id = id,
                title = title,
                action = action,
                position = SnapToGround(approximatePosition),
                radius = radius,
                repeatable = repeatable,
                phase = _hotspots.Count * 1.17f
            };
            CreateMarker(hotspot, color);
            _hotspots.Add(hotspot);
        }

        private Vector3 SnapToGround(Vector3 position)
        {
            Vector3 origin = new Vector3(position.x, 70f, position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 110f, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            float lowestY = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitTransform = hits[i].collider != null ? hits[i].collider.transform : null;
                if (hitTransform == null) continue;
                if (_motorObject != null && (hitTransform == _motorObject.transform || hitTransform.IsChildOf(_motorObject.transform))) continue;
                float y = hits[i].point.y;
                if (y < -18f || y > 30f || y >= lowestY) continue;
                lowestY = y;
                found = true;
            }
            position.y = found ? lowestY + 0.08f : position.y + 0.08f;
            return position;
        }

        private static Vector3 CompanionPositionForNode(string nodeId)
        {
            if (nodeId == "herb_route" || nodeId == "corpse_signs" || nodeId == "rescue")
                return new Vector3(0.3f, 0f, 22.5f);
            if (nodeId == "shijun" || nodeId == "formation")
                return new Vector3(-5.7f, 0f, 57f);
            return new Vector3(-8f, 0f, 52f);
        }

        private void CreateMarker(Hotspot hotspot, Color color)
        {
            GameObject marker = new GameObject("Hotspot_" + hotspot.id);
            marker.transform.SetParent(_markerRoot.transform, true);
            marker.transform.position = hotspot.position;
            hotspot.marker = marker;

            Material glow = CreateGlowMaterial("Hotspot glow " + hotspot.id, color);

            GameObject disc = CreateMarkerPrimitive(PrimitiveType.Cylinder, "Ground halo", marker.transform, glow);
            disc.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            disc.transform.localScale = new Vector3(0.42f, 0.018f, 0.42f);

            GameObject beam = CreateMarkerPrimitive(PrimitiveType.Cylinder, "Spirit beacon", marker.transform, glow);
            beam.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            beam.transform.localScale = new Vector3(0.032f, 0.37f, 0.032f);

            GameObject orb = CreateMarkerPrimitive(PrimitiveType.Sphere, "Interaction mote", marker.transform, glow);
            orb.transform.localPosition = new Vector3(0f, 0.88f, 0f);
            orb.transform.localScale = Vector3.one * 0.18f;
            hotspot.orb = orb.transform;
            hotspot.orbScale = orb.transform.localScale;

            Light light = orb.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 2.1f;
            light.intensity = 0.55f;
            light.color = color;
            light.shadows = LightShadows.None;
        }

        private Material CreateGlowMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            Material material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.72f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.18f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2.35f);
            }
            _runtimeMaterials.Add(material);
            return material;
        }

        private static GameObject CreateMarkerPrimitive(PrimitiveType type, string objectName, Transform parent, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = objectName;
            primitive.transform.SetParent(parent, false);
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            Collider markerCollider = primitive.GetComponent<Collider>();
            if (markerCollider != null)
            {
                markerCollider.enabled = false;
                Destroy(markerCollider);
            }
            return primitive;
        }

        private void Update()
        {
            if (!_initialized || _motorObject == null || _character == null) return;

            AnimateMarkers();

            if (_inputEnabled)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    RequestExit(false);
                    return;
                }
                HandleViewInput();
            }

            HandleMovement(_inputEnabled);
            if (_inputEnabled) UpdatePromptAndInteraction(true);
        }

        private void HandleViewInput()
        {
            if (Input.GetMouseButtonDown(1)) CapturePointer();
            if (Input.GetMouseButtonUp(1)) ReleasePointer();
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * 3.0f;
                _pitch -= Input.GetAxis("Mouse Y") * 2.4f;
                _pitch = Mathf.Clamp(_pitch, 7f, 54f);
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
                _cameraDistance = Mathf.Clamp(_cameraDistance - scroll * 0.55f, MinCameraDistance, MaxCameraDistance);
        }

        private void HandleMovement(bool allowInput)
        {
            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            Vector2 input = allowInput
                ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
                : Vector2.zero;
            input = Vector2.ClampMagnitude(input, 1f);
            _lateralInput = input.x;

            Vector3 forward = _camera != null ? Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up) : _motorObject.transform.forward;
            if (forward.sqrMagnitude < 0.001f) forward = _motorObject.transform.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 desiredDirection = forward * input.y + right * input.x;
            desiredDirection = Vector3.ClampMagnitude(desiredDirection, 1f);

            bool running = allowInput && Input.GetKey(KeyCode.LeftShift);
            float speed = running ? RunSpeed : WalkSpeed;
            float targetLocomotion = desiredDirection.sqrMagnitude > 0.001f ? (running ? 1f : 0.58f) : 0f;
            _locomotionAmount = Mathf.MoveTowards(_locomotionAmount, targetLocomotion, deltaTime * 4.5f);

            if (desiredDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
                float rotationBlend = 1f - Mathf.Exp(-12f * deltaTime);
                _motorObject.transform.rotation = Quaternion.Slerp(_motorObject.transform.rotation, targetRotation, rotationBlend);
            }

            if (_character.isGrounded)
            {
                if (_verticalVelocity < 0f) _verticalVelocity = -2.2f;
                _lastSafePosition = _motorObject.transform.position;
            }
            else
            {
                _verticalVelocity += Gravity * deltaTime;
            }

            Vector3 velocity = desiredDirection * speed;
            velocity.y = _verticalVelocity;
            _character.Move(velocity * deltaTime);

            Vector3 clamped = _motorObject.transform.position;
            clamped.x = Mathf.Clamp(clamped.x, -32.5f, 32.5f);
            clamped.z = Mathf.Clamp(clamped.z, -84f, 77f);
            _motorObject.transform.position = clamped;

            if (_motorObject.transform.position.y < -14f)
            {
                _character.enabled = false;
                _motorObject.transform.position = _lastSafePosition + Vector3.up * 0.35f;
                _verticalVelocity = -2f;
                _character.enabled = true;
            }
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            ApplyProceduralLocomotion();
            UpdateCamera(false);
        }

        private void ApplyProceduralLocomotion()
        {
            if (_player == null) return;
            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            float moving = Mathf.Clamp01(_locomotionAmount);
            if (moving > 0.02f) _strideClock += deltaTime * Mathf.Lerp(5.6f, 10.5f, moving);

            float stride = Mathf.Sin(_strideClock);
            float doubleStride = Mathf.Sin(_strideClock * 2f);
            float bob = Mathf.Abs(stride) * 0.035f * moving;
            Vector3 targetLocalPosition = _visualBaseLocalPosition + Vector3.up * bob;
            _player.transform.localPosition = Vector3.Lerp(
                _player.transform.localPosition,
                targetLocalPosition,
                1f - Mathf.Exp(-18f * deltaTime));

            Quaternion targetLocalRotation = _visualBaseLocalRotation * Quaternion.Euler(3.5f * moving, 0f, -_lateralInput * 2.8f * moving);
            _player.transform.localRotation = Quaternion.Slerp(
                _player.transform.localRotation,
                targetLocalRotation,
                1f - Mathf.Exp(-14f * deltaTime));

            if (_animator == null || !_animator.enabled || !_animator.isHuman || moving <= 0.02f) return;

            float legSwing = stride * Mathf.Lerp(13f, 29f, moving);
            float kneeBend = Mathf.Max(0f, -stride) * Mathf.Lerp(8f, 25f, moving);
            float oppositeKnee = Mathf.Max(0f, stride) * Mathf.Lerp(8f, 25f, moving);
            float armSwing = -stride * Mathf.Lerp(8f, 21f, moving);
            AddLocalPitch(_leftUpperLeg, legSwing);
            AddLocalPitch(_rightUpperLeg, -legSwing);
            AddLocalPitch(_leftLowerLeg, kneeBend);
            AddLocalPitch(_rightLowerLeg, oppositeKnee);
            AddLocalPitch(_leftUpperArm, armSwing);
            AddLocalPitch(_rightUpperArm, -armSwing);
            if (_hips != null) _hips.localPosition += Vector3.up * (doubleStride * 0.008f * moving);
        }

        private static void AddLocalPitch(Transform bone, float degrees)
        {
            if (bone != null) bone.localRotation *= Quaternion.AngleAxis(degrees, Vector3.right);
        }

        private void UpdateCamera(bool immediate)
        {
            if (_camera == null || _motorObject == null) return;

            Vector3 pivot = _motorObject.transform.position + Vector3.up * 1.52f;
            Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 desired = pivot + orbit * new Vector3(0.34f, 0.20f, -_cameraDistance);
            Vector3 displacement = desired - pivot;
            float desiredDistance = displacement.magnitude;
            Vector3 direction = desiredDistance > 0.001f ? displacement / desiredDistance : -_motorObject.transform.forward;

            RaycastHit[] hits = Physics.SphereCastAll(pivot, 0.20f, direction, desiredDistance, ~0, QueryTriggerInteraction.Ignore);
            float collisionDistance = desiredDistance;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null || IsOwnedTransform(hitCollider.transform)) continue;
                collisionDistance = Mathf.Min(collisionDistance, Mathf.Max(0.62f, hits[i].distance - 0.16f));
            }
            desired = pivot + direction * collisionDistance;

            if (immediate)
            {
                _camera.transform.position = desired;
                _cameraVelocity = Vector3.zero;
            }
            else
            {
                _camera.transform.position = Vector3.SmoothDamp(
                    _camera.transform.position,
                    desired,
                    ref _cameraVelocity,
                    0.075f,
                    80f,
                    Mathf.Min(Time.deltaTime, 0.05f));
            }

            Vector3 lookTarget = pivot + _motorObject.transform.forward * 0.34f;
            Quaternion lookRotation = Quaternion.LookRotation((lookTarget - _camera.transform.position).normalized, Vector3.up);
            _camera.transform.rotation = immediate
                ? lookRotation
                : Quaternion.Slerp(_camera.transform.rotation, lookRotation, 1f - Mathf.Exp(-16f * Mathf.Min(Time.deltaTime, 0.05f)));
        }

        private bool IsOwnedTransform(Transform candidate)
        {
            if (candidate == null) return false;
            if (_motorObject != null && (candidate == _motorObject.transform || candidate.IsChildOf(_motorObject.transform))) return true;
            return _markerRoot != null && (candidate == _markerRoot.transform || candidate.IsChildOf(_markerRoot.transform));
        }

        private void AnimateMarkers()
        {
            float time = Time.unscaledTime;
            for (int i = 0; i < _hotspots.Count; i++)
            {
                Hotspot hotspot = _hotspots[i];
                if (hotspot.marker == null || hotspot.consumed) continue;
                float wave = Mathf.Sin(time * 2.1f + hotspot.phase);
                hotspot.marker.transform.position = hotspot.position + Vector3.up * (0.035f + wave * 0.035f);
                hotspot.marker.transform.Rotate(0f, Time.unscaledDeltaTime * 34f, 0f, Space.World);
                if (hotspot.orb != null)
                    hotspot.orb.localScale = hotspot.orbScale * (1f + wave * 0.16f);
            }
        }

        private void UpdatePromptAndInteraction(bool acceptInteraction)
        {
            if (!_initialized || _motorObject == null) return;

            Hotspot nearest = null;
            float nearestDistance = float.PositiveInfinity;
            Vector3 playerPosition = _motorObject.transform.position;
            for (int i = 0; i < _hotspots.Count; i++)
            {
                Hotspot hotspot = _hotspots[i];
                if (hotspot.consumed) continue;
                float distance = Vector3.Distance(playerPosition, hotspot.position);
                // The return marker should not distract the player from objectives at long range.
                float guidanceRange = hotspot.id == "exit" ? hotspot.radius + 0.9f : 9.5f;
                if (distance > guidanceRange || distance >= nearestDistance) continue;
                nearest = hotspot;
                nearestDistance = distance;
            }

            if (nearest == null)
            {
                PublishPrompt(ObjectivePrefix() + "WASD移动 · Shift疾跑 · 按住鼠标右键观察 · 滚轮缩放 · Esc返回剧情");
                return;
            }

            if (nearestDistance <= nearest.radius)
            {
                PublishPrompt("【" + nearest.title + "】按 E " + nearest.action);
                if (acceptInteraction && Input.GetKeyDown(KeyCode.E)) TriggerHotspot(nearest);
            }
            else
            {
                PublishPrompt("前方【" + nearest.title + "】· 距离 " + nearestDistance.ToString("0.0") + " 米");
            }
        }

        private string ObjectivePrefix()
        {
            if (_nodeId == "herb_route" || _nodeId == "corpse_signs") return "目标：调查灵药与神识痕迹　";
            if (_nodeId == "shijun" || _nodeId == "formation") return "目标：查明铜门封印　";
            return "目标：探索营地并寻找秘门　";
        }

        private void TriggerHotspot(Hotspot hotspot)
        {
            if (hotspot == null || hotspot.consumed || _exitRequested) return;

            if (!hotspot.repeatable)
            {
                hotspot.consumed = true;
                if (hotspot.marker != null) hotspot.marker.SetActive(false);
            }

            if (hotspot.id == "exit")
            {
                RequestExit(true);
                return;
            }

            Action<string> callback = _onInteraction;
            callback?.Invoke(hotspot.id);
            if (_initialized && _inputEnabled)
            {
                _lastPrompt = null;
                UpdatePromptAndInteraction(false);
            }
        }

        private void RequestExit(bool throughHotspot)
        {
            if (_exitRequested) return;
            _exitRequested = true;
            _inputEnabled = false;
            ReleasePointer();
            Action<string> interactionCallback = _onInteraction;
            Action exitCallback = _onExit;
            if (throughHotspot) interactionCallback?.Invoke("exit");
            exitCallback?.Invoke();
        }

        private void PublishPrompt(string prompt)
        {
            if (prompt == _lastPrompt) return;
            _lastPrompt = prompt;
            _onPrompt?.Invoke(prompt);
        }

        private void CapturePointer()
        {
            if (_pointerCaptured) return;
            _pointerCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ReleasePointer()
        {
            if (!_pointerCaptured && Cursor.lockState == CursorLockMode.None) return;
            _pointerCaptured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ReleasePointer();
        }

        private void OnDisable()
        {
            ReleasePointer();
        }
    }
}
