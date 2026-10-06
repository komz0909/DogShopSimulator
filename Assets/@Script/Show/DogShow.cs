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
    /// "도그쇼 시작" 버튼을 누르면 내 강아지가 무대로 옮겨지고 미니게임 셋을 한다.
    ///
    /// ① 미모 심사 — 줄어드는 링이 목표 원에 겹칠 때 Space 로 포즈 (5회)
    /// ② 훈련 심사 — 심사위원이 말한 물건 카드를 골라 지시, 강아지가 물어 온다 (5회)
    /// ③ 어질리티 — 달리는 강아지를 Space 로 점프시켜 허들 5개를 넘긴다
    ///
    /// <b>스탯이 결과를 정하고 실력은 거든다.</b> 미모가 높으면 포즈 판정 구간이 넓고, 훈련도가
    /// 높으면 엉뚱한 물건을 덜 물어 오고, 둘을 합친 몸 상태가 달리기 속도·점프 높이·체공 시간을
    /// 정한다. 세 판의 성적 평균이 챔피언십 점수에 0.8~1.2 배로 곱해진다
    /// (<see cref="ChampionshipManager.ShowMultiplier"/>).
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

        [Header("훈련 심사 물건(상품 이름)")]
        [SerializeField] string[] fetchItemNames = { "장난감", "뼈다귀 장난감", "고기 인형", "목줄", "간식" };

        /// <summary>스탯을 0~1 로 펼 때 쓰는 기준. 46차 측정에서 30일 최적 플레이가 미모·훈련도 각 220 안팎이었다.</summary>
        const float StatFull = 240f;

        const int PoseRounds = 5;
        const int FetchRounds = 5;

        /// <summary>물어 오는 달리기 속도. 4.5 면 왕복이 2초도 안 걸려 무엇을 물었는지 볼 틈이 없었다.</summary>
        const float FetchRunSpeed = 3.0f;

        public static DogShow Instance { get; private set; }

        /// <summary>쇼가 진행 중이다(시작 버튼 대기 포함).</summary>
        public static bool Running { get; private set; }

        enum Phase { None, Waiting, Intro, Pose, Fetch, AgilitySpec, Agility, Result }
        Phase phase = Phase.None;

        Dog hero;
        float beautyN, trainingN, bodyN;   // 0~1
        float poseScore, fetchScore, agilityScore;   // 0~1

        // 화면 글
        string headline = "";
        string subline = "";
        float flashUntil;
        string flashText = "";
        Color flashColor = Color.white;

        // ① 미모
        float ringRadius;     // 0~1, 줄어든다
        float targetRadius = 0.32f;   // 미모로 정한다(PoseGame)
        float poseWindow;     // 판정 반폭
        bool posePressed;
        Texture2D ringTex, dotTex, zoneTex;

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

        void Begin()
        {
            hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (hero == null || stage == null || showCamera == null) return;

            phase = Phase.Waiting;
            Running = true;
            headline = "Day 30 — 도그쇼 챔피언십";
            subline = "30일 동안 함께한 " + hero.BreedKo + "와(과) 무대에 오를 시간이다";

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
            bodyN = (beautyN + trainingN) * 0.5f;

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
            subline = "링이 가운데 원에 겹칠 때 Space — 5번. 미모가 높을수록 판정이 넉넉하다";
            yield return Wait(2.6f);
        }

        IEnumerator PoseGame()
        {
            phase = Phase.Pose;
            // 미모가 높을수록 목표 원이 커지고 판정 띠도 두꺼워진다 — 둘이 같이 커져야
            // "키운 만큼 쉬워졌다"가 눈에 보인다
            targetRadius = Mathf.Lerp(0.24f, 0.38f, beautyN);
            poseWindow = Mathf.Lerp(0.035f, 0.11f, beautyN);
            zoneTex = MakeRing(256, (poseWindow * 2f) / (targetRadius + poseWindow));
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
                        if (diff <= poseWindow * 0.5f) { got = 1f; Flash("PERFECT!", new Color(1f, 0.85f, 0.3f)); }
                        else if (diff <= poseWindow) { got = 0.6f; Flash("GOOD", new Color(0.6f, 1f, 0.75f)); }
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
            subline = "심사위원이 말한 물건 카드를 골라 지시하자. 훈련이 덜 됐으면 엉뚱한 걸 물어 온다";
            yield return Wait(2.6f);

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
                yield return RunTo(rowFront, FetchRunSpeed);

                // 훈련이 덜 됐으면 엉뚱한 받침대에서 한 번 멈칫한다 — 헤매는 게 보여야 한다
                if (Random.value > trainingN * 0.9f)
                {
                    int decoy = RandomOther(broughtItem);
                    if (propHome.ContainsKey(decoy))
                    {
                        yield return WalkTo(RowFront(propHome[decoy]), FetchRunSpeed * 0.55f);
                        FaceTo(propHome[decoy]);
                        hero.Animator.Play(DogAnim.Eat);   // 킁킁
                        DogSays("?", 0.9f);
                        yield return Wait(0.9f);
                    }
                }

                Vector3 at = prop != null ? propHome[broughtItem] : rowCenter;
                yield return WalkTo(RowFront(at), FetchRunSpeed * 0.55f);
                FaceTo(at);

                // 코로 톡 — 그 물건이 쏙 사라진다(골랐다는 표시)
                hero.Animator.Play(DogAnim.Eat);
                yield return Wait(0.35f);
                if (prop != null) yield return PopOut(prop);
                hero.Animator.Play(DogAnim.WagTail);
                yield return Wait(0.3f);

                // 갔던 길로 돌아온다
                yield return RunTo(rowFront, FetchRunSpeed);
                yield return RunTo(fetchStart.position, FetchRunSpeed);
                hero.transform.rotation = faceRow;
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

        void FaceTo(Vector3 target)
        {
            Vector3 d = Flat(target - hero.transform.position);
            if (d.sqrMagnitude > 0.0001f) hero.transform.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>걷기. 옆으로 줄을 훑을 때 쓴다 — 뛰면 찾는 게 아니라 지나가는 것처럼 보인다.</summary>
        IEnumerator WalkTo(Vector3 target, float speed)
        {
            hero.Animator.Play(DogAnim.Walk);
            Vector3 flat = new Vector3(target.x, target.y + DogBaseOffset(), target.z);
            while (true)
            {
                Vector3 p = hero.transform.position;
                Vector3 to = Flat(flat - p);
                if (to.magnitude < 0.05f) break;
                hero.transform.rotation = Quaternion.LookRotation(to);
                hero.transform.position = Vector3.MoveTowards(p, flat, speed * Time.deltaTime);
                yield return null;
            }
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
            runSpeed = Mathf.Lerp(2.2f, 4.0f, bodyN);
            jumpHeight = Mathf.Lerp(0.35f, 1.0f, bodyN);
            airTime = Mathf.Lerp(0.42f, 0.8f, bodyN);

            PlaceDog(laneStart.position, Quaternion.LookRotation(laneEnd.position - laneStart.position));
            AimCamera(laneStart.position, 4.2f, 1.6f, 0f);
            headline = "어질리티";
            subline = "허들 앞에서 Space 로 점프! 30일 동안 키운 몸이 그대로 드러난다";
            specShownAt = Time.time;
            yield return Wait(4.2f);

            // 출발선에서 3초 세고 출발 — 바로 뛰면 첫 허들까지 마음의 준비를 할 틈이 없었다
            phase = Phase.Agility;
            headline = "어질리티";
            subline = "출발선에서 대기 — Space 로 점프";
            PlaceDog(laneStart.position, Quaternion.LookRotation(laneEnd.position - laneStart.position));
            hero.Animator.Play(DogAnim.Idle);
            FollowCamera(hero.transform.position);
            for (int n = 3; n >= 1; n--)
            {
                countdown = n.ToString();
                yield return Wait(1f);
            }
            countdown = "GO!";
            countdownUntil = Time.time + 0.7f;

            subline = "Space — 점프";
            cleared = 0;
            jumpRequested = false;
            jumpStart = -10f;

            float baseY = laneStart.position.y + DogBaseOffset();
            Vector3 dir = (laneEnd.position - laneStart.position).normalized;
            float length = Vector3.Distance(laneStart.position, laneEnd.position);
            float pos = 0f;
            var passed = new bool[hurdles.Length];

            hero.Animator.Play(DogAnim.Run);
            hero.Animator.SetPlaybackSpeed(Mathf.Lerp(1f, 1.6f, bodyN));

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

        IEnumerator Result()
        {
            phase = Phase.Result;
            float avg = (poseScore + fetchScore + agilityScore) / 3f;
            float mult = 0.8f + 0.4f * avg;
            ChampionshipManager.Instance.ShowMultiplier = mult;

            PlaceDog(posePoint.position, posePoint.rotation);
            hero.Animator.Play(DogAnim.WagTail);
            AimCamera(posePoint.position, 3.2f, 1.4f, 0f);

            headline = "심사 끝";
            subline = "미모 " + Mathf.RoundToInt(poseScore * 100f) + "  ·  훈련 " + Mathf.RoundToInt(fetchScore * 100f)
                    + "  ·  어질리티 " + Mathf.RoundToInt(agilityScore * 100f) + "   →   무대 점수 " + mult.ToString("0.00") + "배";
            resultReady = true;
            while (resultReady) yield return null;

            // 결과 발표는 마감 화면이 한다 — 같은 경로로 랭크·엔딩까지 이어진다
            Running = false;
            phase = Phase.None;
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

        IEnumerator RunTo(Vector3 target, float speed)
        {
            hero.Animator.Play(DogAnim.Run);
            float baseY = target.y + DogBaseOffset();
            Vector3 flat = new Vector3(target.x, baseY, target.z);
            while (true)
            {
                Vector3 p = hero.transform.position;
                Vector3 to = flat - p;
                to.y = 0f;
                if (to.magnitude < 0.05f) break;
                hero.transform.rotation = Quaternion.LookRotation(to);
                hero.transform.position = Vector3.MoveTowards(p, flat, speed * Mathf.Lerp(0.8f, 1.3f, bodyN) * Time.deltaTime);
                yield return null;
            }
        }

        float DogBaseOffset()
        {
            var agent = hero.GetComponent<UnityEngine.AI.NavMeshAgent>();
            return agent != null ? agent.baseOffset : 0f;
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
            if (phase == Phase.Result) DrawResultButton();

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

            // 목표 원 = 판정 띠 그 자체. 미모가 높을수록 원이 크고 띠가 두껍다.
            // 띠 한가운데 가는 선이 PERFECT 자리다
            float outer = (targetRadius + poseWindow) * size;
            GUI.color = new Color(1f, 0.82f, 0.28f, 0.75f);
            if (zoneTex != null) GUI.DrawTexture(new Rect(c.x - outer, c.y - outer, outer * 2f, outer * 2f), zoneTex);
            GUI.color = new Color(1f, 0.97f, 0.75f, 1f);
            float tr = targetRadius * size;
            GUI.DrawTexture(new Rect(c.x - tr, c.y - tr, tr * 2f, tr * 2f), ringTex);

            if (ringRadius > 0f)
            {
                float rr = ringRadius * size;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), ringTex);
            }
            GUI.color = Color.white;
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
            GUI.Label(new Rect(panel.x, panel.y + 10f, panel.width, 32f), hero.BreedKo + "의 지금 몸 상태", midStyle);

            DrawBar(panel, 0, "달리기 속도", Mathf.Lerp(2.2f, runSpeed, grow).ToString("0.0") + " m/s", Mathf.Lerp(0f, bodyN, grow), UiSkin.Sky);
            DrawBar(panel, 1, "점프 높이", Mathf.Lerp(0.35f, jumpHeight, grow).ToString("0.00") + " m", Mathf.Lerp(0f, bodyN, grow), UiSkin.Green);
            DrawBar(panel, 2, "미모 / 훈련도", Mathf.RoundToInt(hero.Stats.Beauty * grow) + " / " + Mathf.RoundToInt(hero.Stats.Training * grow),
                    Mathf.Lerp(0f, bodyN, grow), UiSkin.Coral);

            GUI.Label(new Rect(panel.x, panel.yMax - 34f, panel.width, 26f), "입양 첫날보다 속도 " + (runSpeed / 2.2f).ToString("0.0") + "배"
                      + "  ·  점프 " + (jumpHeight / 0.35f).ToString("0.0") + "배", UiSkin.Caption);
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

        void DrawResultButton()
        {
            var r = new Rect((Screen.width - 300f) * 0.5f, Screen.height - 110f, 300f, 60f);
            if (GUI.Button(r, "결과 발표", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 22 }))
                resultReady = false;
        }
    }
}
