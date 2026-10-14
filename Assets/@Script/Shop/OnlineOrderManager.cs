using System;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 온라인 주문. 계산대 옆 모니터(<see cref="OnlineOrderMenu"/>)에서 수락하고, 창고 포장대(<see cref="PackingStation"/>)에서
    /// 창고 물건을 상자에 담아 테이프로 봉하면 출고된다. 출고하면 판매가만큼 돈과 명성을 받는다.
    ///
    /// 왜 있나: 2026-10 매장 손님 수를 줄였다(Lv1 9→6명 … Lv10 45→24명). 손님이 빠진 만큼 명성이 덜 오르는데,
    /// 그 몫을 온라인 주문이 채운다 — 손님 한 명이 평균 명성 2(아이 1·청년 2·어른 3)를 주므로
    /// 하루 온라인 주문의 명성 합 ≈ (예전 손님 − 지금 손님) × 2. 가게가 한가할 때 할 일도 된다.
    ///
    /// 주문은 가게가 열려 있는 영업시간(10~19시) 동안 정각마다 확률로 하나씩 들어온다. 비싼 물건일수록 잘 주문된다.
    /// <b>그날 안에</b> 수락하고 출고해야 한다 —
    /// 하루가 끝날 때 수락 안 한 주문, 수락했지만 출고 못 한 주문은 취소되고 명성이 깎인다.
    /// </summary>
    public class OnlineOrderManager : MonoBehaviour, ISaveParticipant
    {
        public static OnlineOrderManager Instance { get; private set; }

        /// <summary>줄이기 전 하루 손님 수(Lv1~10). 지금 손님 수와의 차이가 온라인 주문으로 채울 명성이다.</summary>
        static readonly int[] FormerCustomers = { 9, 12, 15, 18, 23, 27, 32, 36, 41, 45 };
        /// <summary>
        /// 온라인 주문을 만들 때 줄였던 손님 수(Lv1~10). 그 뒤 초반이 심심해 손님을 1.5배(9 … 36명)로 다시 늘렸지만,
        /// 온라인 주문 몫은 그대로 두려고 지금 표가 아니라 이 값으로 계산한다 — 표를 읽으면 차이가 거의 0이 돼 주문 명성이 사라진다.
        /// </summary>
        static readonly int[] OnlineBaseCustomers = { 6, 8, 10, 12, 14, 16, 18, 20, 22, 24 };
        /// <summary>손님 한 명이 주는 평균 명성.</summary>
        const float ReputationPerCustomer = 2f;
        /// <summary>주문 하나에 담는 명성(대략). 이걸로 하루 주문 수를 정한다 — Lv1 1건 … Lv10 5건.</summary>
        const float ReputationPerOrder = 8f;
        const int MaxOrdersPerDay = 6;
        /// <summary>취소되면 그 주문 명성의 이만큼을 깎는다.</summary>
        const float CancelPenaltyRate = 0.5f;
        /// <summary>주문이 들어올 수 있는 정각 — 영업시간(09:30~19:30) 안의 정각, 10시~19시.</summary>
        const int FirstHour = (int)(TimeManager.OpenHour + 0.999f), LastHour = (int)TimeManager.CloseHour;
        /// <summary>한 시간에 주문이 들어올 확률의 상한 — 매시간 꼬박꼬박 오면 확률 같지 않다.</summary>
        const float MaxChancePerHour = 0.75f;
        /// <summary>물건 고르는 가중치 = 판매가^이것. 클수록 비싼(레벨 올라 새로 풀린) 물건에 몰린다.</summary>
        const float PriceBias = 1.5f;
        /// <summary>온라인은 포장·배송 수고가 드는 만큼 가게 판매가보다 더 받는다.</summary>
        const float PriceRate = 1.2f;
        /// <summary>명성도 손님 몫 계산값보다 더 준다.</summary>
        const float ReputationRate = 1.5f;

        public enum State { Waiting, New, Accepted, Shipped, Cancelled }

        [Serializable]
        public class Order
        {
            public int id;
            public int arriveHour;
            public int[] products = new int[0];
            public int[] quantities = new int[0];
            public int reputation;
            public State state;

            public int Price(ProductCatalog catalog)
            {
                int sum = 0;
                for (int i = 0; i < products.Length; i++) sum += catalog.Get(products[i]).retail * quantities[i];
                return Mathf.RoundToInt(sum * PriceRate);
            }
        }

        readonly List<Order> orders = new List<Order>();
        int nextId = 1;

        public IReadOnlyList<Order> Orders => orders;
        public event Action OnChanged;

        /// <summary>오늘 배송한 주문 수와 그 매출, 취소된 주문 수 — 마감 정산서용(목록은 그날 것만 들고 있다).</summary>
        public void TodaySummary(out int shipped, out int revenue, out int cancelled)
        {
            shipped = revenue = cancelled = 0;
            ProductCatalog catalog = InventoryManager.Instance != null ? InventoryManager.Instance.Catalog : null;
            foreach (Order o in orders)
            {
                if (o.state == State.Shipped) { shipped++; if (catalog != null) revenue += o.Price(catalog); }
                else if (o.state == State.Cancelled) cancelled++;
            }
        }

        /// <summary>지금 화면에 보이는 새 주문 수(모니터 힌트용).</summary>
        public int NewCount { get { int n = 0; foreach (Order o in orders) if (o.state == State.New) n++; return n; } }
        /// <summary>수락했고 아직 포장 안 한 주문 — 포장대가 위에서부터 하나씩 꺼낸다.</summary>
        public Order NextToPack { get { foreach (Order o in orders) if (o.state == State.Accepted) return o; return null; } }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnWholeHourChanged += HandleHour;
            TimeManager.Instance.OnDayEnded += HandleDayEnded;
            TimeManager.Instance.OnDayStarted += HandleDayStarted;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnWholeHourChanged -= HandleHour;
                TimeManager.Instance.OnDayEnded -= HandleDayEnded;
                TimeManager.Instance.OnDayStarted -= HandleDayStarted;
            }
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 하루 평균 주문 수와 주문 하나의 명성. 레벨마다 빠진 손님 몫 명성을 채우도록 정한다.
        /// 평균만 맞추고 실제 건수는 시간마다 확률로 굴린다 — 어떤 날은 많고 어떤 날은 적다.
        /// </summary>
        void DailyPlan(out float expected, out int repEach)
        {
            ShopLevelManager levels = ShopLevelManager.Instance;
            int level = Mathf.Clamp(levels.Level, 1, FormerCustomers.Length);
            float missing = Mathf.Max(0, FormerCustomers[level - 1] - OnlineBaseCustomers[level - 1]) * ReputationPerCustomer;
            int count = Mathf.Clamp(Mathf.RoundToInt(missing / ReputationPerOrder), 1, MaxOrdersPerDay);
            expected = count;
            repEach = Mathf.Max(2, Mathf.RoundToInt(missing / count * ReputationRate));
        }

        // ---- 튜토리얼 주문 ----

        /// <summary>튜토리얼(<see cref="TutorialQuestManager"/>)이 보장해 넣은 주문 번호. 0 = 없음.</summary>
        public int TutorialOrderId { get; private set; }

        /// <summary>튜토리얼 주문이 아직 처리 중인가(새 주문이거나 수락만 했거나).</summary>
        public bool TutorialOrderPending
        {
            get
            {
                if (TutorialOrderId <= 0) return false;
                foreach (Order o in orders)
                    if (o.id == TutorialOrderId) return o.state == State.New || o.state == State.Accepted;
                return false;
            }
        }

        /// <summary>
        /// 튜토리얼 주문을 기다리는 동안엔 확률 주문을 굴리지 않는다(같은 주문이 겹쳐 보이지 않게).
        /// 단 튜토리얼 "배송" 퀘스트가 이미 다른 주문으로 끝났으면 남은 튜토리얼 주문이 하루 내내 주문을 막지 않게 푼다.
        /// </summary>
        bool TutorialBlocksRandom
        {
            get
            {
                if (!TutorialOrderPending) return false;
                TutorialQuestManager quests = TutorialQuestManager.Instance;
                return quests != null && quests.WaitsForOnlineOrder;
            }
        }

        /// <summary>
        /// 확률과 상관없이 지금 주문 하나를 넣는다 — 튜토리얼 "온라인 주문 배송하기" 용.
        /// 창고에 가장 많은 해금 물건 1개로 만든다(처음 해 보는 주문이 창고 부족으로 막히지 않게).
        /// 이 주문이 처리 중인 동안은 확률 주문을 굴리지 않는다 — 같은 주문이 겹쳐 보이지 않게.
        /// </summary>
        public bool AddTutorialOrder()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null || ShopLevelManager.Instance == null || TimeManager.Instance == null) return false;
            if (TutorialOrderPending) return false;

            int best = -1;
            for (int i = 0; i < inv.Catalog.Count; i++)
            {
                if (!inv.IsUnlocked(i)) continue;
                if (best < 0 || inv.StorageOf(i) > inv.StorageOf(best)
                    || (inv.StorageOf(i) == inv.StorageOf(best) && inv.Catalog.Get(i).retail < inv.Catalog.Get(best).retail))
                    best = i;
            }
            if (best < 0) return false;

            DailyPlan(out float _, out int repEach);
            var o = new Order
            {
                id = nextId++,
                arriveHour = Mathf.FloorToInt(TimeManager.Instance.CurrentHour),
                products = new[] { best },
                quantities = new[] { 1 },
                reputation = repEach,
                state = State.New
            };
            orders.Add(o);
            TutorialOrderId = o.id;
            Toast("온라인 주문이 들어왔다. 계산대 옆 모니터에서 수락하자");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>영업 중 정각마다 한 번 — 확률로 주문 하나가 들어온다.</summary>
        void HandleHour(int hour)
        {
            if (hour < FirstHour || hour > LastHour) return;
            if (TutorialBlocksRandom) return;
            if (ShopHours.Instance != null && !ShopHours.Instance.IsOpen) return;
            if (InventoryManager.Instance == null || ShopLevelManager.Instance == null) return;
            // 기대 건수만이 아니라 실제 건수도 막는다 — 시간마다 따로 굴리니 운이 좋으면 7건 넘게 와서
            // 주문 창(한 번에 6줄)에 안 보이는 주문이 하루 끝에 취소돼 명성만 깎였다
            if (orders.Count >= MaxOrdersPerDay) return;

            DailyPlan(out float expected, out int repEach);
            float chance = Mathf.Min(MaxChancePerHour, expected / (LastHour - FirstHour + 1));
            if (UnityEngine.Random.value >= chance) return;

            Order o = MakeOrder(hour, repEach);
            if (o == null) return;
            orders.Add(o);
            Toast("온라인 주문이 들어왔다. 계산대 옆 모니터에서 수락하자");
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 한두 가지 물건을 1~3개씩. 비싼 물건일수록 잘 뽑힌다(가중치 판매가^<see cref="PriceBias"/>) —
        /// 레벨이 올라 비싼 물건이 풀리면 주문도 그쪽으로 옮겨 간다.
        /// </summary>
        Order MakeOrder(int hour, int repEach)
        {
            InventoryManager inv = InventoryManager.Instance;
            var unlocked = new List<int>();
            for (int i = 0; i < inv.Catalog.Count; i++) if (inv.IsUnlocked(i)) unlocked.Add(i);
            if (unlocked.Count == 0) return null;

            int kinds = Mathf.Min(unlocked.Count, UnityEngine.Random.value < 0.5f ? 1 : 2);
            var picked = new List<int>();
            while (picked.Count < kinds)
            {
                int p = PickWeighted(unlocked, inv.Catalog);
                picked.Add(p);
                unlocked.Remove(p);
            }
            var o = new Order
            {
                id = nextId++,
                arriveHour = hour,
                products = picked.ToArray(),
                quantities = new int[kinds],
                reputation = repEach,
                state = State.New
            };
            for (int k = 0; k < kinds; k++) o.quantities[k] = UnityEngine.Random.Range(1, 4);
            return o;
        }

        static int PickWeighted(List<int> pool, ProductCatalog catalog)
        {
            float total = 0f;
            foreach (int p in pool) total += Mathf.Pow(Mathf.Max(1, catalog.Get(p).retail), PriceBias);
            float roll = UnityEngine.Random.value * total;
            foreach (int p in pool)
            {
                roll -= Mathf.Pow(Mathf.Max(1, catalog.Get(p).retail), PriceBias);
                if (roll <= 0f) return p;
            }
            return pool[pool.Count - 1];
        }

        void HandleDayEnded()
        {
            int cancelled = 0, lost = 0;
            foreach (Order o in orders)
            {
                if (o.state != State.New && o.state != State.Accepted) continue;
                o.state = State.Cancelled;
                cancelled++;
                // 튜토리얼 주문은 배우는 중이라 명성을 깎지 않는다(다음 날 다시 들어온다)
                if (o.id != TutorialOrderId) lost += Mathf.Max(1, Mathf.RoundToInt(o.reputation * CancelPenaltyRate));
            }
            if (cancelled > 0)
            {
                if (lost > 0) GameManager.Instance.AddReputation(-lost);
                Toast("처리하지 못한 온라인 주문 " + cancelled + "건 취소" + (lost > 0 ? ", 명성 -" + lost : ""));
            }
            OnChanged?.Invoke();
        }

        /// <summary>새날 — 어제 목록(출고·취소 기록)을 비운다. 오늘 주문은 영업 중 시간마다 들어온다.</summary>
        void HandleDayStarted()
        {
            orders.Clear();
            OnChanged?.Invoke();
        }

        public void Accept(Order o)
        {
            if (o == null || o.state != State.New) return;
            o.state = State.Accepted;
            OnChanged?.Invoke();
        }

        /// <summary>그 주문 물건이 창고에 다 있나.</summary>
        public bool HasStock(Order o, out string missing)
        {
            missing = null;
            InventoryManager inv = InventoryManager.Instance;
            for (int i = 0; i < o.products.Length; i++)
            {
                if (inv.StorageOf(o.products[i]) >= o.quantities[i]) continue;
                missing = inv.Catalog.Get(o.products[i]).nameKo + " " + (o.quantities[i] - inv.StorageOf(o.products[i])) + "개";
                return false;
            }
            return true;
        }

        /// <summary>봉한 상자를 내보낸다 — 판매가만큼 돈, 주문 명성.</summary>
        public void Ship(Order o)
        {
            if (o == null || o.state != State.Accepted) return;
            o.state = State.Shipped;
            int paid = o.Price(InventoryManager.Instance.Catalog);
            if (CustomerManager.Instance != null) CustomerManager.Instance.RecordOnlineSale(paid);
            else GameManager.Instance.RegisterSale(paid);
            GameManager.Instance.AddReputation(o.reputation);
            Toast("온라인 주문 출고! +" + paid + "원, 명성 +" + o.reputation);
            OnChanged?.Invoke();
            GameEvents.RaiseOnlineOrderShipped(o.id);
        }

        public static string Describe(Order o)
        {
            ProductCatalog catalog = InventoryManager.Instance.Catalog;
            var parts = new List<string>();
            for (int i = 0; i < o.products.Length; i++) parts.Add(catalog.Get(o.products[i]).nameKo + " x" + o.quantities[i]);
            return string.Join(", ", parts);
        }

        static void Toast(string message)
        {
            var hud = FindAnyObjectByType<UI.GameHud>();
            if (hud != null) hud.Toast(message, new Color(1f, 0.9f, 0.55f));
        }

        // ---- 저장 ----
        // 주문마다 시각·명성·상태, 물건은 [종류 수, (상품, 개수)…] 를 한 줄로 이어 붙인다

        public void CaptureInto(SaveData data)
        {
            var flat = new List<int>();
            foreach (Order o in orders)
            {
                flat.Add(o.id); flat.Add(o.arriveHour); flat.Add(o.reputation); flat.Add((int)o.state);
                flat.Add(o.products.Length);
                for (int i = 0; i < o.products.Length; i++) { flat.Add(o.products[i]); flat.Add(o.quantities[i]); }
            }
            data.onlineOrders = flat.ToArray();
            data.onlineOrderNextId = nextId;
            data.onlineTutorialOrderId = TutorialOrderId;
        }

        public void RestoreFrom(SaveData data)
        {
            orders.Clear();
            nextId = Mathf.Max(1, data.onlineOrderNextId);
            TutorialOrderId = data.onlineTutorialOrderId;
            int[] f = data.onlineOrders ?? new int[0];
            int count = InventoryManager.Instance != null ? InventoryManager.Instance.Catalog.Count : 0;
            int at = 0;
            while (at + 5 <= f.Length)
            {
                var o = new Order { id = f[at], arriveHour = f[at + 1], reputation = f[at + 2], state = (State)f[at + 3] };
                int kinds = f[at + 4];
                at += 5;
                if (kinds < 0 || at + kinds * 2 > f.Length) break;
                o.products = new int[kinds]; o.quantities = new int[kinds];
                bool ok = true;
                for (int i = 0; i < kinds; i++) { o.products[i] = f[at++]; o.quantities[i] = f[at++]; if (o.products[i] < 0 || o.products[i] >= count) ok = false; }
                if (ok) orders.Add(o);
            }
            // 미리 정해 두던 시절의 저장에 남은 '아직 안 온' 주문은 버린다 — 이제 주문은 시간마다 확률로 온다
            orders.RemoveAll(o => o.state == State.Waiting);
            OnChanged?.Invoke();
        }
    }
}
