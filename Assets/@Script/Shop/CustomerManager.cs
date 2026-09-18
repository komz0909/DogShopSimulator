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
    public class CustomerManager : MonoBehaviour, ISaveParticipant
    {
        /// <summary>이 시간까지 계산해 주면 정가를 다 받는다. 넘어가면 값을 깎기 시작한다.</summary>
        public const float Patience = 22f;

        /// <summary>초과 1초당 깎이는 비율.</summary>
        const float DiscountPerSecond = 0.02f;

        /// <summary>아무리 기다려도 이 아래로는 안 내려간다 — 방치가 무한 손해가 되면 복구가 불가능해진다.</summary>
        const float MinPriceFactor = 0.60f;

        /// <summary>이 초 단위마다 명성 1을 잃는다. 값 할인과 달리 상한이 낮게 잡혀 있다.</summary>
        const float SecondsPerReputationLoss = 10f;
        const int MaxReputationPenalty = 5;

        /// <summary>이동이 이 시간을 넘으면 길이 막힌 것으로 보고 내보낸다.</summary>
        public const float TravelTimeout = 45f;

        /// <summary>문 앞 스폰·퇴장 지점. NavMesh 가장자리가 z 0.58 이라 조금 안쪽에 둔다.</summary>
        static readonly Vector3 Door = new Vector3(4f, 0f, 0.7f);

        /// <summary>
        /// 계산대를 사이에 두고 <b>직원 반대편</b>에 서는 대기 줄.
        /// 계산대는 x 6.205~6.995 를 차지하고 직원 자리는 그 오른쪽(x 7.0~8.0) 주머니다.
        /// 손님은 왼쪽 면을 마주 보고 문 쪽으로 늘어선다.
        ///
        /// NavMesh가 반경 0.5로 구워져 장애물에서 0.5m가 깎인다 — 계산대 면(6.205)에서
        /// 0.71m 떨어진 5.50이 손님이 설 수 있는 가장 앞자리다(몸 앞면과 계산대 사이 0.43m).
        /// </summary>
        static readonly Vector3[] QueueSlots =
        {
            new Vector3(5.50f, 0f, 1.00f),
            new Vector3(4.80f, 0f, 1.00f),
            new Vector3(4.10f, 0f, 1.00f),
            new Vector3(3.40f, 0f, 1.00f)
        };

        /// <summary>줄에 선 손님이 바라볼 방향 — 계산대는 손님 줄의 오른쪽(+x)에 있다.</summary>
        static readonly Vector3 CounterFacing = Vector3.right;

        public static CustomerManager Instance { get; private set; }

        [SerializeField] GameObject customerPrefab;

        /// <summary>
        /// 손님 겉모습 후보. 이동 로직은 customerPrefab 하나가 들고 있고 몸만 여기서 고른다.
        /// 어른 5종 + 아이 2종 — 키 차이가 보여야 매장이 사람 사는 곳처럼 보인다.
        /// </summary>
        [SerializeField] GameObject[] appearances = new GameObject[0];

        readonly List<Customer> active = new List<Customer>();
        readonly List<Customer> queue = new List<Customer>();
        readonly List<Customer> finished = new List<Customer>();
        readonly List<int> bag = new List<int>();

        public int SoldToday { get; private set; }
        public int LostToday { get; private set; }
        public int RevenueToday { get; private set; }
        public int AverageBasket => SoldToday > 0 ? RevenueToday / SoldToday : 0;
        public int InStore => active.Count;
        public int QueueLength => queue.Count;

        public event Action<int> OnSale;
        public event Action<int> OnLostSale;

        float pending;

        /// <summary>
        /// NavMesh 표면은 바닥보다 조금 높게 구워진다(굽기 설정에 따라 1~10cm). 상쇄하지 않으면
        /// 손님 발이 그만큼 공중에 뜬다. 열릴 때 한 번 재서 모든 손님에게 물려준다 —
        /// NavMesh를 다시 구워도 값이 알아서 따라온다.
        /// </summary>
        float groundOffset;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            MeasureGroundOffset();
            TimeManager.Instance.OnWholeHourChanged += HandleHour;
            TimeManager.Instance.OnDayStarted += ResetDaily;
        }

        void MeasureGroundOffset()
        {
            groundOffset = 0f;

            UnityEngine.AI.NavMeshHit nav;
            if (!UnityEngine.AI.NavMesh.SamplePosition(Door, out nav, 2f, Customer.WalkableAreas)) return;

            // 가장 낮은 히트가 바닥이다 — 사람이나 상자를 바닥으로 오인하지 않는다
            RaycastHit[] hits = Physics.RaycastAll(nav.position + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            float floorY = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
                if (hits[i].point.y < floorY) floorY = hits[i].point.y;

            if (floorY < float.MaxValue) groundOffset = floorY - nav.position.y;
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
            // 문을 열지 않았으면 아무도 오지 않는다. 늦게 열면 그 시간 몫을 그냥 잃는다 —
            // 따로 벌점을 두지 않아도 늦잠이 손해가 된다.
            float hoursFactor = ShopHours.Instance != null ? ShopHours.Instance.ArrivalFactor : 1f;
            if (hoursFactor <= 0f) return;

            float dirtFactor = CleanlinessManager.Instance != null ? CleanlinessManager.Instance.CustomerFactor : 1f;
            pending += ShopLevelManager.Instance.Current.customersPerDay / TimeManager.HoursPerDay * dirtFactor * hoursFactor;

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
            customer.SetAppearance(NextAppearance());

            UnityEngine.AI.NavMeshAgent agent = instance.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                agent.baseOffset = groundOffset;

                // 회피 우선순위가 전원 같으면 서로 양보하지 않는다. 좁은 통로에서
                // 마주친 둘이 그대로 굳어 TravelTimeout 에 걸렸다(7차 측정: 12명 중 11명 손실).
                // 흩어 놓으면 낮은 쪽이 비켜주며 풀린다.
                agent.avoidancePriority = UnityEngine.Random.Range(30, 71);
            }
            customer.WantedProduct = wanted;
            customer.State = CustomerState.ToShelf;
            customer.MoveTo(shelf.ApproachPoint);

            active.Add(customer);
        }

        /// <summary>
        /// 뽑기 주머니 — 7종이 한 번씩 다 나온 뒤에야 다시 채운다.
        /// 그냥 난수로 고르면 같은 얼굴 셋이 동시에 줄 서 있는 장면이 자주 나온다.
        /// </summary>
        GameObject NextAppearance()
        {
            if (appearances.Length == 0) return null;

            if (bag.Count == 0)
            {
                for (int i = 0; i < appearances.Length; i++) bag.Add(i);
                for (int i = bag.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    int swap = bag[i]; bag[i] = bag[j]; bag[j] = swap;
                }
            }

            int last = bag.Count - 1;
            GameObject picked = appearances[bag[last]];
            bag.RemoveAt(last);
            return picked;
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
            c.FaceDirection(CounterFacing);
        }

        /// <summary>
        /// 손님은 <b>떠나지 않는다</b> — 플레이어가 E를 누를 때까지 줄에서 기다린다.
        /// 대신 기다린 만큼 받는 돈이 줄고 명성이 깎인다. 방치가 손실로 직결되어야
        /// 계산대를 지키는 일이 실제 일거리가 된다.
        /// </summary>
        void TickWaiting(Customer c, float step)
        {
            c.WaitRemaining -= step;
        }

        /// <summary>대기 초과로 깎인 실수령가. 정가는 카탈로그가, 깎는 규칙은 여기가 갖는다.</summary>
        public int PayoutOf(Customer c)
        {
            int retail = InventoryManager.Instance.Catalog.Get(c.WantedProduct).retail;
            if (c.Overtime <= 0f) return retail;

            float factor = Mathf.Max(MinPriceFactor, 1f - c.Overtime * DiscountPerSecond);
            return Mathf.Max(1, Mathf.RoundToInt(retail * factor));
        }

        /// <summary>대기 초과로 잃는 명성. 할인과 달리 오래 끌수록 계속 쌓인다.</summary>
        public int ReputationPenaltyOf(Customer c)
        {
            if (c.Overtime <= 0f) return 0;
            return Mathf.Min(MaxReputationPenalty, Mathf.FloorToInt(c.Overtime / SecondsPerReputationLoss));
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
                // 줄이 슬롯보다 길어지면 문 쪽(-x)으로 계속 이어 붙인다
                if (i >= QueueSlots.Length) slot += new Vector3(-0.7f * (i - QueueSlots.Length + 1), 0f, 0f);
                queue[i].MoveTo(slot);
            }
        }

        /// <summary>계산 처리. CheckoutPicker가 손님을 클릭하면 호출된다.</summary>
        public bool Checkout(Customer c)
        {
            if (c == null || c.State != CustomerState.Waiting || !c.HasItem) return false;

            int paid = PayoutOf(c);
            RevenueToday += paid;
            SoldToday++;
            GameManager.Instance.RegisterSale(paid);
            OnSale?.Invoke(paid);

            int penalty = ReputationPenaltyOf(c);
            if (penalty > 0) GameManager.Instance.AddReputation(-penalty);

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

        // ---- 세이브 ----

        /// <summary>
        /// 그날 집계만 넣고 뺀다. 장내 손님은 저장하지 않는다 — 불러온 자리에서 다시 도착한다.
        /// 매출은 GameManager 가 이미 같은 수를 들고 있어 그 칸을 같이 쓴다.
        /// </summary>
        public void CaptureInto(SaveData data)
        {
            data.soldToday = SoldToday;
            data.lostToday = LostToday;
        }

        public void RestoreFrom(SaveData data)
        {
            SoldToday = data.soldToday;
            LostToday = data.lostToday;
            RevenueToday = data.dailyRevenue;
        }
    }
}
