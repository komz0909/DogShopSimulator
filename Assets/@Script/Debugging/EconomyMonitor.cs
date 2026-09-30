using System.Globalization;
using System.IO;
using System.Text;
using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Debugging
{
    /// <summary>
    /// Plan.md가 "이 게임 경제의 전부"라고 부르는 감시 지표를 실제로 계산한다.
    ///
    ///   0.4  &lt;  A_max / (훈련슬롯 × 최고훈련가)  &lt;  0.95      (두 축 따로)
    ///   A_max = 매출 × 마진율 − 강아지수 × 유지비
    ///
    /// 0.95 초과 → 돈이 병목에서 빠져 매일 최고 훈련만 산다.
    /// 0.4 미만 → 슬롯이 남아 원당효율만 남고 상위 티어가 사장된다.
    ///
    /// <b>목표치와 실측치를 나란히</b> 낸다. 기획서의 손님·바스켓 목표로 계산한 값이
    /// 실제 플레이에서 나오는 매출과 다르면, 틀린 쪽은 언제나 기획서다.
    ///
    /// 마감마다 CSV 한 줄을 남기므로 통짜 플레이 1회가 D27 밸런싱 데이터 전체를 만든다.
    /// 주인공견 누적 성장치도 함께 남기므로 챔피언십 NPC 기준선을 실측으로 검증할 수 있다.
    /// </summary>
    public class EconomyMonitor : MonoBehaviour
    {
        /// <summary>
        /// Plan.md 10레벨 곡선의 가정. 여기만 고치면 지표 전체가 따라온다.
        ///
        /// 카탈로그 실측치다. 판매가를 도매의 2.07배에서 <b>2.90배</b>로 올리면서
        /// 0.5 → 0.655 가 됐다(2026-09-21). 이 값이 실제 마진과 어긋나면
        /// A_max 가 통째로 틀어져 감시 지표를 못 믿는다.
        /// </summary>
        public const float MarginRate = 0.655f;

        /// <summary>
        /// 반려견 유지비. 판매견을 없애 <b>항상 1마리</b>다 —
        /// 예전에는 레벨업마다 마리가 늘어(최대 6) 유지비가 훈련 예산을 눌러 주는
        /// 배수구 노릇을 했는데, 그게 사라져 지표가 위로 밀린다(L2 0.83→0.94).
        /// 대체 배수구(가구 구매·랜덤박스)가 들어오기 전까지는 여유가 없는 구간이다.
        /// </summary>
        public const int UpkeepPerDog = 33;

        /// <summary>
        /// <b>선택 압력</b>의 대역. 재는 것이 바뀌었으므로 뜻도 바뀌었다.
        ///
        /// 예전에는 "훈련이 하루 돈을 다 흡수하는가"를 쟀고, 천장을 넘으면 밸런스 버그로 봤다.
        /// 그 규칙은 <b>훈련이 유일한 돈 구멍이고 같은 훈련을 반복할 수 있던 시절</b>의 것이다.
        /// 지금은 둘 다 아니다 — 가구·진열대·승급·발주가 모두 돈을 먹고, 하루 쿨타임 때문에
        /// 같은 훈련을 두 번 못 한다.
        ///
        /// 그래서 이제 재는 것은 <b>"쓸 데가 여럿인데 다 못 해서 골라야 하는가"</b>다.
        /// 분자는 필수 지출(그날 팔려 나갈 재고)을 뺀 <b>진짜 여윳돈</b>이다.
        ///
        /// 아래(0.4)를 밑돌면 훈련을 채울 여력조차 없다 — 선택이 아니라 궁핍이다.
        /// 위(1.5)를 넘으면 훈련을 다 하고도 돈이 남는다. 그 자체는 버그가 아니다 —
        /// 남은 돈을 모을지 가게에 넣을지가 플레이어의 선택이기 때문이다.
        /// 다만 <b>남는 돈이 갈 데가 없으면</b> 선택이 아니라 사장된 자원이므로,
        /// 이 값이 높을수록 돈 구멍(가구·랜덤박스)이 더 필요하다는 신호다.
        /// </summary>
        public const float RatioFloor = 0.4f;
        public const float RatioCeiling = 1.5f;

        const string FileName = "EconomyLog.csv";

        /// <summary>Assets 옆(프로젝트 루트)에 쓴다 — 애셋 데이터베이스를 건드리지 않고 엑셀로 바로 열 수 있다.</summary>
        static string LogPath => Path.Combine(Application.dataPath, "..", FileName);

        public static EconomyMonitor Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>
        /// 문 열 때 잰 진열 상태. <b>마감 뒤에 세면 쓸모가 없다</b> —
        /// 잘 팔린 날일수록 칸이 비어 "한 종도 진열 안 됨"으로 찍힌다(30차 D27·D29).
        /// 알고 싶은 것은 "오늘 장사를 몇 종으로 시작했는가"다.
        /// </summary>
        int openSlots, openShown, openStranded;

        void Start()
        {
            TimeManager.Instance.OnDayEnded += AppendRow;
            TimeManager.Instance.OnDayStarted += CaptureShelves;

            if (ShopHours.Instance != null) ShopHours.Instance.OnPhaseChanged += HandlePhase;
            CaptureShelves();
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnDayEnded -= AppendRow;
                TimeManager.Instance.OnDayStarted -= CaptureShelves;
            }
            if (ShopHours.Instance != null) ShopHours.Instance.OnPhaseChanged -= HandlePhase;
            if (Instance == this) Instance = null;
        }

        void HandlePhase()
        {
            if (ShopHours.Instance != null && ShopHours.Instance.Current == ShopHours.Phase.Open) CaptureShelves();
        }

        void CaptureShelves() => CountShelves(out openSlots, out openShown, out openStranded);

        // ---- 지표 ----

        /// <summary>기획서 목표치로 계산한 하루 가용액.</summary>
        public int AMaxTarget
        {
            get
            {
                ShopLevelDef level = ShopLevelManager.Instance.Current;
                return Mathf.RoundToInt(level.customersPerDay * level.basketPriceTarget * MarginRate)
                     - UpkeepPerDog;
            }
        }

        /// <summary>실제 그날 매출로 계산한 하루 가용액.</summary>
        public int AMaxActual =>
            Mathf.RoundToInt(CustomerManager.Instance.RevenueToday * MarginRate)
            - UpkeepPerDog;

        /// <summary>
        /// 하루 가용액이 <b>훈련이 흡수할 수 있는 양</b>의 몇 배인가.
        ///
        /// 분모가 예전에는 <c>슬롯 수 x 그 축의 최고가</c>였다. 같은 훈련을 슬롯 수만큼
        /// 반복할 수 있던 시절에는 그게 곧 하루 상한이었다. <b>하루 쿨타임이 생기면서
        /// 그 식은 거짓이 됐다</b> — 예를 들어 L4 는 슬롯이 4개인데 해금된 훈련이 축마다
        /// 3종뿐이라, 같은 것을 반복할 수 없으면 두 축을 섞어야 슬롯을 다 쓴다.
        /// 옛 식은 4 x 150 = 600 이라고 했지만 실제로 쓸 수 있는 돈은 150+150+60+60 = 420 이다.
        ///
        /// 축을 나누지 않는다. 하루 가용액은 두 축이 <b>나눠 쓰는 한 주머니</b>라,
        /// 한쪽 축의 상한과만 견주면 같은 돈을 두 번 세게 된다.
        /// 실제로도 두 축의 비용표가 같아서 예전 네 값은 늘 똑같이 나왔다.
        /// </summary>
        public float RatioTarget => Ratio(SurplusTarget);
        public float RatioActual => Ratio(SurplusActual);

        /// <summary>
        /// 그날 <b>마음대로 쓸 수 있는 돈</b>. 하루 가용액에서 필수 재고비를 뺀다.
        ///
        /// 재고는 선택이 아니다 — 안 채우면 내일 팔 물건이 없다. 그걸 빼지 않으면
        /// 여유가 실제보다 커 보이고, "훈련이냐 저축이냐"를 재는 데 쓸 수 없다.
        /// </summary>
        public int SurplusTarget => AMaxTarget - Restock();
        public int SurplusActual => AMaxActual - Restock();

        static int Restock()
        {
            InventoryManager inv = InventoryManager.Instance;
            ShopLevelManager s = ShopLevelManager.Instance;
            if (inv == null || s == null) return 0;

            return inv.DailyConsumptionCost(s.Level, s.Current.customersPerDay);
        }

        static float Ratio(int surplus)
        {
            TrainingManager tm = TrainingManager.Instance;
            if (tm == null) return 0f;

            int capacity = tm.DailyCapacity;
            return capacity > 0 ? surplus / (float)capacity : 0f;
        }

        /// <summary>
        /// 대역 안인가.
        ///
        /// 아래(0.4)를 밑돌면 슬롯이 남아돌아 상위 훈련이 사장된다 — 예전과 같다.
        /// 위(1.0)를 넘으면 <b>돈이 아니라 훈련 가짓수가 병목</b>이라는 뜻이다.
        /// 천장을 0.95 에서 1.0 으로 올린 이유: 쿨타임이 생긴 뒤로는 돈이 남아도
        /// "어느 훈련을 어떤 순서로 쓸까"라는 선택이 남는다. 예전 천장은
        /// "돈이 남으면 매일 최고가만 반복한다"를 막던 값인데, 이제 반복이 불가능하다.
        /// </summary>
        public static bool InBand(float ratio) => ratio > RatioFloor && ratio < RatioCeiling;

        /// <summary>
        /// HUD 한 줄. 목표치가 대역을 벗어나면 그게 곧 밸런스 버그다.
        /// 매출이 0인 아침에는 실측을 내지 않는다 — 유지비만 남아 음수가 되어
        /// 매일 아침 경고처럼 보인다.
        /// </summary>
        public string StatusLine()
        {
            bool measured = CustomerManager.Instance != null && CustomerManager.Instance.RevenueToday > 0;

            TrainingManager tm = TrainingManager.Instance;

            return "선택압력(0.40~1.50)  목표 " + Mark(RatioTarget)
                 + " / 실측 " + (measured ? Mark(RatioActual) : "-")
                 + "   A_max 목표 " + AMaxTarget
                 + " / 실측 " + (measured ? AMaxActual.ToString() : "-")
                 + "   훈련상한 " + (tm != null ? tm.DailyCapacity.ToString() : "-");
        }

        /// <summary>대역을 벗어나면 !를 붙인다.</summary>
        static string Mark(float ratio) =>
            ratio.ToString("0.00") + (InBand(ratio) ? "" : "!");

        // ---- CSV ----

        void AppendRow()
        {
            GameManager g = GameManager.Instance;
            CustomerManager c = CustomerManager.Instance;
            ShopLevelManager s = ShopLevelManager.Instance;
            TrainingManager tm = TrainingManager.Instance;
            DogManager dm = DogManager.Instance;
            CleanlinessManager clean = CleanlinessManager.Instance;
            if (g == null || c == null || s == null || tm == null || dm == null) return;

            ShopLevelDef level = s.Current;
            DogStats hero = dm.Hero != null ? dm.Hero.Stats : null;

            StringBuilder row = new StringBuilder();
            Append(row, g.Day);
            Append(row, s.Level);
            Append(row, g.Reputation);
            Append(row, g.Money);
            Append(row, c.RevenueToday);
            Append(row, c.SoldToday);
            Append(row, c.LostToday);
            Append(row, c.AverageBasket);
            Append(row, level.basketPriceTarget);
            Append(row, level.customersPerDay);
            Append(row, dm.Hero != null ? 1 : 0);
            Append(row, clean != null ? clean.Cleanliness : 100);
            Append(row, tm.SlotsTotal);
            Append(row, tm.SlotsUsed);
            Append(row, tm.SpentTodayTraining);
            Append(row, tm.SpentTodayBeauty);
            Append(row, tm.TopCostOf(GrowthAxis.Training));
            Append(row, tm.TopCostOf(GrowthAxis.Beauty));
            Append(row, AMaxTarget);
            Append(row, AMaxActual);
            // 축을 나누지 않는다. 두 축이 한 주머니를 나눠 쓰므로 비율은 하나다 —
            // 예전 네 값은 비용표가 같아서 늘 똑같이 나왔다
            Append(row, RatioTarget);
            Append(row, RatioActual);
            Append(row, hero != null ? hero.Beauty : 0);
            Append(row, hero != null ? hero.Training : 0);
            Append(row, hero != null ? hero.GrowthTotal : 0);
            Append(row, hero != null ? hero.UpkeepAverage : 0);

            // 문 열 때의 진열 상태. 28차 측정에서 봇이 산 진열대가 손님이 못 가는 가게 앞에
            // 서 있었는데 매출·손실률만 보고는 그걸 알 수 없었다 — 몇 종으로 장사를 시작했는지 적는다
            Append(row, openSlots);
            Append(row, openShown);
            Append(row, openStranded);

            // 감시비율의 분모. 훈련이 하루에 실제로 흡수할 수 있는 돈이다
            Append(row, tm.DailyCapacity);

            // 그날 마음대로 쓸 수 있었던 돈. money 열과 나란히 보면
            // "벌어서 쓴 것"과 "갈 데가 없어 쌓인 것"이 갈린다
            Append(row, SurplusActual);

            // 상자에 흘러간 돈. 남는 돈이 실제로 이 구멍으로 빠지는지 본다
            RandomBox box = RandomBox.Instance;
            Append(row, box != null ? box.OpenedTotal : 0);
            Append(row, box != null ? box.SpentTotal : 0, last: true);

            try
            {
                bool fresh = !File.Exists(LogPath);
                if (fresh) File.WriteAllText(LogPath, Header() + "\n");
                File.AppendAllText(LogPath, row.ToString() + "\n");

                if (fresh) Debug.Log("[Economy] 로그 시작 — " + Path.GetFullPath(LogPath));
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Economy] CSV 기록 실패 — " + e.Message);
            }
        }

        static string Header() =>
            "day,level,reputation,money,revenue,sold,lost,basketActual,basketTarget,customersTarget,"
            + "dogs,cleanliness,slotsTotal,slotsUsed,spentTraining,spentBeauty,"
            + "topCostTraining,topCostBeauty,aMaxTarget,aMaxActual,"
            + "ratioTarget,ratioActual,"
            + "heroBeauty,heroTraining,heroGrowthTotal,heroUpkeep,"
            + "openSlots,openShown,openStranded,trainCapacity,surplus,boxOpened,boxSpent";

        /// <summary>
        /// 진열 칸 수와, 해금 상품 중 <b>손님이 닿는 자리에</b> 올라와 있는 종수를 센다.
        /// 창고·가게 앞처럼 손님이 못 가는 곳의 진열대는 세지 않는다 — 거기 있는 상품은
        /// 진열된 것이 아니다.
        /// </summary>
        static void CountShelves(out int slots, out int shown, out int stranded)
        {
            slots = 0;
            shown = 0;
            stranded = 0;

            ShelfManager shelves = ShelfManager.Instance;
            InventoryManager inv = InventoryManager.Instance;
            if (shelves == null || inv == null) return;

            for (int i = 0; i < shelves.Count; i++)
                if (shelves.Get(i).CustomersCanReach) slots += shelves.Get(i).SlotCount;

            int level = ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Level : 1;
            for (int p = 0; p < inv.Catalog.Count; p++)
            {
                if (inv.Catalog.Get(p).unlockLevel > level) continue;

                if (shelves.FindFor(p) != null) shown++;
                else stranded++;
            }
        }

        static void Append(StringBuilder row, int value, bool last = false)
        {
            row.Append(value.ToString(CultureInfo.InvariantCulture));
            if (!last) row.Append(',');
        }

        /// <summary>비율은 소수점 세 자리. 엑셀이 지역 설정에 흔들리지 않게 InvariantCulture로 쓴다.</summary>
        static void Append(StringBuilder row, float value, bool last = false)
        {
            row.Append(value.ToString("0.000", CultureInfo.InvariantCulture));
            if (!last) row.Append(',');
        }
    }
}
