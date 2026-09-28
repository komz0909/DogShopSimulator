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
    /// 강아지가 한 마리뿐이므로 슬롯은 전부 그 아이에게 간다. 이 수치는 곧 <b>경제의 상한</b>이다.
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

        /// <summary>
        /// 봇이 여기까지만 승급한다. 0이면 제한 없음.
        ///
        /// 진단용 손잡이다. 봇은 올릴 수 있으면 무조건 올리는데, 승급비를 모으는 동안
        /// 훈련을 건너뛴다 — 32차에서 30일 중 17일이 훈련 0이었다. 그게 <b>봇의 정책</b> 때문인지
        /// <b>상품이 늘어 운전자본이 묶인</b> 탓인지는 승급만 멈춰 보면 갈린다.
        /// </summary>
        [SerializeField] int stopUpgradingAt;

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

            MindShopHours();
            ServeCustomers();
            CleanFloor();
            AdvanceDayIfOver();
        }

        /// <summary>
        /// 문을 09시에 열고 18시에 닫는다. 측정값을 예전과 비교하려면 <b>장사 시간이
        /// 똑같아야</b> 한다 — 늦게까지 열어 두면 18~20시의 남는 손님까지 받아
        /// 손님 수가 달라진다.
        /// </summary>
        void MindShopHours()
        {
            ShopHours hours = ShopHours.Instance;
            if (hours == null) return;

            if (hours.Current == ShopHours.Phase.Preparing)
            {
                string reason;
                if (hours.CanOpen(out reason)) hours.Open();
            }
            else if (hours.Current == ShopHours.Phase.Open
                     && TimeManager.Instance.CurrentHour >= TimeManager.CloseHour)
            {
                hours.Close();
            }
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
            // 밤사이 온 배달을 <b>가장 먼저</b> 들인다. 이 줄이 뒤로 가면 두 가지가 한꺼번에 깨진다.
            // 케어가 창고에서 사료·샴푸를 꺼내는데 그때 창고가 비어 있으면 유지 스탯이 40 아래로
            // 떨어져 그날 성장이 0이 되고, 발주도 창고가 빈 줄 알고 문 앞의 물건을 또 시킨다.
            // 16차 측정이 정확히 그 꼴이었다 — 30일 성장 17(정상 168), 훈련지출 590(정상 5,830),
            // 안 쓴 돈 11,417원이 그대로 쌓였다.
            HaulDelivery();

            // 어젯밤 배달 온 가구를 매장 안으로 들여놓는다. 진열보다 먼저여야
            // 그날 아침부터 손님이 닿는 자리에서 팔린다
            HaulFurniture();

            CareHero();

            // 업그레이드가 발주보다 먼저다. 뒤에 두면 발주로 돈을 쓴 직후,
            // 매출이 들어오기 전 하루 중 가장 가난한 순간에 운전자본을 판정해
            // 명성이 요구치의 19배가 되어도 레벨이 30일간 1에 멈춘다(3차 측정).
            TryUpgrade();
            BuyShelfIfShort();
            Restock();

            // 승급 당일 특급 입고분은 Restock 안에서 문 앞에 떨어지므로 한 번 더 쓸어 담는다
            HaulDelivery();

            TrainHero();
        }

        /// <summary>
        /// 진열할 자리가 상품 수보다 적으면 진열대를 하나 산다.
        ///
        /// 손님은 원하는 물건이 <b>진열대에 없으면 아예 오지 않으므로</b>(CustomerManager.Spawn),
        /// 칸이 모자라면 해금된 상품이 그대로 손님 감소가 된다. 사람은 이걸 보고 가구를 사지만
        /// 봇에게는 규칙이 필요하다.
        ///
        /// 싼 것부터 사되 <b>모자란 만큼 사들인다</b>. 하루 하나씩만 사던 때는 승급 한 번에
        /// 상품이 3종씩 늘어나는 속도를 따라잡지 못해, L7~L9 에서 매일 두세 종이 자리를 못 받고
        /// 손실률이 15%에 머물렀다(35차). 한 번에 여러 개를 사면 재고 살 돈이 마른다는 것이
        /// 하나씩 사던 이유였는데, 그 판정은 아래 소진액 문턱이 이미 하고 있다.
        ///
        /// 다만 <b>부피가 모자란 쪽</b>을 산다. 칸 총수만 세면 제일 싼 기본 벽 진열대(260원,
        /// 작은 것 전용)만 계속 사게 되는데, 사료·침대 같은 큰 물건은 L8 에 다섯 종이 되도록
        /// 받아 줄 칸이 아일랜드 진열대 둘뿐이라 절반이 영영 진열되지 못한다.
        /// </summary>
        void BuyShelfIfShort()
        {
            FurnitureShop shop = FurnitureShop.Instance;
            ShelfManager shelves = ShelfManager.Instance;
            if (shop == null || shop.Catalog == null || shelves == null) return;

            int level = ShopLevelManager.Instance.Level;
            int wantSmall = 0, wantBulky = 0;
            for (int i = 0; i < InventoryManager.Instance.Catalog.Count; i++)
            {
                if (InventoryManager.Instance.Catalog.Get(i).unlockLevel > level) continue;
                if (CarryCrate.SlotCostOf(i) == 2) wantBulky++; else wantSmall++;
            }

            int haveAny = 0, haveSmall = 0, haveBulky = 0;
            for (int i = 0; i < shelves.Count; i++) CountSlots(shelves.Get(i), ref haveAny, ref haveSmall, ref haveBulky);

            // 오는 중인 것도 자리로 친다. 안 그러면 배달 기다리는 동안 매일 하나씩 더 산다
            for (int i = 0; i < shop.OrderedCount; i++)
            {
                GameObject prefab = shop.Catalog.Get(shop.OrderedAt(i)).placedPrefab;
                if (prefab == null) continue;
                CountSlots(prefab.GetComponent<ShelfTable>(), ref haveAny, ref haveSmall, ref haveBulky);
            }

            // 모자란 만큼 사들인다. 한 번 사면 칸이 늘어나므로 다시 세어 가며 반복한다
            int guard = 0;
            while (guard++ < 8 && BuyOneShelf(shop, wantSmall, wantBulky, ref haveAny, ref haveSmall, ref haveBulky)) { }
        }

        /// <summary>모자란 부피의 진열대를 하나 산다. 샀으면 참.</summary>
        bool BuyOneShelf(FurnitureShop shop, int wantSmall, int wantBulky,
                         ref int haveAny, ref int haveSmall, ref int haveBulky)
        {
            int shortSmall = Mathf.Max(0, wantSmall - haveSmall);
            int shortBulky = Mathf.Max(0, wantBulky - haveBulky);
            if (shortSmall + shortBulky <= haveAny) return false;   // 아무거나 받는 칸이 메운다

            // 더 급한 쪽을 산다. 같으면 큰 물건 쪽 — 그쪽 상품이 늘 더 비싸다
            int needBulk = shortBulky >= shortSmall ? 2 : 1;

            int cheapest = -1;
            for (int i = 0; i < shop.Catalog.Count; i++)
            {
                string reason;
                if (!shop.CanBuy(i, out reason)) continue;

                GameObject prefab = shop.Catalog.Get(i).placedPrefab;
                ShelfTable table = prefab != null ? prefab.GetComponent<ShelfTable>() : null;
                if (table == null || table.SlotCount <= 0) continue;
                if (table.AcceptedBulk != 0 && table.AcceptedBulk != needBulk) continue;

                if (cheapest < 0 || shop.Catalog.Get(i).price < shop.Catalog.Get(cheapest).price) cheapest = i;
            }
            if (cheapest < 0) return false;

            // 가구를 사고 나서도 그날 팔려 나갈 만큼은 살 수 있어야 한다.
            // 여기서 DailyRestockCost 를 쓰면 <b>진열하지도 못하는 상품까지 합산</b>해서
            // 문턱이 올라가고, 그 바람에 정작 칸을 늘려 줄 진열대를 영영 못 산다 (24차: 손실률 32.6%).
            if (GameManager.Instance.Money - shop.Catalog.Get(cheapest).price < DailyConsumptionCost()) return false;

            if (!shop.TryBuy(cheapest)) return false;

            // 산 만큼 칸을 미리 더해 둔다 — 안 그러면 같은 부족분을 보고 여덟 번 산다
            CountSlots(shop.Catalog.Get(cheapest).placedPrefab.GetComponent<ShelfTable>(),
                       ref haveAny, ref haveSmall, ref haveBulky);
            return true;
        }

        /// <summary>
        /// 산 가구를 들여놓을 자리를 찾을 범위. <b>매장이 넓어지면 같이 넓어진다</b> —
        /// 상수로 박아 두면 확장한 오른쪽 절반을 봇이 영영 쓰지 못한다.
        /// 벽에서 0.4m 는 남겨 둔다(가구 절반 깊이).
        /// </summary>
        static Rect ShopFloor
        {
            get
            {
                Rect floor = ShopSpace.Instance != null
                    ? ShopSpace.Instance.FloorRect
                    : new Rect(0f, 0f, 8f, 6f);
                return new Rect(floor.xMin + 0.4f, floor.yMin + 0.4f, floor.width - 0.8f, floor.height - 0.8f);
            }
        }

        /// <summary>자리를 훑는 간격. 촘촘하게 볼수록 느리고, 이 정도면 가구 하나가 들어간다.</summary>
        const float HaulStep = 0.4f;

        /// <summary>
        /// 출입구 앞은 비워 둔다. 문틈은 x 3.0~5.0 하나뿐이라 여기에 가구를 세우면
        /// 손님이 들어오다 엉켜 <see cref="CustomerManager.TravelTimeout"/>에 걸린다 —
        /// 29차 측정에서 자리 없는 상품이 0인 날에도 하루 8명을 놓쳤다.
        /// </summary>
        static readonly Rect Doorway = new Rect(2.6f, -0.5f, 2.8f, 2.4f);

        /// <summary>
        /// 배달 온 가구를 <b>매장 안으로 들여놓는다.</b>
        ///
        /// 사람은 가구를 들어서 원하는 자리에 놓지만 봇은 그러지 않아, 산 진열대가
        /// 배달 자리(가게 앞마당)에 그대로 서 있었다. 그런데 앞마당은 손님이 못 들어가는
        /// 직원 구역이라, 거기 올린 상품은 <b>손님이 사러 왔다가 그냥 나간다</b> —
        /// 28차 측정에서 이것 하나로 손실률이 0.3%에서 22.4%로 뛰었다.
        /// </summary>
        void HaulFurniture()
        {
            ShelfManager shelves = ShelfManager.Instance;
            if (shelves == null) return;

            for (int i = 0; i < shelves.Count; i++)
            {
                ShelfTable table = shelves.Get(i);
                if (table.CustomersCanReach) continue;

                PlaceableFurniture furniture = table.GetComponent<PlaceableFurniture>();
                if (furniture == null) continue;

                MoveInside(furniture);
            }
        }

        /// <summary>입구에서 가까운 빈 자리를 찾아 옮긴다. 가까울수록 잘 팔린다.</summary>
        void MoveInside(PlaceableFurniture furniture)
        {
            Vector3 entrance = new Vector3(4f, 0f, 0f);

            Vector3 bestPosition = Vector3.zero;
            Quaternion bestRotation = furniture.transform.rotation;
            float bestDistance = float.MaxValue;

            for (float x = ShopFloor.xMin; x <= ShopFloor.xMax; x += HaulStep)
                for (float z = ShopFloor.yMin; z <= ShopFloor.yMax; z += HaulStep)
                {
                    Vector3 floor = new Vector3(x, 0f, z);
                    float distance = Vector3.Distance(floor, entrance);
                    if (distance >= bestDistance) continue;

                    Vector3 position = floor;
                    Quaternion rotation = furniture.transform.rotation;

                    // 벽걸이형은 등을 붙일 벽이 있어야 한다
                    if (furniture.WallMounted
                        && !furniture.TrySnapToWall(floor, out position, out rotation)) continue;

                    if (BlocksDoorway(furniture, position, rotation)) continue;

                    string reason;
                    if (!furniture.Fits(position, rotation, null, out reason)) continue;

                    bestPosition = position;
                    bestRotation = rotation;
                    bestDistance = distance;
                }

            if (bestDistance >= float.MaxValue) return;

            furniture.transform.SetPositionAndRotation(bestPosition, bestRotation);
            ShelfManager.Instance.RefreshApproach(furniture);
        }

        /// <summary>그 자리에 놓으면 문 앞을 막는가. 발자국이 통로에 조금이라도 걸리면 막는 것이다.</summary>
        static bool BlocksDoorway(PlaceableFurniture furniture, Vector3 position, Quaternion rotation)
        {
            Vector3 center = furniture.CenterAt(position, rotation);
            Vector3 half = furniture.RawHalfExtents;

            // 회전한 발자국의 XZ 외접 사각형. 정확히 맞물릴 필요 없이 넉넉히 보면 된다
            Vector3 x = rotation * new Vector3(half.x, 0f, 0f);
            Vector3 z = rotation * new Vector3(0f, 0f, half.z);
            float spanX = Mathf.Abs(x.x) + Mathf.Abs(z.x);
            float spanZ = Mathf.Abs(x.z) + Mathf.Abs(z.z);

            Rect footprint = new Rect(center.x - spanX, center.z - spanZ, spanX * 2f, spanZ * 2f);
            return footprint.Overlaps(Doorway);
        }

        /// <summary>진열대 하나의 칸을 부피 조건별로 더한다.</summary>
        static void CountSlots(ShelfTable table, ref int any, ref int small, ref int bulky)
        {
            if (table == null) return;

            if (table.AcceptedBulk == 1) small += table.SlotCount;
            else if (table.AcceptedBulk == 2) bulky += table.SlotCount;
            else any += table.SlotCount;
        }

        /// <summary>
        /// 가게 앞에 온 배달을 창고로 들인다. 사람은 상자를 들고 몇 번 왕복하지만
        /// 봇은 상자를 쓰지 않으므로(진열도 창고에서 바로 한다) 한 번에 옮긴다.
        ///
        /// <b>이걸 빼면 측정이 통째로 무너진다</b> — 발주한 물건이 문 앞에 쌓이기만 하고
        /// 창고가 영원히 비어 진열도 케어도 못 한다. 순서는 발주 뒤여야 한다(승급일 특급 입고 포함).
        /// </summary>
        void HaulDelivery()
        {
            InventoryManager inv = InventoryManager.Instance;

            for (int i = 0; i < inv.Catalog.Count; i++)
            {
                int guard = 0;
                while (inv.DeliveredOf(i) > 0 && guard++ < 999)
                {
                    if (!inv.TryTakeDelivered(i)) break;
                    inv.Store(i);
                }
            }
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

        int DailyConsumptionCost() => InventoryManager.Instance.DailyConsumptionCost(
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

            // 나머지는 수요 비례로만 채운다. 다만 <b>올려놓을 자리가 없는 상품은 사지 않는다</b> —
            // 상품이 진열 칸보다 많아지면 팔 수도 없는 재고에 현금이 잠겨, 정작 진열한 물건을
            // 다시 채울 돈이 없어진다 (24차: 손실률 32.6%, 훈련 지출이 2,700 으로 주저앉았다).
            for (int i = 0; i < inv.Catalog.Count; i++)
            {
                int displayable = inv.ShelfOf(i) + inv.ShelfRoom(i);
                if (displayable <= 0) continue;

                // <b>진열할 수 있는 만큼만</b> 산다. 강아지 침대는 칸당 1개라 수요 목표가 7이면
                // 칸 7개를 요구하는데, 실제로 받을 수 있는 칸은 하나뿐이라 나머지 6개 값은
                // 창고에 잠긴 채 돌아오지 않는다
                //
                // <b>진열대 위도 재고로 센다.</b> 창고만 보면, 아침마다 진열을 끝내 창고가 빈
                // 상태로 다시 목표치를 사들여 안 팔리는 상품이 끝없이 쌓인다 —
                // 36차에서 기본 약품(수요 최저)이 15개까지 불어나 칸 3개를 먹는 동안
                // 샴푸(수요 3)는 매일 말라붙었다.
                OrderUpTo(inv, i, Mathf.Min(DemandTarget(i), displayable), countShelf: true);
            }
        }

        /// <param name="countShelf">
        /// 진열대 위도 재고로 셀 것인가. 판매용 발주는 참이다 — 총량을 목표치에 맞춘다.
        /// 케어용(사료·샴푸)은 거짓이다. 그건 <b>창고에서만</b> 꺼내 쓰므로,
        /// 진열대에 쌓인 걸 세면 창고가 빈 채로 밥을 못 준다.
        /// </param>
        void OrderUpTo(InventoryManager inv, int index, int target, bool countShelf = false)
        {
            if (!inv.IsUnlocked(index)) return;

            // 문 앞에 놓인 것도 이미 산 물건이다. 빼먹으면 같은 것을 두 번 시킨다
            int have = inv.StorageOf(index) + inv.IncomingOf(index) + inv.DeliveredOf(index);
            if (countShelf) have += inv.ShelfOf(index);
            int want = target - have;
            if (want <= 0) return;

            int unit = inv.Catalog.Get(index).wholesale;
            if (unit <= 0) return;

            int quantity = Mathf.Min(want, GameManager.Instance.Money / unit);
            if (quantity > 0) inv.TryOrder(index, quantity);
        }

        /// <summary>
        /// 승격 직후 한 번은 발주할 수 있어야 한다. 승급 당일 발주가 <b>즉시 입고</b>되므로
        /// 딱 하루치면 충분하다 — 1.5일치를 요구하던 시절에는 문턱이 2782원까지 올라
        /// L4에서 승격도 훈련도 같이 멈췄다(7차 측정).
        /// </summary>
        int UpgradeReserve()
        {
            ShopLevelManager s = ShopLevelManager.Instance;
            if (s.IsMaxLevel) return 0;

            // <b>그날 팔려 나갈 만큼 + 새로 열리는 상품의 첫 채움.</b> 이게 승급 직후 실제로
            // 나가는 돈이다.
            //
            // 예전에는 진열 목표치 총액(DailyRestockCost)을 썼다. 그건 <b>이미 쌓아 둔 상품까지</b>
            // 여유분(+2)째로 새로 사는 값이라, 상품이 늘 때마다 문턱만 부풀어
            // 해금 사다리를 앞당길 때마다 봇이 그 레벨에 묶였다 — 31차에서는 L4→L5 문턱이
            // 2,710원으로 올라 봇이 30일 내내 L4에 갇혔다(진열은 6/6 완벽, 손실률 0.0%였는데도).
            //
            // 소진액만으로 낮췄던 26차가 실패한 것은 이 값 때문이 아니라 <b>진열 칸</b> 때문이었다.
            // 그때는 L6 해금 12종이 칸을 넘었고, 봇이 산 진열대는 손님이 못 가는 가게 앞에
            // 서 있었다. 둘 다 고친 뒤라 다시 푼다.
            return InventoryManager.Instance.DailyConsumptionCost(s.Level + 1, s.Next.customersPerDay)
                 + InventoryManager.Instance.FirstStockCost(s.Level + 1, s.Next.customersPerDay);
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
            if (stopUpgradingAt > 0 && level.Level >= stopUpgradingAt) return;

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

            // 이틀치 <b>소진액</b>은 건드리지 않는다. 진열 목표치 총액(DailyRestockCost)을
            // 하한선으로 쓰면 여유분까지 매일 다시 쟁이는 셈이라 L4에서 1255원이 묶여
            // 30일 중 13일이 훈련 0이 됐다(8차 측정).
            int floor = InventoryManager.Instance.DailyConsumptionCost(
                ShopLevelManager.Instance.Level,
                ShopLevelManager.Instance.Current.customersPerDay) * 2;

            int guard = 0;
            while (tm.SlotsLeft > 0 && guard++ < 32)
            {
                GrowthAxis axis = hero.Stats.Beauty <= hero.Stats.Training
                    ? GrowthAxis.Beauty
                    : GrowthAxis.Training;

                // 하한선 위 잉여의 <b>절반만</b> 훈련에 쓰고 나머지는 승격 자금으로 쌓는다.
                // 승격 문턱을 통째로 하한선에 넣었더니 그 돈이 훈련 예산을 전부 삼켜
                // 30일 내내 훈련이 0이었다(7차 측정: 성장 4 고정, 챔피언십 2점).
                // 반씩 나누면 훈련도 승격도 멈추지 않는다.
                int budget = (GameManager.Instance.Money - floor) / 2;
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
                if (tm.UsedToday(i)) continue;              // 같은 훈련은 하루 한 번
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

        /// <summary>
        /// 마감했으면 자고 다음 날로 넘어간다. 예전에는 18시에 하루가 저절로 끝났지만
        /// 이제는 <b>자야</b> 넘어가므로, 사람이 침대에서 하는 일을 여기서 대신한다.
        /// </summary>
        void AdvanceDayIfOver()
        {
            if (evening == null) return;
            if (ChampionshipManager.Instance.IsFinalDay(GameManager.Instance.Day)) return;

            if (!TimeManager.Instance.IsDayOver)
            {
                bool closed = ShopHours.Instance == null || ShopHours.Instance.Current == ShopHours.Phase.Closed;
                if (!closed) return;

                TimeManager.Instance.EndDayNow();   // 침대에서 자는 것과 같은 경로
                return;
            }

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
