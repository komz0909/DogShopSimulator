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

        /// <summary>
        /// 패널·버튼 글꼴. GameHud 가 들고 있는 BMJUA 를 시작할 때 넘겨 준다.
        ///
        /// <b>이 글꼴에는 줄표(—)와 가운뎃점(·)의 글리프가 없다.</b> 없는 글자는 경고 없이
        /// 빈칸으로 찍히므로 "약품·미용"이 "약품 미용"으로 보인다. 여기 스타일로 그리는 글은
        /// ASCII 문장부호나 공백만 쓸 것 — 내장 글꼴로 그리는 디버그 화면은 해당 없다.
        /// </summary>
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

        /// <summary>
        /// 그림 한 장을 9-slice 로 그린다. 경계는 <b>텍셀</b> 단위, scale 은 텍셀 하나가 화면에서 몇 픽셀인지.
        /// GUIStyle.border 로 하면 경계가 원본 픽셀 크기로 박혀서, 큰 원화를 작은 창에 쓰면 테두리가 창을 덮는다.
        /// </summary>
        public static void Draw9(Rect r, Texture2D tex, float left, float top, float right, float bottom, float scale)
        {
            if (tex == null) return;
            float tw = tex.width, th = tex.height;

            // 창이 경계 합보다 작으면 경계를 같이 줄인다
            scale = Mathf.Min(scale, r.width / (left + right), r.height / (top + bottom));

            // 조각 경계를 정수로 맞추고, 앞 조각을 다음 조각 쪽으로 1 만큼 겹쳐 그린다 —
            // 소수 경계 그대로면 조각 사이에 뒤 배경이 실선처럼 비친다
            float[] xs = { r.x, Mathf.Round(r.x + left * scale), Mathf.Round(r.xMax - right * scale), r.xMax };
            float[] ys = { r.y, Mathf.Round(r.y + top * scale), Mathf.Round(r.yMax - bottom * scale), r.yMax };
            float[] us = { 0f, left / tw, 1f - right / tw, 1f };
            float[] vs = { 1f, 1f - top / th, bottom / th, 0f };   // 텍스처 v 는 아래가 0

            for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
            {
                var dst = new Rect(xs[i], ys[j], xs[i + 1] - xs[i], ys[j + 1] - ys[j]);
                if (dst.width <= 0f || dst.height <= 0f) continue;
                Rect uv = new Rect(us[i], vs[j + 1], us[i + 1] - us[i], vs[j] - vs[j + 1]);
                if (i < 2) { dst.width += 1f; uv.width += 1f / tw; }
                if (j < 2) { dst.height += 1f; uv.y -= 1f / th; uv.height += 1f / th; }
                GUI.DrawTextureWithTexCoords(dst, tex, uv);
            }
        }

        /// <summary>
        /// 원본 그림(srcW x srcH) 안의 픽셀 사각형(위가 0)을, 그 그림을 늘려 그린 화면 사각형 위로 옮긴다.
        /// 원본 크기를 따로 받는 이유: 임포트 최대 크기에 걸리면 tex.width 가 원본보다 작아져 자리가 어긋난다.
        /// </summary>
        public static Rect TexelRect(Rect drawn, float srcW, float srcH, float x, float y, float w, float h)
        {
            float sx = drawn.width / srcW, sy = drawn.height / srcH;
            return new Rect(drawn.x + x * sx, drawn.y + y * sy, w * sx, h * sy);
        }

        /// <summary>
        /// 창 오른쪽 위 귀퉁이의 발바닥 닫기. 창 그림에서 그 자리 발바닥 무늬를 지우고, 같은 자리에
        /// 빨간 발바닥(UI_Paw 를 빨갛게 칠한 T_UI_ClosePaw)을 버튼으로 얹는다. spot 은 지운 발바닥의 경계다.
        /// 지운 발바닥을 떠서 칠해 봤더니 속이 비고 마디 사이가 번졌다.
        /// 장식과 구분되도록 조금 크게 그리고, 마우스를 올리면 더 커진다. 누르면 true.
        /// 버튼 발바닥은 빨간색이다 — 장식과 같은 금색이면 닫기인 줄 몰랐다. "닫기" 글자 팁은 뺐다(사용자 요청).
        /// </summary>
        /// <summary>
        /// 누르면 클릭음이 나는 버튼. 게임 안 버튼은 전부 이걸 쓴다 —
        /// 화면 전체를 덮는 가림판(아무도 안 받은 클릭을 삼키는 투명 버튼)만 <c>GUI.Button</c> 그대로 둔다.
        /// 거기서 소리가 나면 빈 곳을 눌러도 딸깍거린다.
        /// </summary>
        public static bool Click(Rect rect, string text, GUIStyle style) => Clicked(GUI.Button(rect, text, style));
        public static bool Click(Rect rect, GUIContent content, GUIStyle style) => Clicked(GUI.Button(rect, content, style));

        static bool Clicked(bool pressed)
        {
            if (pressed) Core.Sound.Play(Core.Sfx.Click);
            return pressed;
        }

        public static bool PawClose(Rect spot, Texture2D paw)
        {
            if (paw == null) return UiSkin.Click(spot, "X", Button(Coral));

            bool hover = spot.Contains(Event.current.mousePosition);
            float k = hover ? 1.25f : 1.1f;
            var d = new Rect(spot.center.x - spot.width * k * 0.5f, spot.center.y - spot.height * k * 0.5f,
                             spot.width * k, spot.height * k);
            GUI.DrawTexture(d, paw, ScaleMode.ScaleToFit);

            return UiSkin.Click(spot, GUIContent.none, GUIStyle.none);
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
