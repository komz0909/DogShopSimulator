using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// 게임 설정 창. <b>메인 메뉴와 게임 안(ESC)이 같은 창을 쓴다</b> — 둘이 따로 그리면
    /// 한쪽만 고쳐지고 다른 쪽이 남는다.
    ///
    /// 닫기는 X 가 아니라 발바닥이다. 예전에는 뒤쪽 버튼이 눌리지 않게 화면 전체에 투명 버튼을
    /// <b>먼저</b> 깔았는데, IMGUI 는 먼저 그린 컨트롤이 클릭을 가져가서 그 투명 버튼이
    /// 발바닥 클릭까지 먹었다. 그래서 가림판은 <b>맨 마지막</b>에 깐다 — 창 안 버튼이 먼저
    /// 받고, 아무도 안 받은 클릭만 가림판이 삼킨다. 창보다 <b>먼저</b> 그려진 버튼은 이걸로
    /// 못 막으니, 부르는 쪽이 그동안 <c>GUI.enabled = false</c> 로 그리거나 GUI.depth 를 낮춰야 한다.
    /// </summary>
    public static class SettingsPanel
    {
        /// <summary>고를 수 있는 프레임 상한. 120·무제한은 뺐다 — 이 게임엔 필요 없고 노트북만 뜨거워진다.</summary>
        static readonly int[] FpsChoices = { 30, 45, 60 };

        const float Width = 520f;
        const float Height = 300f;
        const float PawSize = 72f;

        static int fpsIndex = 2;   // 기본 60
        static Texture2D dim;

        /// <summary>게임을 켤 때 한 번. 기본 60fps.</summary>
        public static void ApplyFps()
        {
            int fps = FpsChoices[Mathf.Clamp(fpsIndex, 0, FpsChoices.Length - 1)];

            // vSync 가 켜져 있으면 targetFrameRate 는 무시된다
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }

        /// <summary>창을 그린다. 발바닥을 누르면 true — 닫는 건 부르는 쪽이 한다.</summary>
        public static bool Draw(Texture2D pawImage)
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

            var panel = new Rect((Screen.width - Width) * 0.5f, (Screen.height - Height) * 0.5f, Width, Height);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            var title = new GUIStyle(UiSkin.Title) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(panel.x, panel.y + 18f, panel.width, 32f), "게임 설정", title);

            float row = panel.y + 78f;

            // 사운드
            GUI.Label(new Rect(panel.x + 34f, row, 130f, 26f), "사운드", UiSkin.Label);
            AudioListener.volume = GUI.HorizontalSlider(
                new Rect(panel.x + 170f, row + 9f, panel.width - 270f, 20f), AudioListener.volume, 0f, 1f);
            GUI.Label(new Rect(panel.x + panel.width - 84f, row, 60f, 26f),
                Mathf.RoundToInt(AudioListener.volume * 100f) + "%", UiSkin.Label);

            // FPS
            row += 56f;
            GUI.Label(new Rect(panel.x + 34f, row, 130f, 26f), "FPS 제한", UiSkin.Label);
            float bw = (panel.width - 200f) / FpsChoices.Length;
            for (int i = 0; i < FpsChoices.Length; i++)
            {
                bool on = fpsIndex == i;
                string label = FpsChoices[i].ToString();
                if (GUI.Button(new Rect(panel.x + 170f + i * (bw + 4f), row - 3f, bw, 32f), label,
                        UiSkin.Button(on ? UiSkin.Green : UiSkin.Cream)))
                {
                    fpsIndex = i;
                    ApplyFps();
                }
            }

            // 지금 나오는 프레임을 같이 보여 준다 — 제한을 걸었을 때 먹히는지 눈으로 확인된다.
            // 게임 안에서는 창을 여는 동안 시간이 멈춰 있으니 실시간 기준으로 잰다
            row += 50f;
            float dt = Time.timeScale > 0f ? Time.smoothDeltaTime : Time.unscaledDeltaTime;
            GUI.Label(new Rect(panel.x + 34f, row, panel.width - 68f, 26f),
                "지금 " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, dt)) + " fps", UiSkin.Caption);

            row += 38f;
            GUI.Label(new Rect(panel.x + 34f, row, panel.width - 68f, 26f),
                "조작: WASD 이동 / E 상호작용 / F 가구 배치 / Q 회전 / ESC 설정", UiSkin.Caption);

            // 닫기는 X 가 아니라 발바닥이다
            var pawRect = new Rect(panel.xMax - PawSize * 0.62f, panel.y - PawSize * 0.38f, PawSize, PawSize);
            bool close = PawButton(pawRect, pawImage);

            // 가림판은 맨 마지막 — 위 버튼들이 안 받은 클릭만 삼킨다
            GUI.Button(screen, GUIContent.none, GUIStyle.none);
            return close;
        }

        static bool PawButton(Rect rect, Texture2D pawImage)
        {
            bool hover = rect.Contains(Event.current.mousePosition);

            Color prev = GUI.color;
            GUI.color = hover ? new Color(1.2f, 1.2f, 1.2f, 1f) : Color.white;
            if (pawImage != null) GUI.DrawTexture(rect, pawImage, ScaleMode.ScaleToFit);
            else GUI.Label(rect, "닫기", UiSkin.Title);
            GUI.color = prev;

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }
    }
}
