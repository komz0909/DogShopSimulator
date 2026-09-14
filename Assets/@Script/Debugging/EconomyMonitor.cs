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
        /// <summary>Plan.md 10레벨 곡선의 가정. 여기만 고치면 지표 전체가 따라온다.</summary>
        public const float MarginRate = 0.5f;
        public const int UpkeepPerDog = 33;

        public const float RatioFloor = 0.4f;
        public const float RatioCeiling = 0.95f;

        const string FileName = "EconomyLog.csv";

        /// <summary>Assets 옆(프로젝트 루트)에 쓴다 — 애셋 데이터베이스를 건드리지 않고 엑셀로 바로 열 수 있다.</summary>
        static string LogPath => Path.Combine(Application.dataPath, "..", FileName);

        public static EconomyMonitor Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnDayEnded += AppendRow;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayEnded -= AppendRow;
            if (Instance == this) Instance = null;
        }

        // ---- 지표 ----

        /// <summary>기획서 목표치로 계산한 하루 가용액.</summary>
        public int AMaxTarget
        {
            get
            {
                ShopLevelDef level = ShopLevelManager.Instance.Current;
                return Mathf.RoundToInt(level.customersPerDay * level.basketPriceTarget * MarginRate)
                     - DogManager.Instance.Count * UpkeepPerDog;
            }
        }

        /// <summary>실제 그날 매출로 계산한 하루 가용액.</summary>
        public int AMaxActual =>
            Mathf.RoundToInt(CustomerManager.Instance.RevenueToday * MarginRate)
            - DogManager.Instance.Count * UpkeepPerDog;

        public float RatioTarget(GrowthAxis axis) => Ratio(AMaxTarget, axis);
        public float RatioActual(GrowthAxis axis) => Ratio(AMaxActual, axis);

        static float Ratio(int aMax, GrowthAxis axis)
        {
            TrainingManager tm = TrainingManager.Instance;
            int denominator = tm.SlotsTotal * tm.TopCostOf(axis);
            return denominator > 0 ? aMax / (float)denominator : 0f;
        }

        public static bool InBand(float ratio) => ratio > RatioFloor && ratio < RatioCeiling;

        /// <summary>
        /// HUD 한 줄. 목표치가 대역을 벗어나면 그게 곧 밸런스 버그다.
        /// 매출이 0인 아침에는 실측을 내지 않는다 — 유지비만 남아 음수가 되어
        /// 매일 아침 경고처럼 보인다.
        /// </summary>
        public string StatusLine()
        {
            bool measured = CustomerManager.Instance != null && CustomerManager.Instance.RevenueToday > 0;

            return "감시비율(0.40~0.95)  훈련도 목표 " + Mark(RatioTarget(GrowthAxis.Training))
                 + " / 실측 " + (measured ? Mark(RatioActual(GrowthAxis.Training)) : "—")
                 + "   미모 목표 " + Mark(RatioTarget(GrowthAxis.Beauty))
                 + " / 실측 " + (measured ? Mark(RatioActual(GrowthAxis.Beauty)) : "—")
                 + "   A_max 목표 " + AMaxTarget
                 + " / 실측 " + (measured ? AMaxActual.ToString() : "—");
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
            Append(row, dm.Count);
            Append(row, clean != null ? clean.Cleanliness : 100);
            Append(row, tm.SlotsTotal);
            Append(row, tm.SlotsUsed);
            Append(row, tm.SpentTodayTraining);
            Append(row, tm.SpentTodayBeauty);
            Append(row, tm.TopCostOf(GrowthAxis.Training));
            Append(row, tm.TopCostOf(GrowthAxis.Beauty));
            Append(row, AMaxTarget);
            Append(row, AMaxActual);
            Append(row, RatioTarget(GrowthAxis.Training));
            Append(row, RatioActual(GrowthAxis.Training));
            Append(row, RatioTarget(GrowthAxis.Beauty));
            Append(row, RatioActual(GrowthAxis.Beauty));
            Append(row, hero != null ? hero.Beauty : 0);
            Append(row, hero != null ? hero.Training : 0);
            Append(row, hero != null ? hero.GrowthTotal : 0);
            Append(row, hero != null ? hero.UpkeepAverage : 0, last: true);

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
            + "ratioTrainTarget,ratioTrainActual,ratioBeautyTarget,ratioBeautyActual,"
            + "heroBeauty,heroTraining,heroGrowthTotal,heroUpkeep";

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
