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

        /// <summary>타이틀을 가로로만 늘리는 배수. 1 이면 원래 비율.</summary>
        const float TitleStretch = 1.16f;

        Page page = Page.Home;
        int picked = -1;
        bool settingsOpen;

        void Awake()
        {
            if (uiFont != null) UiSkin.Font = uiFont;
            SettingsPanel.ApplyFps();
        }

        void Update()
        {
            // ESC 로도 설정 창을 닫는다(게임 안과 같은 키)
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (settingsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) settingsOpen = false;
        }

        void OnGUI()
        {
            if (uiFont != null) UiSkin.Font = uiFont;

            DrawBackground();

            // 설정이 떠 있으면 뒤 화면 버튼은 <b>죽여서</b> 그린다. IMGUI 는 먼저 그린 버튼이
            // 클릭을 먼저 받으므로, 뒤에 그리는 설정 창의 가림판으로는 이 버튼들을 막을 수 없다
            GUI.enabled = !settingsOpen;
            if (page == Page.Home) DrawHome();
            else DrawBreedPage();
            GUI.enabled = true;

            // 설정은 <b>덮개</b>다. 뒤 화면을 어둡게 깔고 그 위에 띄운다 —
            // 별도 페이지로 만들면 어디서 왔는지 기억해 두었다 돌아가야 한다
            if (settingsOpen && SettingsPanel.Draw(pawImage)) settingsOpen = false;
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
            float by = y + CardHeight + 14f;

            // 이름 짓기 — 견종을 고르면 나타난다. 비워 두면 견종 이름으로 부른다
            if (picked >= 0)
            {
                var field = new Rect((Screen.width - 360f) * 0.5f, by, 360f, 38f);
                GUI.Label(new Rect(field.x - 110f, by, 100f, 38f), "이름", NameLabelStyle());
                GUI.SetNextControlName("DogName");
                dogName = GUI.TextField(field, dogName, MaxNameLength, NameFieldStyle());
                if (string.IsNullOrEmpty(dogName) && GUI.GetNameOfFocusedControl() != "DogName")
                    GUI.Label(field, breeds[picked].nameKo + " (이름을 지어 주자)", NameHintStyle());
            }
            by += 50f;

            bool ready = picked >= 0;
            if (BoneButton(new Rect(Screen.width * 0.5f - bw - 8f, by, bw, bh), "뒤로"))
            {
                page = Page.Home;
                picked = -1;
            }

            if (BoneButton(new Rect(Screen.width * 0.5f + 8f, by, bw, bh), ready ? "시작" : "고르자", ready))
            {
                GameStart.NewGame(picked, dogName.Trim());
                SceneManager.LoadScene(shopScene);
            }
        }

        /// <summary>이름표에 들어갈 만큼. 한글 8자가 이름표 가운데에 한 줄로 들어간다.</summary>
        const int MaxNameLength = 8;
        string dogName = "";
        GUIStyle nameField, nameHint, nameLabel;

        GUIStyle NameFieldStyle()
        {
            if (nameField != null) return nameField;
            nameField = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleCenter, font = UiSkin.Caption.font };
            nameField.padding = new RectOffset(10, 10, 4, 4);
            return nameField;
        }

        GUIStyle NameHintStyle()
        {
            if (nameHint != null) return nameHint;
            nameHint = new GUIStyle(UiSkin.Caption) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            nameHint.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
            return nameHint;
        }

        GUIStyle NameLabelStyle()
        {
            if (nameLabel != null) return nameLabel;
            nameLabel = new GUIStyle(UiSkin.Caption) { fontSize = 20, alignment = TextAnchor.MiddleRight };
            return nameLabel;
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
