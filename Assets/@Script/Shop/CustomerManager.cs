using System;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Data;
using UnityEngine;
using UnityEngine.AI;

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

        /// <summary>
        /// 스폰·퇴장 지점. <b>문 바깥</b>이다 — 손님이 문틈(x 3~5)을 지나 들어오고 나간다.
        ///
        /// 예전에는 z 0.7, 즉 매장 <b>안쪽</b>이었다. 그러면 손님이 걸어 들어오는 게 아니라
        /// 가게 안에 불쑥 나타나서, 앞벽을 뚫고 들어온 것처럼 보였다.
        /// 앞마당은 배달 야적장이라 직원 전용인데, 문 앞 한 줄(z -0.85~0)만 열어 두었다.
        /// </summary>
        static readonly Vector3 Door = new Vector3(4f, 0f, -0.55f);

        /// <summary>
        /// 계산대를 사이에 두고 <b>직원 반대편</b>에 서는 대기 줄.
        /// 계산대는 x 6.205~6.995 를 차지하고 직원 자리는 그 오른쪽(x 7.0~8.0) 주머니다.
        /// 손님은 왼쪽 면을 마주 보고 문 쪽으로 늘어선다.
        ///
        /// NavMesh가 반경 0.5로 구워져 장애물에서 0.5m가 깎인다 — 계산대 면(6.205)에서
        /// 0.71m 떨어진 5.50이 손님이 설 수 있는 가장 앞자리다(몸 앞면과 계산대 사이 0.43m).
        /// </summary>
        /// <summary>
        /// 계산대가 없을 때 쓸 줄 자리. 계산대는 씬에 항상 있지만, 없더라도
        /// 손님이 원점으로 몰려가 뭉치는 꼴은 보이지 않게 한다.
        /// </summary>
        static readonly Vector3 FallbackQueue = new Vector3(5.50f, 0f, 1.00f);

        public static CustomerManager Instance { get; private set; }

        [SerializeField] GameObject customerPrefab;

        /// <summary>
        /// 손님 겉모습 후보. 이동 로직은 customerPrefab 하나가 들고 있고 몸만 여기서 고른다.
        /// 어른 5종 + 아이 2종 — 키 차이가 보여야 매장이 사람 사는 곳처럼 보인다.
        /// </summary>
        [SerializeField] GameObject[] appearances = new GameObject[0];

        /// <summary>거리 행인(<see cref="StreetLife"/>)도 손님과 같은 겉모습을 쓴다.</summary>
        public int AppearanceCount => appearances.Length;
        public GameObject AppearanceAt(int index) => appearances[Mathf.Clamp(index, 0, appearances.Length - 1)];

        /// <summary><see cref="appearances"/> 와 같은 순서의 연령대. 비어 있는 칸은 어른으로 본다.</summary>
        [SerializeField] CustomerAge[] appearanceAges = new CustomerAge[0];

        readonly List<Customer> active = new List<Customer>();
        readonly List<Customer> queue = new List<Customer>();
        readonly List<Customer> finished = new List<Customer>();
        readonly List<int> bag = new List<int>();

        public int SoldToday { get; private set; }
        public int LostToday { get; private set; }

        /// <summary>오늘 문으로 들어온 손님 수.</summary>
        public int VisitorsToday { get; private set; }

        /// <summary>
        /// 오늘 손님이 쌓아 준 명성. <b>들어온 손님마다 연령대만큼</b> 오른다 —
        /// 명성은 경험치처럼 방문객이 쌓는다(<see cref="CustomerAge"/>).
        ///
        /// 예전에는 마감 때 매출 100원당 +1에 저녁 카드(기부 +8·봉사 +4)를 더했다. 카드는 매일
        /// 같은 걸 고르는 의례였고, 명성을 사는 버튼이 따로 있어 손님을 받는 일과 따로 놀았다.
        /// 산 사람·못 산 사람 가리지 않고 센다 — 찾아왔다는 것 자체가 가게의 평판이다.
        /// 계산대에서 오래 기다린 손님은 따로 명성을 깎는다(<see cref="ReputationPenaltyOf"/>).
        /// </summary>
        public int ReputationToday { get; private set; }

        /// <summary>장식 보너스로 생긴 소수점 명성. 1이 차면 그 손님 몫에 얹는다.</summary>
        float reputationCarry;
        public int RevenueToday { get; private set; }
        public int AverageBasket => SoldToday > 0 ? RevenueToday / SoldToday : 0;
        public int InStore => active.Count;
        public int QueueLength => queue.Count;

        public event Action<int> OnSale;
        public event Action<int> OnLostSale;

        float pending;

        /// <summary>
        /// 도착 수를 정수로 끊을 때 얹어 주는 여유.
        ///
        /// 하루치가 <b>정확히 정수에 떨어지게</b> 설계돼 있다(손님/일 ÷ 9시간 × 9시간).
        /// 그래서 마감 순간에 한 프레임치 조각만 잃어도 9.998 이 되어 손님 하나가 통째로 날아간다 —
        /// 표의 6/8/10/12/15 에 실제로 5/7/9/11/14 명만 오던 원인이 이것이다.
        /// 0.02는 손님 한 명의 2%라 없던 손님을 만들어 내지는 않는다.
        /// </summary>
        const float ArrivalEpsilon = 0.02f;

        /// <summary>지난 프레임의 시각. 흐른 만큼만 손님을 부르려고 들고 있는다.</summary>
        float lastClockHour;

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
            TimeManager.Instance.OnDayStarted += ResetDaily;
            lastClockHour = TimeManager.Instance.CurrentHour;
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
                TimeManager.Instance.OnDayStarted -= ResetDaily;
            }
            if (Instance == this) Instance = null;
        }

        void ResetDaily()
        {
            SoldToday = 0;
            LostToday = 0;
            VisitorsToday = 0;
            ReputationToday = 0;
            RevenueToday = 0;
            pending = 0f;
            if (TimeManager.Instance != null) lastClockHour = TimeManager.Instance.CurrentHour;
        }

        /// <summary>
        /// 손님은 <b>흐른 시간에 비례해</b> 도착한다. 정시마다 한 덩어리씩 오던 방식에는
        /// 경계 문제가 있었다 — 09시에 열고 18시에 닫으면 18시 정각 틱이 "이미 닫힘"으로
        /// 걸러져 아홉 시간 중 여덟 시간치만 들어왔다(표 6/8/10/12/15 에 실제 5/7/9/11/14).
        ///
        /// 연속 누적이면 경계가 사라진다. 09시 30분에 열면 딱 30분치만 잃고,
        /// 18~20시의 줄어드는 구간도 매끄럽게 적분된다.
        /// </summary>
        void AccrueArrivals()
        {
            TimeManager time = TimeManager.Instance;
            if (time == null) return;

            float now = time.CurrentHour;
            float elapsed = now - lastClockHour;
            lastClockHour = now;

            // 새 하루로 넘어가면 시계가 뒤로 간다. 그 프레임은 건너뛴다
            if (elapsed <= 0f || time.IsDayOver) return;

            // 문을 열지 않았으면 아무도 오지 않는다. 늦게 열면 그 시간 몫을 그냥 잃는다 —
            // 따로 벌점을 두지 않아도 늦잠이 손해가 된다.
            float hoursFactor = ShopHours.Instance != null ? ShopHours.Instance.ArrivalFactor : 1f;
            if (hoursFactor <= 0f) return;

            float dirtFactor = CleanlinessManager.Instance != null ? CleanlinessManager.Instance.CustomerFactor : 1f;
            pending += ShopLevelManager.Instance.Current.customersPerDay / TimeManager.HoursPerDay
                     * dirtFactor * hoursFactor * elapsed;

            int arrivals = Mathf.FloorToInt(pending + ArrivalEpsilon);
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
            int appearance = NextAppearance();
            customer.SetAppearance(appearance >= 0 ? appearances[appearance] : null);

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

            if (WalksFromStreet)
            {
                // 인도 어딘가에서 걸어와 앞마당 가운데(배송 자리 사이) 통로로 들어선다.
                // 문 앞에 닿는 순간 NavMeshAgent 로 넘겨받아 평소처럼 진열대로 간다
                float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                Vector3 start = new Vector3(LaneMouth.x + side * UnityEngine.Random.Range(7f, 11f), 0f, StreetLaneZ);
                instance.transform.position = start;
                instance.transform.rotation = Quaternion.LookRotation(LaneMouth - start);
                if (agent != null) agent.enabled = false;

                StreetWalker walker = instance.AddComponent<StreetWalker>();
                walker.Walk(new[] { LaneMouth, Door }, customer.BaseSpeed, () => Arrive(customer, shelf), false);
            }
            else
            {
                customer.MoveTo(shelf.ApproachPoint);
                active.Add(customer);
            }

            // 들어온 순간 명성이 붙는다. 연령대가 곧 명성값이다(아이 1 · 청년 2 · 어른 3).
            // 그 연령대가 좋아하는 장식이 매장에 있으면 더 붙는다. 소수점은 다음 손님에게 이월한다 —
            // 아이 1 x 1.5 를 매번 반올림하면 +50% 가 +100% 로 둔갑한다
            CustomerAge age = AgeOf(appearance);
            reputationCarry += (int)age * (1f + DecorBonus.For(age));
            int reputation = Mathf.FloorToInt(reputationCarry + 0.0001f);
            reputationCarry -= reputation;
            VisitorsToday++;
            ReputationToday += reputation;
            GameManager.Instance.AddReputation(reputation);
        }

        /// <summary>청년 손님이 오기 시작하는 날. 그 전에는 아이만 온다.</summary>
        public const int YoungFromDay = 6;

        /// <summary>어른(중년) 손님이 오기 시작하는 날.</summary>
        public const int AdultFromDay = 13;

        /// <summary>
        /// 오늘 올 수 있는 연령대인가. 가게가 알려질수록 찾는 손님 층이 넓어진다 —
        /// 처음엔 동네 아이들이 구경 오고, 날이 갈수록 청년과 어른이 찾아온다.
        /// 연령대가 곧 명성이라(아이 +1 · 청년 +2 · 어른 +3) 명성은 후반에 가팔라진다.
        /// </summary>
        static bool AgeAllowed(CustomerAge age)
        {
            int day = GameManager.Instance != null ? GameManager.Instance.Day : 1;
            if (age == CustomerAge.Adult) return day >= AdultFromDay;
            if (age == CustomerAge.Young) return day >= YoungFromDay;
            return true;
        }

        CustomerAge AgeOf(int appearance) =>
            appearance >= 0 && appearance < appearanceAges.Length ? appearanceAges[appearance] : CustomerAge.Adult;

        /// <summary>
        /// 뽑기 주머니 — 오늘 올 수 있는 겉모습이 한 번씩 다 나온 뒤에야 다시 채운다.
        /// 그냥 난수로 고르면 같은 얼굴 셋이 동시에 줄 서 있는 장면이 자주 나온다.
        /// 주머니를 채울 때만 연령대를 거르므로, 새 연령대는 다음에 채울 때부터 섞인다.
        /// </summary>
        int NextAppearance()
        {
            if (appearances.Length == 0) return -1;

            if (bag.Count == 0)
            {
                for (int i = 0; i < appearances.Length; i++)
                    if (AgeAllowed(AgeOf(i))) bag.Add(i);

                // 연령대 표가 비어 아무도 못 오게 되면 전원을 넣는다 — 손님이 끊기는 것보다 낫다
                if (bag.Count == 0)
                    for (int i = 0; i < appearances.Length; i++) bag.Add(i);

                for (int i = bag.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    int swap = bag[i]; bag[i] = bag[j]; bag[j] = swap;
                }
            }

            int last = bag.Count - 1;
            int picked = bag[last];
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
            AccrueArrivals();

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
            ItemGrade picked;
            if (inv.TryConsumeShelf(c.WantedProduct, out picked))
            {
                ProductDef p = inv.Catalog.Get(c.WantedProduct);
                c.HasItem = true;
                c.PickedGrade = picked;

                int price = ItemGrades.PriceOf(p.retail, picked);
                c.Label = (picked == ItemGrade.None ? "" : "[" + ItemGrades.NameOf(picked) + "] ")
                        + p.nameKo + "  " + price + "원";

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
            c.FaceDirection(ShopCounter.Instance != null ? ShopCounter.Instance.CustomerFacing : Vector3.right);
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
            // 등급이 붙은 물건은 그 배수로 판다. 정가는 카탈로그가, 등급 배수는 ItemGrades 가 갖는다
            int retail = ItemGrades.PriceOf(
                InventoryManager.Instance.Catalog.Get(c.WantedProduct).retail, c.PickedGrade);

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

        /// <summary>계산대를 옮긴 뒤 줄을 다시 세운다.</summary>
        public void RefreshQueue() => RelayoutQueue();

        /// <summary>
        /// 줄을 계산대 앞에 다시 세운다. 자리는 <see cref="ShopCounter.QueueSlot"/>이 정한다 —
        /// 계산대를 옮기면 줄도 따라간다.
        /// </summary>
        void RelayoutQueue()
        {
            ShopCounter counter = ShopCounter.Instance;

            for (int i = 0; i < queue.Count; i++)
            {
                Vector3 slot = counter != null
                    ? counter.QueueSlot(i)
                    : FallbackQueue + Vector3.left * (0.7f * i);

                // 옮긴 계산대 앞이 벽이나 가구에 막혀 있을 수 있다. NavMesh 위로 끌어다 놓는다
                NavMeshHit hit;
                if (NavMesh.SamplePosition(slot, out hit, 2f, Customer.WalkableAreas)) slot = hit.position;

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
            if (c == null) return;
            if (WalksFromStreet && LeaveToStreet(c)) return;
            Destroy(c.gameObject);
        }

        // ---- 거리 ----

        /// <summary>손님이 오가는 인도 줄(z). 지나가는 행인 줄(<see cref="StreetLife"/>)보다 가게 쪽이다.</summary>
        public const float StreetLaneZ = -5.4f;

        /// <summary>앞마당 가운데 통로 입구. 양옆이 배송 자리라 손님은 가운데로만 드나든다.</summary>
        static readonly Vector3 LaneMouth = new Vector3(4f, 0f, StreetLaneZ);

        /// <summary>
        /// 무인 측정 중에는 예전처럼 문 앞에서 바로 생긴다. 16배속에서 인도 10m 를 걸어오는 데
        /// 게임 시간 한 시간 가까이 들어, 손님 흐름이 그만큼 밀려 측정값이 달라진다.
        /// </summary>
        static bool WalksFromStreet =>
            Debugging.MeasurementMode.Instance == null || !Debugging.MeasurementMode.Instance.Active;

        /// <summary>인도에서 걸어온 손님이 문 앞에 닿았다 — 여기서부터 평소 손님이다.</summary>
        void Arrive(Customer customer, ShelfTable shelf)
        {
            if (customer == null) return;

            NavMeshAgent agent = customer.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(Door);
            }

            // 걸어오는 사이 가게를 닫았거나 진열대가 사라졌으면 그냥 돌아간다
            bool open = ShopHours.Instance == null || ShopHours.Instance.IsOpen;
            if (!open || shelf == null)
            {
                customer.State = CustomerState.ToExit;
                if (!LeaveToStreet(customer)) Destroy(customer.gameObject);
                return;
            }

            customer.ApplySpeed(TimeManager.Instance.SpeedMultiplier);
            customer.MoveTo(shelf.ApproachPoint);
            active.Add(customer);
        }

        /// <summary>
        /// 문 앞에서 인도로 걸어 나가 거리 끝에서 사라진다. 문에서 먼 곳(가게 안 깊숙이)에서
        /// 끝난 손님은 거리까지 데려갈 길이 없으니 그냥 지운다.
        /// </summary>
        bool LeaveToStreet(Customer c)
        {
            Vector3 p = c.transform.position;
            if (new Vector2(p.x - Door.x, p.z - Door.z).magnitude > 1.5f) return false;

            NavMeshAgent agent = c.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            c.Label = "";

            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            float laneZ = StreetLaneZ - 0.6f;   // 들어오는 사람과 부딪히지 않게 한 줄 바깥
            StreetWalker walker = c.GetComponent<StreetWalker>();
            if (walker == null) walker = c.gameObject.AddComponent<StreetWalker>();
            walker.Walk(new[]
            {
                new Vector3(Door.x + 0.5f, 0f, -3f),
                new Vector3(LaneMouth.x + 0.5f, 0f, laneZ),
                new Vector3(LaneMouth.x + side * 42f, 0f, laneZ),
            }, c.BaseSpeed, null, true);
            return true;
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
            data.visitorsToday = VisitorsToday;
            data.reputationToday = ReputationToday;
        }

        public void RestoreFrom(SaveData data)
        {
            SoldToday = data.soldToday;
            LostToday = data.lostToday;
            VisitorsToday = data.visitorsToday;
            ReputationToday = data.reputationToday;
            RevenueToday = data.dailyRevenue;
        }
    }
}
