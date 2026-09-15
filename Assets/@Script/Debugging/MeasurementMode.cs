using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using DogShop.Show;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Debugging
{
    /// <summary>
    /// <b>측정 장치다. 게임 기능이 아니다.</b> M으로 토글한다.
    ///
    /// 16배속에서 30일을 무인으로 돌려 <b>경제 상한</b>을 잰다 — "완벽하게 운영했다면
    /// 하루에 얼마를 벌고 주인공견이 어디까지 자라는가". 그 값이 있어야
    /// 챔피언십 NPC 기준선(210/185/160/130/100)을 근거를 갖고 정할 수 있다.
    ///
    /// 사람이 16배속을 따라갈 수 없기 때문에 필요하다 — 하루가 30초이고
    /// 손님 인내심 22초는 실제 1.4초라, 진열·계산을 손으로 누르는 것이 불가능하다.
    ///
    /// 자른 것 목록의 <b>"자동판매직원"과는 다르다.</b> 그것은 상점 운영을 자동화해
    /// 게임의 중심을 없애는 게임 기능이고, 이것은 빌드에서 빠지는 계측 도구다.
    /// 완성된 게임에서 플레이어는 그대로 직접 나른다.
    ///
    /// <b>측정하지 않는 것</b> — 판매견을 사지 않는다. 훈련 슬롯을 주인공견에 전부 몰기 때문에
    /// 이 수치는 "주인공견 특화 플레이의 상한"이다. 판매견 병행 플레이는 별도로 재야 한다.
    /// </summary>
    public class MeasurementMode : MonoBehaviour
    {
        /// <summary>창고에 유지할 상품별 목표 수량. 진열대 한 판을 채울 만큼만 — 더 사면 현금이 마른다.</summary>
        const int StorageTarget = InventoryManager.ShelfCapacity;

        /// <summary>
        /// 케어용으로 따로 깔아두는 양. <b>사료·샴푸만</b>이다 —
        /// 약은 건강 40 미만에서만 쓰는데 매일 밥을 주면 그 선에 닿지 않는다.
        /// 약(도매 70)을 6개씩 깔았더니 L2 승격 직후 840원이 잘 안 팔리는 재고로 굳어
        /// 가게가 자본 부족에 빠졌다(4차 측정).
        /// </summary>
        const int CareStock = 3;

        /// <summary>마감 이벤트는 전단지(명성 +4)로 고정한다 — 난수를 하나 줄인다.</summary>
        const int FlyerCard = 1;

        public static MeasurementMode Instance { get; private set; }

        public bool Active { get; private set; }

        EveningEventMenu evening;
        GUIStyle banner;

        /// <summary>하루 계획을 다음 Update로 미루는 플래그. HandleDayStarted 주석 참고.</summary>
        bool planPending;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // 빌드에서는 존재만 하고 아무 일도 하지 않는다
            if (!Application.isEditor) enabled = false;
        }

        void Start()
        {
            evening = FindAnyObjectByType<EveningEventMenu>();
            TimeManager.Instance.OnWholeHourChanged += HandleHour;
            TimeManager.Instance.OnDayStarted += HandleDayStarted;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnWholeHourChanged -= HandleHour;
                TimeManager.Instance.OnDayStarted -= HandleDayStarted;
            }
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame) Toggle();

            if (!Active) return;

            if (planPending)
            {
                planPending = false;
                RunDailyPlan();
                ServeShelves();
            }

            ServeCustomers();
            CleanFloor();
            AdvanceDayIfOver();
        }

        void Toggle()
        {
            Active = !Active;

            // 에디터가 포커스를 잃으면 유니티가 프레임을 조여서 16배속이 실효 2.4배로 떨어진다.
            // 무인 측정 중에는 창을 안 보고 있으므로 이걸 켜야 실제로 빨라진다.
            Application.runInBackground = Active;

            Debug.Log("[측정] " + (Active ? "켜짐 — 무인 진행. 3으로 16배속" : "꺼짐"));

            if (Active)
            {
                RunDailyPlan();   // 켠 날도 하루를 놓치지 않게
                ServeShelves();
            }
        }

        // ---- 하루 단위 ----

        /// <summary>
        /// 하루 계획은 <b>한 프레임 미뤄서</b> 돌린다. OnDayStarted 구독 순서가 보장되지 않아
        /// 여기서 바로 훈련하면 TrainingManager.ResetSlots가 그 뒤에 돌아 슬롯 사용량과
        /// 축별 지출이 0으로 지워진다(3차 측정에서 CSV의 slotsUsed·spent가 전부 0이었다).
        /// 성장은 실제로 일어나므로 기능은 맞았지만 계측값이 쓸모없어진다.
        /// </summary>
        void HandleDayStarted()
        {
            if (Active) planPending = true;
        }

        /// <summary>
        /// 우선순위가 곧 측정의 정의다. 케어 → 재고 → 레벨업 → 훈련.
        ///
        /// 첫 측정에서 <b>케어가 빠져</b> 주인공견 유지 스탯이 8일차에 0이 되고
        /// 성장이 12에서 얼어붙었다. 그리고 <b>저축 규칙이 없어</b> 발주가 현금을 전부 털어
        /// 재고도 업그레이드도 못 하는 죽음의 나선에 빠졌다. 둘 다 여기서 막는다.
        /// </summary>
        void RunDailyPlan()
        {
            CareHero();

            // 업그레이드가 발주보다 먼저다. 뒤에 두면 발주로 돈을 쓴 직후,
            // 매출이 들어오기 전 하루 중 가장 가난한 순간에 운전자본을 판정해
            // 명성이 요구치의 19배가 되어도 레벨이 30일간 1에 멈춘다(3차 측정).
            TryUpgrade();
            Restock();
            TrainHero();
        }

        /// <summary>
        /// 유지 스탯은 하루 −15씩 <b>청결과 건강 둘 다</b> 깎인다.
        /// 어느 쪽이든 40 미만이면 그날 성장이 0이므로, 40에 닿기 전에 올려둔다.
        /// 목욕 +45(청결) · 밥 +20(건강) · 약 +40(건강).
        /// </summary>
        void CareHero()
        {
            Dog hero = DogManager.Instance.Hero;
            if (hero == null) return;

            DogStats st = hero.Stats;

            // 문턱을 60으로 잡으면 다음 감소 뒤에도 45 이상이라 차단선(40)에 안 닿는다.
            // 더 후하게 주면 주인공견이 가게에서 가장 잘 팔리는 사료·샴푸를 먹어치운다.
            if (st.Cleanliness < 60) ActionRunner.TryRun(DogCare.CareAction.Bath(hero));
            if (st.Health < 40) ActionRunner.TryRun(DogCare.CareAction.Medicine(hero));
            if (st.Health < 60) ActionRunner.TryRun(DogCare.CareAction.Feed(hero));
        }

        /// <summary>
        /// 하루치 재고를 다시 채우는 데 드는 도매 합계. 손님은 <b>수요가중치</b>로 상품을 고르므로
        /// 전 상품을 똑같이 쌓을 필요가 없다 — L2에서 전 상품 8개는 1120원이지만
        /// 수요 비례로는 310원이면 된다. 이 값이 운전자본의 기준이다.
        /// </summary>
        // 수요 비례 계산은 InventoryManager가 단일 출처다 — HUD의 승격 경고와 같은 숫자를 써야 한다
        int DailyRestockCost() => InventoryManager.Instance.DailyRestockCost(
            ShopLevelManager.Instance.Level,
            ShopLevelManager.Instance.Current.customersPerDay);

        int DemandTarget(int index) => InventoryManager.Instance.DemandTarget(index,
            ShopLevelManager.Instance.Level,
            ShopLevelManager.Instance.Current.customersPerDay);

        void Restock()
        {
            InventoryManager inv = InventoryManager.Instance;

            // 케어가 실제로 소모하는 두 종만 먼저 깔아둔다. 약은 수요 목표에 맡긴다
            OrderUpTo(inv, DogCare.FoodIndex, CareStock);
            OrderUpTo(inv, DogCare.ShampooIndex, CareStock);

            // 나머지는 수요 비례로만 채운다
            for (int i = 0; i < inv.Catalog.Count; i++)
                OrderUpTo(inv, i, DemandTarget(i));
        }

        void OrderUpTo(InventoryManager inv, int index, int target)
        {
            if (!inv.IsUnlocked(index)) return;

            int have = inv.StorageOf(index) + inv.IncomingOf(index);
            int want = target - have;
            if (want <= 0) return;

            int unit = inv.Catalog.Get(index).wholesale;
            if (unit <= 0) return;

            int quantity = Mathf.Min(want, GameManager.Instance.Money / unit);
            if (quantity > 0) inv.TryOrder(index, quantity);
        }

        /// <summary>
        /// 승격 직후 한 번은 발주할 수 있어야 한다. 리드타임이 1일이라 그 한 번이면
        /// 다음날 매출이 들어와 자립한다 — 2일치를 요구했더니 문턱이 1150원이 되어
        /// 30일 최고 보유액 1140원으로 10원이 모자라 승격이 한 번도 안 됐다(5차 측정).
        /// </summary>
        int UpgradeReserve()
        {
            ShopLevelManager s = ShopLevelManager.Instance;
            if (s.IsMaxLevel) return 0;
            return Mathf.RoundToInt(
                InventoryManager.Instance.DailyRestockCost(s.Level + 1, s.Next.customersPerDay) * 1.5f);
        }

        /// <summary>
        /// 업그레이드는 <b>운전자본을 남기고서만</b> 산다. 2차 측정에서 D5에 590원으로
        /// 250원 업그레이드를 사고 340원이 남았는데 L2는 하루 재고에 더 큰 돈이 필요해
        /// 가게가 영구 자본 부족에 빠졌다. 레벨은 올랐지만 팔 물건이 없었다.
        /// </summary>
        void TryUpgrade()
        {
            ShopLevelManager level = ShopLevelManager.Instance;
            if (level.IsMaxLevel) return;

            string reason;
            if (!level.CanLevelUp(out reason)) return;

            // 승격 <b>이후</b> 레벨의 재고비로 판정한다. 현재 레벨(L1 310원) 기준으로 봤더니
            // L2의 실제 필요액을 과소평가해 승격 직후 자본이 말랐다(4차 측정).
            int after = GameManager.Instance.Money - level.Next.upgradeCost;
            if (after < UpgradeReserve()) return;

            level.TryLevelUp();
        }

        /// <summary>
        /// 슬롯을 남기지 않고, 매번 <b>살 수 있는 가장 비싼</b> 훈련을 산다 —
        /// 획득/슬롯이 비용에 따라 단조 증가하므로(1·2·4·7·14) 그것이 성장 최대다.
        /// 두 축을 번갈아 올려 미모≈훈련도로 맞춘다. 챔피언십 가중치가 매번 랜덤이라
        /// 한쪽에 몰면 총점이 가중치 운에 좌우되고, 반반이면 어떤 가중치에도 성장합/2가 보장된다.
        /// </summary>
        void TrainHero()
        {
            TrainingManager tm = TrainingManager.Instance;
            Dog hero = DogManager.Instance.Hero;
            if (hero == null) return;

            // 운전자본 아래로는 훈련에 쓰지 않는다.
            int floor = DailyRestockCost() * 2;

            // 승격이 눈앞이면 그 자금까지 지킨다. 하한선이 승격 문턱보다 낮으면
            // 훈련이 매일 남는 돈을 다 태워 돈이 문턱에 영영 닿지 않는다(5차 측정: 30일 L1 고정).
            // 승격을 사고 나면 하한선이 도로 내려가므로 교착이 되지 않는다.
            ShopLevelManager s = ShopLevelManager.Instance;
            if (!s.IsMaxLevel && GameManager.Instance.Reputation >= s.Next.requiredReputation)
                floor = Mathf.Max(floor, s.Next.upgradeCost + UpgradeReserve());

            int guard = 0;
            while (tm.SlotsLeft > 0 && guard++ < 32)
            {
                GrowthAxis axis = hero.Stats.Beauty <= hero.Stats.Training
                    ? GrowthAxis.Beauty
                    : GrowthAxis.Training;

                int budget = GameManager.Instance.Money - floor;
                if (budget <= 0) return;

                int pick = BestAffordable(tm, axis, budget);
                if (pick < 0) pick = BestAffordable(tm, Other(axis), budget);
                if (pick < 0) return;

                if (!tm.Train(hero, pick)) return;
            }
        }

        static GrowthAxis Other(GrowthAxis axis) =>
            axis == GrowthAxis.Beauty ? GrowthAxis.Training : GrowthAxis.Beauty;

        static int BestAffordable(TrainingManager tm, GrowthAxis axis, int budget)
        {
            int best = -1;
            int bestCost = 0;

            for (int i = 0; i < tm.Catalog.Count; i++)
            {
                TrainingDef def = tm.Catalog.Get(i);
                if (def.axis != axis || !tm.IsUnlocked(i)) continue;
                if (def.cost > budget || def.cost <= bestCost) continue;

                best = i;
                bestCost = def.cost;
            }
            return best;
        }

        // ---- 시간 단위 ----

        void HandleHour(int hour)
        {
            if (Active) ServeShelves();
        }

        /// <summary>창고에서 진열대를 꽉 채운다. 상자로 나르는 동선을 생략한 완벽한 진열이다.</summary>
        void ServeShelves()
        {
            InventoryManager inv = InventoryManager.Instance;

            for (int i = 0; i < inv.Catalog.Count; i++)
            {
                if (!inv.IsUnlocked(i)) continue;

                int room = inv.ShelfRoom(i);
                int move = Mathf.Min(room, inv.StorageOf(i));
                for (int n = 0; n < move; n++)
                {
                    if (!inv.TryConsumeStorage(i)) break;
                    inv.PlaceOnShelf(i, 1);
                }
            }
        }

        void ServeCustomers()
        {
            CustomerManager cm = CustomerManager.Instance;
            if (cm == null) return;

            // 앞에서부터 계산한다. 큐가 줄어들므로 항상 0번을 본다
            int guard = 0;
            while (guard++ < 16)
            {
                Customer c = cm.QueuedAt(0);
                if (c == null || c.State != CustomerState.Waiting) return;
                if (!cm.Checkout(c)) return;
            }
        }

        void CleanFloor()
        {
            CleanlinessManager clean = CleanlinessManager.Instance;
            if (clean == null) return;

            int guard = 0;
            while (clean.SpotCount > 0 && guard++ < 16)
            {
                DirtSpot spot = clean.SpotAt(0);
                if (spot == null) return;
                clean.Clean(spot);
            }
        }

        void AdvanceDayIfOver()
        {
            if (!TimeManager.Instance.IsDayOver || evening == null) return;
            if (ChampionshipManager.Instance.IsFinalDay(GameManager.Instance.Day)) return;

            evening.PickCard(FlyerCard);
        }

        void OnGUI()
        {
            if (!Active) return;

            if (banner == null)
            {
                banner = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                banner.normal.textColor = new Color(1f, 0.4f, 0.4f);
            }

            GUI.Label(new Rect(18f, Screen.height - 40f, 900f, 28f),
                "● 측정 모드 (M) — 무인 진행 중. 실제 플레이가 아니다", banner);
        }
    }
}
