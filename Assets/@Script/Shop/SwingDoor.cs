using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    /// <summary>
    /// 경첩으로 여닫는 문. 가게 정문(두 짝)과 침대방 문(한 짝)에 붙는다.
    ///
    /// - <b>정문</b>(<see cref="followsShopHours"/>)은 <b>가게를 열면 활짝 열리고, 닫으면 닫힌다.</b>
    ///   영업 중에는 열린 채로 고정이고, 영업 시간이 아닐 때만 E 로 여닫는다 —
    ///   문 닫고 앞마당에 나가 있다가 갇히면 침대로 못 간다
    /// - <b>침대방 문</b>은 언제든 E 로 여닫는다
    ///
    /// 닫히면 문짝이 벽처럼 막는다 — 플레이어도 카메라도 못 지나간다.
    /// 손님·강아지는 NavMesh 로 걷고 NavMesh 는 문을 모르므로(구울 때 빼 둔다),
    /// 닫힌 문을 <b>건너가려는</b> 에이전트에게만 잠깐 열어 준다. 영업 중엔 정문이 늘 열려 있으니
    /// 이건 문 닫은 뒤 늦게 나가는 손님이나 방을 드나드는 강아지가 문을 뚫고 지나가 보이지 않게 하는 안전망이다.
    /// "건너가려는"은 목적지가 문 반대편이라는 뜻이다 — 문 앞에서 쉬는 강아지 때문에 문이 안 닫히면 안 된다.
    ///
    /// 문짝 콜라이더는 열려 있는 동안 트리거다. 조준(E)에는 걸리지만 몸은 통과한다 —
    /// 열린 정문 짝은 앞마당 쪽으로 튀어나와 있어서, 단단하면 배달 상자를 나르다 걸린다.
    ///
    /// 열림 상태는 저장하지 않는다. 침대방 문은 열린 채로, 정문은 영업 상태대로 시작한다.
    /// </summary>
    public class SwingDoor : MonoBehaviour
    {
        [System.Serializable]
        public struct Leaf
        {
            public Transform hinge;
            public float closedYaw;
            public float openYaw;
        }

        [SerializeField] Leaf[] leaves;

        /// <summary>켜면 영업 상태를 따라간다. 열 때 열리고 닫을 때 닫히며, 영업 중엔 E 로 못 닫는다.</summary>
        [SerializeField] bool followsShopHours;

        /// <summary>
        /// 초당 회전 각도. 105° 를 0.23초에 돈다. 손님은 문에서 45cm 앞에 생겨나
        /// 0.3초 만에 문간을 지나므로, 이보다 느리면 닫힌 문을 뚫고 들어가는 것처럼 보였다.
        /// </summary>
        [SerializeField] float swingSpeed = 450f;

        /// <summary>이 안에서 문을 건너가려는 에이전트가 있으면 열어 준다. 문간 중심 기준.</summary>
        [SerializeField] float autoOpenRadius = 2.2f;

        /// <summary>마지막으로 에이전트를 본 뒤 이만큼 지나야 다시 닫는다. 줄지어 나가는 손님 사이에 덜컥거리지 않게.</summary>
        const float AutoCloseDelay = 1.2f;

        const float ScanInterval = 0.05f;

        /// <summary>문간에 서 있는 사람을 잡으려고 닫힌 문짝 두께를 앞뒤로 이만큼 늘려 본다.</summary>
        const float DoorwayDepth = 0.35f;

        /// <summary>문 면에서 이만큼 안이면 "문간에 걸쳐 있다"로 본다 — 건너는 중이다.</summary>
        const float OnThreshold = 0.25f;

        static readonly Collider[] buffer = new Collider[32];

        bool wantOpen = true;
        float openness = 1f;
        float lastAgentSeen = -99f;
        float nextScan;
        bool solid;

        /// <summary>지난 프레임의 영업 상태. 바뀌는 순간에만 문을 움직인다 — 그 사이엔 플레이어 몫이다.</summary>
        bool shopWasOpen;

        Collider[] panels;
        Bounds doorway;

        /// <summary>문 면에 수직인 축(월드 x 또는 z). 에이전트가 문 어느 쪽에 있는지 가른다.</summary>
        Vector3 across;

        /// <summary>지금 상태(열림/닫힘). 손님이 지나가느라 잠깐 열린 것은 치지 않는다.</summary>
        public bool IsOpen => wantOpen;

        static bool ShopOpen => ShopHours.Instance != null && ShopHours.Instance.IsOpen;

        /// <summary>지금 E 로 여닫을 수 있는가. 정문은 영업 중엔 못 건드린다.</summary>
        public bool PlayerOperable => !followsShopHours || !ShopOpen;

        void Awake()
        {
            panels = GetComponentsInChildren<Collider>(true);

            // 닫힌 자세에서 문짝이 차지하는 자리를 재 둔다 — 그게 문간이다
            openness = 0f;
            Pose();
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!IsLeafRenderer(r)) continue;
                if (!any) { doorway = r.bounds; any = true; }
                else doorway.Encapsulate(r.bounds);
            }
            // 닫힌 문짝은 얇은 판이다. 얇은 쪽이 문을 건너는 방향이고, 그쪽으로만 늘린다
            across = doorway.size.x < doorway.size.z ? Vector3.right : Vector3.forward;
            doorway.Expand(across * (DoorwayDepth * 2f));

            openness = 1f;
            Pose();
            SetSolid(false);
        }

        void Start()
        {
            // 정문은 첫 화면부터 영업 상태대로 서 있어야 한다 — 아침마다 닫히는 장면을 보여 줄 이유가 없다
            if (!followsShopHours) return;

            shopWasOpen = ShopOpen;
            wantOpen = shopWasOpen;
            openness = wantOpen ? 1f : 0f;
            Pose();
        }

        // 문짝 렌더러는 경첩 아래에 있는 것만 친다 — 문틀·상인방은 빼야 문간이 정확하다
        bool IsLeafRenderer(Renderer r)
        {
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i].hinge != null && r.transform.IsChildOf(leaves[i].hinge)) return true;
            return false;
        }

        /// <summary>E 를 눌렀을 때. 문간에 누가 서 있으면 닫지 않는다 — 문짝이 몸에 박혀 갇힌다.</summary>
        public bool TryToggle(out string reason)
        {
            if (!PlayerOperable)
            {
                reason = "영업 중에는 정문을 닫을 수 없다 — 가게를 닫으면 닫힌다";
                return false;
            }

            if (wantOpen && Occupied())
            {
                reason = "문간에 누가 서 있다 — 비키면 닫을 것";
                return false;
            }

            wantOpen = !wantOpen;
            reason = null;
            return true;
        }

        void Update()
        {
            if (followsShopHours)
            {
                bool shopOpen = ShopOpen;
                if (shopOpen != shopWasOpen) wantOpen = shopOpen;   // 열면 열고, 닫으면 닫는다
                if (shopOpen) wantOpen = true;                      // 영업 중엔 열린 채로 고정
                shopWasOpen = shopOpen;
            }

            if (!wantOpen && Time.time >= nextScan)
            {
                nextScan = Time.time + ScanInterval;
                if (AgentCrossing()) lastAgentSeen = Time.time;
            }

            bool open = wantOpen || Time.time - lastAgentSeen < AutoCloseDelay;
            float before = openness;
            openness = Mathf.MoveTowards(openness, open ? 1f : 0f, swingSpeed / 105f * Time.deltaTime);
            if (!Mathf.Approximately(before, openness)) Pose();

            // 다 닫힌 뒤에야 단단해진다. 닫히는 사이 누가 걸어 들어왔으면 비킬 때까지 기다린다
            bool shut = openness <= 0f;
            if (shut && !solid && !Occupied()) SetSolid(true);
            else if (!shut && solid) SetSolid(false);
        }

        void Pose()
        {
            for (int i = 0; i < leaves.Length; i++)
            {
                if (leaves[i].hinge == null) continue;
                float yaw = Mathf.Lerp(leaves[i].closedYaw, leaves[i].openYaw, openness);
                leaves[i].hinge.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        void SetSolid(bool value)
        {
            solid = value;
            for (int i = 0; i < panels.Length; i++)
                if (panels[i] != null) panels[i].isTrigger = !value;
        }

        /// <summary>문간에 플레이어나 에이전트가 서 있는가.</summary>
        bool Occupied()
        {
            int n = Physics.OverlapBoxNonAlloc(doorway.center, doorway.extents, buffer,
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < n; i++)
            {
                Collider c = buffer[i];
                if (c.transform.IsChildOf(transform)) continue;
                if (c is CharacterController || c.GetComponentInParent<NavMeshAgent>() != null) return true;
            }
            return false;
        }

        /// <summary>
        /// 손님·강아지가 닫힌 문을 <b>건너가려고</b> 가까이 왔는가. 플레이어는 치지 않는다 — 플레이어는 E 로 연다.
        /// 지금 자리와 목적지가 문 면의 반대편이거나, 이미 문간에 걸쳐 걷는 중이면 연다.
        /// </summary>
        bool AgentCrossing()
        {
            int n = Physics.OverlapSphereNonAlloc(doorway.center, autoOpenRadius, buffer,
                ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < n; i++)
            {
                NavMeshAgent agent = buffer[i].GetComponentInParent<NavMeshAgent>();
                if (agent == null || !agent.isActiveAndEnabled) continue;
                if (!agent.hasPath && !agent.pathPending) continue;

                float here = Vector3.Dot(agent.transform.position - doorway.center, across);
                float there = Vector3.Dot(agent.destination - doorway.center, across);

                if (Mathf.Abs(here) < OnThreshold && agent.desiredVelocity.sqrMagnitude > 0.01f) return true;
                if (here * there < 0f) return true;
            }
            return false;
        }
    }
}
