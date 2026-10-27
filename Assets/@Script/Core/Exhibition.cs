using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// <b>시연 모드</b>(지스타 같은 전시용, 3일). 메인 화면의 [시연 모드] 로 들어온다.
    ///
    /// 시연 모드에서만 달라지는 값은 <b>전부 여기</b> 있다 — 각 시스템은 <see cref="Active"/> 일 때만 이 값을 읽고,
    /// 아니면 원래 값을 그대로 쓴다. 30일 본편의 가격·해금·손님·성장 표는 손대지 않는다.
    ///
    /// 바꾸지 않는 것: <b>도그쇼</b>(미니게임 넷, 조작, 스탯 반영, 가중치 20/25/25/30, 순위표). 본편과 같은 코드를 그대로 탄다.
    /// 시연이 바꾸는 건 쇼에 나가기 전까지의 준비 기회(시작 능력치·성장 배율·하루 길이)뿐이다.
    ///
    /// 하루 흐름:
    ///   1일차 가게 운영 맛보기 — 튜토리얼 퀘스트(상자 담기 → 진열 → 발주 → 온라인 주문 → 훈련)를 따라간다
    ///   2일차 강아지 돌보기·훈련 — 밥·목욕·훈련·운동으로 능력치가 오르는 걸 본다
    ///   3일차 마지막 준비 + 도그쇼 — 가게는 열지 않는다. 준비가 끝나면 [도그쇼 시작]
    /// 세이브는 하지 않는다 — 본편 세이브를 덮지 않고, 다시 시작하면 처음부터다.
    /// </summary>
    public static class Exhibition
    {
        /// <summary>지금 판이 시연 모드인가. 메인 화면에서 고른 값이 가게 씬까지 넘어온다(<see cref="GameStart"/>).</summary>
        public static bool Active => GameStart.FromMenu && GameStart.Exhibition;

        /// <summary>시연은 3일. 3일차가 도그쇼 날이다(본편은 30일차).</summary>
        public const int Days = 3;

        /// <summary>
        /// 가게 Lv 7 에서 시작한다. Lv 1 은 상품이 사료·샴푸 두 가지뿐이라 가게를 맛보기엔 너무 비었다.
        /// Lv 7 이면 상품 16종, 훈련 7종 전부, 운동 3종, 하루 훈련 3번이 열리고 가게도 두 번 넓어진(11m) 모습이다.
        ///
        /// Lv 8 바로 아래에서 시작하는 게 핵심이다 — Lv 8 레벨업은 마스코트 "스타 강아지"(능력치 합 260)가 있어야 한다.
        /// 2일차에 훈련으로 승급 → 그 등급으로 가게 레벨업을 시연 안에서 직접 겪게 한다(강아지가 가게를 키운다).
        /// 명성(<see cref="StartReputation"/>)과 능력치(<see cref="StartGrowthStat"/>)도 그 문턱 바로 아래로 맞춘다.
        /// 예전엔 Lv 5·인기 강아지(150) 문턱이었는데, 3일차에 바로 쇼에 나가기엔 능력치(49)가 낮아 한 단계 올렸다.
        /// </summary>
        public const int StartLevel = 7;

        /// <summary>시작 명성. Lv 8 문턱(433) 바로 아래 — 1일차 손님 몇 명이면 넘는다.</summary>
        public const int StartReputation = 428;

        /// <summary>
        /// 기본 창고 선반(4개, 12단)에 더해 뒷벽에 세우는 선반 수. Lv 7 상품 16종이 한 단씩 다 들어가고 남는다.
        /// 자리는 <see cref="Shop.StorageManager"/> 가 정한다(창고 뒷벽 x 3.5~7.4).
        /// </summary>
        public const int ExtraStorageRacks = 4;

        /// <summary>
        /// 시작할 때 매장에 미리 세워 두는 진열대(가구 카탈로그 번호 · 자리 · 방향).
        /// Lv 7 은 상품이 16종인데 씬에 놓인 진열대는 둘(6칸)뿐이고, 11m 로 넓어진 바닥이 거의 비어 있었다.
        /// 아일랜드 하나를 문(x 4)을 사이에 두고 기존 아일랜드와 대칭으로, 벽 진열대 둘을 좌우 벽에 붙인다 — 16칸.
        /// 계산 줄(z 1.1)과 입구 통로를 비키고, 손님 길(NavMesh)로 문에서 전부 닿는 것을 확인한 자리다.
        /// </summary>
        public static readonly int[] StartShelfCatalog = { 0, 1, 1 };
        public static readonly Vector3[] StartShelfPosition =
        {
            new Vector3(7.1f, 0f, 3.3f),     // 진열대(아일랜드)
            new Vector3(9.19f, 0f, 3.2f),    // 벽 진열대 — 오른쪽 벽
            new Vector3(-1.19f, 0f, 3.5f),   // 벽 진열대 — 왼쪽 벽
        };
        public static readonly float[] StartShelfYaw = { 90f, 270f, 90f };

        /// <summary>
        /// 문을 열 때 이미 진열돼 있는 상품 수와 하나당 개수. 수요가 큰 순서로 고른다.
        /// 빈 진열대로 시작하면 관람객이 상자를 나르기 전까지 손님이 한 명도 안 와 가게가 죽어 보였다.
        /// 다 채우지 않는다 — 남은 칸과 팔려 나간 자리를 채우는 것이 1일차 할 일(상자 담기 → 진열)이다.
        /// </summary>
        public const int PrestockKinds = 10;
        public const int PrestockEach = 3;

        /// <summary>
        /// 시작할 때 카메라가 보는 수평 방향(도). 180 이면 입구 쪽 — 문, 계산대, 진열대, 강아지가 한 화면에 들어온다.
        /// 씬 기본값(0)은 매장 뒷벽을 봐서 첫 화면이 빈 벽이었다.
        /// </summary>
        public const float StartYaw = 180f;

        /// <summary>시작 자금. 발주·훈련·운동을 망설이지 않고 해 볼 만큼(본편 900원).</summary>
        public const int StartMoney = 3000;

        /// <summary>열린 상품마다 창고에 깔아 두는 수. 첫날 발주하지 않아도 바로 진열하고 팔 수 있게.</summary>
        public const int StartStockPerProduct = 10;

        /// <summary>
        /// 시작 능력치(미모·훈련도·어질리티). 0 에서 3일 만에 쇼에 나가면 네 종목이 전부 최저 난이도에 묶인다.
        /// "어느 정도 자란 강아지를 데려왔다"는 출발점이다. 만점 기준은 240(<c>DogShow.StatFull</c>).
        /// 셋을 합치면 258 — "스타 강아지"(260) 바로 아래라 2일차에 어떤 훈련이든 한 번 하면 승급한다(가장 싼 훈련이 +5).
        /// 셋 다 손님 반응이 열리는 70(<see cref="Dogs.MascotGreeter.Unlock"/>)을 넘어 1일차부터 반응 세 가지가 다 나온다.
        /// </summary>
        public const int StartGrowthStat = 86;

        /// <summary>
        /// 훈련·운동 성장 배율. 본편 하루치 성장을 시연 하루에 몰아 준다.
        /// 시작값(86)에 훈련을 더한 만큼 쇼가 쉬워진다. 훈련을 건너뛰면 시작값 그대로다.
        /// 다 채워 주지는 않는다 — 돌보고 훈련한 만큼만 쇼가 쉬워진다.
        /// </summary>
        public const int GrowthMultiplier = 5;

        /// <summary>
        /// 하루(09~21시)를 흘리는 실시간(초). 본편은 300초.
        /// 1일차는 가게를 한 바퀴 돌아볼 만큼, 2·3일차는 돌보기·훈련만 하면 되니 더 짧게.
        /// 그보다 빨리 끝내고 싶으면 안내판의 [오늘 마치기]로 언제든 잘 수 있다.
        /// </summary>
        static readonly float[] DaySeconds = { 150f, 100f, 100f };

        public static float SecondsPerGameHour(int day)
        {
            float full = DaySeconds[Mathf.Clamp(day - 1, 0, DaySeconds.Length - 1)];
            return full / (TimeManager.LastHour - TimeManager.DayStartHour);
        }

        /// <summary>
        /// 메인 화면이 열리면 곧바로 시연 견종 고르기로 들어간다(한 번 쓰고 끈다).
        /// 엔딩이 끝났을 때와 스태프 초기화(F12, <see cref="StaffKeys"/>) 때 켠다 — 다음 체험자가 바로 시작하게.
        /// </summary>
        public static bool ReturnToPicker;

        // ---- 키운 결과 기록 (도그쇼 시작 화면에 보여 준다) ----

        /// <summary>시연 동안 번 돈(매장·온라인 매출 합).</summary>
        public static int Earned { get; private set; }
        /// <summary>시연 동안 시킨 훈련·운동 횟수.</summary>
        public static int Trainings { get; private set; }
        public static int Exercises { get; private set; }

        public static void ResetRecord() { Earned = 0; Trainings = 0; Exercises = 0; }
        public static void RecordSale(int paid) { if (Active) Earned += paid; }
        public static void RecordTraining() { if (Active) Trainings++; }
        public static void RecordExercise() { if (Active) Exercises++; }

        /// <summary>오늘 할 일이 남았을 때 시계를 붙잡아 두는 시각. 영업 마감(19:30)보다 앞이라 손님이 계속 온다.</summary>
        public const float HoldHour = 18f;

        /// <summary>
        /// 지금 시계를 붙잡아야 하나 — 시연 중이고 오늘 할 일(<see cref="Shop.TutorialQuestManager"/>)이 남았다.
        /// 시간이 아니라 <b>할 일로</b> 하루가 끝나게 하려는 것이다. 다 끝내면 시계가 다시 흐르고 곧 다음 날로 넘어간다.
        /// </summary>
        public static bool HoldsClock
        {
            get
            {
                if (!Active) return false;
                Shop.TutorialQuestManager quests = Shop.TutorialQuestManager.Instance;
                return quests != null && !quests.IsComplete;
            }
        }

        /// <summary>시연 마지막 날(도그쇼 날)인가.</summary>
        public static bool IsShowDay(int day) => Active && day >= Days;

        /// <summary>
        /// 시연 3일차인데 아직 쇼를 치르지 않았다. 이때 잠자기 자리(침대·21시 안내)는 [도그쇼 시작]이 된다 —
        /// 쇼 없이 하루가 끝나 4일차로 넘어가는 길을 막는다.
        /// </summary>
        /// 본편 30일차도 같다 — 아침에 건강이 모자라 쇼가 안 열렸으면, 돌본 뒤 같은 자리에서 쇼를 연다.
        public static bool ShowPending =>
            GameManager.Instance != null && Show.ChampionshipManager.Instance != null
            && Show.ChampionshipManager.Instance.IsFinalDay(GameManager.Instance.Day)
            && !Show.ChampionshipManager.Instance.HasShowResult
            && !(Debugging.MeasurementMode.Instance != null && Debugging.MeasurementMode.Instance.Active);

        /// <summary>지금 출전견이 도그쇼에 나갈 수 있나(건강 70 이상). 못 나가면 이유.</summary>
        public static bool CanEnterShow(out string reason)
        {
            Show.ChampionshipManager champ = Show.ChampionshipManager.Instance;
            Dogs.Dog hero = Dogs.DogManager.Instance != null ? Dogs.DogManager.Instance.Hero : null;
            if (champ == null) { reason = null; return true; }
            return champ.CanEnter(hero, out reason);
        }

        /// <summary>
        /// 도그쇼 시작 화면을 연다(본편 30일차와 같은 화면). 열었으면 true.
        /// 쇼가 이미 돌고 있거나 하루가 끝났으면 그냥 false. 건강이 70 아래면 이유를 알리고 false.
        /// 그 밖의 이유(무대·출전견 없음)로 못 열면
        /// 알리고 하루를 끝낸다 — 침대·21시 안내가 쇼 시작으로만 바뀌어 있어 3일차에서 영영 못 넘어갔다.
        /// </summary>
        public static bool StartShow()
        {
            Show.DogShow show = Show.DogShow.Instance;
            if (show != null && Show.DogShow.Running) return false;

            TimeManager time = TimeManager.Instance;
            if (time == null || time.IsDayOver) return false;

            // 건강이 모자라면 막는다 — 하루는 끝내지 않는다(돌보고 다시 오거나, 침대에서 출전을 포기하고 잔다)
            string reason;
            if (!CanEnterShow(out reason))
            {
                ActionRunner.Reject(reason + ". 밥, 약으로 건강을 올리고 다시 오자");
                return false;
            }
            if (show != null && show.BeginExhibitionShow()) return true;

            ActionRunner.Reject("도그쇼를 열 수 없어 오늘을 마친다");
            time.EndDayNow();
            return false;
        }
    }
}
