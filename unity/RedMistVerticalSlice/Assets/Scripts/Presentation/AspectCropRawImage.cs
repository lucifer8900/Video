using UnityEngine;
using UnityEngine.UI;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Centre-crops a RawImage to its RectTransform without changing the source aspect ratio.
    /// A smaller source region can be supplied for atlas-style character sheets.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RawImage))]
    public sealed class AspectCropRawImage : MonoBehaviour
    {
        [SerializeField] private Rect sourceRegion = new Rect(0f, 0f, 1f, 1f);

        private RawImage _image;
        private Texture _lastTexture;
        private Vector2 _lastRectSize;
        private Rect _lastSourceRegion;

        public void SetSourceRegion(Rect region)
        {
            sourceRegion = ClampRegion(region);
            Refresh(true);
        }

        public void Refresh(bool force = false)
        {
            EnsureImage();
            if (_image == null || _image.texture == null) return;

            Rect target = _image.rectTransform.rect;
            Vector2 targetSize = target.size;
            if (targetSize.x <= 0.01f || targetSize.y <= 0.01f || _image.texture.height <= 0) return;

            Rect region = ClampRegion(sourceRegion);
            if (!force && _lastTexture == _image.texture && _lastRectSize == targetSize && _lastSourceRegion == region) return;

            float targetAspect = targetSize.x / targetSize.y;
            float sourceAspect = (_image.texture.width * region.width) / (_image.texture.height * region.height);
            Rect crop = region;

            if (sourceAspect > targetAspect)
            {
                float widthScale = targetAspect / sourceAspect;
                crop.width = region.width * widthScale;
                crop.x = region.x + (region.width - crop.width) * 0.5f;
            }
            else
            {
                float heightScale = sourceAspect / targetAspect;
                crop.height = region.height * heightScale;
                crop.y = region.y + (region.height - crop.height) * 0.5f;
            }

            _image.uvRect = crop;
            _lastTexture = _image.texture;
            _lastRectSize = targetSize;
            _lastSourceRegion = region;
        }

        private void OnEnable()
        {
            Refresh(true);
        }

        private void OnRectTransformDimensionsChange()
        {
            Refresh(true);
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void EnsureImage()
        {
            if (_image == null) _image = GetComponent<RawImage>();
        }

        private static Rect ClampRegion(Rect value)
        {
            float x = Mathf.Clamp01(value.x);
            float y = Mathf.Clamp01(value.y);
            float width = Mathf.Clamp(value.width, 0.0001f, 1f - x);
            float height = Mathf.Clamp(value.height, 0.0001f, 1f - y);
            return new Rect(x, y, width, height);
        }
    }
}
