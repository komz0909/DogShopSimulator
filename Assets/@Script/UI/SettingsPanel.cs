using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// 게임 설정 창. <b>메인 메뉴와 게임 안(ESC)이 같은 창을 쓴다</b> — 둘이 따로 그리면
    /// 한쪽만 고쳐지고 다른 쪽이 남는다.
    ///
    /// 창은 상점창·강아지 창과 같은 나무 틀 + 크림 종이(바르코 그림)이고, 닫기는 틀 오른쪽 위 귀퉁이
    /// 발바닥이다(<see cref="UiSkin.PawClose"/>). X 버튼·빨간 발바닥을 덧얹어 봤는데 둘 다 틀과 따로 놀았다.
    ///
    /// 예전에는 뒤쪽 버튼이 눌리지 않게 화면 전체에 투명 버튼을 <b>먼저</b> 깔았는데,
    /// IMGUI 는 먼저 그린 컨트롤이 클릭을 가져가서 그 투명 버튼이 닫기 클릭까지 먹었다.
    /// 그래서 가림판은 <b>맨 마지막</b>에 깐다 — 창 안 버튼이 먼저 받고, 아무도 안 받은 클릭만
    /// 가림판이 삼킨다. 창보다 <b>먼저</b> 그려진 버튼은 이걸로 못 막으니, 부르는 쪽이 그동안
    /// <c>GUI.enabled = false</c> 로 그리거나 GUI.depth 를 낮춰야 한다.
    /// </summary>
    public static class SettingsPanel
    {
        /// <summary>고를 수 있는 프레임 상한. 120·무제한은 뺐다 — 이 게임엔 필요 없고 노트북만 뜨거워진다.</summary>
        static readonly int[] FpsChoices = { 30, 45, 60 };

        // 설계 좌표(1080p 기준). 틀 그림(1112x825) 비율 그대로다 — 늘리면 간판과 톱니가 찌그러진다
        const float Width = 760f;
        const float Height = 564f;

        // 틀 그림 안쪽 크림 종이 자리(그림 비율) — x 0.076~0.921, y 0.154~0.893
        const float Left = 84f, Right = 674f, Top = 101f;

        static int fpsIndex = 2;   // 기본 60
        static Texture2D dim;

        static readonly Color Ink = new Color(0.33f, 0.2f, 0.1f);
        static readonly Color Soft = new Color(0.47f, 0.36f, 0.26f);
        static readonly Color Honey = new Color(0.97f, 0.84f, 0.58f);

        /// <summary>게임을 켤 때 한 번. 기본 60fps.</summary>
        public static void ApplyFps()
        {
            int fps = FpsChoices[Mathf.Clamp(fpsIndex, 0, FpsChoices.Length - 1)];

            // vSync 가 켜져 있으면 targetFrameRate 는 무시된다
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }

        /// <summary>창을 그린다. 닫기를 누르면 true — 닫는 건 부르는 쪽이 한다.</summary>
        public static bool Draw(Texture2D closeImage, Texture2D frameImage)
        {
            if (dim == null)
            {
                dim = new Texture2D(1, 1);
                dim.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
                dim.Apply();
                dim.hideFlags = HideFlags.HideAndDontSave;
            }

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.DrawTexture(screen, dim);

            Matrix4x4 keep = GUI.matrix;
            float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            var panel = new Rect((Screen.width / s - Width) * 0.5f, (Screen.height / s - Height) * 0.5f, Width, Height);
            if (frameImage != null) GUI.DrawTexture(panel, frameImage, ScaleMode.StretchToFill);
            else GUI.Box(panel, GUIContent.none, UiSkin.Tag(UiSkin.Cream));

            Styles();

            // 제목은 간판 위(그림 x 0.34~0.76, 왼쪽 끝 톱니를 피해 오른쪽으로)
            GUI.Label(new Rect(panel.x + 290f, panel.y + 14f, 260f, 64f), "게임 설정", title);

            float x = panel.x + Left;
            float w = Right - Left;
            float field = panel.x + 210f;
            float fieldW = Right - 210f;
            float row = panel.y + Top;

            // 사운드
            GUI.Label(new Rect(x, row, 120f, 30f), "사운드", label);
            AudioListener.volume = Slider(new Rect(field, row + 1f, fieldW - 70f, 28f), AudioListener.volume);
            GUI.Label(new Rect(panel.x + Right - 64f, row, 64f, 30f),
                Mathf.RoundToInt(AudioListener.volume * 100f) + "%", value);

            // FPS
            row += 50f;
            GUI.Label(new Rect(x, row, 120f, 30f), "FPS 제한", label);
            float bw = (fieldW - 8f) / FpsChoices.Length;
            for (int i = 0; i < FpsChoices.Length; i++)
            {
                bool on = fpsIndex == i;
                if (GUI.Button(new Rect(field + i * (bw + 4f), row - 3f, bw, 34f), FpsChoices[i].ToString(),
                        UiSkin.Button(on ? UiSkin.Green : Honey)))
                {
                    fpsIndex = i;
                    ApplyFps();
                }
            }

            // 지금 나오는 프레임을 같이 보여 준다 — 제한을 걸었을 때 먹히는지 눈으로 확인된다.
            // 게임 안에서는 창을 여는 동안 시간이 멈춰 있으니 실시간 기준으로 잰다
            row += 44f;
            float dt = Time.timeScale > 0f ? Time.smoothDeltaTime : Time.unscaledDeltaTime;
            GUI.Label(new Rect(field, row, fieldW, 24f),
                "지금 " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, dt)) + " fps", soft);

            // 단축키 안내 — 두 줄로
            row += 32f;
            Fill(new Rect(x - 4f, row, w + 8f, 2f), new Color(0.55f, 0.38f, 0.22f, 0.3f));
            row += 10f;
            GUI.Label(new Rect(x, row, 200f, 28f), "단축키", label);
            row += 34f;

            float colW = (w - 16f) * 0.5f;
            int rows = (Keys.Length + 1) / 2;
            for (int i = 0; i < Keys.Length; i++)
            {
                float cx = x + (i / rows) * (colW + 16f);
                float cy = row + (i % rows) * KeyRow;
                float chip = Mathf.Max(34f, keyText.CalcSize(new GUIContent(Keys[i].key)).x + 16f);
                GUI.Box(new Rect(cx, cy, chip, 28f), Keys[i].key, UiSkin.Tag(Honey));
                GUI.Label(new Rect(cx + chip + 8f, cy, colW - chip - 8f, 28f), Keys[i].what, keyText);
            }

            // 닫기 — 틀 오른쪽 위 귀퉁이 판의 발바닥 자리(그림 텍셀 1026,58 58x57)가 버튼이다.
            // 틀 그림에서는 그 발바닥을 지워 두었다
            Rect spot = frameImage != null
                ? UiSkin.TexelRect(panel, 1112f, 825f, 1026f, 58f, 58f, 57f)
                : new Rect(panel.xMax - 70f, panel.y + 20f, 50f, 50f);
            bool close = UiSkin.PawClose(spot, closeImage);

            GUI.matrix = keep;

            // 가림판은 맨 마지막 — 위 버튼들이 안 받은 클릭만 삼킨다
            GUI.Button(screen, GUIContent.none, GUIStyle.none);
            return close;
        }

        /// <summary>
        /// 볼륨 막대. 기본 슬라이더는 회색 윈도 모양이라 크림 종이 위에서 혼자 떠 보였다.
        /// 홈은 연한 갈색, 채운 쪽은 초록, 손잡이는 버튼과 같은 산호색 판이다.
        /// </summary>
        static float Slider(Rect r, float v)
        {
            var track = new Rect(r.x, r.y + r.height * 0.5f - 6f, r.width, 12f);
            Fill(track, new Color(0.55f, 0.38f, 0.22f, 0.25f));
            Fill(new Rect(track.x, track.y, track.width * v, track.height), UiSkin.Green);
            return GUI.HorizontalSlider(r, v, 0f, 1f, GUIStyle.none, thumb);
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>ESC 창에 띄우는 단축키 목록. 키를 새로 붙이면 여기에도 적는다.</summary>
        static readonly (string key, string what)[] Keys =
        {
            ("W A S D", "이동"),
            ("우클릭", "끌어서 시점 돌리기"),
            ("V", "1인칭 / 3인칭 바꾸기"),
            ("E", "상호작용 / 상자에 담기"),
            ("R", "상자에서 꺼내기"),
            ("T", "강아지 부르기"),
            ("F", "가구 들기 / 배치"),
            ("Q", "가구 돌리기"),
            ("1  2  3", "게임 속도 x1 / x4 / x16"),
            ("F5 / F9", "저장 / 불러오기"),
            ("ESC", "설정 / 창 닫기"),
            ("Space, 1~4", "도그쇼 점프 / 카드 고르기"),
        };

        const float KeyRow = 35f;

        static GUIStyle title, label, value, soft, keyText, thumb;
        static Font built;

        static void Styles()
        {
            if (title != null && built == UiSkin.Font && thumb.normal.background != null) return;
            built = UiSkin.Font;

            title = Make(26, new Color(1f, 0.96f, 0.86f), TextAnchor.MiddleCenter, true);
            label = Make(18, Ink, TextAnchor.MiddleLeft, true);
            value = Make(17, Ink, TextAnchor.MiddleRight, true);
            soft = Make(14, Soft, TextAnchor.MiddleLeft, false);
            keyText = Make(15, Ink, TextAnchor.MiddleLeft, false);

            thumb = new GUIStyle(UiSkin.Button(UiSkin.Coral)) { fixedWidth = 28f, fixedHeight = 28f, padding = new RectOffset() };
            thumb.hover.background = UiSkin.Button(UiSkin.Coral).hover.background;
        }

        static GUIStyle Make(int size, Color color, TextAnchor anchor, bool bold)
        {
            var s = new GUIStyle
            {
                font = UiSkin.Font, fontSize = size, alignment = anchor, wordWrap = false,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal
            };
            s.normal.textColor = color;
            return s;
        }
    }
}
