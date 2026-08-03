using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lingmai.RedMist
{
    public static class UIFactory
    {
        private static Font _font;
        private static Sprite _whiteSprite;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                string[] preferred = { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Arial" };
                _font = Font.CreateDynamicFontFromOSFont(preferred, 24);
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite != null) return _whiteSprite;
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.name = "RuntimeWhite";
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                texture.Apply();
                _whiteSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
                return _whiteSprite;
            }
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static GameObject CreateUIObject(string name, Transform parent, params Type[] components)
        {
            Type[] all = new Type[components.Length + 1];
            all[0] = typeof(RectTransform);
            Array.Copy(components, 0, all, 1, components.Length);
            GameObject go = new GameObject(name, all);
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color)
        {
            GameObject go = CreateUIObject(name, parent, typeof(Image));
            Image image = go.GetComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            return image;
        }

        public static RawImage Raw(string name, Transform parent, Color color)
        {
            GameObject go = CreateUIObject(name, parent, typeof(RawImage));
            RawImage raw = go.GetComponent<RawImage>();
            raw.color = color;
            return raw;
        }

        public static Text Label(string name, Transform parent, string value, int size, TextAnchor alignment, Color color, FontStyle style = FontStyle.Normal)
        {
            GameObject go = CreateUIObject(name, parent, typeof(Text));
            Text text = go.GetComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(string name, Transform parent, string value, Action onClick, Color normal, Color highlighted, int fontSize = 22)
        {
            GameObject go = CreateUIObject(name, parent, typeof(Image), typeof(Button));
            Image image = go.GetComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = normal;
            Button button = go.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = highlighted * 0.8f;
            colors.selectedColor = highlighted;
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            Text label = Label("Label", go.transform, value, fontSize, TextAnchor.MiddleCenter, new Color(0.93f, 0.95f, 0.92f), FontStyle.Bold);
            Stretch(label.rectTransform, 14, 4, 14, 4);
            return button;
        }

        public static Canvas CreateCanvas(string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static Color Hex(string hex)
        {
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
        }
    }
}
