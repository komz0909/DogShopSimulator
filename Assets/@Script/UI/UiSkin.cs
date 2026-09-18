using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// 클릭 메뉴(OnGUI)가 함께 쓰는 버튼 모양. <b>그림 파일이 아니라 코드로 굽는다.</b>
    ///
    /// 버튼을 AI로 뽑지 않은 이유: 크기를 바꾸면 테두리 두께와 모서리가 같이 늘어나 뭉개지고,
    /// 눌림·비활성 상태를 따로 뽑으면 미묘하게 어긋나며, 배경을 지우면 경계에 색 테두리가 남는다.
    /// 여기서는 9-slice 로 굽기 때문에 어떤 크기로 늘려도 모서리가 그대로고,
    /// 상태는 색만 밝기로 옮겨 만든다.
    ///
    /// 결은 <b>두꺼운 테두리 + 평면</b> — 펫샵 에셋 킷(Rack2·침대)과 같은 화풍이다.
    /// 기준색은 Rack2 몸체에서 실제로 뽑은 값(색상 202도)이다.
    /// </summary>
    public static class UiSkin
    {
        public static readonly Color Sky    = new Color(0.553f, 0.686f, 0.753f);
        public static readonly Color Green  = new Color(0.553f, 0.753f, 0.635f);
        public static readonly Color Coral  = new Color(0.851f, 0.639f, 0.580f);
        public static readonly Color Cream  = new Color(0.902f, 0.863f, 0.784f);

        public static readonly Color Ink    = new Color(0.16f, 0.19f, 0.22f);
        public static readonly Color Panel  = new Color(0.10f, 0.11f, 0.13f, 0.88f);

        const int Radius = 14;
        const int Outline = 4;

        /// <summary>패널·버튼 글꼴. GameHud 가 들고 있는 BMJUA 를 시작할 때 넘겨 준다.</summary>
        public static Font Font { get; set; }

        static readonly System.Collections.Generic.Dictionary<int, GUIStyle> buttons =
            new System.Collections.Generic.Dictionary<int, GUIStyle>();
        static readonly System.Collections.Generic.Dictionary<int, GUIStyle> tags =
            new System.Collections.Generic.Dictionary<int, GUIStyle>();
        static GUIStyle panel;
        static GUIStyle label;
        static GUIStyle title;
        static GUIStyle caption;

        static Color Shift(Color c, float dv)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v + dv));
        }

        /// <summary>
        /// 둥근 사각형 한 장. 9-slice 경계를 반지름에 맞춰 잡으므로 늘려도 모서리가 유지된다.
        /// 테두리는 <see cref="Outline"/> 픽셀 두께로 안쪽에 그린다.
        /// </summary>
        static Texture2D Plate(Color fill, Color edge)
        {
            int size = Radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(Radius - x - 0.5f, x + 0.5f - (size - Radius)));
                float dy = Mathf.Max(0f, Mathf.Max(Radius - y - 0.5f, y + 0.5f - (size - Radius)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);      // 모서리 중심까지의 거리

                float inside = Radius - d;                     // 0 이면 경계, 클수록 안쪽
                if (inside <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                // 테두리는 경계에서 Outline 픽셀 안쪽까지
                float straight = Mathf.Min(Mathf.Min(x + 0.5f, size - 0.5f - x), Mathf.Min(y + 0.5f, size - 0.5f - y));
                float depth = (dx > 0f || dy > 0f) ? inside : straight;

                Color c = depth <= Outline ? edge : fill;
                c.a = Mathf.Clamp01(inside);                   // 가장자리 1px 만 부드럽게
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        static GUIStyle Build(Color tint)
        {
            var style = new GUIStyle
            {
                border = new RectOffset(Radius, Radius, Radius, Radius),
                padding = new RectOffset(14, 14, 8, 10),
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                font = Font,
                wordWrap = false
            };
            Color edge = Shift(tint, -0.26f);
            style.normal.background = Plate(tint, edge);
            style.hover.background = Plate(Shift(tint, 0.07f), edge);
            style.active.background = Plate(Shift(tint, -0.11f), Shift(tint, -0.34f));
            style.normal.textColor = style.hover.textColor = style.active.textColor = Ink;
            return style;
        }

        /// <summary>색만 다른 버튼. 색마다 한 번씩만 굽고 재사용한다.</summary>
        public static GUIStyle Button(Color tint)
        {
            int key = tint.GetHashCode();
            GUIStyle found;
            if (buttons.TryGetValue(key, out found) && found.normal.background != null) return found;

            GUIStyle built = Build(tint);
            buttons[key] = built;
            return built;
        }

        /// <summary>
        /// 가격표처럼 글자를 얹는 색판. 버튼과 같은 판을 쓰되 누름·올림 상태가 없다 —
        /// 값을 읽는 자리에 hover 가 붙으면 누를 수 있는 것처럼 보인다.
        /// </summary>
        public static GUIStyle Tag(Color tint)
        {
            int key = tint.GetHashCode();
            GUIStyle found;
            if (tags.TryGetValue(key, out found) && found.normal.background != null && found.font == Font) return found;

            var style = new GUIStyle
            {
                border = new RectOffset(Radius, Radius, Radius, Radius),
                padding = new RectOffset(6, 6, 2, 3),
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                font = Font,
                wordWrap = false
            };
            style.normal.background = Plate(tint, Shift(tint, -0.26f));
            style.normal.textColor = Ink;

            tags[key] = style;
            return style;
        }

        public static GUIStyle Panel_
        {
            get
            {
                if (panel != null && panel.normal.background != null) return panel;
                panel = new GUIStyle
                {
                    border = new RectOffset(Radius, Radius, Radius, Radius),
                    padding = new RectOffset(14, 14, 12, 12)
                };
                panel.normal.background = Plate(Panel, new Color(1f, 1f, 1f, 0.18f));
                return panel;
            }
        }

        public static GUIStyle Title
        {
            get
            {
                if (title != null && title.font == Font) return title;
                title = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold, font = Font };
                title.normal.textColor = new Color(1f, 0.89f, 0.55f);
                return title;
            }
        }

        public static GUIStyle Label
        {
            get
            {
                if (label != null && label.font == Font) return label;
                label = new GUIStyle { fontSize = 14, wordWrap = true, font = Font };
                label.normal.textColor = new Color(0.82f, 0.85f, 0.87f);
                return label;
            }
        }

        /// <summary>한 줄짜리 가운데 정렬 글. 줄바꿈을 끄지 않으면 좁은 칸에서 글자가 뭉갠다.</summary>
        public static GUIStyle Caption
        {
            get
            {
                if (caption != null && caption.font == Font) return caption;
                caption = new GUIStyle
                {
                    fontSize = 14,
                    wordWrap = false,
                    alignment = TextAnchor.MiddleCenter,
                    font = Font
                };
                caption.normal.textColor = new Color(0.86f, 0.88f, 0.90f);
                return caption;
            }
        }
    }
}
