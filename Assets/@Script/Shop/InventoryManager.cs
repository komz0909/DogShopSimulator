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
        int[] shelf;
        int[] incoming;

        public event Action OnStockChanged;

        public ProductCatalog Catalog => catalog;
        public int StorageOf(int index) => storage[index];
        public int ShelfOf(int index) => shelf[index];
        public int IncomingOf(int index) => incoming[index];
        public int ShelfRoom(int index) => ShelfCapacity - shelf[index];

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
            shelf = new int[catalog.Count];
            incoming = new int[catalog.Count];

            for (int i = 0; i < catalog.Count; i++)
                if (catalog.Get(i).unlockLevel <= 1) storage[i] = startingStoragePerProduct;
        }

        void Start()
        {
            TimeManager.Instance.OnDayStarted += ReceiveOrders;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= ReceiveOrders;
            if (Instance == this) Instance = null;
        }

        public bool IsUnlocked(int index) => catalog.Get(index).unlockLevel <= ShopLevelManager.Instance.Level;

        // ---- 발주: 창고를 채운다 ----

        public bool TryOrder(int index, int quantity)
        {
            if (quantity <= 0 || !IsUnlocked(index)) return false;

            int cost = catalog.Get(index).wholesale * quantity;
            if (!GameManager.Instance.TrySpend(cost)) return false;

            incoming[index] += quantity;
            OnStockChanged?.Invoke();
            return true;
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
            int moved = Mathf.Min(quantity, ShelfRoom(index));
            if (moved <= 0) return;

            shelf[index] += moved;
            OnStockChanged?.Invoke();
        }

        // ---- 소비 ----

        /// <summary>손님 구매. 테이블에서만 나간다 — 창고에 있어도 테이블이 비면 놓친다.</summary>
        public bool TryConsumeShelf(int index)
        {
            if (shelf[index] <= 0) return false;
            shelf[index]--;
            OnStockChanged?.Invoke();
            return true;
        }

        /// <summary>손님이 물건을 두고 나갔다. 테이블이 가득하면 창고로 돌린다.</summary>
        public void ReturnToShelf(int index)
        {
            if (ShelfRoom(index) > 0) shelf[index]++;
            else storage[index]++;
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
            data.shelf = (int[])shelf.Clone();
            data.incoming = (int[])incoming.Clone();
        }

        public void RestoreFrom(SaveData data)
        {
            CopyInto(data.storage, storage);
            CopyInto(data.shelf, shelf);
            CopyInto(data.incoming, incoming);
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
