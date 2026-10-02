using System;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 재고 2단 구조. 발주는 <b>창고</b>를 채우고, 진열은 창고에서 <b>테이블</b>로 옮긴다.
    /// 손님은 테이블에서만 사고, 강아지 소모품은 창고에서만 나간다.
    /// 그래서 진열하는 순간 그 물건은 판매용으로 확정된다 — 이것이 재고 이중용도의 실제 선택 지점이다.
    /// </summary>
    public class InventoryManager : MonoBehaviour, ISaveParticipant
    {
        public const int ShelfCapacity = 8;

        public static InventoryManager Instance { get; private set; }

        [SerializeField] ProductCatalog catalog;
        [SerializeField] int startingStoragePerProduct = 8;

        int[] storage;
        int[] incoming;

        /// <summary>
        /// 가게 앞에 배달되어 아직 안 들인 것. <b>발주가 창고로 바로 들어가지 않는 이유</b>가 여기다 —
        /// 주문한 물건은 아침에 문 앞에 놓이고, 플레이어가 상자로 날라야 창고나 진열대로 간다.
        /// 아침에 한 번 나갔다 오는 그 동선이 발주의 값이고, 늦잠을 자면 그만큼 개점이 늦어진다.
        /// </summary>
        int[] delivered;

        /// <summary>
        /// 승급 당일에만 켜지는 즉시 입고. 승급하면 손님이 한 번에 늘고 새 카테고리가 열리는데
        /// 리드타임이 1일이라 <b>그 수요를 받을 재고가 존재할 수 없다</b>. 매출 0인 하루가 생기면
        /// 다음날 재고를 두세 개밖에 못 사고 그대로 빈곤 함정에 빠진다
        /// (30일 무인 측정 6회 중 승급한 4회가 전부 붕괴).
        ///
        /// <b>전환 한 칸만</b> 없앤다 — 정상 운영 중인 어느 레벨의 수익 구조도 건드리지 않으므로
        /// 10레벨 감시 지표를 다시 계산할 필요가 없다.
        /// </summary>
        bool rushDelivery;

        public event Action OnStockChanged;

        public ProductCatalog Catalog => catalog;
        public int StorageOf(int index) => storage[index];
        /// <summary>
        /// 진열된 개수는 <b>진열대의 칸이 직접 들고 있다</b>. 여기서는 전 진열대를 훑어 합계만 낸다 —
        /// 칸마다 상품이 배정되고 잠기므로 전역 배열로는 "어느 칸에 몇 개"를 표현할 수 없다.
        /// </summary>
        public int ShelfOf(int index)
        {
            ShelfManager shelves = ShelfManager.Instance;
            if (shelves == null) return 0;

            int sum = 0;
            for (int i = 0; i < shelves.Count; i++) sum += shelves.Get(i).TotalOf(index);
            return sum;
        }
        public int IncomingOf(int index) => incoming[index];

        /// <summary>가게 앞에 놓여 있는 개수.</summary>
        public int DeliveredOf(int index) => delivered[index];

        /// <summary>가게 앞에 남은 총 개수. 아직 안 들인 배달이 있는지 한눈에 보려고.</summary>
        public int DeliveredTotal
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < delivered.Length; i++) sum += delivered[i];
                return sum;
            }
        }
        /// <summary>
        /// 그 상품을 더 진열할 수 있는 여유. 빈 칸을 전부 세지 않는다 —
        /// 한 칸도 못 받은 상품이 남아 있으면 그 칸은 내 것이 아니다
        /// (<see cref="ShelfManager.RoomFor"/>).
        /// </summary>
        public int ShelfRoom(int index)
        {
            ShelfManager shelves = ShelfManager.Instance;
            return shelves != null ? shelves.RoomFor(index) : 0;
        }

        /// <summary>오늘 발주가 즉시 입고되는가. 승급한 날 하루만 참이다.</summary>
        public bool RushDelivery => rushDelivery;

        /// <summary>
        /// 그 레벨에서 하루치 재고를 채우는 데 드는 도매 합계 — <b>운전자본의 기준</b>이다.
        /// 손님은 수요가중치로 상품을 고르므로 전 상품을 똑같이 쌓을 필요가 없다.
        /// L2 기준 전 상품 8개는 1120원이지만 수요 비례로는 450원이면 된다.
        /// </summary>
        public int DailyRestockCost(int level, int customersPerDay)
        {
            int total = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog.Get(i).unlockLevel > level) continue;
                total += DemandTarget(i, level, customersPerDay) * catalog.Get(i).wholesale;
            }
            return total;
        }

        /// <summary>
        /// 하루에 실제로 팔려 나가는 만큼의 도매 합계 — <b>하루 소진액</b>이다.
        /// <see cref="DailyRestockCost"/> 는 진열을 목표치까지 채우는 <b>총액</b>이라
        /// 여유분(상품당 +2)까지 포함하는데, 그 여유분은 한 번 사두면 계속 남는다.
        /// 매일 손에 쥐고 있어야 하는 돈은 이쪽이다.
        /// </summary>
        public int DailyConsumptionCost(int level, int customersPerDay)
        {
            int totalWeight = 0;
            for (int i = 0; i < catalog.Count; i++)
                if (catalog.Get(i).unlockLevel <= level) totalWeight += catalog.Get(i).demandWeight;
            if (totalWeight <= 0) return 0;

            float cost = 0f;
            for (int i = 0; i < catalog.Count; i++)
            {
                ProductDef p = catalog.Get(i);
                if (p.unlockLevel > level) continue;
                cost += customersPerDay * (p.demandWeight / (float)totalWeight) * p.wholesale;
            }
            return Mathf.CeilToInt(cost);
        }

        /// <summary>
        /// 그 레벨에서 <b>새로 열리는</b> 상품을 처음 한 번 채우는 데 드는 도매 합계.
        ///
        /// 승급하면 없던 상품이 생기고, 그건 진짜로 한 번 목돈이 든다. 반면
        /// <see cref="DailyRestockCost"/> 는 <b>이미 쌓아 둔 상품까지</b> 매일 여유분(+2)째로
        /// 새로 사는 값을 매겨, 상품이 늘수록 승급 문턱만 부풀린다 —
        /// 해금 사다리를 앞당길 때마다 봇이 그 레벨에 묶인 원인이 이것이다(18·24·31차).
        /// </summary>
        public int FirstStockCost(int level, int customersPerDay)
        {
            int total = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog.Get(i).unlockLevel != level) continue;
                total += DemandTarget(i, level, customersPerDay) * catalog.Get(i).wholesale;
            }
            return total;
        }

        /// <summary>그 상품이 하루에 몇 개 팔릴지 — 손님 수 × (수요가중치 / 전체 가중치) + 여유 1.</summary>
        public int DemandTarget(int index, int level, int customersPerDay)
        {
            int totalWeight = 0;
            for (int i = 0; i < catalog.Count; i++)
                if (catalog.Get(i).unlockLevel <= level) totalWeight += catalog.Get(i).demandWeight;
            if (totalWeight <= 0) return 1;

            // 여유 2개. 손님의 상품 선택은 확률이라 평균보다 몰리는 날이 있고,
            // 여유 1개로는 그 변동을 못 받아 L1에서도 하루 5명까지 놓쳤다(5차 측정).
            int target = Mathf.CeilToInt(customersPerDay * catalog.Get(index).demandWeight / (float)totalWeight) + 2;
            return Mathf.Clamp(target, 1, ShelfCapacity);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            storage = new int[catalog.Count];
            incoming = new int[catalog.Count];
            delivered = new int[catalog.Count];
            graded = new int[catalog.Count * ItemGrades.Count];

            for (int i = 0; i < catalog.Count; i++)
                if (catalog.Get(i).unlockLevel <= 1) storage[i] = startingStoragePerProduct;
        }

        void Start()
        {
            TimeManager.Instance.OnDayStarted += StartDay;
        }

        void StartDay()
        {
            rushDelivery = false;   // 특급 입고는 승급한 그 하루로 끝난다

            // 밤을 넘긴 긴급 발주는 아침 트럭에 같이 실려 온다. 돈은 이미 받았으므로
            // 자고 일어났더니 사라졌다는 일이 있어서는 안 된다
            for (int i = 0; i < express.Count; i++) AddParcel(express[i].product, express[i].quantity);
            express.Clear();
            ExpressUsedToday = false;

            ReceiveOrders();
        }

        /// <summary>긴급 발주 한 건. 값을 더 치르고 그날 안에 받는다.</summary>
        struct ExpressOrder
        {
            public int product;
            public int quantity;
            public float dueHour;
        }

        readonly List<ExpressOrder> express = new List<ExpressOrder>();

        /// <summary>긴급 발주 할증. 도매가에 이만큼 곱한다.</summary>
        public const float ExpressRate = 1.5f;

        /// <summary>긴급 발주가 도착하기까지의 게임 시간.</summary>
        public const float ExpressHours = 1f;

        /// <summary>아직 안 온 긴급 발주 수. 상점창이 보여 준다.</summary>
        public int ExpressPending => express.Count;

        /// <summary>
        /// 오늘 긴급 발주를 이미 썼는가. <b>하루 한 번</b>이다.
        ///
        /// 횟수를 막지 않으면 리드타임 1일이 통째로 사라진다 — 재고가 떨어질 때마다
        /// 1.5배를 내고 한 시간 뒤에 받으면 되므로, 미리 시켜 두는 계획이 필요 없어진다.
        /// 한 번으로 묶으면 "오늘의 구멍 하나를 어디에 쓸까"가 남는다.
        /// </summary>
        public bool ExpressUsedToday { get; private set; }

        /// <summary>가장 먼저 오는 긴급 발주까지 남은 시간. 없으면 0.</summary>
        public float ExpressSoonest
        {
            get
            {
                if (express.Count == 0 || TimeManager.Instance == null) return 0f;

                float soonest = float.MaxValue;
                for (int i = 0; i < express.Count; i++) soonest = Mathf.Min(soonest, express[i].dueHour);
                return Mathf.Max(0f, soonest - TimeManager.Instance.CurrentHour);
            }
        }

        /// <summary>
        /// 시간이 된 긴급 발주를 문 앞에 내린다.
        ///
        /// 정시 이벤트가 아니라 매 프레임 시계를 본다 — 10:30 에 시킨 것은 11:30 에 와야지
        /// 11:00 에 오면 안 된다. 목록이 길어야 서넛이라 훑는 비용은 없다.
        /// </summary>
        void Update()
        {
            if (express.Count == 0 || TimeManager.Instance == null) return;

            float now = TimeManager.Instance.CurrentHour;
            bool any = false;

            for (int i = express.Count - 1; i >= 0; i--)
            {
                if (express[i].dueHour > now) continue;

                AddParcel(express[i].product, express[i].quantity);
                express.RemoveAt(i);
                any = true;
            }

            if (any) OnStockChanged?.Invoke();
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= StartDay;
            if (Instance == this) Instance = null;
        }

        public bool IsUnlocked(int index) => catalog.Get(index).unlockLevel <= ShopLevelManager.Instance.Level;

        // ---- 발주: 창고를 채운다 ----

        public bool TryOrder(int index, int quantity)
        {
            if (quantity <= 0 || !IsUnlocked(index)) return false;

            int cost = catalog.Get(index).wholesale * quantity;
            if (!GameManager.Instance.TrySpend(cost)) return false;

            // 특급 입고도 창고로 바로 넣지 않는다 — 트럭이 그날 안에 올 뿐이다.
            // 문 앞까지 와도 들이는 것은 사람 몫이라는 규칙은 어느 날에도 같다.
            if (rushDelivery) AddParcel(index, quantity);
            else incoming[index] += quantity;

            OnStockChanged?.Invoke();
            return true;
        }

        /// <summary>장바구니 한 개 분의 값. 긴급이면 할증이 붙는다.</summary>
        public int CostOf(IList<int> cart, bool expressed)
        {
            if (cart == null) return 0;

            int sum = 0;
            for (int i = 0; i < cart.Count && i < catalog.Count; i++)
            {
                if (cart[i] <= 0) continue;
                sum += catalog.Get(i).wholesale * cart[i];
            }
            return expressed ? Mathf.CeilToInt(sum * ExpressRate) : sum;
        }

        /// <summary>
        /// 장바구니를 한 번에 발주한다. 값은 <b>통째로 한 번</b> 치른다 —
        /// 품목마다 따로 빼면 중간에 돈이 떨어져 절반만 주문된 채로 끝난다.
        ///
        /// 긴급은 할증을 물고 <see cref="ExpressHours"/> 뒤에 문 앞으로 온다.
        /// 보통 발주는 지금까지처럼 내일 아침 트럭이다.
        /// </summary>
        public bool TryOrderCart(IList<int> cart, bool expressed, out string reason)
        {
            int cost = CostOf(cart, expressed);
            if (cost <= 0) { reason = "담은 것이 없다"; return false; }

            if (expressed && ExpressUsedToday)
            {
                reason = "긴급 발주는 하루 한 번 — 내일 다시";
                return false;
            }

            for (int i = 0; i < cart.Count && i < catalog.Count; i++)
                if (cart[i] > 0 && !IsUnlocked(i)) { reason = catalog.Get(i).nameKo + " 는 아직 못 산다"; return false; }

            if (GameManager.Instance.Money < cost)
            {
                reason = "재화 부족 — " + GameManager.Instance.Money + " / " + cost;
                return false;
            }
            if (!GameManager.Instance.TrySpend(cost)) { reason = "결제 실패"; return false; }

            float due = TimeManager.Instance != null
                ? TimeManager.Instance.CurrentHour + ExpressHours
                : ExpressHours;

            for (int i = 0; i < cart.Count && i < catalog.Count; i++)
            {
                if (cart[i] <= 0) continue;

                // 승급 당일 특급 입고가 켜져 있으면 보통 발주도 그날 안에 온다
                if (expressed) express.Add(new ExpressOrder { product = i, quantity = cart[i], dueHour = due });
                else if (rushDelivery) AddParcel(i, cart[i]);
                else incoming[i] += cart[i];
            }

            // 장바구니 하나가 한 번이다. 품목 수로 세면 나눠 담아 하루에 몇 번이고 쓴다
            if (expressed) ExpressUsedToday = true;

            OnStockChanged?.Invoke();
            reason = null;
            return true;
        }

        /// <summary>승급 직후 하루 동안 발주를 즉시 입고로 바꾼다. ShopLevelManager가 부른다.</summary>
        public void BeginRushDelivery()
        {
            rushDelivery = true;

            // 승급 전에 넣어둔 주문도 같이 당겨준다 — 승급 당일에 두 번 발주하게 만들 이유가 없다
            ReceiveOrders();
        }

        /// <summary>
        /// 가게 앞에 쌓인 <b>안 뜯은 상자</b> 하나. 상품 한 종이 통째로 들어 있다.
        ///
        /// 상자를 거치는 이유는 마당이 깔끔해서다 — 예전에는 배달이 오는 즉시 상품 더미가
        /// 흩어져 나타나서, 상품이 늘수록 마당이 잡동사니 창고처럼 보였다.
        /// </summary>
        struct Parcel
        {
            /// <summary>상자 고유 번호. 목록에서 하나가 빠지면 첨자가 밀리므로 첨자로는 못 가리킨다.</summary>
            public int id;
            public int product;
            public int quantity;
        }

        readonly List<Parcel> parcels = new List<Parcel>();
        int nextParcelId = 1;

        public int ParcelCount => parcels.Count;
        public int ParcelIdAt(int i) => i >= 0 && i < parcels.Count ? parcels[i].id : 0;
        public int ParcelProductAt(int i) => i >= 0 && i < parcels.Count ? parcels[i].product : -1;
        public int ParcelQuantityAt(int i) => i >= 0 && i < parcels.Count ? parcels[i].quantity : 0;

        /// <summary>안 뜯은 상자에 든 총 개수. 발주창이 "아직 안 들인 것"을 셀 때 쓴다.</summary>
        public int ParcelTotal
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < parcels.Count; i++) sum += parcels[i].quantity;
                return sum;
            }
        }

        /// <summary>
        /// 상자를 뜯는다. 내용물은 <b>그 자리에</b> 쏟아져 문 앞 재고가 된다 —
        /// 창고로 바로 넣지 않는 이유는 물건을 직접 나르는 노동이 이 게임의 중심이라서다.
        /// </summary>
        public bool OpenParcel(int id)
        {
            for (int i = 0; i < parcels.Count; i++)
            {
                if (parcels[i].id != id) continue;

                delivered[parcels[i].product] += parcels[i].quantity;
                parcels.RemoveAt(i);
                OnStockChanged?.Invoke();
                return true;
            }
            return false;
        }

        void AddParcel(int product, int quantity)
        {
            if (quantity <= 0) return;
            parcels.Add(new Parcel { id = nextParcelId++, product = product, quantity = quantity });
        }

        /// <summary>트럭이 왔다. 물건은 <b>가게 앞</b>에 상자째 내려놓고 간다.</summary>
        void ReceiveOrders()
        {
            bool any = false;
            for (int i = 0; i < incoming.Length; i++)
            {
                if (incoming[i] <= 0) continue;
                AddParcel(i, incoming[i]);
                incoming[i] = 0;
                any = true;
            }
            if (any) OnStockChanged?.Invoke();
        }

        /// <summary>가게 앞 배달 더미에서 하나 집는다. 집은 것은 호출한 쪽이 상자에 넣는다.</summary>
        public bool TryTakeDelivered(int index)
        {
            if (index < 0 || index >= delivered.Length || delivered[index] <= 0) return false;

            delivered[index]--;
            OnStockChanged?.Invoke();
            return true;
        }

        /// <summary>상자에서 창고로 옮긴다. <see cref="TryConsumeStorage"/> 의 반대 방향이다.</summary>
        public void Store(int index, int quantity = 1)
        {
            if (index < 0 || index >= storage.Length || quantity <= 0) return;

            storage[index] += quantity;
            OnStockChanged?.Invoke();
        }

        /// <summary>대금 없이 창고에 넣는다. 폐품 수집 같은 이벤트 보상용.</summary>
        public void Grant(int index, int quantity)
        {
            if (quantity <= 0) return;
            storage[index] += quantity;
            OnStockChanged?.Invoke();
        }

        // ---- 등급품 (랜덤박스에서만 나온다) ----

        /// <summary>
        /// 등급이 붙은 재고. 상품마다 등급 수만큼 칸을 쓴다.
        ///
        /// 발주로 들여온 물건과 <b>섞지 않는다</b> — 같은 기본 사료라도 S등급은 5배에 팔리므로
        /// 한 덩어리로 세면 어느 값에 팔아야 할지 알 수 없다.
        /// </summary>
        int[] graded;

        int GradeSlot(int index, ItemGrade grade) => index * ItemGrades.Count + (int)grade;

        public int GradedOf(int index, ItemGrade grade) =>
            graded != null && grade != ItemGrade.None ? graded[GradeSlot(index, grade)] : 0;

        /// <summary>등급 상관없이 그 상품의 등급품 총합. HUD와 발주 판단에 쓴다.</summary>
        public int GradedTotalOf(int index)
        {
            if (graded == null) return 0;

            int sum = 0;
            for (int g = 0; g < ItemGrades.Count; g++) sum += graded[GradeSlot(index, g == 0 ? ItemGrade.F : (ItemGrade)g)];
            return sum;
        }

        /// <summary>랜덤박스가 깐 물건을 창고에 넣는다.</summary>
        public void StoreGraded(int index, ItemGrade grade, int quantity = 1)
        {
            if (graded == null || index < 0 || index >= catalog.Count) return;
            if (grade == ItemGrade.None || quantity <= 0) return;

            graded[GradeSlot(index, grade)] += quantity;
            OnStockChanged?.Invoke();
        }

        public bool TryConsumeGraded(int index, ItemGrade grade)
        {
            if (graded == null || grade == ItemGrade.None) return false;

            int slot = GradeSlot(index, grade);
            if (graded[slot] <= 0) return false;

            graded[slot]--;
            OnStockChanged?.Invoke();
            return true;
        }

        // ---- 진열: 상자 -> 테이블 ----

        /// <summary>
        /// 플레이어가 들고 온 상자에서 진열대로 옮긴다. 창고에서 직접 채우는 경로는 없다 —
        /// 창고 선반에서 상자에 담아 걸어와야 하고, 그 동선이 진열의 비용이다.
        /// </summary>
        public void PlaceOnShelf(int index, int quantity) => PlaceOnShelf(index, quantity, ItemGrade.None);

        public void PlaceOnShelf(int index, int quantity, ItemGrade grade)
        {
            ShelfManager shelves = ShelfManager.Instance;
            if (shelves == null) return;

            int left = quantity;
            for (int i = 0; i < shelves.Count && left > 0; i++)
            {
                ShelfTable table = shelves.Get(i);
                while (left > 0)
                {
                    // 등급이 다르면 다른 칸이다 — 섞으면 값을 가릴 수 없다
                    int slot = table.FirstSlotFor(index, grade);
                    if (slot < 0) break;

                    table.Place(slot, index, grade);
                    left--;
                }
            }

            if (left < quantity) OnStockChanged?.Invoke();
        }

        // ---- 소비 ----

        /// <summary>손님 구매. 테이블에서만 나간다 — 창고에 있어도 테이블이 비면 놓친다.</summary>
        public bool TryConsumeShelf(int index)
        {
            ItemGrade taken;
            return TryConsumeShelf(index, out taken);
        }

        /// <summary>손님이 집어 간 물건의 등급까지 돌려준다. 값이 등급마다 다르다.</summary>
        public bool TryConsumeShelf(int index, out ItemGrade taken)
        {
            taken = ItemGrade.None;

            ShelfManager shelves = ShelfManager.Instance;
            if (shelves == null) return false;

            for (int i = 0; i < shelves.Count; i++)
            {
                if (!shelves.Get(i).Consume(index, out taken)) continue;
                OnStockChanged?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 손님이 물건을 두고 나갔다. 테이블이 가득하면 창고로 돌린다.
        ///
        /// 들고 있던 <b>등급 그대로</b> 되돌린다 — 안 그러면 S등급을 집었다 놓친 손님 때문에
        /// 그 물건이 평범한 발주품으로 바뀌어 사라진다.
        /// </summary>
        public void ReturnToShelf(int index, ItemGrade grade = ItemGrade.None)
        {
            ShelfManager shelves = ShelfManager.Instance;

            bool placed = false;
            for (int i = 0; shelves != null && i < shelves.Count && !placed; i++)
                placed = shelves.Get(i).Restore(index, grade);

            // 놓을 칸이 없으면 창고로 돌린다
            if (!placed)
            {
                if (grade == ItemGrade.None) storage[index]++;
                else graded[GradeSlot(index, grade)]++;
            }

            OnStockChanged?.Invoke();
        }

        /// <summary>강아지 소모품. 창고에서만 나간다.</summary>
        public bool TryConsumeStorage(int index)
        {
            if (storage[index] <= 0) return false;
            storage[index]--;
            OnStockChanged?.Invoke();
            return true;
        }

        public void CaptureInto(SaveData data)
        {
            data.storage = (int[])storage.Clone();
            ShelfManager.Instance?.CaptureInto(data);
            data.incoming = (int[])incoming.Clone();
            data.delivered = (int[])delivered.Clone();
            data.graded = (int[])graded.Clone();
            data.rushDelivery = rushDelivery;

            // 긴급 발주는 <b>대금은 냈는데 아직 안 온</b> 물건이다. 안 남기면 영구 유실된다 —
            // 운반 상자를 저장하는 것과 같은 이유다
            data.expressProduct = new int[express.Count];
            data.expressQuantity = new int[express.Count];
            data.expressDueHour = new float[express.Count];
            for (int i = 0; i < express.Count; i++)
            {
                data.expressProduct[i] = express[i].product;
                data.expressQuantity[i] = express[i].quantity;
                data.expressDueHour[i] = express[i].dueHour;
            }

            // 하루 한 번 제한도 같이 남긴다. 안 그러면 저장하고 불러오는 것만으로 다시 쓸 수 있다
            data.expressUsedToday = ExpressUsedToday;

            data.parcelProduct = new int[parcels.Count];
            data.parcelQuantity = new int[parcels.Count];
            for (int i = 0; i < parcels.Count; i++)
            {
                data.parcelProduct[i] = parcels[i].product;
                data.parcelQuantity[i] = parcels[i].quantity;
            }
        }

        public void RestoreFrom(SaveData data)
        {
            CopyInto(data.storage, storage);
            ShelfManager.Instance?.RestoreFrom(data);
            CopyInto(data.incoming, incoming);
            CopyInto(data.delivered, delivered);
            CopyInto(data.graded, graded);
            rushDelivery = data.rushDelivery;

            ExpressUsedToday = data.expressUsedToday;

            parcels.Clear();
            int boxed = data.parcelProduct != null ? data.parcelProduct.Length : 0;
            for (int i = 0; i < boxed; i++)
            {
                int product = data.parcelProduct[i];
                if (product < 0 || product >= catalog.Count) continue;   // 옛 세이브에 없던 상품

                AddParcel(product, data.parcelQuantity != null && i < data.parcelQuantity.Length
                    ? data.parcelQuantity[i] : 0);
            }

            express.Clear();
            int pending = data.expressProduct != null ? data.expressProduct.Length : 0;
            for (int i = 0; i < pending; i++)
            {
                int product = data.expressProduct[i];
                if (product < 0 || product >= catalog.Count) continue;   // 옛 세이브에 없던 상품

                express.Add(new ExpressOrder
                {
                    product = product,
                    quantity = data.expressQuantity != null && i < data.expressQuantity.Length ? data.expressQuantity[i] : 0,
                    dueHour = data.expressDueHour != null && i < data.expressDueHour.Length ? data.expressDueHour[i] : 0f,
                });
            }

            OnStockChanged?.Invoke();
        }

        /// <summary>세이브 이후 상품이 추가되어 길이가 달라져도 안전하게 복원한다.</summary>
        static void CopyInto(int[] source, int[] target)
        {
            if (source == null || target == null) return;

            int count = Mathf.Min(source.Length, target.Length);
            for (int i = 0; i < count; i++) target[i] = source[i];
            for (int i = count; i < target.Length; i++) target[i] = 0;
        }

        public sealed class OrderAction : IPlayerAction
        {
            readonly int index;
            readonly int quantity;

            public OrderAction(int index, int quantity)
            {
                this.index = index;
                this.quantity = quantity;
            }

            public bool CanExecute(out string reason)
            {
                InventoryManager inv = Instance;
                if (!inv.IsUnlocked(index)) { reason = "미해금 상품"; return false; }

                int cost = inv.catalog.Get(index).wholesale * quantity;
                if (GameManager.Instance.Money < cost)
                {
                    reason = "재화 부족 — " + GameManager.Instance.Money + " / " + cost;
                    return false;
                }

                reason = null;
                return true;
            }

            public void Execute() => Instance.TryOrder(index, quantity);
        }

    }
}
