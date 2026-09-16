using System;
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
        public int ShelfRoom(int index)
        {
            ShelfManager shelves = ShelfManager.Instance;
            if (shelves == null) return 0;

            int room = 0;
            for (int i = 0; i < shelves.Count; i++) room += shelves.Get(i).RoomFor(index);
            return room;
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
            ReceiveOrders();
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

            if (rushDelivery) storage[index] += quantity;
            else incoming[index] += quantity;

            OnStockChanged?.Invoke();
            return true;
        }

        /// <summary>승급 직후 하루 동안 발주를 즉시 입고로 바꾼다. ShopLevelManager가 부른다.</summary>
        public void BeginRushDelivery()
        {
            rushDelivery = true;

            // 승급 전에 넣어둔 주문도 같이 당겨준다 — 승급 당일에 두 번 발주하게 만들 이유가 없다
            ReceiveOrders();
        }

        void ReceiveOrders()
        {
            bool any = false;
            for (int i = 0; i < incoming.Length; i++)
            {
                if (incoming[i] <= 0) continue;
                storage[i] += incoming[i];
                incoming[i] = 0;
                any = true;
            }
            if (any) OnStockChanged?.Invoke();
        }

        /// <summary>대금 없이 창고에 넣는다. 폐품 수집 같은 이벤트 보상용.</summary>
        public void Grant(int index, int quantity)
        {
            if (quantity <= 0) return;
            storage[index] += quantity;
            OnStockChanged?.Invoke();
        }

        // ---- 진열: 상자 -> 테이블 ----

        /// <summary>
        /// 플레이어가 들고 온 상자에서 진열대로 옮긴다. 창고에서 직접 채우는 경로는 없다 —
        /// 창고 선반에서 상자에 담아 걸어와야 하고, 그 동선이 진열의 비용이다.
        /// </summary>
        public void PlaceOnShelf(int index, int quantity)
        {
            ShelfManager shelves = ShelfManager.Instance;
            if (shelves == null) return;

            int left = quantity;
            for (int i = 0; i < shelves.Count && left > 0; i++)
            {
                ShelfTable table = shelves.Get(i);
                while (left > 0)
                {
                    int slot = table.FirstSlotFor(index);
                    if (slot < 0) break;

                    table.Place(slot, index);
                    left--;
                }
            }

            if (left < quantity) OnStockChanged?.Invoke();
        }

        // ---- 소비 ----

        /// <summary>손님 구매. 테이블에서만 나간다 — 창고에 있어도 테이블이 비면 놓친다.</summary>
        public bool TryConsumeShelf(int index)
        {
            ShelfManager shelves = ShelfManager.Instance;
            if (shelves == null) return false;

            for (int i = 0; i < shelves.Count; i++)
            {
                if (!shelves.Get(i).Consume(index)) continue;
                OnStockChanged?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>손님이 물건을 두고 나갔다. 테이블이 가득하면 창고로 돌린다.</summary>
        public void ReturnToShelf(int index)
        {
            ShelfManager shelves = ShelfManager.Instance;

            bool placed = false;
            for (int i = 0; shelves != null && i < shelves.Count && !placed; i++)
                placed = shelves.Get(i).Restore(index);

            if (!placed) storage[index]++;   // 놓을 칸이 없으면 창고로 돌린다
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
            data.rushDelivery = rushDelivery;
        }

        public void RestoreFrom(SaveData data)
        {
            CopyInto(data.storage, storage);
            ShelfManager.Instance?.RestoreFrom(data);
            CopyInto(data.incoming, incoming);
            rushDelivery = data.rushDelivery;
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
