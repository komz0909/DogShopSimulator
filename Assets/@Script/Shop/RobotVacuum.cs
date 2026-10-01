using DogShop.Core;
using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    /// <summary>
    /// 로봇청소기. <b>L7 보상</b>이다 — 구석에 대기하다가 바닥이 더러워지면
    /// 가서 지우고 제자리로 돌아온다.
    ///
    /// 해금 전에는 아예 꺼 둔다. 모델만 숨기면 NavMeshAgent 가 계속 돌아
    /// 보이지도 않는 것이 길을 계산한다.
    ///
    /// 청소는 <b>손으로 하던 일을 대신하는 것</b>이지 새 이득이 아니다. 청결도는
    /// 손님 수를 깎는 방향으로만 작동하므로(<see cref="CleanlinessManager"/>),
    /// 이 보상의 값은 "매출이 오른다"가 아니라 "바닥을 줍느라 쓰던 시간이 돌아온다"이다.
    /// 하루가 5분뿐이라 그 시간이 곧 값이다.
    /// </summary>
    public class RobotVacuum : MonoBehaviour
    {
        /// <summary>이 레벨부터 작동한다.</summary>
        [SerializeField] int unlockLevel = 7;

        [SerializeField] float speed = 0.7f;

        /// <summary>한 지점을 빨아들이는 데 걸리는 시간.</summary>
        [SerializeField] float cleanSeconds = 1.2f;

        /// <summary>이만큼 다가가면 닿은 것으로 본다.</summary>
        [SerializeField] float reach = 0.35f;

        /// <summary>제자리에 돌아왔다고 보는 거리.</summary>
        [SerializeField] float homeReach = 0.15f;

        /// <summary>바닥을 훑는 느낌을 주는 회전 속도(도/초). 청소 중에만 돈다.</summary>
        [SerializeField] float spinSpeed = 240f;

        enum State { Docked, Heading, Cleaning, Returning }

        NavMeshAgent agent;
        Renderer[] visuals;

        Vector3 home;
        Quaternion homeRotation;

        DirtSpot target;
        State state = State.Docked;
        float timer;
        bool running;

        /// <summary>
        /// 한 번이라도 상태를 적용했는가. 이게 없으면 <b>최초 호출이 그냥 반환한다</b> —
        /// running 도 unlocked 도 false 라 "바뀐 게 없다"로 보여서, 해금 전인데도
        /// 모델이 켜진 채로 남는다.
        /// </summary>
        bool applied;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            visuals = GetComponentsInChildren<Renderer>(true);

            // 제자리는 <b>씬에 놓인 그 자리</b>다. 매장이 넓어져도(ShopSpace) 원점 쪽
            // 구석은 그대로 남으므로 좌표를 박아 두지 않고 놓인 곳을 기억한다
            home = transform.position;
            homeRotation = transform.rotation;

            if (agent != null)
            {
                agent.speed = speed;
                agent.angularSpeed = 720f;
                agent.acceleration = 8f;
                agent.stoppingDistance = 0f;
                agent.autoBraking = true;

                // 손님이 못 가는 직원 구역(3)도 청소한다 — 바닥은 어디든 더러워진다
                agent.areaMask = NavMesh.AllAreas;

                // 사람이 우선이다. 청소기가 길을 막고 서 있으면 계산대 줄이 엉킨다
                agent.avoidancePriority = 80;
            }
        }

        void Start()
        {
            ShopLevelManager level = ShopLevelManager.Instance;
            if (level != null) level.OnLevelUp += HandleLevel;

            // 세이브를 불러오면 이미 L7 이상일 수 있다. 이벤트만 기다리면 그때 안 켜진다
            Apply(level != null ? level.Level : 1);
        }

        void OnDestroy()
        {
            if (ShopLevelManager.Instance != null) ShopLevelManager.Instance.OnLevelUp -= HandleLevel;
        }

        void HandleLevel(int level) => Apply(level);

        void Apply(int level)
        {
            bool unlocked = level >= unlockLevel;
            if (applied && unlocked == running) return;

            applied = true;
            running = unlocked;
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null) visuals[i].enabled = unlocked;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = !unlocked;
            }

            if (!unlocked) return;

            // Warp 로 보낸다. transform.position 에 직접 넣으면 에이전트의 높이 보정
            // (baseOffset)을 건너뛰어 NavMesh 높이만큼 공중에 뜬 채로 시작한다
            if (agent != null && agent.isOnNavMesh) agent.Warp(home);
            else transform.position = home;

            transform.rotation = homeRotation;
            state = State.Docked;
            target = null;
        }

        void Update()
        {
            if (!running || agent == null || !agent.isOnNavMesh) return;

            switch (state)
            {
                case State.Docked: Dock(); break;
                case State.Heading: Head(); break;
                case State.Cleaning: CleanUp(); break;
                case State.Returning: Return(); break;
            }
        }

        /// <summary>대기 중. 오염이 생기면 가장 가까운 것을 집는다.</summary>
        void Dock()
        {
            DirtSpot next = Nearest();
            if (next == null) return;

            target = next;
            state = State.Heading;
            agent.isStopped = false;
            agent.SetDestination(target.transform.position);
        }

        void Head()
        {
            // 플레이어가 먼저 주웠을 수 있다. 사라진 목표를 계속 쫓지 않는다
            if (target == null) { GoHome(); return; }

            agent.SetDestination(target.transform.position);

            if (Flat(transform.position - target.transform.position) > reach * reach) return;

            agent.isStopped = true;
            state = State.Cleaning;
            timer = cleanSeconds;
        }

        void CleanUp()
        {
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            if (target != null && CleanlinessManager.Instance != null)
                CleanlinessManager.Instance.Clean(target);
            target = null;

            // 아직 남은 게 있으면 쉬지 않고 다음으로 간다. 한 번에 하나씩 왕복하면
            // 여러 개가 겹칠 때 영영 못 따라잡는다
            DirtSpot next = Nearest();
            if (next != null)
            {
                target = next;
                state = State.Heading;
                agent.isStopped = false;
                agent.SetDestination(target.transform.position);
                return;
            }

            GoHome();
        }

        void GoHome()
        {
            target = null;
            state = State.Returning;
            agent.isStopped = false;
            agent.SetDestination(home);
        }

        void Return()
        {
            // 돌아가는 길에 새로 더러워지면 방향을 튼다
            DirtSpot next = Nearest();
            if (next != null)
            {
                target = next;
                state = State.Heading;
                agent.SetDestination(target.transform.position);
                return;
            }

            if (Flat(transform.position - home) > homeReach * homeReach) return;

            // 자리만 잡고 방향을 튼다. 좌표를 다시 박으면 <b>높이가 튄다</b> —
            // home 은 씬에 놓인 값이라 NavMesh 높이를 품고 있는데, 달리는 동안의
            // 실제 높이는 baseOffset 으로 내려간 바닥이다
            agent.isStopped = true;
            transform.rotation = homeRotation;
            state = State.Docked;
        }

        /// <summary>가장 가까운 오염 지점. 없으면 null.</summary>
        DirtSpot Nearest()
        {
            CleanlinessManager dirt = CleanlinessManager.Instance;
            if (dirt == null) return null;

            DirtSpot best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < dirt.SpotCount; i++)
            {
                DirtSpot spot = dirt.SpotAt(i);
                if (spot == null) continue;

                float sqr = Flat(transform.position - spot.transform.position);
                if (sqr >= bestSqr) continue;

                best = spot;
                bestSqr = sqr;
            }
            return best;
        }

        /// <summary>높이를 뺀 거리의 제곱. 청소기는 바닥에, 오염은 살짝 떠 있다.</summary>
        static float Flat(Vector3 delta)
        {
            delta.y = 0f;
            return delta.sqrMagnitude;
        }
    }
}
