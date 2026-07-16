using System.Collections.Generic;
using UnityEngine;

namespace Lingmai.RedMist
{
    /// <summary>
    /// High-fidelity 2.5D presentation skin for an actor that still moves and collides in 3D.
    /// The photographed front/rear turnarounds replace the visibly placeholder humanoid mesh
    /// during exploration without changing the motor, world position, or interaction logic.
    /// </summary>
    public sealed class ExplorationCharacterBillboard : MonoBehaviour
    {
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private readonly List<bool> _rendererStates = new List<bool>();
        private Camera _camera;
        private Texture2D _front;
        private Texture2D _back;
        private GameObject _quad;
        private Material _material;
        private bool _initialized;
        private float _phase;

        public void Initialize(Camera camera, Texture2D front, Texture2D back, float phase = 0f)
        {
            Shutdown();
            if (camera == null || front == null || back == null) return;

            _camera = camera;
            _front = front;
            _back = back;
            _phase = phase;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                _hiddenRenderers.Add(renderer);
                _rendererStates.Add(renderer.enabled);
                renderer.enabled = false;
            }

            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "Cinematic character presentation";
            _quad.transform.SetParent(transform, false);
            _quad.transform.localPosition = new Vector3(0f, 1.04f, 0f);
            _quad.transform.localScale = new Vector3(1.50f, 2.08f, 1f);
            Collider collider = _quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            Shader shader = Resources.Load<Shader>("Shaders/CinematicCharacterBillboard");
            if (shader == null) shader = Shader.Find("Unlit/Transparent Cutout");
            _material = new Material(shader) { name = name + " cinematic turnaround" };
            _material.mainTexture = _back;
            Renderer quadRenderer = _quad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = _material;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;

            _initialized = true;
            UpdateFacing();
        }

        public void Shutdown()
        {
            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                if (_hiddenRenderers[i] != null) _hiddenRenderers[i].enabled = _rendererStates[i];
            }
            _hiddenRenderers.Clear();
            _rendererStates.Clear();
            if (_quad != null) Destroy(_quad);
            if (_material != null) Destroy(_material);
            _quad = null;
            _material = null;
            _camera = null;
            _front = null;
            _back = null;
            _initialized = false;
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            UpdateFacing();
            float breath = Mathf.Sin(Time.unscaledTime * 1.7f + _phase) * 0.008f;
            _quad.transform.localScale = new Vector3(1.50f, 2.08f + breath, 1f);
        }

        private void UpdateFacing()
        {
            if (_quad == null || _camera == null) return;
            Vector3 toCamera = _camera.transform.position - _quad.transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 0.001f) toCamera = -transform.forward;
            toCamera.Normalize();

            // Unity's built-in Quad faces its local -Z side. Point +Z away from the camera so
            // the authored sprite remains unmirrored while the card follows the orbit camera.
            _quad.transform.rotation = Quaternion.LookRotation(-toCamera, Vector3.up);
            bool cameraInFront = Vector3.Dot(transform.forward, toCamera) > 0.08f;
            Texture2D desired = cameraInFront ? _front : _back;
            if (_material != null && _material.mainTexture != desired) _material.mainTexture = desired;
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
