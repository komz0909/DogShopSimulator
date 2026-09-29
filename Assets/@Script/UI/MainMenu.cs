using DogShop.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DogShop.UI
{
    /// <summary>
    /// 첫 화면. 타이틀을 보여 주고, 새로 시작하면 <b>견종을 고르고</b> 가게 씬으로 넘어간다.
    ///
    /// 고른 견종은 <see cref="GameStart"/>가 정적 필드로 들고 간다. 씬을 갈아타면
    /// 오브젝트가 사라지므로 DontDestroyOnLoad 오브젝트를 하나 만들 수도 있지만,
    /// 넘길 것이 정수 하나와 불 하나뿐이라 그 무게를 질 이유가 없다.
    ///
    /// 게임의 다른 창과 같은 IMGUI로 그린다 — 여기만 다른 UI 체계를 쓰면 폰트·색이
    /// 어긋나고, 그걸 맞추는 일이 메뉴 만드는 일보다 커진다.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        enum Page { Home, Breeds }

        [SerializeField] Font uiFont;
        [SerializeField] string shopScene = "Shop";

        [Header("그림")]
        [SerializeField] Texture2D background;  // 인게임 화면을 모자이크한 것
        [SerializeField] Texture2D titleImage;
        [SerializeField] Texture2D boneImage;   // 버튼 바탕
        [SerializeField] Texture2D pawImage;    // 설정 창 닫기

        /// <summary>
        /// 고를 수 있는 견종. <see cref="Dogs.DogManager"/>의 breedPrefabs 순서와 <b>같아야 한다</b> —
        /// 여기서 고른 번호가 그대로 그쪽 배열의 첨자로 쓰인다.
        /// </summary>
        [System.Serializable]
        public class Breed
        {
            public string nameKo = "";
            public string blurb = "";
            public Texture2D portrait;

            /// <summary>몸 길이(m). 걸음걸이가 여기서 역산되므로 고르는 사람에게 보여 준다.</summary>
            public float bodyLength = 1f;

            /// <summary>뛰기 속도(m/s). 주인은 3.2 라 작은 견종은 따라오지 못한다.</summary>
            public float runSpeed = 2.5f;
        }

        [SerializeField] Breed[] breeds = new Breed[0];

        const float CardWidth = 200f;
        const float CardHeight = 316f;
        const float Gap = 16f;

        /// <summary>
        /// 뼈다귀 버튼 크기. 그림의 실제 가로세로비(690x328 = 2.10)에 맞춘다 —
        /// 안 맞추면 ScaleToFit 이 남는 쪽을 여백으로 만들어 뼈가 글자보다 작아진다.
        /// </summary>
        const float BoneWidth = 320f;
        const float BoneAspect = 2.10f;

        const float PawSize = 72f;

        /// <summary>타이틀을 가로로만 늘리는 배수. 1 이면 원래 비율.</summary>
        const float TitleStretch = 1.16f;

        /// <summary>고를 수 있는 프레임 상한. 0 은 제한 없음.</summary>
        static readonly int[] FpsChoices = { 30, 60, 120, 0 };

        Page page = Page.Home;
        int picked = -1;
        bool settingsOpen;
        int fpsIndex = 1;

        Texture2D dim;   // 설정 창 뒤에 까는 반투명 검은 판

        void Awake()
        {
            if (uiFont != null) UiSkin.Font = uiFont;

            dim = new Texture2D(1, 1);
            dim.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
            dim.Apply();

            ApplyFps();
        }

        void OnDestroy()
        {
            if (dim != null) Destroy(dim);
        }

        void OnGUI()
        {
            if (uiFont != null) UiSkin.Font = uiFont;

            DrawBackground();

            if (page == Page.Home) DrawHome();
            else DrawBreedPage();

            // 설정은 <b>덮개</b>다. 뒤 화면을 어둡게 깔고 그 위에 띄운다 —
            // 별도 페이지로 만들면 어디서 왔는지 기억해 두었다 돌아가야 한다
            if (settingsOpen) DrawSettings();
        }

        void DrawBackground()
        {
            if (background == null) return;

            // 화면비가 달라도 빈 곳이 생기지 않게 채워서 자른다
            float scale = Mathf.Max(Screen.width / (float)background.width,
                                    Screen.height / (float)background.height);
            float w = background.width * scale, h = background.height * scale;
            GUI.DrawTexture(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), background);
        }

        // ---- 공통 부품 ----

        /// <summary>
        /// 뼈다귀 모양 버튼. 그림을 깔고 그 위에 글자를 얹는다 —
        /// IMGUI 버튼 스타일에 알파가 있는 그림을 넣으면 9조각으로 늘어나 뼈 모양이 뭉개진다.
        /// </summary>
        bool BoneButton(Rect rect, string label, bool enabled = true)
        {
            bool hover = rect.Contains(Event.current.mousePosition);

            Color prev = GUI.color;
            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            else if (hover) GUI.color = new Color(1.12f, 1.12f, 1.12f, 1f);

            if (boneImage != null) GUI.DrawTexture(rect, boneImage, ScaleMode.ScaleToFit);
            GUI.color = prev;

            var style = new GUIStyle(UiSkin.Caption)
            {
                fontSize = Mathf.RoundToInt(rect.height * 0.30f),
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = enabled ? UiSkin.Ink : new Color(0.35f, 0.32f, 0.30f);
            GUI.Label(rect, label, style);

            if (!enabled) return false;
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>발바닥 모양 닫기 버튼. 설정 창에서 X 대신 쓴다.</summary>
        bool PawButton(Rect rect)
        {
            bool hover = rect.Contains(Event.current.mousePosition);

            Color prev = GUI.color;
            GUI.color = hover ? new Color(1.2f, 1.2f, 1.2f, 1f) : Color.white;
            if (pawImage != null) GUI.DrawTexture(rect, pawImage, ScaleMode.ScaleToFit);
            GUI.color = prev;

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>
        /// 타이틀을 그리고 그 <b>아래끝 y</b>를 돌려준다 — 버튼을 그 아래에 놓아야 하므로.
        ///
        /// 세로는 그대로 두고 가로만 <see cref="TitleStretch"/>배 늘린다. 그래서
        /// ScaleToFit(비율 유지)이 아니라 StretchToFill 로 그린다.
        /// </summary>
        float DrawTitleImage(float centerY, float height)
        {
            if (titleImage == null) return centerY;

            float width = height * titleImage.width / titleImage.height * TitleStretch;
            var rect = new Rect((Screen.width - width) * 0.5f, centerY - height * 0.5f, width, height);
            GUI.DrawTexture(rect, titleImage, ScaleMode.StretchToFill);
            return rect.yMax;
        }

        // ---- 첫 화면 ----

        void DrawHome()
        {
            // 세로 크기를 준다. 가로는 비율 x 늘림배수로 정해지므로 화면보다 넓어지지 않게 묶는다
            float titleHeight = Mathf.Min(380f, Screen.height * 0.36f,
                Screen.width * 0.60f / (titleImage != null ? titleImage.width / (float)titleImage.height : 1.5f) / TitleStretch);
            float titleBottom = DrawTitleImage(Screen.height * 0.24f, titleHeight);

            // 버튼 넷을 <b>타이틀 아래 남는 공간에</b> 맞춘다. 화면 비율이 달라져도
            // 마지막 버튼이 아래로 잘리지 않는다
            const int count = 4;
            float room = Mathf.Max(200f, Screen.height - 40f - titleBottom);
            float step = Mathf.Min(BoneWidth / BoneAspect + 6f, room / count);
            float height = Mathf.Min(BoneWidth / BoneAspect, step - 4f);
            float width = height * BoneAspect;

            float x = (Screen.width - width) * 0.5f;
            float y = titleBottom + (room - step * count) * 0.5f;

            if (BoneButton(new Rect(x, y, width, height), "게임 시작")) page = Page.Breeds;

            bool hasSave = SaveManager.HasSaveFile;
            if (BoneButton(new Rect(x, y + step, width, height), "이어하기", hasSave))
            {
                GameStart.Resume();
                SceneManager.LoadScene(shopScene);
            }

            if (BoneButton(new Rect(x, y + step * 2f, width, height), "게임 설정")) settingsOpen = true;

            if (BoneButton(new Rect(x, y + step * 3f, width, height), "게임 종료")) Quit();
        }

        // ---- 견종 고르기 ----

        void DrawBreedPage()
        {
            DrawTitleImage(Screen.height * 0.12f, Mathf.Min(150f, Screen.height * 0.16f));

            GUI.Label(new Rect(0f, Screen.height * 0.22f, Screen.width, 26f),
                "함께 지낼 강아지를 고르자", UiSkin.Caption);

            float width = breeds.Length * CardWidth + (breeds.Length - 1) * Gap;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.5f - CardHeight * 0.42f;

            DrawBreeds(x, y);

            float bw = 240f, bh = bw / BoneAspect;
            float by = y + CardHeight + 18f;

            bool ready = picked >= 0;
            if (BoneButton(new Rect(Screen.width * 0.5f - bw - 8f, by, bw, bh), "뒤로"))
            {
                page = Page.Home;
                picked = -1;
            }

            if (BoneButton(new Rect(Screen.width * 0.5f + 8f, by, bw, bh), ready ? "시작" : "고르자", ready))
            {
                GameStart.NewGame(picked);
                SceneManager.LoadScene(shopScene);
            }
        }

        void DrawBreeds(float x, float y)
        {
            for (int i = 0; i < breeds.Length; i++)
            {
                var card = new Rect(x + i * (CardWidth + Gap), y, CardWidth, CardHeight);
                GUI.Box(card, GUIContent.none, UiSkin.Panel_);

                // 고른 카드는 테두리 대신 색판을 깔아 표시한다 — IMGUI 에 테두리만 그리는 수단이 마땅치 않다
                if (picked == i)
                    GUI.Box(new Rect(card.x + 6f, card.y + 6f, card.width - 12f, 26f), "고름", UiSkin.Tag(UiSkin.Green));

                float side = card.width - 56f;
                var portrait = new Rect(card.x + 28f, card.y + 38f, side, side);
                if (breeds[i].portrait != null) GUI.DrawTexture(portrait, breeds[i].portrait, ScaleMode.ScaleToFit);
                else GUI.Box(portrait, GUIContent.none, UiSkin.Tag(UiSkin.Cream));

                var name = new GUIStyle(UiSkin.Caption) { fontSize = 18 };
                GUI.Label(new Rect(card.x, portrait.yMax + 6f, card.width, 26f), breeds[i].nameKo, name);

                // 몸집과 속도. 견종을 고르는 것이 겉모습만의 일이 아니라는 걸 숫자로 보여 준다
                float tagY = portrait.yMax + 34f;
                float tagW = (card.width - 28f) * 0.5f;
                GUI.Box(new Rect(card.x + 10f, tagY, tagW, 24f),
                    "몸길이 " + breeds[i].bodyLength.ToString("0.0") + "m", UiSkin.Tag(UiSkin.Cream));
                GUI.Box(new Rect(card.x + 18f + tagW, tagY, tagW, 24f),
                    "달리기 " + breeds[i].runSpeed.ToString("0.0"), UiSkin.Tag(UiSkin.Sky));

                var blurb = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.UpperCenter, fontSize = 13 };
                GUI.Label(new Rect(card.x + 10f, tagY + 30f, card.width - 20f, 52f), breeds[i].blurb, blurb);

                if (GUI.Button(new Rect(card.x, card.y, card.width, card.height), GUIContent.none, GUIStyle.none))
                    picked = i;
            }
        }

        // ---- 설정 ----

        void DrawSettings()
        {
            // 화면 전체를 어둡게 덮는다. 뒤쪽 버튼이 눌리지 않게 덮개 위에 투명 버튼을 깐다
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dim);
            GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);

            const float w = 520f, h = 300f;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
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
                string label = FpsChoices[i] == 0 ? "무제한" : FpsChoices[i].ToString();
                if (GUI.Button(new Rect(panel.x + 170f + i * (bw + 4f), row - 3f, bw, 32f), label,
                        UiSkin.Button(on ? UiSkin.Green : UiSkin.Cream)))
                {
                    fpsIndex = i;
                    ApplyFps();
                }
            }

            // 지금 나오는 프레임을 같이 보여 준다 — 제한을 걸었을 때 먹히는지 눈으로 확인된다
            row += 50f;
            GUI.Label(new Rect(panel.x + 34f, row, panel.width - 68f, 26f),
                "지금 " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)) + " fps", UiSkin.Caption);

            row += 38f;
            GUI.Label(new Rect(panel.x + 34f, row, panel.width - 68f, 26f),
                "조작: WASD 이동 / E 상호작용 / F 가구 배치 / Q 회전", UiSkin.Caption);

            // 닫기는 X 가 아니라 발바닥이다
            if (PawButton(new Rect(panel.xMax - PawSize * 0.62f, panel.y - PawSize * 0.38f, PawSize, PawSize)))
                settingsOpen = false;
        }

        void ApplyFps()
        {
            int fps = FpsChoices[Mathf.Clamp(fpsIndex, 0, FpsChoices.Length - 1)];

            // vSync 가 켜져 있으면 targetFrameRate 는 무시된다
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps == 0 ? -1 : fps;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
