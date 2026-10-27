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

        /// <summary>
        /// 이탈 이유. 셋의 합이 <see cref="LostToday"/> 다. 측정 기록(EconomyLog)에 남겨 원인을 가른다 -
        /// 수만 남기면 품절로 빠진 손님과 길이 막혀 빠진 손님이 섞여 보인다(55~60차).
        /// </summary>
        public int LostSoldOutToday { get; private set; }
        /// <summary>진열대로 가다가(둘러보기 포함) 이동 시간 초과로 나간 손님.</summary>
        public int LostTravelToShelfToday { get; private set; }
        /// <summary>물건을 들고 계산대 줄로 가다가 이동 시간 초과로 나간 손님.</summary>
        public int LostTravelToCounterToday { get; private set; }

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
            lastClockHour = TimeManager.Instance.FlowHour;
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
            LostSoldOutToday = 0;
            LostTravelToShelfToday = 0;
            LostTravelToCounterToday = 0;
            VisitorsToday = 0;
            ReputationToday = 0;
            RevenueToday = 0;
            pending = 0f;
            if (TimeManager.Instance != null) lastClockHour = TimeManager.Instance.FlowHour;
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

            float now = time.FlowHour;
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

            // 문 앞에서 바로 만든다. 원점에서 만든 뒤 위치만 옮기면 NavMeshAgent 는 생성 순간 원점에 가장 가까운 면에
            // 붙어 버린다 — 매장이 넓어져 다시 구운 뒤엔 그 면이 가게 왼쪽 밖(-0.2, -0.7)의 끊긴 조각이라,
            // 무인 측정에서 Lv7 이후 하루 손님 전원이 거기 갇혀 나갔다(56·57·60·61·62차의 붕괴)
            GameObject instance = Instantiate(customerPrefab, Door, Quaternion.identity, transform);

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
                if (agent != null && agent.isActiveAndEnabled) agent.Warp(Door);
                BeginBrowse(customer, shelf);
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

                if (c.State == CustomerState.ToShelf) TickToShelf(c, step);
                else if (c.State == CustomerState.ToCounter) TickToCounter(c);
                else if (c.State == CustomerState.Waiting) TickWaiting(c, step);
                else if (c.State == CustomerState.ToExit && c.Arrived) finished.Add(c);
            }

            for (int i = 0; i < finished.Count; i++) Despawn(finished[i]);
        }

        void TickToShelf(Customer c, float step)
        {
            // 둘러볼 자리에 끝내 닿지 못하면(진열대에 깎인 면 가장자리 등) 그 자리에서 구경한 것으로 친다.
            // 안 그러면 0.6m 앞에서 45초를 서 있다 이동 시간 초과로 나간다(61차: 하루 1~2명)
            if (!c.Arrived && c.TravelTime > BrowseLegGiveUp && (c.BrowseLeft > 0 || c.BrowseLinger > 0f || c.BrowsePeeks > 0))
                c.MoveTo(c.transform.position);

            if (!c.Arrived || c.Holding) return;

            // 둘러보는 중 — 들른 곳에서 잠깐 구경하고 다음 곳으로, 다 보면 원하는 진열대로
            if (c.BrowseLeft > 0 || c.BrowseLinger > 0f || c.BrowsePeeks > 0)
            {
                if (c.BrowseLinger > 0f)
                {
                    // 서 있는 동안엔 구경하는 진열대를 본다. 도착 판정(0.6m) 뒤에도 몇 걸음 더 걷는데,
                    // 그동안 몸을 돌리면 옆걸음질로 미끄러져 보여 거의 멈춘 뒤에 돌린다
                    Vector3 left = c.Destination - c.transform.position;
                    left.y = 0f;
                    Vector3 look = c.BrowseLook - c.transform.position;
                    look.y = 0f;
                    if (left.sqrMagnitude < 0.2f * 0.2f && look.sqrMagnitude > 0.04f) c.FaceDirection(look);
                    c.BrowseLinger -= step;
                    return;
                }
                if (c.BrowsePeeks > 0) { c.BrowsePeeks--; Peek(c); return; }
                if (c.BrowseLeft > 0) { c.BrowseLeft--; GoBrowse(c); return; }
            }
            if (c.BrowseLinger <= 0f && c.BrowseLeft == 0 && (c.transform.position - c.ShelfTarget).sqrMagnitude > 1f && c.ShelfTarget != Vector3.zero)
            {
                Vector3 target = c.ShelfTarget;
                c.ShelfTarget = Vector3.zero;
                c.MoveTo(FreeSpot(target, c));
                return;
            }

            InventoryManager inv = InventoryManager.Instance;
            ItemGrade picked;
            if (inv.TryConsumeShelf(c.WantedProduct, out picked))
            {
                ProductDef p = inv.Catalog.Get(c.WantedProduct);
                c.HasItem = true;
                c.PickedGrade = picked;

                int price = inv.WithRecommendBonus(c.WantedProduct, ItemGrades.PriceOf(p.retail, picked));
                c.Label = (inv.IsRecommendedToday(c.WantedProduct) ? "[추천] " : "")
                        + (picked == ItemGrade.None ? "" : "[" + ItemGrades.NameOf(picked) + "] ")
                        + p.nameKo + "  " + price + "원";

                c.State = CustomerState.ToCounter;
                EnterQueue(c);
            }
            else
            {
                // 들어올 땐 진열대에 있었는데(FindFor) 도착해 보니 다 팔렸다
                LostToday++;
                LostSoldOutToday++;
                Sound.Play(Sfx.CustomerLost);
                OnLostSale?.Invoke(c.WantedProduct);
                SendHome(c);
            }
        }

        /// <summary>둘러볼 곳 수(게임). 들어온 손님이 진열대로 가기 전 가게를 한두 군데 구경한다 — 강아지가 다가갈 시간이 생긴다.</summary>
        static readonly Vector2Int BrowseStops = new Vector2Int(2, 4);   // 2~3곳
        /// <summary>
        /// 한 곳에서 멈춰 구경하는 게임 시간(초). 예전엔 3~6초 가만히 서 있어 멍해 보였다 —
        /// 짧게 서고 자주 움직인다(옆으로 몇 걸음 <see cref="Peek"/>, 다음 진열대). 둘러보는 전체 시간은 예전과 비슷하다.
        /// </summary>
        static readonly Vector2 BrowseLingerRange = new Vector2(1.2f, 2.5f);
        /// <summary>한 진열대 앞에서 옆으로 옮겨 더 볼 때 걷는 거리(m)와 그 자리에서 서 있는 시간(초).</summary>
        static readonly Vector2 PeekStepRange = new Vector2(0.6f, 1.1f);
        static readonly Vector2 PeekLingerRange = new Vector2(1f, 2f);
        /// <summary>둘러보기 한 구간에 쓰는 최대 이동 시간(게임 초). 가게 안 한 구간은 몇 m 라 이보다 훨씬 짧다.</summary>
        const float BrowseLegGiveUp = 10f;

        /// <summary>서 있는 손님끼리 이만큼은 떨어진다(m). 몸 반지름 0.28 두 개 + 여유.</summary>
        const float StandGap = 0.62f;

        /// <summary>
        /// <paramref name="p"/> 근처에서 다른 손님이 서 있거나 가려는 자리와 겹치지 않는 곳. 손님끼리 통과하게 바꿨으니
        /// 멈춰 서는 자리만큼은 나눠 줘야 한 점에 여러 명이 포개져 선다. 다 차 있으면 가장 덜 붐비는 후보.
        /// </summary>
        Vector3 FreeSpot(Vector3 p, Customer self)
        {
            if (IsFree(p, self)) return p;
            Vector3 best = p;
            for (int ring = 1; ring <= 3; ring++)
            {
                float r = StandGap * ring;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f + ring * 0.4f;
                    Vector3 cand = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    NavMeshHit hit;
                    if (!NavMesh.SamplePosition(cand, out hit, 0.3f, Customer.WalkableAreas)) continue;
                    if (IsFree(hit.position, self)) return hit.position;
                }
            }
            return best;
        }

        bool IsFree(Vector3 p, Customer self)
        {
            for (int i = 0; i < active.Count; i++)
            {
                Customer o = active[i];
                if (o == self || o.State == CustomerState.ToExit) continue;
                Vector3 d = o.Destination - p; d.y = 0f;
                if (d.sqrMagnitude < StandGap * StandGap) return false;
            }
            return true;
        }

        void BeginBrowse(Customer c, ShelfTable shelf)
        {
            c.ShelfTarget = shelf.ApproachPoint;
            c.BrowseLeft = UnityEngine.Random.Range(BrowseStops.x, BrowseStops.y);
            c.BrowseLinger = 0f;
            c.BrowseLeft--;
            GoBrowse(c);
        }

        /// <summary>다른 진열대 앞 한 곳을 골라 걸어가 구경한다. 갈 데가 없으면 바로 원하는 진열대로.</summary>
        void GoBrowse(Customer c)
        {
            ShelfManager sm = ShelfManager.Instance;
            Vector3 pick = c.ShelfTarget;
            Vector3 look = c.ShelfTarget;
            if (sm != null && sm.Count > 0)
            {
                for (int tries = 0; tries < 6; tries++)
                {
                    ShelfTable t = sm.Get(UnityEngine.Random.Range(0, sm.Count));
                    if (t == null || !t.CustomersCanReach) continue;
                    if ((t.ApproachPoint - c.ShelfTarget).sqrMagnitude < 0.25f && sm.Count > 1) continue;
                    pick = t.ApproachPoint + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 0f, UnityEngine.Random.Range(-0.3f, 0.3f));
                    // 접근점에 흔든 값을 더하면 진열대가 깎아 낸 자리에 떨어질 수 있다 — 손님이 걷는 면 위로 맞춘다
                    NavMeshHit onMesh;
                    if (NavMesh.SamplePosition(pick, out onMesh, 0.6f, Customer.WalkableAreas)) pick = onMesh.position;
                    else pick = t.ApproachPoint;
                    look = t.transform.position;
                    break;
                }
            }
            c.MoveTo(FreeSpot(pick, c));
            c.BrowseLook = look;
            c.BrowsePeeks = UnityEngine.Random.Range(0, 2);   // 절반쯤은 옆으로 한 번 더 옮겨 본다
            c.BrowseLinger = UnityEngine.Random.Range(BrowseLingerRange.x, BrowseLingerRange.y);
        }

        /// <summary>
        /// 같은 진열대 앞에서 옆으로 몇 걸음 옮겨 다시 구경한다 — 진열대를 따라 훑어보는 느낌.
        /// 진열대를 바라본 방향의 옆(좌우 무작위)으로 걷고, 막히면 반대쪽, 그래도 막히면 제자리에서 조금 더 본다.
        /// </summary>
        void Peek(Customer c)
        {
            Vector3 face = c.BrowseLook - c.transform.position;
            face.y = 0f;
            if (face.sqrMagnitude < 0.01f) face = c.transform.forward;
            face.Normalize();
            Vector3 side = new Vector3(-face.z, 0f, face.x) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            float dist = UnityEngine.Random.Range(PeekStepRange.x, PeekStepRange.y);

            for (int k = 0; k < 2; k++, side = -side)
            {
                NavMeshHit hit;
                Vector3 cand = c.transform.position + side * dist;
                if (!NavMesh.SamplePosition(cand, out hit, 0.3f, Customer.WalkableAreas)) continue;
                if (!IsFree(hit.position, c)) continue;
                c.MoveTo(hit.position);
                break;
            }
            c.BrowseLinger = UnityEngine.Random.Range(PeekLingerRange.x, PeekLingerRange.y);
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
            retail = InventoryManager.Instance.WithRecommendBonus(c.WantedProduct, retail);   // 오늘의 추천 상품

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

            bool toCounter = c.State == CustomerState.ToCounter;
            if (toCounter) LostTravelToCounterToday++;
            else LostTravelToShelfToday++;

            // 길이 막힌 자리를 찾으려고 남긴다. 드문 일이라 할당은 신경 쓰지 않는다
            bool browsing = c.BrowseLeft > 0 || c.BrowseLinger > 0f || c.BrowsePeeks > 0;
            Debug.Log("[손님 이탈] 이동 시간 초과 - " + (toCounter ? "계산대로" : browsing ? "둘러보는 중" : "진열대로")
                + " / 위치 " + c.transform.position.ToString("F1") + " / 목적지 " + c.Destination.ToString("F1")
                + " / " + (GameManager.Instance != null ? GameManager.Instance.Day : 0) + "일차 "
                + TimeManager.Instance.CurrentHour.ToString("F1") + "시");

            if (c.HasItem)
            {
                InventoryManager.Instance.ReturnToShelf(c.WantedProduct);
                c.HasItem = false;
            }

            Sound.Play(Sfx.CustomerLost);
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

            // 줄은 계산대 앞에서 곧게 뻗다가
            //   입구 통로에 걸리는 자리는 건너뛴다 — 줄이 문 바로 안쪽을 가로질러 들어오는 손님이 줄을 뚫고 지나갔다
            //   벽·가구에 막히면 가게 안쪽으로 꺾어 이어 간다 — 예전엔 막힌 자리들이 NavMesh 위 한 점으로 끌려와 여럿이 겹쳐 섰다
            Vector3 dir = counter != null ? counter.CustomerSide : Vector3.left;
            dir.y = 0f;
            dir.Normalize();
            Vector3 p = counter != null ? counter.QueueSlot(0) : FallbackQueue;
            Vector3 prev = p;
            bool turned = false;

            for (int i = 0; i < queue.Count; i++)
            {
                for (int guard = 0; guard < 6 && InDoorway(p); guard++) p += dir * QueueSpacing;

                Vector3 slot = p;
                bool placed = TrySlot(ref slot);
                if (!placed && i > 0 && !turned)
                {
                    // 막혔다 — 직전 자리에서 직각으로 꺾는다. 두 방향 중 문에서 먼 쪽(가게 안쪽)
                    turned = true;
                    Vector3 side = new Vector3(-dir.z, 0f, dir.x);
                    if ((prev + side).z < (prev - side).z) side = -side;
                    dir = side;
                    p = prev + dir * QueueSpacing;
                    slot = p;
                    placed = TrySlot(ref slot);
                }
                if (!placed)
                {
                    // 그래도 막히면 예전처럼 가까운 NavMesh 위로
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(p, out hit, 2f, Customer.WalkableAreas)) slot = hit.position;
                }

                queue[i].MoveTo(slot);
                prev = p;
                p += dir * QueueSpacing;
            }
        }

        /// <summary>줄 간격(<see cref="ShopCounter"/> 와 같다).</summary>
        const float QueueSpacing = 0.70f;

        /// <summary>그 자리가 NavMesh 위 제자리에 잡히나. 많이 끌려가면(벽·가구 안) 못 선 것으로 본다.</summary>
        static bool TrySlot(ref Vector3 slot)
        {
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(slot, out hit, 2f, Customer.WalkableAreas)) return false;
            Vector3 d = hit.position - slot;
            d.y = 0f;
            if (d.sqrMagnitude > 0.3f * 0.3f) return false;
            slot = hit.position;
            return true;
        }

        /// <summary>입구 통로(문에서 안쪽으로 곧게)의 반폭과 깊이(m). 줄이 여기 서지 않는다.</summary>
        const float DoorwayHalfWidth = 0.75f;
        const float DoorwayDepth = 2.6f;

        /// <summary>그 자리가 입구 통로 안인가 — 문(<see cref="Door"/>)에서 가게 안쪽(+z)으로 뻗은 띠.</summary>
        static bool InDoorway(Vector3 p)
        {
            return Mathf.Abs(p.x - Door.x) < DoorwayHalfWidth && p.z > Door.z - 0.5f && p.z < Door.z + DoorwayDepth;
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
            GameEvents.RaiseCustomerCheckedOut();
            Sound.Play(Sfx.Checkout);
            Sound.Play(Sfx.CoinJingle);
            UI.JuiceFx.FloatText(c.transform.position + Vector3.up * (c.LabelHeight + 0.2f), "+" + paid.ToString("N0") + "원", new Color(1f, 0.85f, 0.3f));

            int penalty = ReputationPenaltyOf(c);
            if (penalty > 0) GameManager.Instance.AddReputation(-penalty);

            c.HasItem = false;
            LeaveQueue(c);
            SendHome(c);
            return true;
        }

        /// <summary>가게 안 손님(줄 선 손님 포함). 강아지 반응이 고를 대상.</summary>
        public Customer ActiveAt(int index) => index >= 0 && index < active.Count ? active[index] : null;

        public Customer QueuedAt(int index) => index >= 0 && index < queue.Count ? queue[index] : null;

        void SendHome(Customer c)
        {
            c.Label = "";
            c.State = CustomerState.ToExit;
            c.PassThroughOthers();   // 나가는 길은 남을 밀지도, 남에게 막히지도 않는다
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
            BeginBrowse(customer, shelf);
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

        /// <summary>온라인 주문 출고(OnlineOrderManager). 매장 판매처럼 오늘 매출·판매 건수에 넣는다.</summary>
        public void RecordOnlineSale(int paid)
        {
            RevenueToday += paid;
            SoldToday++;
            GameManager.Instance.RegisterSale(paid);
            OnSale?.Invoke(paid);
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

            // 도착 누적의 기준 시각도 저장된 시각으로 — 안 맞추면 Start 에서 잡은 09:00 부터
            // 불러온 시각(예 15:00)까지 여섯 시간치 손님이 한 프레임에 문 앞에 몰렸다(명성·매출 이중)
            lastClockHour = Mathf.Clamp(data.currentHour, TimeManager.DayStartHour, TimeManager.LastHour);
            pending = 0f;
        }
    }
}
