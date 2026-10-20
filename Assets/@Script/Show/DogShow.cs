using System.Collections;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using DogShop.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Show
{
    /// <summary>
    /// D30 도그쇼. 30일차 아침이 되면 가게를 열지 않고 <b>바로 쇼</b>다 — 화면 가운데
    /// "도그쇼 시작" 버튼을 누르면 내 강아지가 무대로 옮겨지고 미니게임 넷을 한다.
    ///
    /// ① 미모 심사 — 줄어드는 링이 목표 원에 겹칠 때 Space 로 포즈 (5회)
    /// ② 훈련 심사 — 심사위원이 말한 물건 카드를 골라 지시, 강아지가 물어 온다 (5회)
    /// ③ 어질리티 — 달리는 강아지를 Space 로 점프시켜 허들 5개를 넘긴다
    /// ④ 장애물 달리기 — WASD 로 A-프레임, 터널, 도그 워크, 위브, 시소, 테이블을 순서대로 지난다(<see cref="ObstacleCourse"/>)
    ///
    /// <b>스탯이 결과를 정하고 실력은 거든다.</b> 미모가 높으면 포즈 판정 구간이 넓고, 훈련도가
    /// 높으면 엉뚱한 물건을 덜 물어 오고, 어질리티가 ③ 의 달리기 속도·점프 높이·체공 시간을,
    /// 셋의 평균(종합 스탯)이 ④ 의 달리기 속도를 정한다. 네 판의 가중 평균(미모 20 / 훈련 25 / 어질리티 25 / 장애물 30%)이 100점 만점 총점이고,
    /// 챔피언십 순위는 이 총점으로만 정한다(<see cref="ChampionshipManager.SetShowResult"/>).
    ///
    /// 무대는 가게에서 멀리 떨어진 곳에 미리 지어 둔 <see cref="stage"/> 이고 전용 카메라로 비춘다.
    /// 쇼 동안 플레이어 조작·HUD·게임 시계는 꺼 둔다. 무인 측정 중에는 쇼를 건너뛴다(배수 1).
    /// </summary>
    public class DogShow : MonoBehaviour
    {
        [Header("무대")]
        [SerializeField] Transform stage;
        [SerializeField] Camera showCamera;
        [SerializeField] Transform posePoint;      // 미모 심사 단상
        [SerializeField] Transform fetchStart;     // 훈련 심사 출발점(주인 옆)
        [SerializeField] Transform itemTable;      // 물건이 놓인 탁자
        [SerializeField] Transform laneStart;      // 어질리티 출발
        [SerializeField] Transform laneEnd;        // 어질리티 도착
        [SerializeField] Transform[] hurdles = new Transform[0];
        [SerializeField] Transform[] itemPedestals = new Transform[0];   // 훈련 심사 물건 받침대

        [Header("미모 심사 링 그림(바르코). 비우면 코드로 그린 흰 링")]
        [SerializeField] Texture2D ringArt;     // 줄어드는 링
        [SerializeField] Texture2D centerArt;   // 목표 원 안쪽 메달

        [Header("④ 장애물 달리기")]
        [SerializeField] ObstacleCourse course;

        [Header("훈련 심사 물건(상품 이름)")]
        [SerializeField] string[] fetchItemNames = { "장난감", "뼈다귀 장난감", "고기 인형", "목줄", "강아지껌" };

        /// <summary>스탯을 0~1 로 펼 때 쓰는 기준. 46차 측정에서 30일 최적 플레이가 미모·훈련도 각 220 안팎이었다.</summary>
        const float StatFull = 240f;

        /// <summary>미모 심사 판 수. 시연은 쇼만 3분 가까이 걸려 체험 시간의 절반을 먹었다 — 종목 넷은 그대로 두고 판 수만 줄인다.</summary>
        static int PoseRounds => Exhibition.Active ? 3 : 5;
        /// <summary>물어오기 판 수. 한 판이 강아지가 다녀오는 데 10초 넘게 걸려 5판은 늘어졌다 — 3판으로(시연은 2판).</summary>
        static int FetchRounds => Exhibition.Active ? 2 : 3;

        public static DogShow Instance { get; private set; }

        /// <summary>쇼가 진행 중이다(시작 버튼 대기 포함).</summary>
        public static bool Running { get; private set; }

        enum Phase { None, Waiting, Intro, Pose, Fetch, AgilitySpec, Agility, CourseGuide, Course, Result, Guide }
        bool guideReady;
        // 종목 시작 전 안내창(①~③). ④ 는 장애물 목록을 따로 그린다(DrawCourseGuide)
        string guideTitle = "";
        string[] guideLines = new string[0];
        Phase phase = Phase.None;

        Dog hero;
        float beautyN, trainingN, agilityN, totalN;   // 0~1
        float poseScore, fetchScore, agilityScore, courseScore;   // 0~1
        /// <summary>총점 가중치(합 1). 장애물 달리기가 가장 길고 손이 많이 가서 비중을 크게 뒀다.</summary>
        const float PoseWeight = 0.20f, FetchWeight = 0.25f, AgilityWeight = 0.25f, CourseWeight = 0.30f;

        // 화면 글
        string headline = "";
        string subline = "";
        float flashUntil;
        string flashText = "";
        Color flashColor = Color.white;

        // ① 미모
        float ringRadius;     // 0~1, 줄어든다
        float targetRadius = 0.32f;   // 미모로 정한다(PoseGame)
        float perfectHalf;    // PERFECT 띠 반폭 — 미모가 두껍게 한다
        float goodHalf;       // GOOD 띠 반폭 = PERFECT 반폭 + 고정 폭
        bool posePressed;
        Texture2D ringTex, dotTex, goodTex, perfectTex;

        // ② 훈련
        List<int> fetchItems = new List<int>();
        int requested = -1;
        int[] cards = new int[4];
        int chosenCard = -1;
        int broughtItem = -1;
        bool awaitingChoice;

        // ③ 어질리티
        float runSpeed, jumpHeight, airTime;
        bool jumpRequested;
        float jumpStart = -10f;
        int cleared;

        readonly List<Behaviour> silenced = new List<Behaviour>();
        GUIStyle bigStyle, midStyle, smallStyle;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Running = false;
            if (showCamera != null) showCamera.enabled = false;
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; Running = false; }
        }

        void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted += CheckDay;
            CheckDay();
        }

        void CheckDay()
        {
            if (phase != Phase.None) return;
            CheckNotice();
            GameManager g = GameManager.Instance;
            TimeManager t = TimeManager.Instance;
            if (g == null || t == null || t.IsDayOver) return;
            if (!ChampionshipManager.Instance.IsFinalDay(g.Day)) return;

            // 시연 모드는 3일차 아침에 바로 쇼로 가지 않는다 — 마지막 준비(밥·목욕·훈련)를 하고 [도그쇼 시작]으로 들어간다
            if (Exhibition.Active) return;

            // 무인 측정은 사람이 못 누르니 건너뛴다 — 배수 1(=스탯 그대로)로 하루를 마친다
            if (Debugging.MeasurementMode.Instance != null && Debugging.MeasurementMode.Instance.Active) return;

            // 건강이 모자라면 아침에 바로 무대로 가지 않는다 — 돌볼 시간을 주고, 침대·21시 안내에서 쇼를 연다
            string reason;
            if (!Exhibition.CanEnterShow(out reason))
            {
                OpenNotice("오늘은 도그쇼 날", reason,
                           "밥, 약으로 건강을 " + ChampionshipManager.HealthGate + " 이상 올린 뒤 침대에서 [도그쇼 시작]",
                           "건강을 못 올리면 출전하지 못한 채 챔피언십이 끝난다");
                return;
            }

            Begin();
        }

        // ---- 도그쇼 참가 조건 안내 ----

        /// <summary>본편에서 참가 조건을 미리 알려 주는 날. 쇼 날(30일차)까지 열흘 — 건강을 챙길 시간이 있다.</summary>
        public const int NoticeDay = 20;

        int noticeShownDay = -1;   // 같은 날 두 번 띄우지 않는다
        string noticeTitle;
        string[] noticeLines;
        GUIStyle noticeTitleStyle, noticeBodyStyle;

        void CheckNotice()
        {
            GameManager g = GameManager.Instance;
            if (g == null || Exhibition.Active || g.Day != NoticeDay || noticeShownDay == g.Day) return;
            if (Debugging.MeasurementMode.Instance != null && Debugging.MeasurementMode.Instance.Active) return;

            Dogs.Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            string now = hero != null ? "지금 " + hero.DisplayName + "의 건강은 " + hero.Stats.Health : null;
            noticeShownDay = g.Day;
            OpenNotice("도그쇼 참가 조건 안내",
                       ChampionshipManager.FinalDay + "일차 도그쇼는 건강 " + ChampionshipManager.HealthGate + " 이상이어야 나갈 수 있다",
                       now != null ? now + ". 밥, 약으로 미리 챙겨 두자" : "밥, 약으로 미리 챙겨 두자",
                       "미모, 훈련도, 어질리티가 높을수록 도그쇼 종목이 쉬워진다");
        }

        void OpenNotice(string title, params string[] lines)
        {
            noticeTitle = title;
            noticeLines = lines;
            PointerMenus.SetOpen(this, true);
        }

        void CloseNotice()
        {
            noticeTitle = null;
            noticeLines = null;
            if (!Running) PointerMenus.SetOpen(this, false);
        }

        void DrawNotice()
        {
            if (noticeTitleStyle == null || noticeTitleStyle.font != UiSkin.Font)
            {
                noticeTitleStyle = new GUIStyle(UiSkin.Title) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
                noticeBodyStyle = new GUIStyle(UiSkin.Label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }
            GUI.depth = -60;
            const float W = 720f, Pad = 24f, Line = 34f, Btn = 52f;
            float h = Pad * 2f + 44f + noticeLines.Length * Line + 16f + Btn;
            var panel = new Rect((SW - W) * 0.5f, (SH - h) * 0.42f, W, h);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            float x = panel.x + Pad, w = W - Pad * 2f, y = panel.y + Pad;
            GUI.Label(new Rect(x, y, w, 44f), noticeTitle, noticeTitleStyle);
            y += 44f;
            foreach (string line in noticeLines)
            {
                GUI.Label(new Rect(x, y, w, Line), line, noticeBodyStyle);
                y += Line;
            }
            y += 16f;
            if (UiSkin.Click(new Rect(panel.center.x - 120f, y, 240f, Btn), "확인", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 20 }))
                CloseNotice();
        }

        // ---- 시작·정리 ----

        /// <summary>
        /// 테스트용(디버그 0 키). 30일을 기다리지 않고 바로 도그쇼로 간다.
        /// 날짜를 D30 으로 올려 쇼가 끝난 뒤 챔피언십 결과·엔딩까지 이어서 볼 수 있게 하고,
        /// 미모·훈련도·어질리티는 만점 기준(<see cref="StatFull"/>)의 80% 로 맞춘다 — 잘 키운 강아지 기준.
        /// </summary>
        public bool DebugStart(out string reason)
        {
            reason = null;
            if (phase != Phase.None) { reason = "이미 도그쇼 진행 중"; return false; }
            Dog dog = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (dog == null) { reason = "출전견 없음"; return false; }
            // 능력치·날짜를 바꾸기 전에 확인한다 — 무대가 없어 못 열면 상태만 바뀐 채 남았다
            if (stage == null || showCamera == null) { reason = "무대/카메라 연결 없음"; return false; }
            if (TimeManager.Instance != null && TimeManager.Instance.IsDayOver) { reason = "하루가 끝난 뒤에는 열 수 없다, 다음 날 아침에"; return false; }

            int stat = Mathf.RoundToInt(StatFull * 0.8f);
            dog.Stats.Restore(DogStats.MaxUpkeep, DogStats.MaxUpkeep, stat, stat, stat);
            GameManager.Instance.DebugSetDay(ChampionshipManager.FinalDay);

            Begin();
            if (phase == Phase.None) { reason = "무대/카메라 연결 없음"; return false; }
            return true;
        }

        /// <summary>
        /// 시연 모드 3일차의 [도그쇼 시작]. 본편 30일차와 <b>같은</b> 쇼 시작 화면을 연다 — 종목·채점·순위는 그대로다.
        /// 하루가 이미 끝났거나 쇼가 돌고 있으면 못 연다.
        /// </summary>
        public bool BeginExhibitionShow()
        {
            if (phase != Phase.None || Running) return false;
            if (TimeManager.Instance != null && TimeManager.Instance.IsDayOver) return false;
            Begin();
            return phase != Phase.None;
        }

        void Begin()
        {
            hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (hero == null || stage == null || showCamera == null) return;

            phase = Phase.Waiting;
            Running = true;
            Sound.PlayBgm(Bgm.DogShow);
            headline = "Day " + ChampionshipManager.LastDay + "  도그쇼 챔피언십";
            subline = ChampionshipManager.LastDay + "일 동안 함께한 " + hero.DisplayName + "와(과) 무대에 오를 시간이다";

            // 가게는 오늘 안 연다. 시계·조작·HUD 를 끈다
            Silence<TimeManager>();
            Silence<Player.PlayerController>();
            Silence<Player.PlayerInteraction>();
            Silence<Player.ThirdPersonCamera>();
            Silence<Player.FurniturePlacer>();
            Silence<GameHud>();
            Silence<ShopHours>();   // 가게 열기 버튼(OnGUI)
            Silence<SleepPrompt>();

            // HUD 는 uGUI 캔버스라 컴포넌트를 꺼도 화면에 남는다 — 캔버스를 같이 끈다
            foreach (GameHud hud in FindObjectsByType<GameHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (Canvas c in hud.GetComponentsInChildren<Canvas>())
                    if (c.enabled) { c.enabled = false; silenced.Add(c); }
            Silence<GameSettings>();
            Silence<Debugging.DebugHud>();
            PointerMenus.SetOpen(this, true);   // 커서를 풀어 둔다
            // 1인칭(커서 잠금)에서 G 로 바로 열면 커서가 숨은 채 남았다 — 쇼 버튼을 눌러야 하니 직접 보이게 한다
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Silence<T>() where T : Behaviour
        {
            foreach (T b in FindObjectsByType<T>(FindObjectsSortMode.None))
                if (b.enabled) { b.enabled = false; silenced.Add(b); }
        }

        void Restore()
        {
            foreach (Behaviour b in silenced) if (b != null) b.enabled = true;
            silenced.Clear();
            PointerMenus.SetOpen(this, false);
        }

        IEnumerator RunShow()
        {
            DogStats st = hero.Stats;
            beautyN = Mathf.Clamp01(st.Beauty / StatFull);
            trainingN = Mathf.Clamp01(st.Training / StatFull);
            agilityN = Mathf.Clamp01(st.Agility / StatFull);
            // 종목마다 맡은 스탯: ① 미모, ② 훈련도, ③ 어질리티, ④ 셋의 평균
            totalN = (beautyN + trainingN + agilityN) / 3f;

            // 발바닥 높이는 무대로 옮기기 전에 잰다(가게에서 서 있던 자세 그대로)
            pawOffset = float.NaN;
            DogBaseOffset();

            // 강아지를 무대로. 길찾기·배회를 끄고 손으로 옮긴다
            var roamer = hero.GetComponent<DogRoamer>();
            if (roamer != null) roamer.enabled = false;
            var agent = hero.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            showCamera.enabled = true;

            yield return Intro();
            yield return PoseGame();
            yield return FetchGame();
            yield return AgilityGame();
            yield return CourseGame();
            yield return Result();
        }

        // ---- 진행 ----

        IEnumerator Intro()
        {
            phase = Phase.Intro;
            PlaceDog(posePoint.position, posePoint.rotation);
            hero.Animator.Play(DogAnim.WagTail);
            AimCamera(posePoint.position, 2.6f, 1.1f, 0f);

            headline = "미모 심사";
            subline = "안내를 읽고 [준비완료]를 누르면 3초 뒤 시작";
            // 안내 글에는 글꼴에 없는 글자(①, —)를 쓰지 않는다 — 빈칸으로 나온다
            yield return Guide("1. 미모 심사  (" + PoseRounds + "번)",
                "바깥에서 링이 줄어들며 가운데 원으로 다가온다",
                "링이 가운데 원의 띠에 겹치는 순간 Space 로 포즈!",
                "가는 안쪽 띠 = PERFECT(100점), 그 바깥 띠 = GOOD(60점), 놓치면 MISS",
                "뒤로 갈수록 링이 빨라진다",
                "미모가 높을수록 띠가 넓어진다  (지금 미모 " + hero.Stats.Beauty + ")",
                PoseRounds + "번의 평균이 점수, 총점의 20%");
        }

        /// <summary>
        /// 종목 시작 전 안내창. 진행 방식을 보여 주고 [준비완료](Space)를 누르면 3초 세고 시작한다.
        /// 무대·카메라는 부르기 전에 맞춰 둔다 — 안내창 뒤로 그 종목 자리가 보인다.
        /// </summary>
        IEnumerator Guide(string title, params string[] lines)
        {
            Phase keep = phase;
            guideTitle = title;
            guideLines = lines;
            guideReady = false;
            phase = Phase.Guide;
            while (!guideReady) yield return null;
            phase = keep;
            yield return CountdownToStart();
        }

        IEnumerator CountdownToStart()
        {
            for (int n = 3; n >= 1; n--)
            {
                countdown = n.ToString();
                Sound.Play(Sfx.Countdown);   // 3, 2, 1 마다
                yield return Wait(1f);
            }
            countdown = "GO!";
            Sound.Play(Sfx.Go);
            countdownUntil = Time.time + 0.7f;
        }

        IEnumerator PoseGame()
        {
            phase = Phase.Pose;
            // 미모가 높을수록 목표 원이 커지고 <b>PERFECT 띠</b>가 두꺼워진다 — "키운 만큼 쉬워졌다"가 눈에 보인다.
            // GOOD 띠는 PERFECT 바깥으로 늘 같은 폭만큼 붙는다. 예전엔 GOOD 까지 같이 넓어져서
            // 미모가 높으면 대충 눌러도 다 맞았고, 두 판정이 한 색이라 어디가 PERFECT 인지 몰랐다
            targetRadius = Mathf.Lerp(0.26f, 0.36f, beautyN);
            perfectHalf = Mathf.Lerp(0.012f, 0.045f, beautyN);
            goodHalf = perfectHalf + 0.04f;
            goodTex = MakeBand(512, (targetRadius - goodHalf) / (targetRadius + goodHalf), 6f);
            perfectTex = MakeBand(512, (targetRadius - perfectHalf) / (targetRadius + perfectHalf), 1.5f);
            float total = 0f;

            for (int round = 0; round < PoseRounds; round++)
            {
                headline = "미모 심사  " + (round + 1) + " / " + PoseRounds;
                subline = "Space 로 포즈!";
                hero.Animator.Play(DogAnim.Idle);

                float duration = Mathf.Lerp(1.7f, 1.15f, round / (float)(PoseRounds - 1));
                float t = 0f;
                posePressed = false;
                float got = 0f;
                bool judged = false;

                while (t < duration)
                {
                    t += Time.deltaTime;
                    ringRadius = Mathf.Lerp(1f, 0f, t / duration);

                    if (posePressed && !judged)
                    {
                        float diff = Mathf.Abs(ringRadius - targetRadius);
                        if (diff <= perfectHalf) { got = 1f; Flash("PERFECT!", PerfectColor); Sound.Play(Sfx.PosePerfect); }
                        else if (diff <= goodHalf) { got = 0.6f; Flash("GOOD", GoodColor); Sound.Play(Sfx.PoseGood); }
                        else { Flash("MISS", new Color(1f, 0.55f, 0.5f)); Sound.Play(Sfx.PoseMiss); }
                        judged = true;
                        break;
                    }
                    yield return null;
                }

                if (!judged) { Flash("MISS", new Color(1f, 0.55f, 0.5f)); Sound.Play(Sfx.PoseMiss); }   // 안 누르고 넘어가도 MISS 소리
                hero.Animator.Play(got >= 1f ? DogAnim.WagTail : got > 0f ? DogAnim.Sit : DogAnim.Angry);
                total += got;
                ringRadius = -1f;
                yield return Wait(0.9f);
            }

            poseScore = total / PoseRounds;
            headline = "미모 심사 끝";
            subline = "성적 " + Mathf.RoundToInt(poseScore * 100f) + "점";
            yield return Wait(1.6f);
        }

        IEnumerator FetchGame()
        {
            phase = Phase.Fetch;
            BuildFetchItems();
            SpawnFetchProps();
            SpawnHandler();

            // 주인 옆에 앉아 대기 — 주인과 강아지가 받침대 줄을 바라본다
            Vector3 rowCenter = itemTable.position;
            Quaternion faceRow = Quaternion.LookRotation(Flat(rowCenter - fetchStart.position));
            PlaceDog(fetchStart.position, faceRow);
            hero.Animator.Play(DogAnim.Sit);
            FetchCamera();

            headline = "훈련 심사";
            subline = "안내를 읽고 [준비완료]를 누르면 3초 뒤 시작";
            yield return Guide("2. 훈련 심사  (" + FetchRounds + "번)",
                "심사위원이 가져올 물건을 말한다",
                "아래 카드 중 그 물건 카드를 눌러(또는 숫자 1~4) 강아지에게 지시하자",
                "강아지가 받침대 줄에서 물건을 찾아 물어 온다",
                "카드를 맞게 골라도 훈련도가 낮으면 헤매거나 엉뚱한 걸 물어 온다",
                "훈련도가 높을수록 정확하다  (지금 훈련도 " + hero.Stats.Training + ")",
                "맞게 물어 온 횟수가 점수, 총점의 25%");

            // 카드를 맞게 골랐을 때 제대로 물어 올 확률. 예전 Lerp(0.35, 0.95) 는 훈련도 중간(0.5)에서 65% —
            // 3번 다 맞힐 확률이 27% 라 평균으로 키운 강아지도 거의 늘 틀렸다. 제곱근 곡선으로 중간을 끌어올린다.
            //   훈련도   0.25 → 79%   0.5 → 87%   0.8 → 94%   1.0 → 98%
            float success = Mathf.Lerp(0.6f, 0.98f, Mathf.Sqrt(trainingN));
            int got = 0;

            for (int round = 0; round < FetchRounds; round++)
            {
                requested = fetchItems[Random.Range(0, fetchItems.Count)];
                DealCards();
                headline = "훈련 심사  " + (round + 1) + " / " + FetchRounds;
                subline = "심사위원: \"" + ItemName(requested) + "을(를) 가져오게 하세요\"";
                chosenCard = -1;
                broughtItem = -1;
                awaitingChoice = true;
                hero.Animator.Play(DogAnim.Sit);

                while (chosenCard < 0) yield return null;
                awaitingChoice = false;
                Sound.Play(Sfx.ShowCard);

                int ordered = cards[chosenCard];
                bool right = ordered == requested && Random.value < success;
                broughtItem = right ? ordered : RandomOther(requested);

                // 주인이 지시한다
                Say(ItemName(ordered) + " 가져와!", 1.6f);
                subline = "";
                yield return Wait(0.9f);

                // ① 받침대 줄 앞까지 곧장 → ② 줄을 따라 옆으로 걸으며 찾는다
                Transform prop = propOf.TryGetValue(broughtItem, out Transform pr) ? pr : null;
                Vector3 rowFront = RowFront(rowCenter);
                yield return RunTo(rowFront, false);   // 멈추지 않고 걸음으로 이어진다

                // 훈련이 덜 됐으면 엉뚱한 받침대에서 한 번 멈칫한다 — 헤매는 게 보여야 한다
                // 틀린 걸 물어 올 땐 늘 헤매고, 맞게 물어 와도 가끔(실패 확률의 절반) 멈칫한다.
                // 예전엔 정답이어도 훈련도 중간이면 절반 넘게 헤매서 실패처럼 보였다
                if (!right || Random.value < (1f - success) * 0.5f)
                {
                    int decoy = RandomOther(broughtItem);
                    if (propHome.ContainsKey(decoy))
                    {
                        yield return WalkTo(RowFront(propHome[decoy]));
                        yield return TurnTo(propHome[decoy]);
                        hero.Animator.Play(DogAnim.Eat);   // 킁킁
                        DogSays("?", 0.9f);
                        yield return Wait(0.9f);
                    }
                }

                Vector3 at = prop != null ? propHome[broughtItem] : rowCenter;
                yield return WalkTo(RowFront(at));
                yield return TurnTo(at);

                // 코로 톡 — 그 물건이 쏙 사라진다(골랐다는 표시)
                hero.Animator.Play(DogAnim.Eat);
                yield return Wait(0.35f);
                if (prop != null) yield return PopOut(prop);
                hero.Animator.Play(DogAnim.WagTail);
                yield return Wait(0.3f);

                // 갔던 길로 돌아온다
                yield return RunTo(rowFront, false);
                yield return RunTo(fetchStart.position);
                yield return TurnTo(faceRow);   // 주인 옆에서 돌아앉는다
                hero.Animator.Play(DogAnim.Sit);

                if (broughtItem == requested)
                {
                    got++;
                    Flash("정답!", new Color(0.6f, 1f, 0.75f));
                    Sound.Play(Sfx.FetchSuccess);   // 2단계 성공음
                    Say("잘했어!", 1.4f);
                    hero.Animator.Play(DogAnim.WagTail);
                }
                else
                {
                    Flash(ItemName(broughtItem) + "...", new Color(1f, 0.55f, 0.5f));
                    Sound.Play(Sfx.FetchFail);      // 2단계 실패음
                    Say("그게 아닌데...", 1.4f);
                }
                subline = "골라 온 것: " + ItemName(broughtItem);
                yield return Wait(1.4f);

                // 다음 판을 위해 받침대 위로 되돌린다
                if (prop != null) yield return PopIn(prop, broughtItem);
                broughtItem = -1;
            }

            foreach (Transform p in propOf.Values) if (p != null) Destroy(p.gameObject);
            propOf.Clear();
            if (handler != null) Destroy(handler);

            fetchScore = got / (float)FetchRounds;
            requested = -1;
            headline = "훈련 심사 끝";
            subline = FetchRounds + "번 중 " + got + "번 성공";
            yield return Wait(1.6f);
        }

        // ---- 훈련 심사 연출 ----

        /// <summary>
        /// 주인. 플레이어 겉모습(애니메이터가 달린 Model)만 복제해 강아지 옆에 세운다.
        /// 강아지 혼자 대각선으로 뛰어갔다 오기만 하면 무대가 비어 보였다.
        /// </summary>
        GameObject handler;

        void SpawnHandler()
        {
            if (handler != null) Destroy(handler);
            Player.PlayerCarry player = FindAnyObjectByType<Player.PlayerCarry>();
            Animator body = player != null ? player.GetComponentInChildren<Animator>() : null;
            if (body == null) return;

            // 강아지 왼쪽, 반 걸음 뒤
            Vector3 toRow = Flat(itemTable.position - fetchStart.position).normalized;
            Vector3 left = Vector3.Cross(Vector3.up, toRow) * -1f;
            Vector3 at = fetchStart.position + left * 0.75f - toRow * 0.35f;

            handler = Instantiate(body.gameObject, at, Quaternion.LookRotation(toRow));
            handler.name = "ShowHandler";
            foreach (MonoBehaviour mb in handler.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
        }

        /// <summary>받침대 줄 바로 앞(강아지가 서는 자리). 줄은 무대 x 축으로 늘어서 있다.</summary>
        Vector3 RowFront(Vector3 pedestal)
        {
            Vector3 toward = Flat(itemTable.position - fetchStart.position).normalized;
            Vector3 p = pedestal - toward * (DogSize() * 0.5f + 0.45f);
            return new Vector3(p.x, fetchStart.position.y, p.z);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>걷기. 옆으로 줄을 훑을 때 쓴다 — 뛰면 찾는 게 아니라 지나가는 것처럼 보인다.</summary>
        IEnumerator WalkTo(Vector3 target) => MoveTo(target, false, true);

        /// <summary>뛰기. <paramref name="stop"/> 이 false 면 감속 없이 지나쳐 다음 구간으로 이어 달린다.</summary>
        IEnumerator RunTo(Vector3 target, bool stop = true) => MoveTo(target, true, stop);

        const float TurnRate = 400f;   // 도/초

        /// <summary>
        /// 예전엔 목표 쪽으로 몸을 순간 회전시키고 일정 속도로 미끄러졌다 — 꺾이는 곳마다 각이 지고,
        /// 다리 회전과 실제 속도가 따로 놀았다. 지금은
        ///  · 몸은 초당 <see cref="TurnRate"/> 도까지만 돈다. 크게 돌아야 하면 거의 멈춰 서서 돈다
        ///  · 출발은 가속, 도착 직전은 감속
        ///  · 다리 회전(재생 배속)을 실제 속력에 맞춘다 — 가게 배회(DogRoamer)와 같은 기준
        /// </summary>
        IEnumerator MoveTo(Vector3 target, bool run, bool stop)
        {
            const float Accel = 5f;
            var roamer = hero.GetComponent<DogRoamer>();
            float refSpeed = run ? (roamer != null ? roamer.RunSpeed : 2.6f) : (roamer != null ? roamer.WalkSpeed : 0.75f);
            // 몸이 작은 견종도 무대가 지루하지 않게 하한을 둔다(다리는 최대 2배속까지 따라 돈다)
            float pace = run ? Mathf.Max(1.8f, refSpeed * 1.35f) * Mathf.Lerp(0.9f, 1.15f, trainingN)
                             : Mathf.Max(0.7f, refSpeed * 1.1f);
            hero.Animator.Play(run ? DogAnim.Run : DogAnim.Walk);

            Vector3 goal = new Vector3(target.x, target.y + DogBaseOffset(), target.z);
            float speed = moveSpeed;
            float giveUp = Time.time + 10f;
            while (Time.time < giveUp)
            {
                Vector3 p = hero.transform.position;
                Vector3 to = Flat(goal - p);
                float dist = to.magnitude;
                if (dist < (stop ? 0.04f : 0.3f)) break;

                Quaternion want = Quaternion.LookRotation(to);
                float angle = Quaternion.Angle(hero.transform.rotation, want);
                hero.transform.rotation = Quaternion.RotateTowards(hero.transform.rotation, want, TurnRate * Time.deltaTime);

                // 30도까지는 전속, 120도 넘게 돌아야 하면 거의 제자리에서 돈다 / 멈출 곳 0.7m 앞부터 감속
                float turnK = Mathf.Lerp(1f, 0.15f, Mathf.InverseLerp(30f, 120f, angle));
                float arriveK = stop ? Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(dist / 0.7f)) : 1f;
                speed = Mathf.MoveTowards(speed, pace * turnK * arriveK, Accel * Time.deltaTime);

                // 멀면 몸이 향한 쪽으로 나아가 곡선을 그리고, 가까우면 목표로 곧장 붙는다(빙빙 돌지 않게)
                Vector3 dir = dist > 0.35f ? hero.transform.forward : to / dist;
                Vector3 next = p + dir * Mathf.Min(speed * Time.deltaTime, dist);
                hero.transform.position = new Vector3(next.x, goal.y, next.z);

                hero.Animator.SetPlaybackSpeed(speed / refSpeed);
                yield return null;
            }

            if (stop)
            {
                hero.transform.position = goal;
                hero.Animator.SetPlaybackSpeed(1f);
                moveSpeed = 0f;
            }
            else moveSpeed = speed;   // 이어 달리는 구간이 이 속도에서 시작한다
        }

        float moveSpeed;

        /// <summary>제자리에서 천천히 돌아선다. 순간 회전은 로봇처럼 보였다.</summary>
        IEnumerator TurnTo(Quaternion want)
        {
            while (Quaternion.Angle(hero.transform.rotation, want) > 1f)
            {
                hero.transform.rotation = Quaternion.RotateTowards(hero.transform.rotation, want, TurnRate * Time.deltaTime);
                yield return null;
            }
            hero.transform.rotation = want;
        }

        IEnumerator TurnTo(Vector3 target)
        {
            Vector3 d = Flat(target - hero.transform.position);
            if (d.sqrMagnitude > 0.0001f) yield return TurnTo(Quaternion.LookRotation(d));
        }

        /// <summary>주인 등 뒤에서 받침대 줄을 본다 — 주인·강아지·물건이 한 화면에 든다.</summary>
        void FetchCamera()
        {
            Vector3 toRow = Flat(itemTable.position - fetchStart.position).normalized;
            float k = Mathf.Lerp(0.85f, 1.3f, Mathf.InverseLerp(0.4f, 1.4f, DogSize()));
            showCamera.transform.position = fetchStart.position - toRow * 3.8f * k + Vector3.up * 3.3f * k;   // 높여서 주인 머리가 받침대를 덜 가린다
            showCamera.transform.LookAt(Vector3.Lerp(fetchStart.position, itemTable.position, 0.6f) + Vector3.up * 0.3f);
        }

        // 말풍선
        string ownerLine = "", dogLine = "";
        float ownerUntil, dogUntil;
        void Say(string text, float seconds) { ownerLine = text; ownerUntil = Time.time + seconds; }
        void DogSays(string text, float seconds) { dogLine = text; dogUntil = Time.time + seconds; }

        void DrawBubbles()
        {
            if (handler != null && Time.time < ownerUntil)
                DrawBubble(handler.transform.position + Vector3.up * 1.95f, ownerLine);   // 머리 바로 위
            if (Time.time < dogUntil)
                DrawBubble(hero.transform.position + Vector3.up * (DogSize() * 0.9f + 0.15f), dogLine);
        }

        void DrawBubble(Vector3 world, string text)
        {
            Vector3 sp = GuiPoint(showCamera.WorldToScreenPoint(world));
            if (sp.z <= 0f) return;
            float w = Mathf.Max(60f, text.Length * 18f + 30f);
            var r = new Rect(sp.x - w * 0.5f, SH - sp.y - 40f, w, 38f);
            GUI.Box(r, GUIContent.none, UiSkin.Panel_);
            GUI.Label(r, text, midStyle);
        }

        IEnumerator AgilityGame()
        {
            // 몸 상태를 먼저 보여 준다 — 30일 키운 게 숫자로 커지는 장면
            phase = Phase.AgilitySpec;
            // 사람이 허들마다 반응할 수 있는 속도. 6m/s 를 넘기면 16m 코스가 3초도 안 걸렸다
            runSpeed = Mathf.Lerp(2.2f, 4.0f, agilityN);
            // 허들 사이가 3.5m 다. 예전 값(높이 1m, 체공 0.8초)이면 한 번 뛰면 다음 허들 앞까지 날아가서
            // 허들이 다닥다닥 붙어 보였다. 바(0.25m)를 넉넉히 넘는 정도로 낮췄다
            jumpHeight = Mathf.Lerp(0.35f, 0.62f, agilityN);
            airTime = Mathf.Lerp(0.42f, 0.55f, agilityN);

            BuildTrack();
            Vector3 startFace;
            Vector3 trackStart = stage.TransformPoint(TrackPoint(0f, out startFace));
            PlaceDog(trackStart, Quaternion.LookRotation(stage.TransformDirection(startFace)));
            AimCamera(trackStart, 4.2f, 1.6f, 0f);
            headline = "어질리티";
            subline = "허들 앞에서 Space 로 점프! " + ChampionshipManager.LastDay + "일 동안 키운 몸이 그대로 드러난다";
            specShownAt = Time.time;
            yield return Wait(4.2f);

            // 출발선에서 안내 → 3초 세고 출발 — 바로 뛰면 첫 허들까지 마음의 준비를 할 틈이 없었다
            phase = Phase.Agility;
            headline = "어질리티";
            subline = "안내를 읽고 [준비완료]를 누르면 3초 뒤 시작";
            PlaceDog(trackStart, Quaternion.LookRotation(stage.TransformDirection(startFace)));
            hero.Animator.Play(DogAnim.Idle);
            FollowCamera(hero.transform.position);
            yield return Guide("3. 어질리티  (허들 " + trackHurdles.Count + "개 + 링)",
                "출발하면 강아지가 U자 레인을 따라 저절로 달린다. 점프만 누르면 된다",
                "허들 바로 앞에서 Space 로 점프! 너무 이르거나 늦으면 허들에 걸린다",
                "점프 중에는 다시 뛸 수 없다. 착지한 뒤 다음 허들을 노리자",
                "어질리티가 높을수록 빠르고, 높고 오래 뛴다  (지금 어질리티 " + hero.Stats.Agility + ")",
                "마지막 링 안으로 뛰어들면 끝! 넘은 허들과 링 수가 점수, 총점의 25%");

            subline = "Space: 점프";
            cleared = 0;
            jumpRequested = false;
            jumpStart = -10f;

            float baseY = laneStart.position.y + DogBaseOffset();
            float pos = 0f;
            int total = trackHurdles.Count + 1;   // 허들 + 마지막 링
            var passed = new bool[trackHurdles.Count];
            bool hoopDone = false;

            hero.Animator.Play(DogAnim.Run);
            hero.Animator.SetPlaybackSpeed(Mathf.Lerp(1f, 1.6f, agilityN));

            while (pos < TrackLength)
            {
                pos += runSpeed * Time.deltaTime;
                if (jumpRequested && Time.time - jumpStart > airTime) { jumpStart = Time.time; Sound.Play(Sfx.ShowJump); }
                jumpRequested = false;

                float air = Time.time - jumpStart;
                float h = air < airTime ? jumpHeight * 4f * (air / airTime) * (1f - air / airTime) : 0f;
                Vector3 tangent;
                Vector3 p = stage.TransformPoint(TrackPoint(pos, out tangent));
                hero.transform.position = new Vector3(p.x, baseY + h, p.z);
                // U자 모퉁이에서도 달리는 쪽을 본다
                Vector3 face = stage.TransformDirection(tangent);
                hero.transform.rotation = Quaternion.Slerp(hero.transform.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-14f * Time.deltaTime));

                for (int i = 0; i < trackHurdles.Count; i++)
                {
                    if (passed[i] || pos < TrackHurdleAt[i]) continue;
                    passed[i] = true;
                    // 바가 ~0.3m. 그 순간 그만큼 떠 있어야 넘는다
                    if (h > 0.25f) { cleared++; Flash("CLEAR!", new Color(0.6f, 1f, 0.75f)); Sound.Play(Sfx.HurdleClear); }
                    else { Flash("쿵!", new Color(1f, 0.55f, 0.5f)); KnockHurdle(trackHurdles[i]); Sound.Play(Sfx.HurdleFail); }
                }

                // 마지막 링 — 링 안쪽 아래 끝(HoopInnerBottom)보다 높이 떠 있어야 원 안으로 들어간다
                if (!hoopDone && pos >= TrackHoopAt)
                {
                    hoopDone = true;
                    if (h > (hoopModel != null ? HoopModelClear : HoopInnerBottom)) { cleared++; Flash("링 통과!", PerfectColor); Sound.Play(Sfx.HurdleClear); }
                    else { Flash("링에 걸렸다", new Color(1f, 0.55f, 0.5f)); hoopWobble = 1f; Sound.Play(Sfx.HurdleFail); }
                }
                if (hoop != null && hoopWobble > 0f)
                {
                    hoopWobble = Mathf.MoveTowards(hoopWobble, 0f, Time.deltaTime * 1.5f);
                    hoop.localRotation = hoopRest * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 24f) * 10f * hoopWobble);
                }

                FollowCamera(hero.transform.position);
                yield return null;
            }

            hero.Animator.SetPlaybackSpeed(1f);
            hero.Animator.Play(DogAnim.WagTail);
            agilityScore = cleared / (float)total;
            headline = "어질리티 끝";
            subline = "허들과 링 " + total + "개 중 " + cleared + "개";
            yield return Wait(1.8f);
        }

        // ---- ③ U자 점프 트랙 ----
        // 예전엔 무대 가운데 직선 16m 에 허들 4개라 금방 끝났다. 무대 앞쪽 빈 바닥에 U자로 32m 를 깐다.
        //   윗줄(→)  z -2.0 에서 x -8 → 6.5, 허들 4개
        //   반원     중심 (6.5, -3.6), 반지름 1.6 — 모퉁이에선 점프 없이 돈다
        //   아랫줄(←) z -5.2 에서 x 6.5 → -7, 허들 3개, 끝에 링 하나(통과하면 끝)
        // 아랫줄은 무대 앞 테두리(z -5.9)에서 0.7m 안쪽이고, ④ 장애물 코스(z -6.1 아래)와 겹치지 않는다.

        const float TrackX0 = -8f, TrackTurnX = 6.5f, TrackEndX = -7f, TrackZ1 = -2f, TrackZ2 = -5.2f;
        static float TrackR => (TrackZ1 - TrackZ2) * 0.5f;
        static float TrackLeg1 => TrackTurnX - TrackX0;
        static float TrackArc => Mathf.PI * TrackR;
        static float TrackLength => TrackLeg1 + TrackArc + (TrackTurnX - TrackEndX);

        /// <summary>허들 자리(트랙 처음부터 거리). 윗줄 x -5, -1.5, 2, 5.5 / 아랫줄 x 4, 0.5, -3. 사이 3.5m.</summary>
        static readonly float[] TrackHurdleAt =
        {
            -5f - TrackX0, -1.5f - TrackX0, 2f - TrackX0, 5.5f - TrackX0,
            TrackLeg1 + TrackArc + (TrackTurnX - 4f), TrackLeg1 + TrackArc + (TrackTurnX - 0.5f), TrackLeg1 + TrackArc + (TrackTurnX + 3f),
        };
        /// <summary>링 자리(아랫줄 x -5.5). 링을 지나 1.5m 뒤가 결승.</summary>
        static float TrackHoopAt => TrackLeg1 + TrackArc + (TrackTurnX + 5.5f);

        /// <summary>
        /// 링 치수(m). 링 가운데 높이 0.62, 바깥 반지름 0.42, 굵기 0.06 — 안쪽 아래 끝이 0.26 이라
        /// 허들 바(0.25)와 비슷한 높이로 떠 있어야 원 안으로 들어간다.
        /// </summary>
        const float HoopCenterY = 0.62f, HoopRadius = 0.42f, HoopTube = 0.06f;
        const float HoopInnerBottom = 0.22f;   // 발 높이 기준. 안쪽 아래 끝(0.26)에서 몸이 둥근 만큼 조금 봐준다 — 허들(0.25)과 비슷한 박자

        [Header("③ 링 점프(비우면 코드로 만든 줄무늬 링)")]
        [SerializeField] GameObject hoopModel;

        readonly List<Transform> trackHurdles = new List<Transform>();
        Transform hoop;
        Quaternion hoopRest;
        float hoopWobble;

        /// <summary>트랙 처음부터 <paramref name="d"/> 만큼 간 곳(무대 기준)과 달리는 방향.</summary>
        static Vector3 TrackPoint(float d, out Vector3 tangent)
        {
            if (d < TrackLeg1)
            {
                tangent = Vector3.right;
                return new Vector3(TrackX0 + d, 0f, TrackZ1);
            }
            if (d < TrackLeg1 + TrackArc)
            {
                float a = (d - TrackLeg1) / TrackR;
                tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                return new Vector3(TrackTurnX + TrackR * Mathf.Sin(a), 0f, (TrackZ1 + TrackZ2) * 0.5f + TrackR * Mathf.Cos(a));
            }
            tangent = Vector3.left;
            return new Vector3(TrackTurnX - (d - TrackLeg1 - TrackArc), 0f, TrackZ2);
        }

        /// <summary>
        /// 트랙을 깐다. 무대에 놓인 허들(4개)을 본으로 모자란 만큼 복제해 U자 위에 세우고,
        /// 출발선·결승선을 옮기고, 마지막에 링을 세운다. 두 번 불려도 한 번만 만든다.
        /// </summary>
        void BuildTrack()
        {
            if (trackHurdles.Count == 0 && hurdles.Length > 0)
            {
                Transform proto = hurdles[0];
                for (int i = 0; i < TrackHurdleAt.Length; i++)
                {
                    Transform h = i < hurdles.Length && hurdles[i] != null ? hurdles[i]
                                : Instantiate(proto.gameObject, stage).transform;
                    h.name = "TrackHurdle_" + i;
                    Vector3 t;
                    h.position = stage.TransformPoint(TrackPoint(TrackHurdleAt[i], out t));
                    // 본 허들은 레인(+x)을 보고 서 있었다 — 달리는 방향만큼 돌린다
                    h.rotation = stage.rotation * Quaternion.FromToRotation(Vector3.right, t) * Quaternion.Inverse(stage.rotation) * proto.rotation;
                    trackHurdles.Add(h);
                }
            }

            Vector3 tan;
            Transform startLine = stage.Find("StartLine"), finishLine = stage.Find("FinishLine");
            if (startLine != null) startLine.localPosition = TrackPoint(0.4f, out tan) + Vector3.up * 0.01f;
            if (finishLine != null) finishLine.localPosition = TrackPoint(TrackLength - 0.4f, out tan) + Vector3.up * 0.01f;

            if (hoop == null)
            {
                Vector3 at = TrackPoint(TrackHoopAt, out tan);
                hoop = (hoopModel != null ? FitHoopModel() : BuildHoopShape()).transform;
                hoop.name = "TrackHoop";
                hoop.SetParent(stage, false);
                hoop.localPosition = at;
                hoop.localRotation = Quaternion.LookRotation(tan);   // 링 면이 달리는 방향을 마주 본다
                hoopRest = hoop.localRotation;
            }
        }

        /// <summary>
        /// 사진 같은 링 점프를 기본 도형으로 세운다: 주황·흰·보라 줄무늬 링을 양옆 흰 A자 기둥이 받친다.
        /// 링은 로컬 xy 평면(달리는 방향 z 를 마주 봄), 바닥 가운데가 원점이다.
        /// </summary>
        GameObject BuildHoopShape()
        {
            var root = new GameObject("Hoop");
            Color[] stripe = { new Color(0.98f, 0.55f, 0.18f), new Color(0.97f, 0.97f, 0.95f), new Color(0.62f, 0.28f, 0.62f), new Color(0.97f, 0.97f, 0.95f) };
            const int Segs = 32, Sides = 10;
            // 줄무늬마다 메시를 따로 — 머티리얼 하나에 정점색을 쓰면 URP Lit 이 색을 무시한다
            for (int s = 0; s < stripe.Length; s++)
            {
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var tris = new List<int>();
                for (int i = s * 2; i < Segs; i += stripe.Length * 2)
                {
                    for (int k = 0; k < 2; k++)   // 줄 하나 = 마디 2개
                    {
                        int seg = i + k;
                        int baseIndex = verts.Count;
                        for (int e = 0; e <= 1; e++)
                        {
                            float a = (seg + e) / (float)Segs * Mathf.PI * 2f;
                            Vector3 c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                            for (int j = 0; j <= Sides; j++)
                            {
                                float b = j / (float)Sides * Mathf.PI * 2f;
                                Vector3 n = c * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                                verts.Add(c * HoopRadius + n * HoopTube + Vector3.up * HoopCenterY);
                                norms.Add(n);
                            }
                        }
                        for (int j = 0; j < Sides; j++)
                        {
                            int a0 = baseIndex + j, a1 = a0 + 1, b0 = a0 + Sides + 1, b1 = b0 + 1;
                            tris.Add(a0); tris.Add(b0); tris.Add(a1);
                            tris.Add(a1); tris.Add(b0); tris.Add(b1);
                        }
                    }
                }
                var mesh = new Mesh { name = "HoopStripe" + s };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                var go = new GameObject("Stripe" + s);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = HoopMat(stripe[s]);
            }

            // 양옆 A자 기둥(흰색)과 바닥 받침
            Material white = HoopMat(new Color(0.95f, 0.95f, 0.93f));
            float side = HoopRadius + 0.12f;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                for (int leg = -1; leg <= 1; leg += 2)
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(p.GetComponent<Collider>());
                    p.transform.SetParent(root.transform, false);
                    float h = HoopCenterY + HoopRadius + 0.1f;
                    p.transform.localScale = new Vector3(0.04f, h * 0.5f, 0.04f);
                    p.transform.localPosition = new Vector3(sgn * side, h * 0.5f, leg * 0.18f);
                    p.transform.localRotation = Quaternion.Euler(-leg * 14f, 0f, 0f);
                    p.GetComponent<MeshRenderer>().sharedMaterial = white;
                }
                var foot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(foot.GetComponent<Collider>());
                foot.transform.SetParent(root.transform, false);
                foot.transform.localScale = new Vector3(0.05f, 0.03f, 0.7f);
                foot.transform.localPosition = new Vector3(sgn * side, 0.015f, 0f);
                foot.GetComponent<MeshRenderer>().sharedMaterial = white;
            }
            // 링을 기둥에 거는 가로대(위)
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(root.transform, false);
            bar.transform.localScale = new Vector3(side * 2f + 0.06f, 0.04f, 0.04f);
            bar.transform.localPosition = new Vector3(0f, HoopCenterY + HoopRadius + 0.1f, 0f);
            bar.GetComponent<MeshRenderer>().sharedMaterial = white;
            return root;
        }

        /// <summary>
        /// 바르코 링 모델 높이(m). 모델은 링 가운데가 높이의 55%, 구멍 반지름이 높이의 19% 다 —
        /// 1.1m 로 맞추면 구멍 아래 끝이 약 0.39m, 구멍 지름 0.42m. 링 튜브가 두꺼워 기본 링(0.26)보다 조금 높다.
        /// </summary>
        const float HoopModelHeight = 1.1f;   // 0.95 는 시바견 몸이 구멍보다 커 보였다
        /// <summary>바르코 링을 통과했다고 보는 발 높이. 구멍 아래 끝(0.39)보다 조금 봐준다 — 어질리티가 낮아도(최고 0.35m) 꼭대기에서 맞추면 들어간다.</summary>
        const float HoopModelClear = 0.3f;

        /// <summary>
        /// 바르코 링 모델을 바닥 가운데 원점에 높이 <see cref="HoopModelHeight"/> 로 맞춰 세운다.
        /// 링 면이 앞(+z)을 보게 맞춘다 — 붙잡는 부모를 달리는 방향으로 돌리면 링이 강아지를 마주 본다.
        /// </summary>
        GameObject FitHoopModel()
        {
            var holder = new GameObject("Hoop");
            GameObject model = Instantiate(hoopModel, holder.transform);
            model.name = "Model";
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            Renderer[] rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return holder;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            // 링 면이 x 쪽을 보고 있으면(x 두께가 z 보다 얇으면) 90도 돌려 z 를 보게 한다
            if (b.size.x < b.size.z)
            {
                model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
                b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            }
            float k = HoopModelHeight / Mathf.Max(0.01f, b.size.y);
            model.transform.localScale *= k;
            model.transform.localPosition = (model.transform.localPosition - new Vector3(b.center.x, b.min.y, b.center.z)) * k;
            return holder;
        }

        static Material HoopMat(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(s != null ? s : Shader.Find("Standard")) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
            return m;
        }

        /// <summary>코스 제한 시간. 넘기면 거기까지 지난 장애물만 센다.</summary>
        const float CourseLimit = 90f;
        float courseTime;

        /// <summary>
        /// 위브에서 맞춰야 할 화살표 수. 성장도(종합 스탯)가 높을수록 적다 — 제한 시간(10초)은 같다.
        /// 16개는 초당 1.6개를 쉬지 않고 눌러야 하는 박자, 8개는 여유 있게 따라가는 박자다.
        /// </summary>
        const int WeaveArrowsMax = 16, WeaveArrowsMin = 8;
        int weaveArrows = 12;

        /// <summary>구간 한 줄 안내를 띄운 구간 번호와 시각.</summary>
        int introFor = -1;
        float introAt;
        const float IntroShow = 2.6f;
        Vector3 courseFacing;

        /// <summary>
        /// ④ 장애물 달리기. 점프 없이 WASD 로 몰고 다닌다 — 속도는 미모·훈련도·어질리티 평균(종합 스탯)에서 나온다.
        /// 오르막은 장애물 표면을 따라 높이를 맞추고, 경사면에선 몸도 기울인다.
        /// </summary>
        IEnumerator CourseGame()
        {
            phase = Phase.Course;
            if (course == null) course = GetComponent<ObstacleCourse>();
            if (course == null) course = gameObject.AddComponent<ObstacleCourse>();
            course.Build(stage);

            float floorY = stage.TransformPoint(Vector3.zero).y;
            courseFacing = stage.TransformDirection(ObstacleCourse.StartFacing);
            PlaceDog(stage.TransformPoint(ObstacleCourse.StartPoint), Quaternion.LookRotation(courseFacing));
            hero.Animator.SetPlaybackSpeed(1f);
            hero.Animator.Play(DogAnim.Idle);
            CourseCamera(hero.transform.position, true);

            // 출발 전 안내 — 점프 키가 없는 종목이라 장애물마다 지나는 법을 먼저 보여 준다
            headline = "장애물 달리기";
            subline = "WASD 로 장애물 6개를 순서대로 지나자. 빨리 도착할수록 점수가 높고, 실수하면 감점";
            phase = Phase.CourseGuide;
            guideReady = false;
            while (!guideReady) yield return null;
            phase = Phase.Course;

            yield return CountdownToStart();
            subline = "WASD: 달리기";

            courseTime = 0f;
            float y = hero.transform.position.y;
            bool running = false;
            runSpeed = Mathf.Lerp(2.2f, 4.0f, totalN);   // ③ 은 어질리티, ④ 는 종합 스탯
            float runAnimSpeed = Mathf.Lerp(1f, 1.6f, totalN);
            weaveArrows = Mathf.RoundToInt(Mathf.Lerp(WeaveArrowsMax, WeaveArrowsMin, totalN));
            introFor = -1;
            course.TableEase = totalN;   // 잘 키웠으면 단상이 덜 기운다

            while (!course.Finished && courseTime < CourseLimit)
            {
                float dt = Time.deltaTime;
                courseTime += dt;

                Vector2 input = Vector2.zero;
                Keyboard kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
                }
                // 구간이 바뀌면 그 구간 한 줄 안내를 띄운다
                if (!course.Finished && course.Next != introFor) { introFor = course.Next; introAt = Time.time; }

                Vector3 dir;
                Vector3 pos;
                if (course.WeaveRhythm)
                {
                    // 위브는 리듬으로 — 손으로 몰지 않고 화면 위 화살표 줄을 차례로 맞추면 봉 사이를 오가며 나아간다
                    int pressed = -1;
                    if (kb != null)
                    {
                        if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) pressed = ObstacleCourse.Up;
                        else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) pressed = ObstacleCourse.Right;
                        else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) pressed = ObstacleCourse.Down;
                        else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) pressed = ObstacleCourse.Left;
                    }
                    string miss;
                    pos = course.TickWeave(weaveArrows, pressed, dt, hero.transform.position, out miss);
                    if (miss != null) Flash(miss, new Color(1f, 0.45f, 0.4f));
                    dir = Flat(pos - hero.transform.position);
                    dir = dir.sqrMagnitude > 1e-6f ? dir.normalized * Mathf.Min(1f, dir.magnitude / Mathf.Max(1e-4f, runSpeed * dt)) : Vector3.zero;
                }
                else
                {
                    // 화면 기준으로 움직인다 — W 는 화면 위쪽
                    Vector3 camF = Flat(showCamera.transform.forward).normalized;
                    Vector3 camR = Flat(showCamera.transform.right).normalized;
                    dir = camF * input.y + camR * input.x;
                    if (dir.sqrMagnitude > 1f) dir.Normalize();

                    // 테이블 위에선 걸음을 늦추고 기울기로 미끄러지는 몫을 더한다 — 반대쪽으로 버텨야 한다
                    float speed = course.Balancing ? runSpeed * ObstacleCourse.TableWalk : runSpeed;
                    pos = hero.transform.position + dir * speed * dt + stage.TransformVector(course.TableSlide(dt));
                    Vector3 ls = stage.InverseTransformPoint(pos);
                    ls.x = Mathf.Clamp(ls.x, ObstacleCourse.Area.xMin, ObstacleCourse.Area.xMax);
                    ls.z = Mathf.Clamp(ls.z, ObstacleCourse.Area.yMin, ObstacleCourse.Area.yMax);
                    // 장애물에 닿아 시작했으면 지나온 쪽으로는 못 돌아간다(옆으로는 자유)
                    Vector3 from = stage.InverseTransformPoint(hero.transform.position);
                    ls = course.ConstrainForward(new Vector3(from.x, 0f, from.z), new Vector3(ls.x, 0f, ls.z));
                    pos = stage.TransformPoint(new Vector3(ls.x, 0f, ls.z));
                }
                bool still = dir.sqrMagnitude < 0.01f;

                Vector3 normal;
                float surf = course.SurfaceY(pos, floorY, out normal);
                y = Mathf.Lerp(y, surf + DogBaseOffset(), 1f - Mathf.Exp(-18f * dt));
                pos.y = y;
                hero.transform.position = pos;

                if (!still) courseFacing = dir.normalized;
                // 터널 안에선 몸을 터널 방향으로 — 뒤로 못 가니 앞쪽(지나가는 쪽)을 본다
                Vector3 tunnelFwd;
                if (course.InTunnel(pos, out tunnelFwd)) courseFacing = Flat(tunnelFwd).normalized;
                Quaternion want = Quaternion.LookRotation(Vector3.ProjectOnPlane(courseFacing, normal), normal);
                hero.transform.rotation = Quaternion.Slerp(hero.transform.rotation, want, 1f - Mathf.Exp(-12f * dt));

                if (!still && !running) { hero.Animator.Play(DogAnim.Run); hero.Animator.SetPlaybackSpeed(runAnimSpeed); running = true; }
                else if (still && running) { hero.Animator.SetPlaybackSpeed(1f); hero.Animator.Play(DogAnim.Idle); running = false; }

                string warn, penalty;
                ObstacleCourse.Obstacle done = course.Tick(hero.transform.position, surf, floorY, dt, still, out warn, out penalty);
                if (penalty != null) Flash(penalty, new Color(1f, 0.45f, 0.4f));
                else if (done != null)
                {
                    if (done.kind == ObstacleCourse.Kind.Weave && done.alternations >= ObstacleCourse.PoleCount - 1) Flash("위브 완벽!", PerfectColor);
                    else Flash(done.nameKo + " 통과!", new Color(0.6f, 1f, 0.75f));
                }
                else if (warn != null) Flash(warn, new Color(1f, 0.75f, 0.5f));
                subline = course.Finished ? "" : course.Obstacles[course.Next].nameKo + ": " + course.Obstacles[course.Next].hint;

                CourseCamera(hero.transform.position, false);
                yield return null;
            }

            hero.Animator.SetPlaybackSpeed(1f);
            hero.Animator.Play(course.Finished ? DogAnim.WagTail : DogAnim.Sit);

            // 점수 = 도착 시간 점수(빠를수록 100 에 가깝다) - 감점.
            // 기준 시간: 코스 약 50m 를 제 속도로 뛰고, 시소에서 기다리고, 테이블에서 버틴 시간 + 방향 바꾸는 몫
            float par = 50f / Mathf.Max(0.5f, runSpeed) + ObstacleCourse.SeesawWait + ObstacleCourse.TableHold + ObstacleCourse.WeaveLimit * 0.5f + 6f;
            float timePoints = course.Finished
                ? Mathf.Lerp(100f, 40f, Mathf.InverseLerp(par, CourseLimit, courseTime))
                : 40f * course.Next / Mathf.Max(1, course.Obstacles.Count);   // 못 끝냈으면 지난 장애물 몫만
            float points = Mathf.Max(0f, timePoints - course.PenaltyPoints);
            courseScore = points / 100f;

            headline = course.Finished ? "장애물 달리기 끝" : "시간 종료";
            subline = "기록 " + courseTime.ToString("0.0") + "초 (" + Mathf.RoundToInt(timePoints) + "점)   감점 -" + course.PenaltyPoints
                    + "   성적 " + Mathf.RoundToInt(points) + "점";
            yield return Wait(2.2f);
        }

        void CourseCamera(Vector3 dogPos, bool snap)
        {
            // 코스 안쪽(무대 뒤편) 위에서 앞쪽을 내려다본다 — 윗줄은 화면 오른쪽, 아랫줄은 왼쪽으로 달린다
            float k = Mathf.Lerp(0.85f, 1.3f, Mathf.InverseLerp(0.4f, 1.4f, DogSize()));
            Vector3 focus = new Vector3(dogPos.x, stage.position.y, dogPos.z);
            Vector3 want = focus + stage.rotation * new Vector3(0f, 3.6f * k, 4.2f * k);
            showCamera.transform.position = snap ? want : Vector3.Lerp(showCamera.transform.position, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            showCamera.transform.LookAt(focus + Vector3.up * 0.3f);
        }

        IEnumerator Result()
        {
            phase = Phase.Result;
            // 네 판 가중 평균(100점 만점): 미모 20% / 훈련 25% / 어질리티 25% / 장애물 30%.
            // 챔피언십 순위는 이 총점으로만 정한다
            float avg = poseScore * PoseWeight + fetchScore * FetchWeight + agilityScore * AgilityWeight + courseScore * CourseWeight;
            int total = Mathf.RoundToInt(avg * 100f);
            ChampionshipManager.Instance.SetShowResult(hero, total);

            PlaceDog(posePoint.position, posePoint.rotation);
            hero.Animator.Play(DogAnim.WagTail);
            AimCamera(posePoint.position, 3.2f, 1.4f, 0f);

            headline = "심사 결과";
            subline = "미모 " + Mathf.RoundToInt(poseScore * 100f) + "  /  훈련 " + Mathf.RoundToInt(fetchScore * 100f)
                    + "  /  어질리티 " + Mathf.RoundToInt(agilityScore * 100f) + "  /  장애물 " + Mathf.RoundToInt(courseScore * 100f)
                    + "   /   총점 " + total + "점";
            boardShownAt = Time.time;
            Sound.Play(Sfx.Ranking);
            resultReady = true;
            while (resultReady)
            {
                // 시연은 아무도 안 눌러도 순위표를 잠시 보여 준 뒤 엔딩으로 — 부스에서 다음 사람이 바로 받을 수 있게
                if (Exhibition.Active && Time.time - boardShownAt > ExhibitionBoardSeconds) resultReady = false;
                yield return null;
            }

            // 결과 발표는 마감 화면이 한다 — 같은 경로로 랭크·엔딩까지 이어진다
            Running = false;
            phase = Phase.None;
            if (course != null) course.Clear();
            Restore();
            TimeManager.Instance.EndDayNow();
        }

        bool resultReady;
        /// <summary>시연에서 순위표를 띄워 두는 시간(초). 그 안에 누르면 바로 넘어간다.</summary>
        const float ExhibitionBoardSeconds = 20f;
        float specShownAt;
        string countdown = "";
        float countdownUntil;

        // ---- 도우미 ----

        IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        /// <summary>
        /// 발바닥이 무대 바닥에 닿는 높이(강아지 원점 기준). NavMeshAgent.baseOffset 을 쓰면 안 된다 —
        /// 그건 가게 내비메시(바닥보다 4cm 남짓 떠 있다)에 맞춘 값이라, 무대에서는 다리 짧은 견종이
        /// 바닥과 단상에 발이 묻혀 보였다. 쇼를 시작할 때 실제 메시를 구워 가장 낮은 점(발바닥)을 잰다.
        /// </summary>
        float pawOffset = float.NaN;

        float DogBaseOffset()
        {
            if (float.IsNaN(pawOffset)) pawOffset = MeasurePawOffset();
            return pawOffset;
        }

        float MeasurePawOffset()
        {
            float minY = float.MaxValue;
            var baked = new Mesh();
            foreach (SkinnedMeshRenderer smr in hero.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(baked, true);
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                foreach (Vector3 v in baked.vertices) minY = Mathf.Min(minY, m.MultiplyPoint3x4(v).y);
            }
            Destroy(baked);
            if (minY == float.MaxValue) return 0f;
            return Mathf.Clamp(hero.transform.position.y - minY, -0.2f, 0.2f);
        }

        void PlaceDog(Vector3 at, Quaternion rot)
        {
            hero.transform.SetPositionAndRotation(at + Vector3.up * DogBaseOffset(), rot);
        }

        /// <summary>강아지 크기에 맞춰 거리를 늘린다 — 치와와와 셰퍼드는 세 배 차이다.</summary>
        void AimCamera(Vector3 focus, float distance, float height, float yaw)
        {
            float size = DogSize();
            float k = Mathf.Lerp(0.8f, 1.5f, Mathf.InverseLerp(0.4f, 1.4f, size));
            Quaternion r = Quaternion.Euler(0f, yaw, 0f) * stage.rotation;
            showCamera.transform.position = focus + r * new Vector3(0f, height * k, -distance * k);
            showCamera.transform.LookAt(focus + Vector3.up * size * 0.45f);
        }

        void FollowCamera(Vector3 dogPos)
        {
            Quaternion r = stage.rotation;   // 코스는 무대 x 축 — 앞(-z)에서 옆모습을 본다
            float k = Mathf.Lerp(0.8f, 1.5f, Mathf.InverseLerp(0.4f, 1.4f, DogSize()));
            Vector3 focus = new Vector3(dogPos.x, laneStart.position.y, dogPos.z);
            showCamera.transform.position = focus + r * new Vector3(0f, 1.6f * k, -4.2f * k);
            showCamera.transform.LookAt(focus + Vector3.up * 0.4f);
        }

        float dogSize = -1f;
        float DogSize()
        {
            if (dogSize > 0f) return dogSize;
            var rs = hero.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return dogSize = 1f;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return dogSize = Mathf.Max(b.size.x, b.size.y, b.size.z);
        }

        /// <summary>
        /// 부딪힌 허들이 진행 방향으로 휘청였다가 다시 선다. 예전엔 옆으로 28도 기운 채 남아
        /// 한쪽 다리가 바닥에 박혀 버그처럼 보였다 — 앞쪽 바닥 모서리를 축으로 흔들고 제자리로 돌린다.
        /// </summary>
        void KnockHurdle(Transform hurdle)
        {
            Transform model = hurdle.Find("Model");
            if (model == null || hero == null) return;
            Vector3 forward = Flat(hero.transform.forward);
            if (forward.sqrMagnitude < 1e-4f) return;
            StartCoroutine(WobbleHurdle(model, forward.normalized));
        }

        IEnumerator WobbleHurdle(Transform model, Vector3 forward)
        {
            Vector3 homePos = model.position;
            Quaternion homeRot = model.rotation;
            Vector3 pivot = homePos + forward * 0.12f;          // 앞쪽 다리 바닥
            Vector3 axis = Vector3.Cross(Vector3.up, forward);  // 위가 진행 방향으로 넘어간다
            const float Duration = 0.9f, MaxAngle = 24f;
            for (float t = 0f; t < Duration; t += Time.deltaTime)
            {
                if (model == null) yield break;
                float k = t / Duration;
                // 한 번 크게 넘어갔다가 줄어들며 흔들린다. 뒤로는 넘어가지 않는다(바닥을 뚫지 않게)
                float angle = MaxAngle * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 2.5f)) * (1f - k);
                model.SetPositionAndRotation(homePos, homeRot);
                model.RotateAround(pivot, axis, angle);
                yield return null;
            }
            if (model != null) model.SetPositionAndRotation(homePos, homeRot);
        }

        void Flash(string text, Color color)
        {
            flashText = text;
            flashColor = color;
            flashUntil = Time.time + 0.8f;
        }

        // ② 받침대 위 물건
        readonly Dictionary<int, Transform> propOf = new Dictionary<int, Transform>();
        readonly Dictionary<int, Vector3> propHome = new Dictionary<int, Vector3>();
        readonly Dictionary<int, float> propLift = new Dictionary<int, float>();

        /// <summary>받침대에 올릴 물건 크기(가장 긴 변). 진열대 프롭은 크기가 제각각이라 맞춘다.</summary>
        const float PropSize = 0.42f;

        /// <summary>
        /// 물건을 <b>실물로</b> 받침대 위에 하나씩 올린다. 상점 진열대에 올라가는 그 프롭이라
        /// 손님이 사 가던 물건을 강아지가 골라 오는 모양이 된다. 받침대가 없으면 바닥에 늘어놓는다.
        /// </summary>
        void SpawnFetchProps()
        {
            propOf.Clear(); propHome.Clear(); propLift.Clear(); propScale.Clear();
            ProductCatalog catalog = InventoryManager.Instance.Catalog;
            Vector3 right = stage.right;

            for (int k = 0; k < fetchItems.Count; k++)
            {
                int item = fetchItems[k];
                GameObject prefab = catalog.Get(item).propPrefab;
                GameObject go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "FetchProp_" + catalog.Get(item).nameKo;
                foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c);

                // 가장 긴 변을 PropSize 로
                Bounds b = BoundsOf(go);
                float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (longest > 0.0001f) go.transform.localScale *= PropSize / longest;
                b = BoundsOf(go);

                Vector3 home;
                if (k < itemPedestals.Length && itemPedestals[k] != null)
                {
                    // 받침대 윗면 = 받침대 경계 상자의 꼭대기
                    Bounds pb = BoundsOf(itemPedestals[k].gameObject);
                    home = new Vector3(itemPedestals[k].position.x, pb.max.y, itemPedestals[k].position.z);
                }
                else home = itemTable.position + right * ((k - (fetchItems.Count - 1) * 0.5f) * 0.9f);

                // 피벗이 바닥에 있다는 보장이 없다 — 경계 상자 바닥을 받침대 윗면에 맞춘다
                float lift = go.transform.position.y - b.min.y;
                go.transform.position = home + Vector3.up * lift;

                propOf[item] = go.transform;
                propHome[item] = go.transform.position;
                propLift[item] = lift;
                propScale[item] = go.transform.localScale;
            }
        }

        readonly Dictionary<int, Vector3> propScale = new Dictionary<int, Vector3>();

        /// <summary>살짝 부풀었다가 쏙 줄어 사라진다(0.3초).</summary>
        IEnumerator PopOut(Transform prop)
        {
            Vector3 s0 = prop.localScale;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                float k = t < 0.08f ? 1f + t / 0.08f * 0.25f : Mathf.Lerp(1.25f, 0f, (t - 0.08f) / 0.22f);
                prop.localScale = s0 * k;
                yield return null;
            }
            prop.gameObject.SetActive(false);
            prop.localScale = s0;
        }

        IEnumerator PopIn(Transform prop, int item)
        {
            Vector3 s = propScale.TryGetValue(item, out Vector3 v) ? v : prop.localScale;
            prop.position = propHome[item];
            prop.localScale = Vector3.zero;
            prop.gameObject.SetActive(true);
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                prop.localScale = s * Mathf.SmoothStep(0f, 1f, t / 0.25f);
                yield return null;
            }
            prop.localScale = s;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        void BuildFetchItems()
        {
            fetchItems.Clear();
            ProductCatalog catalog = InventoryManager.Instance.Catalog;
            foreach (string name in fetchItemNames)
                for (int i = 0; i < catalog.Count; i++)
                    if (catalog.Get(i).nameKo == name) { fetchItems.Add(i); break; }

            // 이름이 바뀌어 하나도 못 찾으면 앞쪽 상품으로 채운다 — 쇼가 멈추는 것보다 낫다
            for (int i = 0; fetchItems.Count < 4 && i < catalog.Count; i++)
                if (!fetchItems.Contains(i)) fetchItems.Add(i);
        }

        void DealCards()
        {
            var pool = new List<int>(fetchItems);
            pool.Remove(requested);
            for (int i = pool.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            int correctSlot = Random.Range(0, cards.Length);
            for (int i = 0, k = 0; i < cards.Length; i++)
                cards[i] = i == correctSlot ? requested : pool[k++ % pool.Count];
        }

        int RandomOther(int not)
        {
            var pool = new List<int>(fetchItems);
            pool.Remove(not);
            return pool[Random.Range(0, pool.Count)];
        }

        string ItemName(int index) => index >= 0 ? InventoryManager.Instance.Catalog.Get(index).nameKo : "";
        Texture2D ItemIcon(int index) => index >= 0 ? InventoryManager.Instance.Catalog.Get(index).icon : null;

        // ---- 입력 ----

        void Update()
        {
            if (!Running) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            bool space = kb.spaceKey.wasPressedThisFrame;
            if (phase == Phase.Pose && space) posePressed = true;
            if (phase == Phase.Agility && space) jumpRequested = true;

            if (phase == Phase.Fetch && awaitingChoice)
            {
                if (kb.digit1Key.wasPressedThisFrame) chosenCard = 0;
                else if (kb.digit2Key.wasPressedThisFrame) chosenCard = 1;
                else if (kb.digit3Key.wasPressedThisFrame) chosenCard = 2;
                else if (kb.digit4Key.wasPressedThisFrame) chosenCard = 3;
            }
        }

        // ---- 화면 ----

        // ---- 화면 배율 ----
        // 도그쇼 화면은 1080p 픽셀 좌표로 그린다. UiSkin.Boost 배로 키우려고 GUI.matrix 를 걸고,
        // 화면 크기는 SW·SH(배율로 나눈 값)로 잰다 — 위치가 화면 비율로 정해지니 화면 밖으로 나가지 않는다
        float guiScale = 1f;
        float SW => Screen.width / guiScale;
        float SH => Screen.height / guiScale;

        /// <summary>카메라 화면 좌표(픽셀)를 배율을 건 GUI 좌표로. y 는 그대로 아래에서 위 — 쓰는 쪽이 SH - y 로 뒤집는다.</summary>
        Vector3 GuiPoint(Vector3 screen) => new Vector3(screen.x / guiScale, screen.y / guiScale, screen.z);

        void OnGUI()
        {
            if (!Running && noticeTitle == null) return;
            guiScale = UiSkin.ScreenScale;
            if (!Running)
            {
                Matrix4x4 was = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));
                DrawNotice();
                GUI.matrix = was;
                return;
            }
            Matrix4x4 keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));
            DrawGui();
            GUI.matrix = keep;
        }

        void DrawGui()
        {
            GUI.depth = -80;
            EnsureStyles();

            if (phase == Phase.Waiting) { DrawStartButton(); return; }

            // 위쪽 제목 띠
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, SW, 92f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, 10f, SW, 40f), headline, bigStyle);
            GUI.Label(new Rect(20f, 52f, SW - 40f, 30f), subline, smallStyle);

            if (phase == Phase.Pose) DrawRing();
            if (phase == Phase.Fetch) { DrawBubbles(); DrawFetch(); }
            if (phase == Phase.AgilitySpec) DrawSpec();
            if (phase == Phase.Course && course != null) DrawCourse();
            if (phase == Phase.CourseGuide && course != null) DrawCourseGuide();
            if (phase == Phase.Guide) DrawGuide();
            if (phase == Phase.Result) DrawBoard();

            // 카운트다운: "GO!" 는 잠깐만, 숫자는 다음 숫자가 올 때까지
            if (countdown.Length > 0 && (countdown != "GO!" || Time.time < countdownUntil))
            {
                var s = new GUIStyle(bigStyle) { fontSize = 96 };
                s.normal.textColor = countdown == "GO!" ? new Color(0.6f, 1f, 0.75f) : Color.white;
                GUI.Label(new Rect(0f, SH * 0.36f, SW, 120f), countdown, s);
            }
            else if (countdown == "GO!") countdown = "";

            if (Time.time < flashUntil)
            {
                var s = new GUIStyle(bigStyle) { fontSize = 46 };
                s.normal.textColor = flashColor;
                GUI.Label(new Rect(0f, SH * 0.30f, SW, 60f), flashText, s);
            }
        }

        void EnsureStyles()
        {
            if (bigStyle != null) return;
            bigStyle = new GUIStyle(UiSkin.Title) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            bigStyle.normal.textColor = Color.white;
            midStyle = new GUIStyle(UiSkin.Title) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            midStyle.normal.textColor = Color.white;
            smallStyle = new GUIStyle(UiSkin.Label) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            smallStyle.normal.textColor = new Color(1f, 0.95f, 0.85f);

            ringTex = MakeRing(256, 0.08f);
            dotTex = MakeRing(256, 1f);
        }

        static readonly Color PerfectColor = new Color(1f, 0.8f, 0.22f);
        static readonly Color GoodColor = new Color(0.45f, 0.9f, 0.78f);

        /// <summary>
        /// 안쪽 반지름(바깥=1 기준 비율)부터 바깥까지 채운 띠. <paramref name="featherPx"/> 만큼
        /// 양쪽 가장자리를 부드럽게 흐린다 — 딱딱한 테두리 두 줄이 겹쳐 보이던 게 어색했다.
        /// </summary>
        static Texture2D MakeBand(int size, float inner, float featherPx)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float r = size * 0.5f;
            float f = Mathf.Max(1f, featherPx);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));   // 픽셀
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - inner * r) / f + 0.5f))
                            * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 1f - d) / f + 0.5f));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeRing(int size, float thickness)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = thickness >= 1f ? Mathf.Clamp01((1f - d) * size * 0.5f)
                                              : Mathf.Clamp01((thickness * 0.5f - Mathf.Abs(d - (1f - thickness * 0.5f))) * size * 0.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        void DrawStartButton()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, SW, SH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 시연은 쇼 직전에 "사흘 동안 키운 결과"를 한 장 보여 준다 — 가게에서 번 돈 → 훈련 → 능력치 → 쉬워진 종목으로 이어지는 걸 눈으로
            bool recap = Exhibition.Active && hero != null;
            float top = recap ? SH * 0.1f : SH * 0.32f;
            GUI.Label(new Rect(0f, top, SW, 44f), headline, bigStyle);
            GUI.Label(new Rect(0f, top + 46f, SW, 30f), subline, smallStyle);

            float buttonY = SH * 0.5f;
            if (recap) buttonY = DrawRecap(top + 92f) + 20f;

            var r = new Rect((SW - 300f) * 0.5f, buttonY, 300f, 64f);
            if (UiSkin.Click(r, "도그쇼 시작", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 24 }))
                StartCoroutine(RunShow());
        }

        GUIStyle recapTitle, recapRow, recapValue, recapNote;

        /// <summary>
        /// 시연 쇼 시작 화면의 "키운 결과" 판. 번 돈·훈련 횟수 → 능력치 변화 → 마스코트 등급 → 종목이 얼마나 쉬워졌나.
        /// 아래 끝 y 를 돌려준다(그 밑에 시작 버튼).
        /// </summary>
        float DrawRecap(float y)
        {
            if (recapTitle == null || recapTitle.font != UiSkin.Font)
            {
                recapTitle = new GUIStyle(UiSkin.Title) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                recapRow = new GUIStyle(UiSkin.Label) { fontSize = 19, alignment = TextAnchor.MiddleLeft };
                recapValue = new GUIStyle(UiSkin.Title) { fontSize = 20, alignment = TextAnchor.MiddleRight };
                recapNote = new GUIStyle(UiSkin.Label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }

            DogStats st = hero.Stats;
            int start = Exhibition.StartGrowthStat;
            const float W = 520f, RowH = 32f, Pad = 22f;
            float h = Pad * 2f + 36f + RowH * 5f + 12f + 56f + 50f;
            var panel = new Rect((SW - W) * 0.5f, y, W, h);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            float x = panel.x + Pad, w = W - Pad * 2f, cy = panel.y + Pad;
            GUI.Label(new Rect(x, cy, w, 36f), Exhibition.Days + "일 동안 키운 결과", recapTitle);
            cy += 36f;

            Row(x, ref cy, w, RowH, "가게에서 번 돈", Exhibition.Earned.ToString("N0") + "원", Color.white);
            Row(x, ref cy, w, RowH, "훈련 " + Exhibition.Trainings + "번  운동 " + Exhibition.Exercises + "번", "", Color.white);
            Row(x, ref cy, w, RowH, "미모", start + "  >  " + st.Beauty + Gain(st.Beauty - start), new Color(1f, 0.6f, 0.8f));
            Row(x, ref cy, w, RowH, "훈련도", start + "  >  " + st.Training + Gain(st.Training - start), new Color(0.55f, 0.75f, 1f));
            Row(x, ref cy, w, RowH, "어질리티", start + "  >  " + st.Agility + Gain(st.Agility - start), new Color(1f, 0.72f, 0.4f));
            cy += 12f;

            // 마스코트 등급(배지 + 이름)
            int grade = MascotGrade.GradeOf(st.MascotTotal);
            Texture2D badge = MascotGrade.Badge(grade);
            float bx = x + w * 0.5f - 130f;
            if (badge != null) GUI.DrawTexture(new Rect(bx, cy, 52f, 52f), badge, ScaleMode.ScaleToFit);
            var gradeStyle = new GUIStyle(recapTitle) { alignment = TextAnchor.MiddleLeft };
            gradeStyle.normal.textColor = MascotGrade.Colors[grade];
            GUI.Label(new Rect(bx + 62f, cy, 260f, 52f), "마스코트 " + MascotGrade.Names[grade], gradeStyle);
            cy += 56f;

            // 키운 만큼 쉬워진 것 — 미모 심사 PERFECT 띠(미모가 정한다)가 시작 때보다 얼마나 넓어졌나
            float before = Mathf.Lerp(0.012f, 0.045f, Mathf.Clamp01(start / StatFull));
            float now = Mathf.Lerp(0.012f, 0.045f, Mathf.Clamp01(st.Beauty / StatFull));
            int wider = Mathf.RoundToInt((now / before - 1f) * 100f);
            string note = wider > 0
                ? "미모 심사 PERFECT 띠가 처음보다 " + wider + "% 넓어졌다\n키운 만큼 종목이 쉬워진다"
                : "능력치가 높을수록 종목이 쉬워진다\n다음엔 훈련을 더 해 보자";
            GUI.Label(new Rect(x, cy, w, 50f), note, recapNote);

            return panel.yMax;
        }

        void Row(float x, ref float y, float w, float h, string label, string value, Color tint)
        {
            recapRow.normal.textColor = tint;
            GUI.Label(new Rect(x, y, w, h), label, recapRow);
            GUI.Label(new Rect(x, y, w, h), value, recapValue);
            y += h;
        }

        static string Gain(int d) => d > 0 ? "  (+" + d + ")" : "";

        void DrawRing()
        {
            float size = Mathf.Min(SW, SH) * 0.42f;
            Vector2 c = new Vector2(SW * 0.5f, SH * 0.58f);

            // 목표 원 안쪽 메달(바르코 그림). 판정 띠보다 안쪽에 두어 띠를 가리지 않는다
            if (centerArt != null)
            {
                float cr = Mathf.Max(0.05f, targetRadius - goodHalf - 0.015f) * size;
                GUI.color = new Color(1f, 1f, 1f, 0.92f);
                GUI.DrawTexture(new Rect(c.x - cr, c.y - cr, cr * 2f, cr * 2f), centerArt, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }

            // 바깥 GOOD 띠(민트, 반투명·가장자리 부드럽게) 위에 PERFECT 띠(금색, 또렷하게)를 얹는다.
            // 미모가 높을수록 금색 띠가 두껍다
            float g = (targetRadius + goodHalf) * size;
            GUI.color = new Color(GoodColor.r, GoodColor.g, GoodColor.b, 0.42f);
            if (goodTex != null) GUI.DrawTexture(new Rect(c.x - g, c.y - g, g * 2f, g * 2f), goodTex);
            float pr = (targetRadius + perfectHalf) * size;
            GUI.color = new Color(PerfectColor.r, PerfectColor.g, PerfectColor.b, 0.95f);
            if (perfectTex != null) GUI.DrawTexture(new Rect(c.x - pr, c.y - pr, pr * 2f, pr * 2f), perfectTex);

            if (ringRadius > 0f)
            {
                // 줄어드는 원은 지금 누르면 받을 판정의 색으로 물든다
                float diff = Mathf.Abs(ringRadius - targetRadius);
                Color judge = diff <= perfectHalf ? PerfectColor : diff <= goodHalf ? GoodColor : Color.white;
                // 그림 링은 제 색이 있으니 판정 색을 살짝만 입힌다
                GUI.color = ringArt != null ? Color.Lerp(Color.white, judge, 0.65f) : judge;
                float rr = ringRadius * size;
                GUI.DrawTexture(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), ringArt != null ? ringArt : ringTex, ScaleMode.ScaleToFit);
            }
            GUI.color = Color.white;
        }

        /// <summary>①~③ 종목 안내판 — 진행 방식을 줄마다 번호를 붙여 보여 준다. [준비완료](Space)로 닫는다.</summary>
        void DrawGuide()
        {
            const float rowH = 38f;
            float h = 70f + guideLines.Length * rowH + 90f;
            var panel = new Rect(SW * 0.5f - 400f, SH * 0.5f - h * 0.5f + 20f, 800f, h);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);
            GUI.Label(new Rect(panel.x, panel.y + 14f, panel.width, 34f), guideTitle, midStyle);

            var line = new GUIStyle(UiSkin.Label) { fontSize = 17, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            float y = panel.y + 62f;
            for (int i = 0; i < guideLines.Length; i++)
            {
                GUI.Box(new Rect(panel.x + 26f, y + 3f, 28f, 28f), (i + 1).ToString(), UiSkin.Tag(UiSkin.Sky));
                GUI.Label(new Rect(panel.x + 66f, y, panel.width - 90f, 34f), guideLines[i], line);
                y += rowH;
            }

            var ok = new Rect(panel.center.x - 120f, panel.yMax - 66f, 240f, 48f);
            bool space = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (UiSkin.Click(ok, "준비완료 (Space)", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 20 }) || space)
                guideReady = true;
        }

        /// <summary>출발 전 안내판 — 장애물 6개를 지나는 법과 감점 규칙. [준비완료]를 눌러야 출발한다.</summary>
        void DrawCourseGuide()
        {
            var panel = new Rect(SW * 0.5f - 350f, SH * 0.5f - 220f, 700f, 450f);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);
            GUI.Label(new Rect(panel.x, panel.y + 12f, panel.width, 32f), "장애물 달리기 안내", midStyle);

            var line = new GUIStyle(UiSkin.Label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            float y = panel.y + 56f;
            for (int i = 0; i < course.Obstacles.Count; i++)
            {
                ObstacleCourse.Obstacle o = course.Obstacles[i];
                GUI.Box(new Rect(panel.x + 24f, y, 120f, 28f), (i + 1) + ". " + o.nameKo, UiSkin.Tag(UiSkin.Sky));
                GUI.Label(new Rect(panel.x + 156f, y, panel.width - 180f, 28f), o.hint, line);
                y += 38f;
            }
            y += 6f;
            GUI.Label(new Rect(panel.x + 24f, y, panel.width - 48f, 26f),
                      "닿으면 시작! 시작한 장애물은 뒤로 못 가고, 떨어지거나 벗어나면 감점", line);
            GUI.Label(new Rect(panel.x + 24f, y + 26f, panel.width - 48f, 26f),
                      "감점: 떨어지면 -" + ObstacleCourse.FallPenalty + ", 건너뛰면 -" + ObstacleCourse.SkipPenalty
                      + ", 위브 화살표를 놓치면 -" + ObstacleCourse.WeaveMissPenalty, line);
            GUI.Label(new Rect(panel.x + 24f, y + 52f, panel.width - 48f, 26f),
                      "빨리 도착할수록 점수가 높다. 제한 시간 " + CourseLimit.ToString("0") + "초", line);

            var ok = new Rect(panel.center.x - 120f, panel.yMax - 62f, 240f, 48f);
            bool space = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (UiSkin.Click(ok, "준비완료 (Space)", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 20 }) || space)
                guideReady = true;
        }

        void DrawCourse()
        {
            // 장애물 순서표: 지난 것 초록, 다음 것 하늘색, 남은 것 크림
            float w = 108f, gap = 8f;
            int n = course.Obstacles.Count;
            float x = (SW - (n * w + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                ObstacleCourse.Obstacle o = course.Obstacles[i];
                Color c = o.failed ? UiSkin.Coral : o.done ? UiSkin.Green : i == course.Next ? UiSkin.Sky : UiSkin.Cream;
                GUI.Box(new Rect(x + i * (w + gap), 100f, w, 28f), (i + 1) + ". " + o.nameKo, UiSkin.Tag(c));
            }

            GUI.Label(new Rect(SW - 220f, 100f, 200f, 28f), "시간 " + courseTime.ToString("0.0") + " / " + CourseLimit.ToString("0") + "초", UiSkin.Caption);
            if (course.PenaltyPoints > 0)
                GUI.Box(new Rect(SW - 160f, 132f, 120f, 28f), "감점 -" + course.PenaltyPoints, UiSkin.Tag(UiSkin.Coral));

            if (course.Finished) return;
            ObstacleCourse.Obstacle next = course.Obstacles[course.Next];

            // 다음 장애물 입구 위에 표시
            Vector3 sp = GuiPoint(showCamera.WorldToScreenPoint(course.EntryWorld(next) + Vector3.up * 0.9f));
            if (sp.z > 0f)
            {
                float bob = Mathf.Sin(Time.time * 5f) * 4f;
                GUI.Box(new Rect(sp.x - 70f, SH - sp.y - 30f + bob, 140f, 28f), "다음: " + next.nameKo, UiSkin.Tag(UiSkin.Sky));
            }

            if (next.kind == ObstacleCourse.Kind.Table && next.stay > 0f)
                GUI.Label(new Rect(0f, SH * 0.70f, SW, 40f),
                          "버티기 " + next.stay.ToString("0.0") + " / " + ObstacleCourse.TableHold.ToString("0") + "초", midStyle);
            else if (next.kind == ObstacleCourse.Kind.Seesaw && next.endWait > 0f && !next.tipped)
                GUI.Label(new Rect(0f, SH * 0.70f, SW, 40f),
                          "기다리기 " + Mathf.RoundToInt(course.SeesawProgress(next) * 100f) + "%", midStyle);
            if (next.kind == ObstacleCourse.Kind.Weave && next.rhythm) DrawWeaveArrows(next);

            // 구간 한 줄 안내 — 들어설 때 잠깐 떴다가 사라진다
            float since = Time.time - introAt;
            if (introFor == course.Next && since < IntroShow)
            {
                float a = Mathf.Clamp01(since / 0.2f) * Mathf.Clamp01((IntroShow - since) / 0.4f);
                string text = ObstacleCourse.IntroOf(next.kind);
                var style = new GUIStyle(midStyle) { fontSize = 30 };
                float bw = style.CalcSize(new GUIContent(text)).x + 80f;
                // 화면 아래쪽 — 위쪽은 감점 알림(Flash)과 위브 화살표 줄이 쓴다
                var box = new Rect((SW - bw) * 0.5f, SH * 0.80f, bw, 60f);
                Color keep = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Box(box, GUIContent.none, UiSkin.Panel_);
                GUI.Label(box, text, style);
                GUI.color = keep;
            }
        }

        static Texture2D arrowTex;

        /// <summary>
        /// 오른쪽을 보는 굵은 화살표(크림 속, 갈색 테두리, 발바닥 무늬) — Resources/T_UI_WeaveArrow.png.
        /// 다른 방향은 DrawArrow 가 돌려 그리고, 색은 GUI.color 로 입힌다.
        /// 그림이 없으면 예전처럼 단순한 화살표를 굽는다(글꼴에 → 글리프가 없다).
        /// </summary>
        static Texture2D ArrowTexture()
        {
            if (arrowTex != null) return arrowTex;
            arrowTex = Resources.Load<Texture2D>("T_UI_WeaveArrow");
            if (arrowTex != null) return arrowTex;
            const int W = 128, H = 104, Edge = 7;
            bool Inside(float x, float y)
            {
                if (x >= 10f && x <= 70f && y >= 34f && y <= 70f) return true;             // 몸통
                if (x < 60f || x > 118f) return false;                                      // 머리(삼각형)
                float half = 44f * (118f - x) / 58f;
                return Mathf.Abs(y - 52f) <= half;
            }
            var fill = new bool[W, H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) fill[x, y] = Inside(x + 0.5f, y + 0.5f);
            arrowTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var ink = new Color(0.36f, 0.22f, 0.12f, 1f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = Color.clear;
                if (fill[x, y])
                {
                    // 테두리: 안쪽에서 Edge 픽셀 안에 바깥이 있으면
                    bool edge = false;
                    for (int d = 1; d <= Edge && !edge; d++)
                        edge = x - d < 0 || x + d >= W || y - d < 0 || y + d >= H
                               || !fill[x - d, y] || !fill[x + d, y] || !fill[x, y - d] || !fill[x, y + d];
                    c = edge ? ink : Color.white;
                }
                arrowTex.SetPixel(x, y, c);
            }
            arrowTex.Apply();
            return arrowTex;
        }

        /// <summary>화살표 하나. dir 0 위, 1 오른쪽, 2 아래, 3 왼쪽 — 오른쪽 그림을 가운데 기준으로 돌려 그린다.</summary>
        static void DrawArrow(Rect r, int dir, Color tint)
        {
            Matrix4x4 m = GUI.matrix;
            Color keep = GUI.color;
            GUI.color = tint;
            // 오른쪽(1)이 0도, 시계 방향이 +. GUIUtility.RotateAroundPivot 은 배율 행렬(guiScale)이 걸려 있으면 축이 어긋나
            // 화살표가 창 밖으로 튀어 나갔다 — 축을 화면 좌표로 옮겨 직접 돌린다
            // 왼쪽(3)은 돌리지 않고 좌우로 뒤집는다 — 180도 돌리면 발바닥 무늬가 거꾸로 선다
            Vector3 pivot = m.MultiplyPoint3x4(new Vector3(r.center.x, r.center.y, 0f));
            bool mirror = dir == 3;
            GUI.matrix = Matrix4x4.TRS(pivot, Quaternion.Euler(0f, 0f, mirror ? 0f : (dir - 1) * 90f), mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one)
                       * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one) * m;
            GUI.DrawTexture(r, ArrowTexture(), ScaleMode.ScaleToFit);
            GUI.matrix = m;
            GUI.color = keep;
        }

        /// <summary>
        /// 위브 리듬 화살표 줄(화면 위쪽). 맨 앞(지금 맞출 것)이 크게 노랗게 깜빡이고, 뒤로 다음 것들이 흐리게 줄 선다.
        /// 맞추면 줄이 한 칸 당겨지고, 틀리면 맨 앞이 빨갛게 흔들린다. 아래 막대는 남은 시간(10초).
        /// </summary>
        void DrawWeaveArrows(ObstacleCourse.Obstacle o)
        {
            if (o.rSeq == null) return;
            const int Show = 7;
            const float S = 64f, Big = 96f, Gap = 16f;
            float total = Big + (Show - 1) * (S + Gap);
            float x0 = (SW - total) * 0.5f;
            const float top = 150f;

            var back = new Rect(x0 - 28f, top - 16f, total + 56f, Big + 70f);
            GUI.Box(back, GUIContent.none, UiSkin.Panel_);

            bool over = o.rIndex >= o.rSeq.Length || o.rTimeUp;
            float x = x0;
            for (int k = 0; k < Show; k++)
            {
                int i = o.rIndex + k;
                if (i >= o.rSeq.Length) break;
                bool now = k == 0 && !over;
                float size = now ? Big * (1f + Mathf.Sin(Time.time * 10f) * 0.03f) : S;
                float slot = k == 0 ? Big : S;
                float shake = now && Time.time - o.rWrongAt < 0.25f ? Mathf.Sin(Time.time * 70f) * 6f : 0f;
                var r = new Rect(x + (slot - size) * 0.5f + shake, top + (Big - size) * 0.5f, size, size);
                Color tint = !now ? new Color(1f, 0.97f, 0.88f, 0.75f - k * 0.07f)
                           : Time.time - o.rWrongAt < 0.25f ? new Color(1f, 0.45f, 0.4f)
                           : new Color(1f, 0.88f, 0.35f);
                DrawArrow(r, o.rSeq[i], tint);
                x += slot + Gap;
            }

            // 방금 맞춘 화살표가 초록으로 번쩍 사라지는 자리
            float sinceHit = Time.time - o.rHitAt;
            if (sinceHit < 0.2f && o.rIndex > 0)
            {
                float g = 1f + sinceHit * 2f;
                var r = new Rect(x0 + (Big - Big * g) * 0.5f, top + (Big - Big * g) * 0.5f, Big * g, Big * g);
                DrawArrow(r, o.rSeq[o.rIndex - 1], new Color(0.55f, 0.95f, 0.6f, 1f - sinceHit / 0.2f));
            }

            // 남은 시간 막대와 맞춘 개수
            float left = Mathf.Clamp01(1f - o.rTimer / ObstacleCourse.WeaveLimit);
            var bar = new Rect(x0, top + Big + 14f, total, 14f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, new Color(0f, 0f, 0f, 0.35f), 0f, 7f);
            Color fill = left > 0.3f ? new Color(0.55f, 0.85f, 0.6f) : new Color(1f, 0.5f, 0.4f);
            if (left > 0f)
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * left, bar.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, fill, 0f, 7f);
            GUI.Label(new Rect(bar.x, bar.yMax + 2f, total, 24f),
                      o.rIndex + " / " + o.rSeq.Length + "     남은 시간 " + Mathf.Max(0f, ObstacleCourse.WeaveLimit - o.rTimer).ToString("0.0") + "초",
                      UiSkin.Caption);
        }

        void DrawFetch()
        {
            // 바닥 물건 이름표
            foreach (var kv in propOf)
            {
                if (kv.Value == null || !kv.Value.gameObject.activeSelf) continue;
                Vector3 sp = GuiPoint(showCamera.WorldToScreenPoint(kv.Value.position + Vector3.up * (PropSize * 0.9f)));
                if (sp.z <= 0f) continue;
                GUI.Label(new Rect(sp.x - 70f, SH - sp.y - 24f, 140f, 22f), ItemName(kv.Key), UiSkin.Caption);
            }

            // 심사위원이 원하는 물건
            if (requested >= 0 && awaitingChoice)
            {
                var ask = new Rect(SW * 0.5f - 90f, 104f, 180f, 120f);
                GUI.Box(ask, GUIContent.none, UiSkin.Panel_);
                Texture2D icon = ItemIcon(requested);
                if (icon != null) GUI.DrawTexture(new Rect(ask.x + 50f, ask.y + 8f, 80f, 80f), icon, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(ask.x, ask.y + 88f, ask.width, 26f), ItemName(requested), UiSkin.Caption);
            }

            if (awaitingChoice)
            {
                float w = 150f, h = 170f, gap = 16f;
                float total = cards.Length * w + (cards.Length - 1) * gap;
                float x = (SW - total) * 0.5f, y = SH - h - 24f;
                for (int i = 0; i < cards.Length; i++)
                {
                    var r = new Rect(x + i * (w + gap), y, w, h);
                    GUI.Box(r, GUIContent.none, UiSkin.Panel_);
                    Texture2D icon = ItemIcon(cards[i]);
                    if (icon != null) GUI.DrawTexture(new Rect(r.x + 20f, r.y + 14f, w - 40f, w - 40f), icon, ScaleMode.ScaleToFit);
                    GUI.Label(new Rect(r.x, r.y + h - 48f, w, 22f), ItemName(cards[i]), UiSkin.Caption);
                    GUI.Label(new Rect(r.x, r.y + h - 26f, w, 20f), "[" + (i + 1) + "]", UiSkin.Caption);
                    if (UiSkin.Click(r, GUIContent.none, GUIStyle.none)) chosenCard = i;
                }
            }
        }

        void DrawSpec()
        {
            // 입양 첫날(스탯 0) 대비 지금 — 막대가 차오르는 연출
            float grow = Mathf.Clamp01((Time.time - specShownAt) / 1.6f);
            var panel = new Rect(SW * 0.5f - 300f, SH * 0.5f - 120f, 600f, 250f);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);
            GUI.Label(new Rect(panel.x, panel.y + 10f, panel.width, 32f), hero.DisplayName + "의 지금 몸 상태", midStyle);

            DrawBar(panel, 0, "달리기 속도", Mathf.Lerp(2.2f, runSpeed, grow).ToString("0.0") + " m/s", Mathf.Lerp(0f, agilityN, grow), UiSkin.Sky);
            DrawBar(panel, 1, "점프 높이", Mathf.Lerp(0.35f, jumpHeight, grow).ToString("0.00") + " m", Mathf.Lerp(0f, agilityN, grow), UiSkin.Green);
            DrawBar(panel, 2, "어질리티", Mathf.RoundToInt(hero.Stats.Agility * grow) + " / " + Mathf.RoundToInt(StatFull),
                    Mathf.Lerp(0f, agilityN, grow), UiSkin.Coral);

            GUI.Label(new Rect(panel.x, panel.yMax - 34f, panel.width, 26f), "입양 첫날보다 속도 " + (runSpeed / 2.2f).ToString("0.0") + "배"
                      + "  /  점프 " + (jumpHeight / 0.35f).ToString("0.0") + "배", UiSkin.Caption);
        }

        void DrawBar(Rect panel, int row, string label, string value, float fill, Color color)
        {
            float y = panel.y + 56f + row * 50f;
            GUI.Label(new Rect(panel.x + 24f, y, 150f, 30f), label, UiSkin.Label);
            var bg = new Rect(panel.x + 180f, y + 6f, 280f, 20f);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(bg.x, bg.y, bg.width * fill, bg.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(bg.xMax + 12f, y, 120f, 30f), value, UiSkin.Label);
        }

        // ---- 순위표 ----

        [Header("순위표")]
        [SerializeField] Texture2D boardArt;
        [Tooltip("금·은·동 순서")]
        [SerializeField] Texture2D[] medalArt = new Texture2D[0];
        [Tooltip("견종 번호 순서의 초상화(메인 메뉴 견종 카드와 같은 그림)")]
        [SerializeField] Texture2D[] breedPortraits = new Texture2D[0];

        /// <summary>순위표 그림 안쪽 빈 판(그림 크기 대비 비율). 그림에 맞춰 잡았다.</summary>
        static readonly Rect BoardInner = new Rect(0.17f, 0.215f, 0.66f, 0.65f);
        /// <summary>위쪽 빨간 띠(제목 자리).</summary>
        static readonly Rect BoardRibbon = new Rect(0.2f, 0.06f, 0.6f, 0.09f);
        /// <summary>6위부터 한 줄씩 공개하는 간격(초).</summary>
        const float RevealStep = 0.45f;

        float boardShownAt;
        GUIStyle rowNameStyle, rowBreedStyle, rowScoreStyle, rowRankStyle, placeStyle, cheerStyle;

        /// <summary>
        /// 최종 순위표. 6위부터 1위까지 한 줄씩 올라오고, 다 열리면 몇 등인지 크게 띄운다.
        /// 1~3위는 금·은·동 메달, 주인공 줄은 노랗게 칠한다.
        /// </summary>
        void DrawBoard()
        {
            ChampionshipManager champ = ChampionshipManager.Instance;
            if (champ == null || !champ.HasShowResult) return;
            System.Collections.Generic.List<ChampionshipManager.ShowEntry> list = champ.ShowBoard;
            int n = list.Count;

            if (rowNameStyle == null)
            {
                rowNameStyle = new GUIStyle(UiSkin.Title) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
                rowNameStyle.normal.textColor = UiSkin.Ink;
                rowBreedStyle = new GUIStyle(UiSkin.Label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
                rowBreedStyle.normal.textColor = new Color(UiSkin.Ink.r, UiSkin.Ink.g, UiSkin.Ink.b, 0.7f);
                rowScoreStyle = new GUIStyle(UiSkin.Title) { fontSize = 22, alignment = TextAnchor.MiddleRight };
                rowScoreStyle.normal.textColor = UiSkin.Ink;
                rowRankStyle = new GUIStyle(UiSkin.Title) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                rowRankStyle.normal.textColor = UiSkin.Ink;
                placeStyle = new GUIStyle(UiSkin.Title) { fontSize = 64, alignment = TextAnchor.MiddleCenter };
                cheerStyle = new GUIStyle(UiSkin.Title) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                cheerStyle.normal.textColor = Color.white;
            }

            // 3:4 판을 화면 오른쪽에. 강아지는 가운데에서 왼쪽으로 보인다
            float h = Mathf.Min(SH - 120f, 660f);
            float w = h * 0.75f;
            var board = new Rect(Mathf.Min(SW * 0.52f, SW - w - 20f), 100f, w, h);
            if (boardArt != null) GUI.DrawTexture(board, boardArt, ScaleMode.StretchToFill);
            else GUI.Box(board, GUIContent.none, UiSkin.Panel_);
            cheerStyle.fontSize = Mathf.RoundToInt(board.height * 0.042f);
            GUI.Label(new Rect(board.x + board.width * BoardRibbon.x, board.y + board.height * BoardRibbon.y,
                               board.width * BoardRibbon.width, board.height * BoardRibbon.height), "최종 순위", cheerStyle);
            cheerStyle.fontSize = 24;

            var inner = new Rect(board.x + board.width * BoardInner.x, board.y + board.height * BoardInner.y,
                                 board.width * BoardInner.width, board.height * BoardInner.height);
            float rowH = inner.height / n;
            float elapsed = Time.time - boardShownAt;

            for (int i = 0; i < n; i++)
            {
                // 아래(6위)부터 공개
                float showAt = 0.3f + (n - 1 - i) * RevealStep;
                if (elapsed < showAt) continue;
                float a = Mathf.Clamp01((elapsed - showAt) / 0.25f);
                ChampionshipManager.ShowEntry e = list[i];
                var row = new Rect(inner.x, inner.y + i * rowH + (1f - a) * 12f, inner.width, rowH);

                GUI.color = new Color(1f, 1f, 1f, a);
                if (e.hero)
                {
                    GUI.color = new Color(1f, 0.82f, 0.25f, 0.45f * a);
                    GUI.DrawTexture(new Rect(row.x - 4f, row.y + 2f, row.width + 8f, row.height - 4f), Texture2D.whiteTexture);
                    GUI.color = new Color(1f, 1f, 1f, a);
                }

                float icon = Mathf.Min(rowH * 0.86f, 64f);
                var rankRect = new Rect(row.x, row.y + (rowH - icon) * 0.5f, icon, icon);
                if (i < 3 && i < medalArt.Length && medalArt[i] != null) GUI.DrawTexture(rankRect, medalArt[i], ScaleMode.ScaleToFit);
                else GUI.Label(rankRect, (i + 1).ToString(), rowRankStyle);

                float x = rankRect.xMax + 8f;
                Texture2D face = e.breedIndex >= 0 && e.breedIndex < breedPortraits.Length ? breedPortraits[e.breedIndex] : null;
                if (face != null)
                {
                    GUI.DrawTexture(new Rect(x, row.y + (rowH - icon) * 0.5f, icon, icon), face, ScaleMode.ScaleToFit);
                    x += icon + 8f;
                }

                float scoreW = 70f;
                GUI.Label(new Rect(x, row.y + rowH * 0.12f, row.xMax - scoreW - x, rowH * 0.5f), e.name, rowNameStyle);
                GUI.Label(new Rect(x, row.y + rowH * 0.55f, row.xMax - scoreW - x, rowH * 0.35f), e.breed + (e.hero ? "  (우리 강아지)" : ""), rowBreedStyle);
                GUI.Label(new Rect(row.xMax - scoreW, row.y, scoreW, rowH), e.score + "점", rowScoreStyle);
            }
            GUI.color = Color.white;

            // 다 열리면 몇 등인지
            float doneAt = 0.3f + n * RevealStep + 0.2f;
            if (elapsed < doneAt) return;

            int rank = champ.ShowRank;
            var left = new Rect(0f, 0f, board.x, SH);
            placeStyle.normal.textColor = rank == 1 ? PerfectColor : rank <= 3 ? new Color(0.85f, 0.9f, 1f) : Color.white;
            GUI.Label(new Rect(left.x, SH * 0.62f, left.width, 80f), rank + "등!", placeStyle);
            GUI.Label(new Rect(left.x, SH * 0.62f + 78f, left.width, 34f),
                      rank == 1 ? "우승! 최고의 강아지로 뽑혔다" : "다음 대회를 노려보자, 화이팅!", cheerStyle);

            var ok = new Rect(left.center.x - 130f, SH - 96f, 260f, 56f);
            if (UiSkin.Click(ok, "확인", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 22 }))
                resultReady = false;
        }
    }
}
