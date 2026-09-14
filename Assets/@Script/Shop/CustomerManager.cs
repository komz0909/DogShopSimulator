using System;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 손님 도착·구매·계산. 손님은 실제로 걸어다니며 진열대에서 물건을 집고 계산대에 줄을 선다.
    /// 원하는 상품이 진열대에 없으면 그냥 나가고, 계산을 오래 기다리면 물건을 두고 떠난다 — 둘 다 매출 손실.
    /// 손님은 이 매니저의 자식으로 런타임 생성된다.
    /// </summary>
    public class CustomerManager : MonoBehaviour
    {
        public const float Patience = 22f;

        /// <summary>이동이 이 시간을 넘으면 길이 막힌 것으로 보고 내보낸다.</summary>
        public const float TravelTimeout = 45f;

        static readonly Vector3 Door = new Vector3(4f, 0f, 0.5f);

        /// <summary>
        /// 계산대 왼쪽으로 늘어서는 대기 줄. 계산대(x 5.7~7.3)와 진열대(z 1.7~2.3) 사이의
        /// 빈 앞쪽 통로에 둔다 — 손님 반경 0.28을 확보해야 NavMesh 위에 올라간다.
        /// </summary>
        static readonly Vector3[] QueueSlots =
        {
            new Vector3(5.2f, 0f, 1.0f),
            new Vector3(4.5f, 0f, 1.0f),
            new Vector3(3.8f, 0f, 1.0f),
            new Vector3(3.1f, 0f, 1.0f)
        };

        public static CustomerManager Instance { get; private set; }

        [SerializeField] GameObject customerPrefab;

        readonly List<Customer> active = new List<Customer>();
        readonly List<Customer> queue = new List<Customer>();
        readonly List<Customer> finished = new List<Customer>();

        public int SoldToday { get; private set; }
        public int LostToday { get; private set; }
        public int RevenueToday { get; private set; }
        public int AverageBasket => SoldToday > 0 ? RevenueToday / SoldToday : 0;
        public int InStore => active.Count;
        public int QueueLength => queue.Count;

        public event Action<int> OnSale;
        public event Action<int> OnLostSale;

        float pending;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnWholeHourChanged += HandleHour;
            TimeManager.Instance.OnDayStarted += ResetDaily;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnWholeHourChanged -= HandleHour;
                TimeManager.Instance.OnDayStarted -= ResetDaily;
            }
            if (Instance == this) Instance = null;
        }

        void ResetDaily()
        {
            SoldToday = 0;
            LostToday = 0;
            RevenueToday = 0;
            pending = 0f;
        }

        void HandleHour(int hour)
        {
            float dirtFactor = CleanlinessManager.Instance != null ? CleanlinessManager.Instance.CustomerFactor : 1f;
            pending += ShopLevelManager.Instance.Current.customersPerDay / TimeManager.HoursPerDay * dirtFactor;

            int arrivals = Mathf.FloorToInt(pending);
            pending -= arrivals;

            for (int i = 0; i < arrivals; i++) Spawn();
        }

        void Spawn()
        {
            if (customerPrefab == null) return;

            int wanted = PickWantedProduct();
            if (wanted < 0) return;

            ShelfTable shelf = ShelfManager.Instance.FindFor(wanted);
            if (shelf == null) return;

            GameObject instance = Instantiate(customerPrefab, transform);
            instance.transform.position = Door;

            Customer customer = instance.GetComponent<Customer>();
            customer.WantedProduct = wanted;
            customer.State = CustomerState.ToShelf;
            customer.MoveTo(shelf.ApproachPoint);

            active.Add(customer);
        }

        /// <summary>수요 가중치 x 진열대 입지. 입구에 가까운 테이블의 상품이 더 자주 선택된다.</summary>
        int PickWantedProduct()
        {
            ProductCatalog catalog = InventoryManager.Instance.Catalog;
            ShelfManager shelves = ShelfManager.Instance;
            int level = ShopLevelManager.Instance.Level;

            float total = 0f;
            for (int i = 0; i < catalog.Count; i++)
            {
                ProductDef p = catalog.Get(i);
                if (p.unlockLevel <= level) total += p.demandWeight * shelves.ProximityMultiplier(i);
            }
            if (total <= 0f) return -1;

            float roll = UnityEngine.Random.Range(0f, total);
            for (int i = 0; i < catalog.Count; i++)
            {
                ProductDef p = catalog.Get(i);
                if (p.unlockLevel > level) continue;

                roll -= p.demandWeight * shelves.ProximityMultiplier(i);
                if (roll < 0f) return i;
            }
            return -1;
        }

        void Update()
        {
            int speed = TimeManager.Instance.SpeedMultiplier;
            float step = Time.deltaTime * speed;

            finished.Clear();

            for (int i = 0; i < active.Count; i++)
            {
                Customer c = active[i];
                c.ApplySpeed(speed);

                if (c.State != CustomerState.Waiting)
                {
                    c.TravelTime += step;
                    if (c.TravelTime > TravelTimeout)
                    {
                        Abandon(c);
                        continue;
                    }
                }

                if (c.State == CustomerState.ToShelf) TickToShelf(c);
                else if (c.State == CustomerState.ToCounter) TickToCounter(c);
                else if (c.State == CustomerState.Waiting) TickWaiting(c, step);
                else if (c.State == CustomerState.ToExit && c.Arrived) finished.Add(c);
            }

            for (int i = 0; i < finished.Count; i++) Despawn(finished[i]);
        }

        void TickToShelf(Customer c)
        {
            if (!c.Arrived) return;

            InventoryManager inv = InventoryManager.Instance;
            if (inv.TryConsumeShelf(c.WantedProduct))
            {
                ProductDef p = inv.Catalog.Get(c.WantedProduct);
                c.HasItem = true;
                c.Label = p.nameKo + "  " + p.retail + "원";
                c.State = CustomerState.ToCounter;
                EnterQueue(c);
            }
            else
            {
                LostToday++;
                OnLostSale?.Invoke(c.WantedProduct);
                SendHome(c);
            }
        }

        void TickToCounter(Customer c)
        {
            if (!c.Arrived) return;

            c.State = CustomerState.Waiting;
            c.WaitRemaining = Patience;
        }

        void TickWaiting(Customer c, float step)
        {
            c.WaitRemaining -= step;
            if (c.WaitRemaining > 0f) return;

            // 기다리다 지쳐 물건을 두고 나간다
            InventoryManager.Instance.ReturnToShelf(c.WantedProduct);
            c.HasItem = false;
            LostToday++;
            OnLostSale?.Invoke(c.WantedProduct);
            LeaveQueue(c);
            SendHome(c);
        }

        /// <summary>길이 막혔거나 시간 초과. 들고 있던 물건은 진열대로 돌린다.</summary>
        void Abandon(Customer c)
        {
            if (c.State == CustomerState.ToExit)
            {
                finished.Add(c);
                return;
            }

            if (c.HasItem)
            {
                InventoryManager.Instance.ReturnToShelf(c.WantedProduct);
                c.HasItem = false;
            }

            LostToday++;
            OnLostSale?.Invoke(c.WantedProduct);
            LeaveQueue(c);
            SendHome(c);
        }

        void EnterQueue(Customer c)
        {
            queue.Add(c);
            RelayoutQueue();
        }

        void LeaveQueue(Customer c)
        {
            if (!queue.Remove(c)) return;
            RelayoutQueue();
        }

        void RelayoutQueue()
        {
            for (int i = 0; i < queue.Count; i++)
            {
                Vector3 slot = QueueSlots[Mathf.Min(i, QueueSlots.Length - 1)];
                if (i >= QueueSlots.Length) slot += new Vector3(0f, 0f, -0.7f * (i - QueueSlots.Length + 1));
                queue[i].MoveTo(slot);
            }
        }

        /// <summary>계산 처리. CheckoutPicker가 손님을 클릭하면 호출된다.</summary>
        public bool Checkout(Customer c)
        {
            if (c == null || c.State != CustomerState.Waiting || !c.HasItem) return false;

            int retail = InventoryManager.Instance.Catalog.Get(c.WantedProduct).retail;
            RevenueToday += retail;
            SoldToday++;
            GameManager.Instance.RegisterSale(retail);
            OnSale?.Invoke(retail);

            c.HasItem = false;
            LeaveQueue(c);
            SendHome(c);
            return true;
        }

        public Customer QueuedAt(int index) => index >= 0 && index < queue.Count ? queue[index] : null;

        void SendHome(Customer c)
        {
            c.Label = "";
            c.State = CustomerState.ToExit;
            c.MoveTo(Door);
        }

        void Despawn(Customer c)
        {
            active.Remove(c);
            queue.Remove(c);
            Destroy(c.gameObject);
        }
    }
}
