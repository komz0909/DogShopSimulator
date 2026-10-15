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
        /// 가게 Lv 3 에서 시작한다. Lv 1 은 상품이 사료·샴푸 두 가지뿐이라 가게를 맛보기엔 너무 비었다.
        /// Lv 3 이면 상품 5종(사료·샴푸·약품·목줄·강아지껌), 훈련 4종, 운동 2종, 하루 훈련 3번이 열린다.
        /// 넓이는 Lv 1 과 같아(8m) 가게 모양이 달라지지 않는다.
        /// </summary>
        public const int StartLevel = 3;

        /// <summary>시작 자금. 발주·훈련·운동을 망설이지 않고 해 볼 만큼(본편 900원).</summary>
        public const int StartMoney = 3000;

        /// <summary>열린 상품마다 창고에 깔아 두는 수. 첫날 발주하지 않아도 바로 진열하고 팔 수 있게.</summary>
        public const int StartStockPerProduct = 12;

        /// <summary>
        /// 시작 능력치(미모·훈련도·어질리티). 0 에서 3일 만에 쇼에 나가면 네 종목이 전부 최저 난이도에 묶인다.
        /// "어느 정도 자란 강아지를 데려왔다"는 출발점이다. 만점 기준은 240(<c>DogShow.StatFull</c>).
        /// </summary>
        public const int StartGrowthStat = 40;

        /// <summary>
        /// 훈련·운동 성장 배율. 본편 하루치 성장을 시연 하루에 몰아 준다.
        /// 사흘 동안 빠짐없이 훈련하면 미모·훈련도 ~100, 어질리티 ~130 — 만점의 절반쯤이고, 훈련을 건너뛰면 40 그대로다.
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

        /// <summary>시연 마지막 날(도그쇼 날)인가.</summary>
        public static bool IsShowDay(int day) => Active && day >= Days;

        /// <summary>
        /// 시연 3일차인데 아직 쇼를 치르지 않았다. 이때 잠자기 자리(침대·21시 안내)는 [도그쇼 시작]이 된다 —
        /// 쇼 없이 하루가 끝나 4일차로 넘어가는 길을 막는다.
        /// </summary>
        public static bool ShowPending =>
            GameManager.Instance != null && IsShowDay(GameManager.Instance.Day)
            && Show.ChampionshipManager.Instance != null && !Show.ChampionshipManager.Instance.HasShowResult;

        /// <summary>도그쇼 시작 화면을 연다(본편 30일차와 같은 화면). 열었으면 true.</summary>
        public static bool StartShow() => Show.DogShow.Instance != null && Show.DogShow.Instance.BeginExhibitionShow();
    }
}
