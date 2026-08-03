using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Lingmai.RedMist
{
    public sealed class CinematicDirector : MonoBehaviour
    {
        private RawImage _backdrop;
        private RectTransform _backdropRect;
        private Image _flash;
        private Text _cue;
        private RectTransform _shakeRoot;
        private bool _reducedMotion;
        private float _drift;

        public void Initialize(RawImage backdrop, Image flash, Text cue, RectTransform shakeRoot)
        {
            _backdrop = backdrop;
            _backdropRect = backdrop.rectTransform;
            _flash = flash;
            _cue = cue;
            _shakeRoot = shakeRoot;
            _flash.color = Color.clear;
            _cue.gameObject.SetActive(false);
        }

        public void SetReducedMotion(bool value)
        {
            _reducedMotion = value;
            if (value && _backdropRect != null)
            {
                _backdropRect.localScale = Vector3.one;
                _backdropRect.anchoredPosition = Vector2.zero;
            }
        }

        public void SetBackdrop(Texture texture, Color tint)
        {
            if (_backdrop == null) return;
            _backdrop.texture = texture;
            _backdrop.color = tint;
            _drift = 0f;
            if (!_reducedMotion) StartCoroutine(FadeBackdrop());
        }

        public void PlayCue(string text, Color color, bool shake = false)
        {
            StartCoroutine(CueRoutine(text, color, shake));
        }

        private void Update()
        {
            if (_reducedMotion || _backdropRect == null || _backdrop == null || _backdrop.texture == null) return;
            _drift += Time.unscaledDeltaTime;
            float zoom = 1.045f + Mathf.Sin(_drift * 0.12f) * 0.012f;
            _backdropRect.localScale = new Vector3(zoom, zoom, 1f);
            _backdropRect.anchoredPosition = new Vector2(Mathf.Sin(_drift * 0.08f) * 18f, Mathf.Cos(_drift * 0.06f) * 10f);
        }

        private IEnumerator FadeBackdrop()
        {
            Color target = _backdrop.color;
            for (float t = 0f; t < 0.55f; t += Time.unscaledDeltaTime)
            {
                float p = Mathf.Clamp01(t / 0.55f);
                _backdrop.color = new Color(target.r, target.g, target.b, p);
                yield return null;
            }
            _backdrop.color = target;
        }

        private IEnumerator CueRoutine(string text, Color color, bool shake)
        {
            _cue.text = text;
            _cue.color = Color.white;
            _cue.gameObject.SetActive(true);
            Vector2 original = _shakeRoot != null ? _shakeRoot.anchoredPosition : Vector2.zero;
            for (float t = 0f; t < 0.65f; t += Time.unscaledDeltaTime)
            {
                float p = Mathf.Clamp01(t / 0.65f);
                _flash.color = new Color(color.r, color.g, color.b, Mathf.Sin(p * Mathf.PI) * 0.44f);
                _cue.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, p);
                if (shake && !_reducedMotion && _shakeRoot != null)
                {
                    float strength = (1f - p) * 18f;
                    _shakeRoot.anchoredPosition = original + Random.insideUnitCircle * strength;
                }
                yield return null;
            }
            if (_shakeRoot != null) _shakeRoot.anchoredPosition = original;
            _flash.color = Color.clear;
            _cue.gameObject.SetActive(false);
        }
    }
}
