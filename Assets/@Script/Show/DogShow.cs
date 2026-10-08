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
        [SerializeField] string[] fetchItemNames = { "장난감", "뼈다귀 장난감", "고기 인형", "목줄", "간식" };

        /// <summary>스탯을 0~1 로 펼 때 쓰는 기준. 46차 측정에서 30일 최적 플레이가 미모·훈련도 각 220 안팎이었다.</summary>
        const float StatFull = 240f;

        const int PoseRounds = 5;
        const int FetchRounds = 5;

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
            GameManager g = GameManager.Instance;
            TimeManager t = TimeManager.Instance;
            if (g == null || t == null || t.IsDayOver) return;
            if (!ChampionshipManager.Instance.IsFinalDay(g.Day)) return;

            // 무인 측정은 사람이 못 누르니 건너뛴다 — 배수 1(=스탯 그대로)로 하루를 마친다
            if (Debugging.MeasurementMode.Instance != null && Debugging.MeasurementMode.Instance.Active) return;

            Begin();
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

        void Begin()
        {
            hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (hero == null || stage == null || showCamera == null) return;

            phase = Phase.Waiting;
            Running = true;
            headline = "Day 30  도그쇼 챔피언십";
            subline = "30일 동안 함께한 " + hero.DisplayName + "와(과) 무대에 오를 시간이다";

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
                "다섯 번의 평균이 점수, 총점의 20%");
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
                yield return Wait(1f);
            }
            countdown = "GO!";
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
                        if (diff <= perfectHalf) { got = 1f; Flash("PERFECT!", PerfectColor); }
                        else if (diff <= goodHalf) { got = 0.6f; Flash("GOOD", GoodColor); }
                        else Flash("MISS", new Color(1f, 0.55f, 0.5f));
                        judged = true;
                        break;
                    }
                    yield return null;
                }

                if (!judged) Flash("MISS", new Color(1f, 0.55f, 0.5f));
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

            float success = Mathf.Lerp(0.35f, 0.95f, trainingN);
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
                if (Random.value > trainingN * 0.9f)
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
                    Say("잘했어!", 1.4f);
                    hero.Animator.Play(DogAnim.WagTail);
                }
                else
                {
                    Flash(ItemName(broughtItem) + "...", new Color(1f, 0.55f, 0.5f));
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
            Vector3 sp = showCamera.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;
            float w = Mathf.Max(60f, text.Length * 18f + 30f);
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - 40f, w, 38f);
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

            PlaceDog(laneStart.position, Quaternion.LookRotation(laneEnd.position - laneStart.position));
            AimCamera(laneStart.position, 4.2f, 1.6f, 0f);
            headline = "어질리티";
            subline = "허들 앞에서 Space 로 점프! 30일 동안 키운 몸이 그대로 드러난다";
            specShownAt = Time.time;
            yield return Wait(4.2f);

            // 출발선에서 안내 → 3초 세고 출발 — 바로 뛰면 첫 허들까지 마음의 준비를 할 틈이 없었다
            phase = Phase.Agility;
            headline = "어질리티";
            subline = "안내를 읽고 [준비완료]를 누르면 3초 뒤 시작";
            PlaceDog(laneStart.position, Quaternion.LookRotation(laneEnd.position - laneStart.position));
            hero.Animator.Play(DogAnim.Idle);
            FollowCamera(hero.transform.position);
            yield return Guide("3. 어질리티  (허들 " + hurdles.Length + "개)",
                "출발하면 강아지가 레인을 따라 저절로 달린다",
                "허들 바로 앞에서 Space 로 점프! 너무 이르거나 늦으면 허들에 걸린다",
                "점프 중에는 다시 뛸 수 없다. 착지한 뒤 다음 허들을 노리자",
                "어질리티가 높을수록 빠르고, 높고 오래 뛴다  (지금 어질리티 " + hero.Stats.Agility + ")",
                "넘은 허들 수가 점수, 총점의 25%");

            subline = "Space: 점프";
            cleared = 0;
            jumpRequested = false;
            jumpStart = -10f;

            float baseY = laneStart.position.y + DogBaseOffset();
            Vector3 dir = (laneEnd.position - laneStart.position).normalized;
            float length = Vector3.Distance(laneStart.position, laneEnd.position);
            float pos = 0f;
            var passed = new bool[hurdles.Length];

            hero.Animator.Play(DogAnim.Run);
            hero.Animator.SetPlaybackSpeed(Mathf.Lerp(1f, 1.6f, agilityN));

            while (pos < length)
            {
                pos += runSpeed * Time.deltaTime;
                if (jumpRequested && Time.time - jumpStart > airTime) jumpStart = Time.time;
                jumpRequested = false;

                float air = Time.time - jumpStart;
                float h = air < airTime ? jumpHeight * 4f * (air / airTime) * (1f - air / airTime) : 0f;
                Vector3 p = laneStart.position + dir * pos;
                hero.transform.position = new Vector3(p.x, baseY + h, p.z);

                for (int i = 0; i < hurdles.Length; i++)
                {
                    if (passed[i] || hurdles[i] == null) continue;
                    float hurdleAt = Vector3.Dot(hurdles[i].position - laneStart.position, dir);
                    if (pos < hurdleAt) continue;

                    passed[i] = true;
                    // 바가 ~0.3m. 그 순간 그만큼 떠 있어야 넘는다
                    if (h > 0.25f) { cleared++; Flash("CLEAR!", new Color(0.6f, 1f, 0.75f)); }
                    else { Flash("쿵!", new Color(1f, 0.55f, 0.5f)); KnockHurdle(hurdles[i]); }
                }

                FollowCamera(hero.transform.position);
                yield return null;
            }

            hero.Animator.SetPlaybackSpeed(1f);
            hero.Animator.Play(DogAnim.WagTail);
            agilityScore = hurdles.Length > 0 ? cleared / (float)hurdles.Length : 0f;
            headline = "어질리티 끝";
            subline = "허들 " + hurdles.Length + "개 중 " + cleared + "개";
            yield return Wait(1.8f);
        }

        /// <summary>코스 제한 시간. 넘기면 거기까지 지난 장애물만 센다.</summary>
        const float CourseLimit = 90f;
        float courseTime;
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
                // 화면 기준으로 움직인다 — W 는 화면 위쪽
                Vector3 camF = Flat(showCamera.transform.forward).normalized;
                Vector3 camR = Flat(showCamera.transform.right).normalized;
                Vector3 dir = camF * input.y + camR * input.x;
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                bool still = dir.sqrMagnitude < 0.01f;

                Vector3 pos = hero.transform.position + dir * runSpeed * dt;
                Vector3 ls = stage.InverseTransformPoint(pos);
                ls.x = Mathf.Clamp(ls.x, ObstacleCourse.Area.xMin, ObstacleCourse.Area.xMax);
                ls.z = Mathf.Clamp(ls.z, ObstacleCourse.Area.yMin, ObstacleCourse.Area.yMax);
                // 장애물에 닿아 시작했으면 지나온 쪽으로는 못 돌아간다(옆으로는 자유)
                Vector3 from = stage.InverseTransformPoint(hero.transform.position);
                ls = course.ConstrainForward(new Vector3(from.x, 0f, from.z), new Vector3(ls.x, 0f, ls.z));
                pos = stage.TransformPoint(new Vector3(ls.x, 0f, ls.z));

                Vector3 normal;
                float surf = course.SurfaceY(pos, floorY, out normal);
                y = Mathf.Lerp(y, surf + DogBaseOffset(), 1f - Mathf.Exp(-18f * dt));
                pos.y = y;
                hero.transform.position = pos;

                if (!still) courseFacing = dir.normalized;
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
            float par = 50f / Mathf.Max(0.5f, runSpeed) + ObstacleCourse.SeesawWait + ObstacleCourse.TableHold + 6f;
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
            resultReady = true;
            while (resultReady) yield return null;

            // 결과 발표는 마감 화면이 한다 — 같은 경로로 랭크·엔딩까지 이어진다
            Running = false;
            phase = Phase.None;
            if (course != null) course.Clear();
            Restore();
            TimeManager.Instance.EndDayNow();
        }

        bool resultReady;
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

        void KnockHurdle(Transform hurdle)
        {
            // 허들을 진행 방향으로 기울여 넘어뜨린다. 다음 판이 없으니 되돌리지 않는다
            Transform model = hurdle.Find("Model");
            if (model != null) model.localRotation = Quaternion.Euler(0f, 0f, -28f) * model.localRotation;
            Transform bar = hurdle.Find("Bar");
            if (bar != null) bar.localRotation = Quaternion.Euler(0f, 0f, 35f);
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

        void OnGUI()
        {
            if (!Running) return;
            GUI.depth = -80;
            EnsureStyles();

            if (phase == Phase.Waiting) { DrawStartButton(); return; }

            // 위쪽 제목 띠
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 92f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, 10f, Screen.width, 40f), headline, bigStyle);
            GUI.Label(new Rect(20f, 52f, Screen.width - 40f, 30f), subline, smallStyle);

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
                GUI.Label(new Rect(0f, Screen.height * 0.36f, Screen.width, 120f), countdown, s);
            }
            else if (countdown == "GO!") countdown = "";

            if (Time.time < flashUntil)
            {
                var s = new GUIStyle(bigStyle) { fontSize = 46 };
                s.normal.textColor = flashColor;
                GUI.Label(new Rect(0f, Screen.height * 0.30f, Screen.width, 60f), flashText, s);
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
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(0f, Screen.height * 0.32f, Screen.width, 44f), headline, bigStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.32f + 46f, Screen.width, 30f), subline, smallStyle);

            var r = new Rect((Screen.width - 300f) * 0.5f, Screen.height * 0.5f, 300f, 64f);
            if (GUI.Button(r, "도그쇼 시작", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 24 }))
                StartCoroutine(RunShow());
        }

        void DrawRing()
        {
            float size = Mathf.Min(Screen.width, Screen.height) * 0.42f;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.58f);

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
            var panel = new Rect(Screen.width * 0.5f - 400f, Screen.height * 0.5f - h * 0.5f + 20f, 800f, h);
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
            if (GUI.Button(ok, "준비완료 (Space)", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 20 }) || space)
                guideReady = true;
        }

        /// <summary>출발 전 안내판 — 장애물 6개를 지나는 법과 감점 규칙. [준비완료]를 눌러야 출발한다.</summary>
        void DrawCourseGuide()
        {
            var panel = new Rect(Screen.width * 0.5f - 350f, Screen.height * 0.5f - 220f, 700f, 450f);
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
                      + ", 봉에 닿으면 -" + ObstacleCourse.TouchPenalty + ", 지그재그 빼먹은 칸 -" + ObstacleCourse.WeaveMissPenalty, line);
            GUI.Label(new Rect(panel.x + 24f, y + 52f, panel.width - 48f, 26f),
                      "빨리 도착할수록 점수가 높다. 제한 시간 " + CourseLimit.ToString("0") + "초", line);

            var ok = new Rect(panel.center.x - 120f, panel.yMax - 62f, 240f, 48f);
            bool space = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (GUI.Button(ok, "준비완료 (Space)", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 20 }) || space)
                guideReady = true;
        }

        void DrawCourse()
        {
            // 장애물 순서표: 지난 것 초록, 다음 것 하늘색, 남은 것 크림
            float w = 108f, gap = 8f;
            int n = course.Obstacles.Count;
            float x = (Screen.width - (n * w + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                ObstacleCourse.Obstacle o = course.Obstacles[i];
                Color c = o.failed ? UiSkin.Coral : o.done ? UiSkin.Green : i == course.Next ? UiSkin.Sky : UiSkin.Cream;
                GUI.Box(new Rect(x + i * (w + gap), 100f, w, 28f), (i + 1) + ". " + o.nameKo, UiSkin.Tag(c));
            }

            GUI.Label(new Rect(Screen.width - 220f, 100f, 200f, 28f), "시간 " + courseTime.ToString("0.0") + " / " + CourseLimit.ToString("0") + "초", UiSkin.Caption);
            if (course.PenaltyPoints > 0)
                GUI.Box(new Rect(Screen.width - 160f, 132f, 120f, 28f), "감점 -" + course.PenaltyPoints, UiSkin.Tag(UiSkin.Coral));

            if (course.Finished) return;
            ObstacleCourse.Obstacle next = course.Obstacles[course.Next];

            // 다음 장애물 입구 위에 표시
            Vector3 sp = showCamera.WorldToScreenPoint(course.EntryWorld(next) + Vector3.up * 0.9f);
            if (sp.z > 0f)
            {
                float bob = Mathf.Sin(Time.time * 5f) * 4f;
                GUI.Box(new Rect(sp.x - 70f, Screen.height - sp.y - 30f + bob, 140f, 28f), "다음: " + next.nameKo, UiSkin.Tag(UiSkin.Sky));
            }

            if (next.kind == ObstacleCourse.Kind.Table && next.stay > 0f)
                GUI.Label(new Rect(0f, Screen.height * 0.70f, Screen.width, 40f),
                          "버티기 " + next.stay.ToString("0.0") + " / " + ObstacleCourse.TableHold.ToString("0") + "초", midStyle);
            else if (next.kind == ObstacleCourse.Kind.Seesaw && next.endWait > 0f && !next.tipped)
                GUI.Label(new Rect(0f, Screen.height * 0.70f, Screen.width, 40f),
                          "기다리기 " + Mathf.RoundToInt(course.SeesawProgress(next) * 100f) + "%", midStyle);
            else if (next.kind == ObstacleCourse.Kind.Weave && next.entered)
                GUI.Label(new Rect(0f, Screen.height * 0.70f, Screen.width, 40f),
                          "지그재그 " + next.alternations + " / " + (ObstacleCourse.PoleCount - 1), midStyle);
        }

        void DrawFetch()
        {
            // 바닥 물건 이름표
            foreach (var kv in propOf)
            {
                if (kv.Value == null || !kv.Value.gameObject.activeSelf) continue;
                Vector3 sp = showCamera.WorldToScreenPoint(kv.Value.position + Vector3.up * (PropSize * 0.9f));
                if (sp.z <= 0f) continue;
                GUI.Label(new Rect(sp.x - 70f, Screen.height - sp.y - 24f, 140f, 22f), ItemName(kv.Key), UiSkin.Caption);
            }

            // 심사위원이 원하는 물건
            if (requested >= 0 && awaitingChoice)
            {
                var ask = new Rect(Screen.width * 0.5f - 90f, 104f, 180f, 120f);
                GUI.Box(ask, GUIContent.none, UiSkin.Panel_);
                Texture2D icon = ItemIcon(requested);
                if (icon != null) GUI.DrawTexture(new Rect(ask.x + 50f, ask.y + 8f, 80f, 80f), icon, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(ask.x, ask.y + 88f, ask.width, 26f), ItemName(requested), UiSkin.Caption);
            }

            if (awaitingChoice)
            {
                float w = 150f, h = 170f, gap = 16f;
                float total = cards.Length * w + (cards.Length - 1) * gap;
                float x = (Screen.width - total) * 0.5f, y = Screen.height - h - 24f;
                for (int i = 0; i < cards.Length; i++)
                {
                    var r = new Rect(x + i * (w + gap), y, w, h);
                    GUI.Box(r, GUIContent.none, UiSkin.Panel_);
                    Texture2D icon = ItemIcon(cards[i]);
                    if (icon != null) GUI.DrawTexture(new Rect(r.x + 20f, r.y + 14f, w - 40f, w - 40f), icon, ScaleMode.ScaleToFit);
                    GUI.Label(new Rect(r.x, r.y + h - 48f, w, 22f), ItemName(cards[i]), UiSkin.Caption);
                    GUI.Label(new Rect(r.x, r.y + h - 26f, w, 20f), "[" + (i + 1) + "]", UiSkin.Caption);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) chosenCard = i;
                }
            }
        }

        void DrawSpec()
        {
            // 입양 첫날(스탯 0) 대비 지금 — 막대가 차오르는 연출
            float grow = Mathf.Clamp01((Time.time - specShownAt) / 1.6f);
            var panel = new Rect(Screen.width * 0.5f - 300f, Screen.height * 0.5f - 120f, 600f, 250f);
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
            float h = Mathf.Min(Screen.height - 120f, 660f);
            float w = h * 0.75f;
            var board = new Rect(Mathf.Min(Screen.width * 0.52f, Screen.width - w - 20f), 100f, w, h);
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
            var left = new Rect(0f, 0f, board.x, Screen.height);
            placeStyle.normal.textColor = rank == 1 ? PerfectColor : rank <= 3 ? new Color(0.85f, 0.9f, 1f) : Color.white;
            GUI.Label(new Rect(left.x, Screen.height * 0.62f, left.width, 80f), rank + "등!", placeStyle);
            GUI.Label(new Rect(left.x, Screen.height * 0.62f + 78f, left.width, 34f),
                      rank == 1 ? "우승! 최고의 강아지로 뽑혔다" : "다음 대회를 노려보자, 화이팅!", cheerStyle);

            var ok = new Rect(left.center.x - 130f, Screen.height - 96f, 260f, 56f);
            if (GUI.Button(ok, "확인", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 22 }))
                resultReady = false;
        }
    }
}
