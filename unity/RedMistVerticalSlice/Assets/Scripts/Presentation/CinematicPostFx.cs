using UnityEngine;

namespace Lingmai.RedMist
{
    [RequireComponent(typeof(Camera))]
    public sealed class CinematicPostFx : MonoBehaviour
    {
        private Material _material;

        private void OnEnable()
        {
            Shader shader = Resources.Load<Shader>("Shaders/CinematicPostFx");
            if (shader != null && shader.isSupported) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            _material.SetFloat("_TimeValue", Time.unscaledTime);
            Graphics.Blit(source, destination, _material);
        }

        private void OnDisable()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
