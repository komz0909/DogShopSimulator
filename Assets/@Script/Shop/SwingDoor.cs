using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    /// <summary>
    /// 경첩으로 여닫는 문. 가게 정문(두 짝)과 침대방 문(한 짝)에 붙는다.
    ///
    /// <b>자동문</b>이다 — 주인공이 다가오거나 손님·강아지가 문을 건너가려 하면 열리고, 아무도 없으면 닫힌다.
    /// 예전엔 정문은 영업 시간을 따르고 E 로 여닫았는데, 상자를 들고 오갈 때마다 멈춰 E 를 눌러야 했다.
    ///
    /// 닫히면 문짝이 벽처럼 막는다 — 플레이어도 카메라도 못 지나간다.
    /// 손님·강아지는 NavMesh 로 걷고 NavMesh 는 문을 모르므로(구울 때 빼 둔다),
    /// 문을 <b>건너가려는</b> 에이전트가 오면 열어 준다.
    /// "건너가려는"은 목적지가 문 반대편이라는 뜻이다 — 문 앞에서 쉬는 강아지 때문에 문이 안 닫히면 안 된다.
    ///
    /// 문짝 콜라이더는 열려 있는 동안 트리거다. 몸은 통과한다 —
    /// 열린 정문 짝은 앞마당 쪽으로 튀어나와 있어서, 단단하면 배달 상자를 나르다 걸린다.
    ///
    /// 열림 상태는 저장하지 않는다. 처음엔 닫힌 채로 시작한다.
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

        /// <summary>
        /// 문 둘레의 충돌체를 담는 칸. 바닥·벽·앞마당·배송 상자 더미까지 다 걸리므로 넉넉히 둔다 —
        /// 32 칸이면 배송 상자가 쌓였을 때 플레이어·손님이 목록 밖으로 밀려나 문이 안 열리거나, 사람이 선 채로 닫혔다.
        /// </summary>
        static readonly Collider[] buffer = new Collider[256];

        bool wantOpen = true;
        float openness = 1f;
        float lastAgentSeen = -99f;
        float nextScan;
        bool solid;

        Collider[] panels;
        Bounds doorway;

        /// <summary>문 면에 수직인 축(월드 x 또는 z). 에이전트가 문 어느 쪽에 있는지 가른다.</summary>
        Vector3 across;

        /// <summary>지금 상태(열림/닫힘). 손님이 지나가느라 잠깐 열린 것은 치지 않는다.</summary>
        public bool IsOpen => wantOpen;

        /// <summary>E 로 여닫지 않는다(자동문).</summary>
        public bool PlayerOperable => false;

        /// <summary>문짝 경첩과 닫힌 각도. 건물 모델의 문을 문짝으로 붙일 때 쓴다(ShopExterior).</summary>
        public int LeafCount => leaves != null ? leaves.Length : 0;
        public Transform HingeOf(int i) => leaves[i].hinge;
        public float ClosedYawOf(int i) => leaves[i].closedYaw;

        void Awake()
        {
            panels = GetComponentsInChildren<Collider>(true);
            Remeasure();
            SetSolid(false);
        }

        /// <summary>
        /// 문간 자리를 다시 잰다. 가게 단계마다 건물 모델의 문 구멍에 맞춰 문짝 크기가 바뀌므로(ShopExterior) 그때마다 부른다.
        /// </summary>
        public void Remeasure()
        {
            float keep = openness;

            // 닫힌 자세에서 문짝이 차지하는 자리를 재 둔다 — 그게 문간이다
            openness = 0f;
            Pose();
            Physics.SyncTransforms();
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

            openness = keep;
            Pose();
        }

        /// <summary>닫힌 자세로 세운다 — 실행 전 에디터 화면을 게임 시작 때와 같게 보이려고(ShopExterior).</summary>
        public void ShowClosed()
        {
            wantOpen = false;
            openness = 0f;
            Pose();
        }

        void Start()
        {
            // 자동문 — 처음엔 닫힌 채로 선다. 누가 다가오면 그때 열린다
            wantOpen = false;
            openness = 0f;
            Pose();
        }

        // 문짝 렌더러는 경첩 아래에 있는 것만 친다 — 문틀·상인방은 빼야 문간이 정확하다
        bool IsLeafRenderer(Renderer r)
        {
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i].hinge != null && r.transform.IsChildOf(leaves[i].hinge)) return true;
            return false;
        }

        /// <summary>주인공이 이 거리 안(문간 중심 기준)으로 오면 연다.</summary>
        const float PlayerOpenRadius = 2.0f;

        /// <summary>
        /// <b>자동문</b>이다. 주인공이 다가오거나, 손님·강아지가 문을 건너가려 하면 열리고,
        /// 아무도 없으면 <see cref="AutoCloseDelay"/> 뒤에 닫힌다. E 로 여닫던 것은 없앴다 —
        /// 상자를 들고 다니는 가게에서 문 앞에서 매번 멈춰 E 를 누르는 게 번거로웠다.
        /// </summary>
        void Update()
        {
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + ScanInterval;
                if (PlayerNear() || AgentCrossing() || StreetCustomerNear()) lastAgentSeen = Time.time;
            }

            wantOpen = Time.time - lastAgentSeen < AutoCloseDelay || Occupied();
            bool open = wantOpen;
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

        /// <summary>주인공(CharacterController)이 문 가까이 있는가.</summary>
        bool PlayerNear()
        {
            int n = Physics.OverlapSphereNonAlloc(doorway.center, PlayerOpenRadius, buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (buffer[i] is CharacterController && !buffer[i].transform.IsChildOf(transform)) return true;
            return false;
        }

        /// <summary>
        /// 인도에서 걸어 들어오거나 나가는 손님. 이때는 길찾기(NavMeshAgent)가 꺼져 있어서
        /// <see cref="AgentCrossing"/>에 안 잡힌다 — 거리를 걷는 중인 손님이 가까이 오면 연다.
        /// </summary>
        bool StreetCustomerNear()
        {
            int n = Physics.OverlapSphereNonAlloc(doorway.center, autoOpenRadius, buffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                StreetWalker w = buffer[i].GetComponentInParent<StreetWalker>();
                if (w != null && w.Walking) return true;
            }
            return false;
        }

        /// <summary>
        /// 손님·강아지가 닫힌 문을 <b>건너가려고</b> 가까이 왔는가.
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
