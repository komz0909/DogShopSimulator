
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

        /// <summary>목적지에 닿았다고 볼 거리.</summary>
        const float ArriveTolerance = 0.45f;

        /// <summary>
        /// 한 걸음 주기에 몸 길이의 몇 배를 가는가. 걷기·뛰기 속도는 이 값과
        /// <b>실제로 잰 몸 길이</b>에서 역산한다 — 클립에 루트 모션이 없어서
        /// (averageSpeed 0) 속도를 임의로 주면 다리가 도는 것보다 빨리 나가 미끄러진다.
        ///
        /// 상수로 박지 않는 이유: 견종마다 몸이 배 이상 차이 난다(치와와 0.30m ~ 셰퍼드 0.80m).
        /// 시작할 때 고른 견종이 무엇이든 여기서 다시 계산된다.
        /// </summary>
        const float WalkStride = 1.2f;
        const float RunStride = 2.0f;

        /// <summary>클립을 못 찾았을 때 쓸 길이(초). 이 킷의 값이다.</summary>
        const float WalkClipFallback = 1.67f;
        const float RunClipFallback = 0.79f;

        /// <summary>주인(3.2 m/s)이 계속 달리면 뛰기로도 못 따라잡는다. 멀어진 만큼 더 낸다.</summary>
        const float MaxCatchUp = 1.6f;

        /// <summary>
        /// 이보다 벌어지면 주인 뒤로 옮겨 놓는다.
        ///
        /// 작은 견종은 뛰어도 주인을 못 따라간다 — 치와와는 뛰기가 0.99 m/s 라
        /// 따라잡기 배수(1.6)를 다 써도 1.58 m/s 로 주인(3.2)의 절반이다.
        /// 배수를 더 올리면 다리가 도는 것보다 빨리 나가 다시 미끄러지므로,
        /// <b>걸음걸이는 정직하게 두고 너무 벌어졌을 때만 옮긴다.</b>
        /// </summary>
        const float LostRange = 12f;

        float walkSpeed = 0.75f;
        float runSpeed = 2.64f;
        float heelDistance = HeelDistance;

        /// <summary>
        /// 주인과 이보다 가까우면 <b>주인 쪽으로는 더 가지 않는다.</b>
        ///
        /// 사람은 CharacterController 라 중력 때문에 매 프레임 Move 를 부르고, 그때마다
        /// 겹친 콜라이더에서 자기를 밀어낸다. 그래서 반려견이 몸으로 파고들면
        /// <b>사람이 옆으로 밀려난다</b> — 서 있기만 해도 떠밀린다.
        /// 반대로 사람이 반려견을 미는 일은 없다. 반려견에는 Rigidbody가 없어서
        /// CharacterController가 밀어낼 대상이 아니고, 부딪히면 사람이 그냥 막힌다.
        ///
        /// 두 몸 반지름을 더해 잡는다. 견종마다 몸이 배 이상 차이 나므로 상수로 박지 않는다.
        /// </summary>
        float bodyClearance = 0.7f;

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

            MeasureGait();

            agent.areaMask = NavMesh.AllAreas;   // 손님과 달리 직원 구역도 다닌다
            agent.speed = walkSpeed;
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

            MeasureBodies(player);
            Warp(transform.position);
            pauseTimer = Random.Range(PauseRange.x, PauseRange.y);
        }

        /// <summary>
        /// 몸 길이와 클립 길이를 재어 걷기·뛰기 속도를 정한다.
        /// 견종을 바꿔도(코기·셰퍼드·치와와…) 이 계산이 다시 돌아 보폭이 맞는다.
        /// </summary>
        void MeasureGait()
        {
            float body = 0.6f;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                body = Mathf.Max(0.2f, Mathf.Max(b.size.x, b.size.z));
            }

            float walkClip = WalkClipFallback, runClip = RunClipFallback;
            if (animator != null)
            {
                // 이 킷의 클립 이름은 <견종>_Walking01 / <견종>_Running 으로 통일돼 있다
                Animator raw = GetComponentInChildren<Animator>();
                if (raw != null && raw.runtimeAnimatorController != null)
                    foreach (AnimationClip c in raw.runtimeAnimatorController.animationClips)
                    {
                        if (c.name.Contains("Walking01")) walkClip = c.length;
                        else if (c.name.Contains("Running")) runClip = c.length;
                    }
            }

            walkSpeed = body * WalkStride / Mathf.Max(0.1f, walkClip);
            runSpeed = body * RunStride / Mathf.Max(0.1f, runClip);
            heelDistance = Mathf.Max(0.9f, body * 1.4f);
        }

        /// <summary>
        /// 두 몸의 반지름을 재어 서로 닿지 않을 거리를 잡는다.
        /// 반려견 쪽은 <b>단단한 콜라이더</b>에서 잰다 — 조준용 트리거가 더 크게 잡혀 있다.
        /// </summary>
        void MeasureBodies(GameObject player)
        {
            float dogRadius = agent.radius;
            foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger) continue;

                Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
                dogRadius = Mathf.Max(dogRadius, Mathf.Max(size.x, size.z));
            }

            float ownerRadius = 0.3f;
            if (player != null)
            {
                CharacterController controller = player.GetComponent<CharacterController>();
                if (controller != null) ownerRadius = controller.radius * Mathf.Max(
                    Mathf.Abs(player.transform.lossyScale.x), Mathf.Abs(player.transform.lossyScale.z));
            }

            bodyClearance = dogRadius + ownerRadius + 0.05f;
        }

        /// <summary>
        /// 주인 몸에 닿았는데 <b>그쪽으로 더 가려 하는가</b>.
        ///
        /// 그냥 가까우면 멈추게 하면, 주인이 옆에 서 있는 동안 반려견이 얼어붙는다.
        /// 멀어지는 방향은 막지 않아야 스스로 빠져나온다.
        /// </summary>
        bool CrowdingOwner(float toOwner)
        {
            if (owner == null || toOwner > bodyClearance) return false;

            Vector3 toward = owner.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.0001f) return true;   // 정확히 겹쳤다

            Vector3 desired = agent.desiredVelocity;
            desired.y = 0f;
            if (desired.sqrMagnitude < 0.0001f) return true;   // 갈 데가 없으면 그냥 선다

            return Vector3.Dot(desired.normalized, toward.normalized) > 0f;
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
                agent.speed = following ? runSpeed : walkSpeed;
                pauseTimer = 0f;
            }

            // 주인 몸에 닿았으면 그쪽으로는 더 가지 않는다. 밀고 들어가면 사람이 떠밀린다
            if (CrowdingOwner(toOwner))
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;

                // 어슬렁거리던 길이 주인에게 막혔으면 그 길을 버린다. 안 그러면 주인이
                // 비켜 줄 때까지 코를 박고 서 있는다 — 길을 풀면 다음 프레임에 다른 데를 고른다
                if (!following)
                {
                    agent.ResetPath();
                    pauseTimer = 0f;
                }
            }
            else if (following) Follow(toOwner);
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

            // <b>자기 몸은 빼고</b> 본다. 사람이 뚫고 지나가지 못하게 단단한 콜라이더를 붙였더니
            // 이 레이가 그걸 맞아 baseOffset 을 올리고, 올라간 자리에서 또 자기를 맞아
            // 강아지가 공중으로 끝없이 떠올랐다.
            RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);

            float floorY = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].point.y < floorY) floorY = hits[i].point.y;
            }
            if (floorY >= float.MaxValue) return;

            NavMeshHit nav;
            if (!NavMesh.SamplePosition(transform.position, out nav, 1f, NavMesh.AllAreas)) return;

            agent.baseOffset = floorY - nav.position.y;
        }

        void Follow(float toOwner)
        {
            if (toOwner <= heelDistance)
            {
                agent.isStopped = true;
                return;
            }

            // 너무 벌어졌으면 달리게 두지 말고 옮긴다 (위 LostRange 주석 참고)
            if (toOwner > LostRange)
            {
                Warp(owner.position - owner.forward * heelDistance);
                return;
            }

            agent.isStopped = false;
            agent.speed = runSpeed * Mathf.Clamp(toOwner / FollowRange, 1f, MaxCatchUp);

            // 주인 발밑이 아니라 주인 뒤쪽으로 간다 — 겹쳐 서면 몸이 파묻혀 보인다
            Vector3 behind = owner.position - owner.forward * heelDistance;
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

            // 다리 회전을 실제 속력에 맞춘다. 기준 속도에서 1배가 되고, 빠르면 더 빨리 돈다
            if (speed > (walkSpeed + runSpeed) * 0.5f)
            {
                animator.Play(DogAnim.Run);
                animator.SetPlaybackSpeed(speed / runSpeed);
            }
            else if (speed > 0.12f)
            {
                animator.Play(DogAnim.Walk);
                animator.SetPlaybackSpeed(speed / walkSpeed);
            }
            else
            {
                animator.SetPlaybackSpeed(1f);
                if (stats != null && stats.GrowthBlocked) animator.Play(DogAnim.Angry);
                else if (stats != null && stats.UpkeepAverage >= 80) animator.Play(DogAnim.WagTail);
                else animator.Play(DogAnim.Idle);
            }
        }
    }
}
