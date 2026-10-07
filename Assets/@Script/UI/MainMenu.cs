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

            // ---- 소개 카드(고르면 뜨는 상태창) ----
            // 견종별 성격·특성은 아직 구상 중이다. 지금 값은 <b>보여 주기용 자리</b>이고 게임 수치에는 쓰이지 않는다 —
            // 성격 체계가 정해지면 여기 값을 그쪽에서 읽게 바꾼다.

            /// <summary>등급 꼬리표(일반·희귀 …).</summary>
            public string grade = "일반";
            [Range(0f, 1f)] public float affinity = 0.5f;   // 친화력
            [Range(0f, 1f)] public float health = 0.5f;     // 건강
            public string personality = "활발함";
            [TextArea(2, 4)] public string description = "";
        }

        [Header("강아지 소개 카드 — 견종을 누르면 뜬다")]
        [Tooltip("DogManager.breedPrefabs 와 같은 순서. 카드 받침 위에 실제 3D 강아지를 띄운다")]
        [SerializeField] GameObject[] breedPrefabs = new GameObject[0];
        [SerializeField] Texture2D cardBackground;   // 흐린 가게 안 + 구름 가장자리
        [SerializeField] Texture2D pedestalImage;    // 둥근 나무 받침
        [SerializeField] Texture2D newBadge;
        [SerializeField] Texture2D confirmButton;    // 노란 알약 버튼(글자 없음)
        [SerializeField] Texture2D iconHeart, iconBone, iconStar, iconPaw;

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
                "함께 지낼 강아지를 골라 보자 — 누르면 자세히 볼 수 있다", UiSkin.Caption);

            float width = breeds.Length * CardWidth + (breeds.Length - 1) * Gap;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.5f - CardHeight * 0.42f;

            DrawBreeds(x, y);

            // 시작은 소개 카드의 [확인]이 맡는다. 여기는 뒤로만
            float bw = 240f, bh = bw / BoneAspect;
            float by = y + CardHeight + 30f;
            if (BoneButton(new Rect((Screen.width - bw) * 0.5f, by, bw, bh), "뒤로"))
            {
                page = Page.Home;
                picked = -1;
            }
        }

        /// <summary>이름표에 들어갈 만큼. 한글 8자가 이름표 가운데에 한 줄로 들어간다.</summary>
        const int MaxNameLength = 8;
        string dogName = "";

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
            if (previewTexture != null) GUI.DrawTexture(R(0.02f, 0.24f, 0.72f, 0.94f), previewTexture, ScaleMode.ScaleToFit, true);

            // NEW 딱지 — 살짝 두근거린다
            if (newBadge != null)
            {
                float beat = 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.025f;
                Rect nb = R(0.02f, 0.03f, 0.40f, 0.22f);
                nb = new Rect(nb.center.x - nb.width * beat * 0.5f, nb.center.y - nb.height * beat * 0.5f, nb.width * beat, nb.height * beat);
                GUI.DrawTexture(nb, newBadge, ScaleMode.ScaleToFit);
            }

            // 오른쪽 정보판
            Rect panel = R(0.665f, 0.10f, 0.985f, 0.66f);
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

            StatRow(ref y, px, pw, line, iconHeart, "친화력", breed.affinity);
            StatRow(ref y, px, pw, line, iconBone, "건강", breed.health);
            Icon(new Rect(px, y, line * 0.8f, line * 0.8f), iconStar, "★");
            GUI.Label(new Rect(px + line, y, pw * 0.4f, line * 0.8f), "성격", cardText);
            GUI.Label(new Rect(px, y, pw, line * 0.8f), breed.personality, cardAccent);
            y += line * 1.05f;
            Divider(px, y, pw); y += S * 0.016f;

            string desc = string.IsNullOrEmpty(breed.description) ? breed.blurb.Replace("\n", " ") : breed.description;
            GUI.Label(new Rect(px, y, pw, S * 0.13f), desc, cardBody);
            y += S * 0.135f;

            // 이름 — 비워 두면 견종 이름으로 부른다
            GUI.Label(new Rect(px, y, pw * 0.25f, line * 0.8f), "이름", cardText);
            var field = new Rect(px + pw * 0.24f, y - S * 0.004f, pw * 0.76f, line * 0.85f);
            GUI.SetNextControlName("DogName");
            dogName = GUI.TextField(field, dogName, MaxNameLength, CardFieldStyle());
            if (string.IsNullOrEmpty(dogName) && GUI.GetNameOfFocusedControl() != "DogName")
                GUI.Label(field, "이름을 지어 주자", cardHint);

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
            if (GUI.Button(ok, GUIContent.none, GUIStyle.none))
            {
                GameStart.NewGame(picked, dogName.Trim());
                SceneManager.LoadScene(shopScene);
                return;
            }

            // 다른 강아지 보기
            Rect back = R(0.765f, 0.86f, 0.975f, 0.92f);
            GUI.DrawTexture(back, RoundedPill(new Color(0.55f, 0.52f, 0.5f, 0.55f)), ScaleMode.StretchToFill);
            GUI.Label(back, "다른 강아지", cardBackStyle);
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) { CloseCard(); return; }

            // 카드 밖을 누르면 닫힌다. 카드 안 빈 곳 클릭은 삼킨다 — 맨 마지막에 깐다(위 버튼이 먼저 받게)
            if (Event.current.type == EventType.MouseDown && !card.Contains(Event.current.mousePosition)) { CloseCard(); Event.current.Use(); return; }
            GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);
        }

        void StatRow(ref float y, float px, float pw, float line, Texture2D icon, string label, float value)
        {
            Icon(new Rect(px, y, line * 0.8f, line * 0.8f), icon, "");
            GUI.Label(new Rect(px + line, y, pw * 0.35f, line * 0.8f), label, cardText);
            float bx = px + pw * 0.40f, bw = pw * 0.36f, bh = line * 0.3f;
            var track = new Rect(bx, y + line * 0.25f, bw, bh);
            GUI.DrawTexture(track, RoundedPill(new Color(1f, 1f, 1f, 0.22f)), ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(track.x, track.y, Mathf.Max(bh, bw * Mathf.Clamp01(value)), bh), RoundedPill(new Color(0.94f, 0.78f, 0.36f)), ScaleMode.StretchToFill);
            GUI.Label(new Rect(px, y, pw, line * 0.8f), value < 0.34f ? "낮음" : value < 0.67f ? "보통" : "높음", cardValue);
            y += line * 1.0f;
        }

        void Icon(Rect r, Texture2D tex, string fallback)
        {
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            else if (!string.IsNullOrEmpty(fallback)) GUI.Label(r, fallback, cardText);
        }

        void Divider(float x, float y, float w) => GUI.DrawTexture(new Rect(x, y, w, 1.5f), Solid(new Color(1f, 1f, 1f, 0.25f)));

        // ---- 카드 스타일·바탕 그림(코드로 만든다) ----

        GUIStyle cardTitle, cardTag, cardText, cardValue, cardAccent, cardBody, cardHint, cardField, cardBackStyle;
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
            cardValue = Make(0.021f, TextAnchor.MiddleRight, cream);
            cardAccent = Make(0.024f, TextAnchor.MiddleRight, new Color(1f, 0.82f, 0.35f));
            cardBody = Make(0.019f, TextAnchor.UpperLeft, cream);
            cardHint = Make(0.019f, TextAnchor.MiddleCenter, new Color(0.55f, 0.52f, 0.5f));
            cardBackStyle = Make(0.019f, TextAnchor.MiddleCenter, new Color(0.25f, 0.22f, 0.2f));
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
