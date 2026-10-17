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
        [SerializeField] Texture2D closeImage;    // 설정 창 닫기 귀퉁이 발바닥 (T_UI_SettingsClosePaw)
        [SerializeField] Texture2D frameImage;    // 설정 창 틀 (T_UI_SettingsFrame)

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

            // ---- 소개 카드(고르면 뜨는 상태창) ----
            // 능력치 별·특성 글은 견종 번호로 <see cref="Dogs.BreedTraits"/> 에서 읽는다(게임 수치와 같은 곳).

            /// <summary>등급 꼬리표(일반·희귀 …).</summary>
            public string grade = "일반";
        }

        [Header("강아지 소개 카드 — 견종을 누르면 뜬다")]
        [Tooltip("DogManager.breedPrefabs 와 같은 순서. 카드 받침 위에 실제 3D 강아지를 띄운다")]
        [SerializeField] GameObject[] breedPrefabs = new GameObject[0];
        [SerializeField] Texture2D cardBackground;   // 흐린 가게 안 + 구름 가장자리
        [SerializeField] Texture2D breedCardImage;   // 견종 목록의 작은 카드 바탕(크림 판 + 나무 테두리 + 받침)
        [SerializeField] Texture2D pedestalImage;    // 둥근 나무 받침
        [SerializeField] Texture2D newBadge;
        [SerializeField] Texture2D confirmButton;    // 노란 알약 버튼(글자 없음)
        [SerializeField] Texture2D otherButton;      // 같은 알약을 빨간색으로 돌린 것 — [다른 강아지]
        [SerializeField] Texture2D iconStar, iconPaw;
        [Tooltip("첫 화면 타이틀 — 타이틀 그림에서 아래 작은 가게를 뺀 로고만")]
        [SerializeField] Texture2D titleLogo;
        [Tooltip("건강·청결·미모·훈련도·어질리티 순서(가게 훈련장 창과 같은 아이콘)")]
        [SerializeField] Texture2D[] statIcons = new Texture2D[0];

        [SerializeField] Breed[] breeds = new Breed[0];

        const float CardWidth = 200f;
        const float CardHeight = 316f;
        const float Gap = 16f;

        /// <summary>
        /// 뼈다귀 버튼 가로세로비. 그림의 실제 비(690x328 = 2.10)에 맞춘다 —
        /// 안 맞추면 ScaleToFit 이 남는 쪽을 여백으로 만들어 뼈가 글자보다 작아진다.
        /// </summary>
        const float BoneAspect = 2.10f;

        /// <summary>타이틀을 가로로만 늘리는 배수. 1 이면 원래 비율.</summary>
        const float TitleStretch = 1.16f;

        Page page = Page.Home;

        /// <summary>[시연 모드]로 견종 고르기에 들어왔나. [확인]에서 시연 판으로 시작한다.</summary>
        bool exhibitionPick;
        int picked = -1;
        bool settingsOpen;

        void Awake()
        {
            if (uiFont != null) UiSkin.Font = uiFont;
            SettingsPanel.ApplyFps();
        }

        // 메인 화면도 가게와 같은 배경음. 이어하기로 가게에 들어가면 곡이 끊기지 않고 이어진다(같은 곡이면 다시 틀지 않는다)
        void Start() => Sound.PlayBgm(Bgm.Shop);

        void Update()
        {
            // ESC 로도 설정 창을 닫는다(게임 안과 같은 키)
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool esc = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (settingsOpen && esc) settingsOpen = false;
            else if (cardOpen && esc) CloseCard();
        }

        void OnDestroy() => ClearPreview();

        void OnGUI()
        {
            if (uiFont != null) UiSkin.Font = uiFont;

            DrawBackground();

            // 설정이 떠 있으면 뒤 화면 버튼은 <b>죽여서</b> 그린다. IMGUI 는 먼저 그린 버튼이
            // 클릭을 먼저 받으므로, 뒤에 그리는 설정 창의 가림판으로는 이 버튼들을 막을 수 없다
            GUI.enabled = !settingsOpen && !cardOpen;
            if (page == Page.Home) DrawHome();
            else if (!cardOpen) DrawBreedPage();   // 카드가 뜨면 견종 목록은 숨긴다 — 카드 가장자리가 흐려지며 비쳐 어수선했다
            GUI.enabled = !settingsOpen;
            if (page == Page.Breeds && cardOpen) DrawIntroCard();
            GUI.enabled = true;

            // 설정은 <b>덮개</b>다. 뒤 화면을 어둡게 깔고 그 위에 띄운다 —
            // 별도 페이지로 만들면 어디서 왔는지 기억해 두었다 돌아가야 한다
            if (settingsOpen && SettingsPanel.Draw(closeImage, frameImage)) settingsOpen = false;
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
        bool BoneButton(Rect rect, string label, bool enabled = true, bool fill = false)
        {
            bool hover = rect.Contains(Event.current.mousePosition);

            Color prev = GUI.color;
            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            else if (hover) GUI.color = new Color(1.12f, 1.12f, 1.12f, 1f);

            if (boneImage != null) GUI.DrawTexture(rect, boneImage, fill ? ScaleMode.StretchToFill : ScaleMode.ScaleToFit);
            GUI.color = prev;

            var style = new GUIStyle(UiSkin.Caption)
            {
                fontSize = Mathf.RoundToInt(rect.height * 0.30f),
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = enabled ? UiSkin.Ink : new Color(0.35f, 0.32f, 0.30f);
            GUI.Label(rect, label, style);

            if (!enabled) return false;
            return UiSkin.Click(rect, GUIContent.none, GUIStyle.none);
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

        // 첫 화면 배치 — 사용자가 준 시안(16:9)을 잰 값. 위치는 화면 비율, 크기는 1920x1080 기준 px 에
        // 화면 배율(가로·세로 중 작은 쪽)을 곱한다. 화면비가 달라도 타이틀은 왼쪽 위, 버튼은 오른쪽 아래에
        // 붙어 있고 크기가 같은 비율로 줄어 화면 밖으로 나가지 않는다.

        /// <summary>타이틀(로고만): 왼쪽 위 모서리 위치(화면 비율)와 너비(1920 기준 px). 높이는 그림 비율대로.</summary>
        const float HomeTitleLeft = 0.057f, HomeTitleTop = 0.107f, HomeTitleWidth = 989f;

        /// <summary>버튼 넷: 오른쪽 여백·아래 여백(화면 비율), 한 개 크기와 줄 간격(1920x1080 기준 px).</summary>
        const float HomeButtonsRight = 0.068f, HomeButtonsBottom = 0.104f;
        const float HomeButtonW = 309f, HomeButtonH = 121f, HomeButtonStep = 133f;

        void DrawHome()
        {
            // 타이틀은 왼쪽 위 가게 위로 크게, 버튼은 오른쪽 아래 — 가운데 거리·하늘은 비워 둔다
            float s = UiSkin.ScreenScale;   // 버튼 다섯 개 + 로고가 1080p 에서도 화면 안에 든다(버튼 줄 751px)

            // 타이틀: 아래 작은 가게 그림을 뺀 로고만(UI_TitleLogo). 없으면 원래 타이틀을 쓴다
            Texture2D logo = titleLogo != null ? titleLogo : titleImage;
            if (logo != null)
            {
                float tw = HomeTitleWidth * s, th = tw * logo.height / logo.width;
                GUI.DrawTexture(new Rect(Screen.width * HomeTitleLeft, Screen.height * HomeTitleTop, tw, th), logo, ScaleMode.StretchToFill);
            }

            const int count = 5;
            float width = HomeButtonW * s, height = HomeButtonH * s, step = HomeButtonStep * s;
            float x = Screen.width * (1f - HomeButtonsRight) - width;
            float y = Screen.height * (1f - HomeButtonsBottom) - (step * (count - 1) + height);   // 맨 위 버튼

            // 시안처럼 뼈를 칸에 꽉 채워(조금 납작하게) 그린다
            // 시연 모드(3일, 전시용) — 게임 시작 위. 견종 고르기는 본편과 같은 화면을 쓴다
            if (BoneButton(new Rect(x, y, width, height), "시연 모드", true, true)) { exhibitionPick = true; page = Page.Breeds; }
            y += step;

            if (BoneButton(new Rect(x, y, width, height), "게임 시작", true, true)) { exhibitionPick = false; page = Page.Breeds; }

            bool hasSave = SaveManager.HasSaveFile;
            if (BoneButton(new Rect(x, y + step, width, height), "이어하기", hasSave, true))
            {
                GameStart.Resume();
                SceneManager.LoadScene(shopScene);
            }

            if (BoneButton(new Rect(x, y + step * 2f, width, height), "게임 설정", true, true)) settingsOpen = true;

            if (BoneButton(new Rect(x, y + step * 3f, width, height), "게임 종료", true, true)) Quit();
        }

        // ---- 견종 고르기 ----

        void DrawBreedPage()
        {
            DrawTitleImage(Screen.height * 0.12f, Mathf.Min(150f, Screen.height * 0.16f));

            GUI.Label(new Rect(0f, Screen.height * 0.22f, Screen.width, 26f),
                exhibitionPick ? "시연 모드 (3일): 함께할 강아지를 골라 보자. 3일차에 도그쇼에 나간다"
                               : "함께 지낼 강아지를 골라 보자. 누르면 자세히 볼 수 있다", UiSkin.Caption);

            float width = breeds.Length * CardWidth + (breeds.Length - 1) * Gap;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.5f - CardHeight * 0.42f;

            // 견종이 6종이라 카드 줄이 1280px 이다. 화면이 그보다 좁으면 줄 전체를 가운데 기준으로 줄인다
            float fit = Mathf.Min(1f, (Screen.width - 40f) / width);
            Matrix4x4 before = GUI.matrix;
            if (fit < 1f) GUIUtility.ScaleAroundPivot(new Vector2(fit, fit), new Vector2(Screen.width * 0.5f, y));
            DrawBreeds(x, y);
            GUI.matrix = before;

            // 시작은 소개 카드의 [확인]이 맡는다. 여기는 뒤로만
            float bw = 240f, bh = bw / BoneAspect;
            float by = y + CardHeight + 30f;
            if (BoneButton(new Rect((Screen.width - bw) * 0.5f, by, bw, bh), "뒤로"))
            {
                page = Page.Home;
                picked = -1;
            }
        }

        /// <summary>이름 글자 수 상한(6자). 머리 위 이름표가 이 길이에 맞춰 글자 크기를 줄인다.</summary>
        const int MaxNameLength = Dogs.Dog.MaxNameLength;
        string dogName = "";
        float nameRejectedAt = -10f;

        /// <summary>
        /// 이름에는 글자(한글·영어)와 띄어쓰기만 남긴다. 숫자·기호는 버린다.
        /// 비워 두고 시작하면 견종 이름으로 부른다(<see cref="Dogs.Dog.DisplayName"/>).
        /// </summary>
        static string CleanName(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetter(c) || c == ' ') sb.Append(c);
            return sb.ToString();
        }

        void DrawBreeds(float x, float y)
        {
            for (int i = 0; i < breeds.Length; i++)
            {
                var card = new Rect(x + i * (CardWidth + Gap), y, CardWidth, CardHeight);
                // 그림 카드가 있으면 그것을 깐다(밝은 크림 판이라 글자를 진하게 바꾼다). 없으면 예전 어두운 판
                bool art = breedCardImage != null;
                if (art) GUI.DrawTexture(card, breedCardImage, ScaleMode.StretchToFill);
                else GUI.Box(card, GUIContent.none, UiSkin.Panel_);

                // 고른 카드는 테두리 대신 색판을 깔아 표시한다 — IMGUI 에 테두리만 그리는 수단이 마땅치 않다
                if (picked == i)
                    GUI.Box(new Rect(card.x + 6f, card.y + 6f, card.width - 12f, 26f), "선택중", UiSkin.Tag(UiSkin.Green));

                // 그림 카드는 받침이 높이 50~59% 에 있다 — 발이 받침 위에 오게 사진을 내리고, 글자는 아래 흰 칸(70%~)에 둔다
                float side = art ? 140f : card.width - 56f;
                var portrait = art ? new Rect(card.center.x - side * 0.5f, card.y + 46f, side, side)
                                   : new Rect(card.x + 28f, card.y + 38f, side, side);
                if (breeds[i].portrait != null) GUI.DrawTexture(portrait, breeds[i].portrait, ScaleMode.ScaleToFit);
                else GUI.Box(portrait, GUIContent.none, UiSkin.Tag(UiSkin.Cream));

                var name = new GUIStyle(UiSkin.Caption) { fontSize = 18 };
                if (art) name.normal.textColor = UiSkin.Ink;
                float nameY = art ? card.y + 190f : portrait.yMax + 6f;
                GUI.Label(new Rect(card.x, nameY, card.width, 26f), breeds[i].nameKo, name);

                // 몸집과 속도. 견종을 고르는 것이 겉모습만의 일이 아니라는 걸 숫자로 보여 준다
                float tagY = art ? card.y + 224f : portrait.yMax + 34f;
                float tagW = (card.width - 28f) * 0.5f;
                GUI.Box(new Rect(card.x + 10f, tagY, tagW, 24f),
                    "몸길이 " + breeds[i].bodyLength.ToString("0.0") + "m", UiSkin.Tag(UiSkin.Cream));
                GUI.Box(new Rect(card.x + 18f + tagW, tagY, tagW, 24f),
                    "달리기 " + breeds[i].runSpeed.ToString("0.0"), UiSkin.Tag(UiSkin.Sky));

                // 소개 글은 두세 줄이라 카드 아래 끝까지 쓴다(예전 한 줄 반짜리 칸에서는 셋째 줄이 잘렸다)
                var blurb = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.UpperCenter, fontSize = 12 };
                if (art) blurb.normal.textColor = UiSkin.Ink;
                GUI.Label(new Rect(card.x + 12f, tagY + 29f, card.width - 24f, card.yMax - tagY - 36f), breeds[i].blurb, blurb);

                if (UiSkin.Click(new Rect(card.x, card.y, card.width, card.height), GUIContent.none, GUIStyle.none))
                    OpenCard(i);
            }
        }

        // ---- 소개 카드 ----
        //
        // 견종을 누르면 뜨는 상태창. 받침 위에 <b>실제 게임 속 3D 강아지</b>를 띄우고(작은 초상화 그림은 흐릿했다),
        // 오른쪽 판에 등급·친화력·건강·성격·소개를 적는다. 이름을 짓고 [확인]을 누르면 바로 가게로 간다.
        // 그림이 아직 없으면(바르코로 뽑기 전) 같은 자리에 단색 판을 그린다.

        bool cardOpen;
        GameObject previewRoot;
        Camera previewCamera;
        RenderTexture previewTexture;
        float cardOpenedAt;

        /// <summary>미리보기 강아지(돌리는 대상)와 지금 돌린 각도.</summary>
        Transform previewDog;
        float previewYaw = 200f;
        bool spinning;

        /// <summary>왼쪽 마우스를 누른 채 좌우로 끌면 강아지가 돈다. 한 화면 너비만큼 끌면 한 바퀴 반쯤.</summary>
        void SpinPreview(Rect area)
        {
            if (previewDog == null) return;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition)) { spinning = true; e.Use(); }
            else if (e.type == EventType.MouseUp && e.button == 0) spinning = false;
            else if (e.type == EventType.MouseDrag && spinning)
            {
                previewYaw -= e.delta.x * 540f / Mathf.Max(1f, Screen.width);
                previewDog.localRotation = Quaternion.Euler(0f, previewYaw, 0f);
                e.Use();
            }
        }

        /// <summary>미리보기 강아지를 세울 곳. 메뉴 씬의 카메라에 안 잡히게 멀리 둔다.</summary>
        static readonly Vector3 PreviewSpot = new Vector3(0f, -500f, 0f);

        void OpenCard(int breed)
        {
            picked = breed;
            cardOpen = true;
            cardOpenedAt = Time.unscaledTime;
            dogName = "";
            SpawnPreview(breed);
        }

        void CloseCard()
        {
            cardOpen = false;
            Input.imeCompositionMode = IMECompositionMode.Auto;
            ClearPreview();
        }

        /// <summary>
        /// 견종 프리팹을 띄워 전용 카메라로 찍는다. 게임 로직(이동·능력치·길찾기)은 메뉴 씬에 매니저가 없어서
        /// 깨어나자마자 오류를 내므로, <b>꺼진 부모 밑에 만들어</b> Awake 가 돌기 전에 떼어 내고 애니메이터만 남긴다.
        /// </summary>
        void SpawnPreview(int breed)
        {
            ClearPreview();
            if (breed < 0 || breed >= breedPrefabs.Length || breedPrefabs[breed] == null) return;

            var holder = new GameObject("BreedPreview");
            holder.SetActive(false);
            holder.transform.position = PreviewSpot;
            GameObject dog = Instantiate(breedPrefabs[breed], holder.transform);
            dog.transform.localPosition = Vector3.zero;
            dog.transform.localRotation = Quaternion.Euler(0f, 200f, 0f);   // 카메라 쪽을 보며 살짝 비스듬히(3/4)

            // 뒤에서부터 뗀다 — 뒤에 붙은 스크립트가 앞의 것을 [RequireComponent] 로 잡고 있어서 앞에서부터 떼면 거부된다
            MonoBehaviour[] scripts = dog.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = scripts.Length - 1; i >= 0; i--) DestroyImmediate(scripts[i]);
            foreach (UnityEngine.AI.NavMeshAgent agent in dog.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) DestroyImmediate(agent);
            foreach (Collider col in dog.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
            holder.SetActive(true);
            previewRoot = holder;
            previewDog = dog.transform;
            previewYaw = 200f;

            // 몸 크기에 맞춰 카메라를 둔다 — 셰퍼드와 치와와가 받침 위에서 비슷한 크기로 보이게
            Bounds b = new Bounds(holder.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in dog.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);

            previewTexture = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var camGo = new GameObject("BreedPreviewCamera");
            camGo.transform.SetParent(holder.transform, false);
            previewCamera = camGo.AddComponent<Camera>();
            var urp = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 투명 — 받침·배경 그림 위에 얹는다
            previewCamera.targetTexture = previewTexture;
            previewCamera.fieldOfView = 26f;
            previewCamera.nearClipPlane = 0.05f;
            previewCamera.farClipPlane = 50f;
            Vector3 look = new Vector3(b.center.x, b.min.y + b.size.y * 0.45f, b.center.z);
            float dist = size * 0.5f / Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
            camGo.transform.position = look + new Vector3(0f, 0.32f, -1f).normalized * dist;
            camGo.transform.LookAt(look);

            // 메뉴 씬 빛이 어떻든 털이 예쁘게 보이게 앞쪽 위에서 비추는 빛 하나
            var lightGo = new GameObject("BreedPreviewLight");
            lightGo.transform.SetParent(holder.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(35f, 20f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.9f);
        }

        void ClearPreview()
        {
            if (previewRoot != null) Destroy(previewRoot);
            previewRoot = null;
            previewCamera = null;
            if (previewTexture != null) { previewTexture.Release(); Destroy(previewTexture); }
            previewTexture = null;
        }

        void DrawIntroCard()
        {
            Breed breed = picked >= 0 && picked < breeds.Length ? breeds[picked] : null;
            if (breed == null) { cardOpen = false; return; }
            EnsureCardStyles();

            // 뒤를 어둡게 덮는다
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Solid(new Color(0.12f, 0.08f, 0.05f, 0.45f)));

            // 정사각 카드. 열릴 때 살짝 커지며 나타난다
            float t = Mathf.Clamp01((Time.unscaledTime - cardOpenedAt) / 0.22f);
            float pop = Mathf.Lerp(0.92f, 1f, 1f - (1f - t) * (1f - t));
            float S = Mathf.Min(Screen.height * 0.94f, Screen.width * 0.66f) * pop;
            var card = new Rect((Screen.width - S) * 0.5f, (Screen.height - S) * 0.5f, S, S);
            Rect R(float x0, float y0, float x1, float y1) => new Rect(card.x + x0 * S, card.y + y0 * S, (x1 - x0) * S, (y1 - y0) * S);

            if (cardBackground != null) GUI.DrawTexture(card, cardBackground, ScaleMode.ScaleAndCrop);
            else GUI.Box(card, GUIContent.none, UiSkin.Panel_);

            // 받침 + 강아지
            Rect pedestal = R(0.06f, 0.62f, 0.68f, 0.92f);
            if (pedestalImage != null) GUI.DrawTexture(pedestal, pedestalImage, ScaleMode.ScaleToFit);
            else GUI.DrawTexture(R(0.12f, 0.74f, 0.62f, 0.86f), Solid(new Color(0.85f, 0.7f, 0.5f, 0.9f)));
            Rect previewRect = R(0.02f, 0.24f, 0.72f, 0.94f);
            if (previewTexture != null) GUI.DrawTexture(previewRect, previewTexture, ScaleMode.ScaleToFit, true);
            // 돌리기 칸은 [확인] 버튼(아래 0.835~) 위까지만 — 겹치면 누르는 순간 돌리기가 클릭을 가져가 버튼이 안 눌렸다
            SpinPreview(R(0.02f, 0.24f, 0.72f, 0.82f));

            // NEW 딱지 — 살짝 두근거린다
            if (newBadge != null)
            {
                float beat = 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.025f;
                Rect nb = R(0.02f, 0.03f, 0.40f, 0.22f);
                nb = new Rect(nb.center.x - nb.width * beat * 0.5f, nb.center.y - nb.height * beat * 0.5f, nb.width * beat, nb.height * beat);
                GUI.DrawTexture(nb, newBadge, ScaleMode.ScaleToFit);
            }

            // 오른쪽 정보판
            Rect panel = R(0.665f, 0.10f, 0.985f, 0.76f);
            GUI.DrawTexture(panel, RoundedPanel(), ScaleMode.StretchToFill);
            float px = panel.x + S * 0.025f, pw = panel.width - S * 0.05f;
            float y = panel.y + S * 0.022f, line = S * 0.05f;

            Icon(new Rect(px, y, line, line), iconPaw, "🐾");
            GUI.Label(new Rect(px + line * 1.15f, y - S * 0.004f, pw - line, line), breed.nameKo, cardTitle);
            y += line * 1.25f;

            float tagW = S * 0.1f;
            GUI.DrawTexture(new Rect(px, y, tagW, line * 0.75f), RoundedPill(), ScaleMode.StretchToFill);
            GUI.Label(new Rect(px, y, tagW, line * 0.75f), breed.grade, cardTag);
            y += line * 1.05f;
            Divider(px, y, pw); y += S * 0.018f;

            // 능력치 별 — 별 3개가 기본(0%), +15% 가 5개, 반별은 0.5. 건강·청결은 견종 보정이 없어 언제나 3개
            float row = line * 0.84f;
            StarRow(ref y, px, pw, row, 0, "건강", 0f);
            StarRow(ref y, px, pw, row, 1, "청결", 0f);
            StarRow(ref y, px, pw, row, 2, "미모", Dogs.BreedTraits.BonusPercent(picked, Data.GrowthAxis.Beauty));
            StarRow(ref y, px, pw, row, 3, "훈련도", Dogs.BreedTraits.BonusPercent(picked, Data.GrowthAxis.Training));
            StarRow(ref y, px, pw, row, 4, "어질리티", Dogs.BreedTraits.BonusPercent(picked, Data.GrowthAxis.Agility));
            y += S * 0.006f;
            Divider(px, y, pw); y += S * 0.014f;

            // 특성 — 한 줄 설명 + 실제 보정 수치
            GUI.Label(new Rect(px, y, pw, line * 0.7f), "특성", cardAccentLeft);
            y += line * 0.72f;
            // 「」 는 글꼴에 없어 빈칸으로 나왔다. 줄은 "~지만" 뒤에서 직접 끊는다(그냥 두면 낱말 가운데서 잘렸다)
            GUI.Label(new Rect(px, y, pw, S * 0.06f), Dogs.BreedTraits.TraitText(picked).Replace("지만 ", "지만\n"), cardBody);
            y += S * 0.062f;
            GUI.Label(new Rect(px, y, pw, line * 0.6f),
                "미모 " + Dogs.BreedTraits.Format(Dogs.BreedTraits.BonusPercent(picked, Data.GrowthAxis.Beauty))
                + " / 훈련 " + Dogs.BreedTraits.Format(Dogs.BreedTraits.BonusPercent(picked, Data.GrowthAxis.Training))
                + " / 어질리티 " + Dogs.BreedTraits.Format(Dogs.BreedTraits.BonusPercent(picked, Data.GrowthAxis.Agility)), cardSmallNote);
            y += line * 0.75f;
            Divider(px, y, pw); y += S * 0.016f;

            // 이름 — 비워 두면 견종 이름으로 부른다
            GUI.Label(new Rect(px, y, pw * 0.25f, line * 0.8f), "이름", cardText);
            var field = new Rect(px + pw * 0.24f, y - S * 0.004f, pw * 0.76f, line * 0.85f);
            GUI.SetNextControlName("DogName");
            bool typing = GUI.GetNameOfFocusedControl() == "DogName";
            // 한글을 치려면 IME 가 켜져 있어야 한다. Auto 는 창에 따라 안 켜질 때가 있어 입력칸에 있는 동안만 강제로 켠다
            Input.imeCompositionMode = typing ? IMECompositionMode.On : IMECompositionMode.Auto;
            string typed = GUI.TextField(field, dogName, MaxNameLength, CardFieldStyle());
            dogName = CleanName(typed);
            if (dogName != typed) nameRejectedAt = Time.unscaledTime;
            if (string.IsNullOrEmpty(dogName) && !typing)
                GUI.Label(field, "비워 두면 " + breed.nameKo, cardHint);
            // 숫자·기호를 쳤을 때 아무 반응이 없으면 키보드가 고장 난 줄 안다
            if (Time.unscaledTime - nameRejectedAt < 2f)
                GUI.Label(new Rect(field.x, field.yMax, field.width, line * 0.6f), "한글과 영어만 쓸 수 있다", cardSmallNote);

            // 확인 — 바로 가게로
            Rect ok = R(0.33f, 0.835f, 0.67f, 0.945f);
            bool hover = ok.Contains(Event.current.mousePosition);
            Color keep = GUI.color;
            if (hover) GUI.color = new Color(1.1f, 1.1f, 1.1f, 1f);
            if (confirmButton != null) GUI.DrawTexture(ok, confirmButton, ScaleMode.StretchToFill);
            else GUI.DrawTexture(ok, RoundedPill(new Color(1f, 0.8f, 0.25f)), ScaleMode.StretchToFill);
            GUI.color = keep;
            float iconS = ok.height * 0.5f;
            Icon(new Rect(ok.center.x - ok.width * 0.2f - iconS * 0.5f, ok.center.y - iconS * 0.5f, iconS, iconS), iconPaw, "");
            var okStyle = new GUIStyle(cardTitle) { fontSize = Mathf.RoundToInt(ok.height * 0.4f), alignment = TextAnchor.MiddleCenter };
            okStyle.normal.textColor = new Color(0.36f, 0.2f, 0.08f);
            GUI.Label(new Rect(ok.x + ok.width * 0.08f, ok.y, ok.width, ok.height), "확인", okStyle);
            if (UiSkin.Click(ok, GUIContent.none, GUIStyle.none))
            {
                if (exhibitionPick) GameStart.NewExhibition(picked, dogName.Trim());
                else GameStart.NewGame(picked, dogName.Trim());
                SceneManager.LoadScene(shopScene);
                return;
            }

            // 다른 강아지 보기 — [확인]과 같은 알약을 빨간색으로. 회색 반투명 판은 눈에 띄지 않았다
            Rect back = R(0.705f, 0.85f, 0.965f, 0.93f);
            bool backHover = back.Contains(Event.current.mousePosition);
            if (backHover) GUI.color = new Color(1.1f, 1.1f, 1.1f, 1f);
            if (otherButton != null) GUI.DrawTexture(back, otherButton, ScaleMode.StretchToFill);
            else GUI.DrawTexture(back, RoundedPill(new Color(0.88f, 0.22f, 0.22f)), ScaleMode.StretchToFill);
            GUI.color = keep;
            var backStyle = new GUIStyle(cardTitle) { fontSize = Mathf.RoundToInt(back.height * 0.36f), alignment = TextAnchor.MiddleCenter };
            backStyle.normal.textColor = Color.white;
            GUI.Label(back, "다른 강아지", backStyle);
            if (UiSkin.Click(back, GUIContent.none, GUIStyle.none)) { CloseCard(); return; }

            // 카드 밖을 누르면 닫힌다. 카드 안 빈 곳 클릭은 삼킨다 — 맨 마지막에 깐다(위 버튼이 먼저 받게)
            if (Event.current.type == EventType.MouseDown && !card.Contains(Event.current.mousePosition)) { CloseCard(); Event.current.Use(); return; }
            GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);
        }

        /// <summary>아이콘 + 이름 + 별 5칸(0.5 단위, 반별은 왼쪽 반만 칠한다).</summary>
        void StarRow(ref float y, float px, float pw, float row, int iconIndex, string label, float bonusPercent)
        {
            Texture2D icon = iconIndex < statIcons.Length ? statIcons[iconIndex] : null;
            Icon(new Rect(px, y, row * 0.9f, row * 0.9f), icon, "");
            GUI.Label(new Rect(px + row, y, pw * 0.4f, row * 0.9f), label, cardRowLabel);

            float stars = Dogs.BreedTraits.Stars(bonusPercent);
            // 이름 칸("어질리티")과 겹치지 않게 별을 조금 작게
            float size = row * 0.64f, step = size * 1.04f;
            float sx = px + pw - step * 5f;
            float sy = y + (row * 0.9f - size) * 0.5f;
            for (int i = 0; i < 5; i++)
            {
                var r = new Rect(sx + i * step, sy, size, size);
                float fill = Mathf.Clamp01(stars - i);   // 1 = 꽉, 0.5 = 반, 0 = 빈칸
                DrawStar(r, new Color(0.3f, 0.24f, 0.2f, 0.55f), 1f);   // 빈 칸(어둡게)
                if (fill > 0f) DrawStar(r, Color.white, fill);
            }
            y += row;
        }

        void DrawStar(Rect r, Color tint, float fill)
        {
            Color keep = GUI.color;
            GUI.color = tint;
            if (iconStar != null)
                GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, r.width * fill, r.height), iconStar, new Rect(0f, 0f, fill, 1f));
            else
            {
                GUI.BeginGroup(new Rect(r.x, r.y, r.width * fill, r.height));
                GUI.Label(new Rect(0f, 0f, r.width, r.height), "★", cardStarGlyph);
                GUI.EndGroup();
            }
            GUI.color = keep;
        }

        void Icon(Rect r, Texture2D tex, string fallback)
        {
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            else if (!string.IsNullOrEmpty(fallback)) GUI.Label(r, fallback, cardText);
        }

        void Divider(float x, float y, float w) => GUI.DrawTexture(new Rect(x, y, w, 1.5f), Solid(new Color(1f, 1f, 1f, 0.25f)));

        // ---- 카드 스타일·바탕 그림(코드로 만든다) ----

        GUIStyle cardTitle, cardTag, cardText, cardRowLabel, cardAccentLeft, cardSmallNote, cardStarGlyph, cardBody, cardHint, cardField;
        float stylesFor = -1f;
        readonly System.Collections.Generic.Dictionary<Color, Texture2D> solids = new System.Collections.Generic.Dictionary<Color, Texture2D>();
        readonly System.Collections.Generic.Dictionary<Color, Texture2D> pills = new System.Collections.Generic.Dictionary<Color, Texture2D>();
        Texture2D panelTex;

        void EnsureCardStyles()
        {
            float S = Mathf.Min(Screen.height * 0.94f, Screen.width * 0.66f);
            if (Mathf.Abs(stylesFor - S) < 1f && cardTitle != null) return;
            stylesFor = S;
            Color cream = new Color(1f, 0.97f, 0.9f);
            GUIStyle Make(float size, TextAnchor anchor, Color color)
            {
                var st = new GUIStyle(UiSkin.Caption) { fontSize = Mathf.RoundToInt(S * size), alignment = anchor, wordWrap = true };
                st.normal.textColor = color;
                return st;
            }
            cardTitle = Make(0.036f, TextAnchor.MiddleLeft, cream);
            cardTag = Make(0.02f, TextAnchor.MiddleCenter, new Color(0.35f, 0.22f, 0.2f));
            cardText = Make(0.022f, TextAnchor.MiddleLeft, cream);
            cardRowLabel = Make(0.02f, TextAnchor.MiddleLeft, cream);
            cardRowLabel.wordWrap = false;
            cardAccentLeft = Make(0.022f, TextAnchor.MiddleLeft, new Color(1f, 0.82f, 0.35f));
            cardSmallNote = Make(0.0145f, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.75f, 0.85f));
            cardSmallNote.wordWrap = false;
            cardStarGlyph = Make(0.026f, TextAnchor.MiddleCenter, Color.white);
            cardBody = Make(0.019f, TextAnchor.UpperLeft, cream);
            cardHint = Make(0.019f, TextAnchor.MiddleCenter, new Color(0.55f, 0.52f, 0.5f));
            cardField = null;
        }

        GUIStyle CardFieldStyle()
        {
            if (cardField != null) return cardField;
            cardField = new GUIStyle(GUI.skin.textField) { fontSize = cardText.fontSize, alignment = TextAnchor.MiddleCenter, font = UiSkin.Caption.font };
            cardField.padding = new RectOffset(8, 8, 2, 2);
            return cardField;
        }

        Texture2D Solid(Color c)
        {
            if (solids.TryGetValue(c, out Texture2D tex) && tex != null) return tex;
            tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, c);
            tex.Apply();
            solids[c] = tex;
            return tex;
        }

        /// <summary>모서리가 둥근 반투명 갈색 판(정보판). 늘려 그려도 모서리가 크게 안 뭉개지게 넉넉한 크기로 만든다.</summary>
        Texture2D RoundedPanel()
        {
            if (panelTex != null) return panelTex;
            panelTex = RoundRect(320, 560, 26f, new Color(0.27f, 0.2f, 0.17f, 0.86f), new Color(1f, 0.93f, 0.82f, 0.85f), 3f);
            return panelTex;
        }

        Texture2D RoundedPill() => RoundedPill(new Color(0.9f, 0.8f, 0.82f));

        Texture2D RoundedPill(Color c)
        {
            if (pills.TryGetValue(c, out Texture2D tex) && tex != null) return tex;
            tex = RoundRect(128, 32, 16f, c, Color.clear, 0f);
            pills[c] = tex;
            return tex;
        }

        static Texture2D RoundRect(int w, int h, float radius, Color fill, Color border, float borderWidth)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 둥근 사각형까지의 거리(안쪽이 음수)
                    float qx = Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - radius);
                    float qy = Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - radius);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    Color c = borderWidth > 0f && d > -borderWidth ? border : fill;
                    c.a *= a;
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
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
