
using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Dogs
{
    /// <summary>
    /// 반려견이 가게를 돌아다니고, 주인이 멀어지면 따라간다.
    ///
    /// 자리에 못 박아 두면 소품과 다를 게 없다. 돌아다니게 하되 <b>매장·창고·옆방 전부</b>를
    /// 쓴다 — 창고와 옆방은 손님에게만 막힌 Staff 영역이라 반려견은 들어갈 수 있다.
    ///
    /// 배속은 건드리지 않는다. TimeManager는 Time.timeScale이 아니라 자기 시계만 돌리므로
    /// 16배속에서도 이 아이는 실제 속도로 걷는다 — 손님처럼 배속을 곱하면
    /// 한 프레임에 몸 길이를 건너뛰어 경로 판정이 무너진다(11차 측정).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class DogRoamer : MonoBehaviour
    {
        /// <summary>주인이 이보다 멀어지면 따라간다. 매장 대각선(10m)보다 짧게 잡는다.</summary>
        const float FollowRange = 6f;

        /// <summary>주인 곁에 설 거리. 너무 붙으면 밀고 다니는 것처럼 보인다.</summary>
        const float HeelDistance = 1.6f;

        /// <summary>한 번에 어슬렁거릴 최대 거리.</summary>
        const float WanderRadius = 5f;

        const float ArriveTolerance = 0.45f;
        const float WalkSpeed = 1.3f;
        const float RunSpeed = 2.6f;

        static readonly Vector2 PauseRange = new Vector2(1.5f, 4.5f);

        /// <summary>바닥 높이를 다시 재는 주기.</summary>
        const float GroundCheckInterval = 0.4f;

        NavMeshAgent agent;
        DogAnimator animator;
        DogStats stats;
        Transform owner;

        float pauseTimer;
        float groundTimer;
        bool following;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<DogAnimator>();
            stats = GetComponent<DogStats>();

            agent.areaMask = NavMesh.AllAreas;   // 손님과 달리 직원 구역도 다닌다
            agent.speed = WalkSpeed;
            agent.angularSpeed = 300f;
            agent.acceleration = 8f;
            agent.stoppingDistance = 0f;
            agent.autoBraking = true;
        }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                var controller = FindFirstObjectByType<CharacterController>();
                if (controller != null) player = controller.gameObject;
            }
            owner = player != null ? player.transform : null;

            Warp(transform.position);
            pauseTimer = Random.Range(PauseRange.x, PauseRange.y);
        }

        /// <summary>세이브 복원처럼 순간이동시킬 때. NavMeshAgent는 transform 대입을 싫어한다.</summary>
        public void Warp(Vector3 position)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        void Update()
        {
            if (agent == null || !agent.isOnNavMesh) return;

            float toOwner = owner != null
                ? Vector3.Distance(transform.position, owner.position)
                : 0f;

            // 주인이 멀어지면 하던 일을 멈추고 붙는다. 가게 밖으로 나가도 이 규칙 하나로 따라온다.
            bool shouldFollow = owner != null && toOwner > FollowRange;
            if (shouldFollow != following)
            {
                following = shouldFollow;
                agent.speed = following ? RunSpeed : WalkSpeed;
                pauseTimer = 0f;
            }

            if (following) Follow(toOwner);
            else Wander();

            StickToGround();
            Animate();
        }

        /// <summary>
        /// 구운 NavMesh 표면은 바닥보다 조금 위에 뜬다. 그 값이 <b>구역마다 다르다</b> —
        /// 매장 7.7cm, 창고·옆방 4.2cm. 고정값을 주면 한쪽에서 발이 뜨거나 묻히므로
        /// 지금 서 있는 자리에서 다시 재어 맞춘다.
        /// </summary>
        void StickToGround()
        {
            groundTimer -= Time.deltaTime;
            if (groundTimer > 0f) return;
            groundTimer = GroundCheckInterval;

            RaycastHit floor;
            if (!Physics.Raycast(transform.position + Vector3.up, Vector3.down, out floor, 4f, ~0, QueryTriggerInteraction.Ignore)) return;

            NavMeshHit nav;
            if (!NavMesh.SamplePosition(transform.position, out nav, 1f, NavMesh.AllAreas)) return;

            agent.baseOffset = floor.point.y - nav.position.y;
        }

        void Follow(float toOwner)
        {
            if (toOwner <= HeelDistance)
            {
                agent.isStopped = true;
                return;
            }

            agent.isStopped = false;

            // 주인 발밑이 아니라 주인 뒤쪽으로 간다 — 겹쳐 서면 몸이 파묻혀 보인다
            Vector3 behind = owner.position - owner.forward * HeelDistance;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(behind, out hit, 2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
        }

        void Wander()
        {
            agent.isStopped = false;

            bool arrived = !agent.pathPending && agent.remainingDistance <= ArriveTolerance;
            if (!arrived) return;

            if (pauseTimer > 0f)
            {
                pauseTimer -= Time.deltaTime;
                return;
            }

            // 주인 주변이 아니라 <b>제자리 주변</b>에서 고른다. 주인을 중심으로 돌면
            // 따라다니는 것처럼 보여 자유롭게 지낸다는 인상이 사라진다.
            Vector3 candidate = transform.position + new Vector3(
                Random.Range(-WanderRadius, WanderRadius), 0f, Random.Range(-WanderRadius, WanderRadius));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                pauseTimer = Random.Range(PauseRange.x, PauseRange.y);
            }
        }

        /// <summary>
        /// 컨디션은 <see cref="Dog.SyncMood"/>가 정지 상태에서만 보여 준다.
        /// 여기서는 <b>움직임</b>만 애니메이션으로 옮긴다.
        /// </summary>
        void Animate()
        {
            if (animator == null) return;

            float speed = agent.velocity.magnitude;

            if (speed > RunSpeed * 0.6f) animator.Play(DogAnim.Run);
            else if (speed > 0.15f) animator.Play(DogAnim.Walk);
            else if (stats != null && stats.GrowthBlocked) animator.Play(DogAnim.Angry);
            else if (stats != null && stats.UpkeepAverage >= 80) animator.Play(DogAnim.WagTail);
            else animator.Play(DogAnim.Idle);
        }
    }
}
