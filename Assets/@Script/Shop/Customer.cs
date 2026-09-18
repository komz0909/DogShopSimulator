using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    public enum CustomerState { ToShelf, ToCounter, Waiting, ToExit }

    /// <summary>
    /// 손님 1명. 이동만 담당하고 상태 전이는 CustomerManager가 몰아서 처리한다
    /// (상점 로직이 한 곳에 모여 있어야 밸런싱이 쉽다).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Customer : MonoBehaviour
    {
        const float ArriveRadius = 0.6f;

        /// <summary>충돌 캡슐 반경. NavMesh가 이 값으로 구워져 있으므로 키와 무관하게 고정한다.</summary>
        const float BodyRadius = 0.28f;

        /// <summary>머리 위 이만큼에 가격표를 띄운다.</summary>
        const float LabelClearance = 0.35f;

        /// <summary>대기 자세로 돌아설 때의 회전 속도(도/초).</summary>
        const float TurnSpeed = 360f;

        NavMeshAgent agent;
        float baseSpeed;
        float baseAcceleration;
        float baseAngularSpeed;
        Vector3 destination;
        Vector3 facing;

        public CustomerState State { get; set; }
        public int WantedProduct { get; set; } = -1;
        public bool HasItem { get; set; }
        /// <summary>인내 시간이 남은 만큼. 손님은 떠나지 않으므로 이 값은 음수까지 내려간다.</summary>
        public float WaitRemaining { get; set; }

        /// <summary>인내 시간을 넘겨 기다린 초. 할인율과 명성 손실이 여기서 나온다.</summary>
        public float Overtime { get { return Mathf.Max(0f, -WaitRemaining); } }
        public string Label { get; set; } = "";

        /// <summary>겉모습마다 키가 달라 라벨 높이도 따라간다 — 아이 머리 위 2.1m는 너무 높다.</summary>
        public float LabelHeight { get; private set; } = 2.1f;

        /// <summary>이동 상태에 머문 시간. 길이 막혔을 때 큐를 영구 점유하지 않게 하는 안전장치.</summary>
        public float TravelTime { get; set; }

        /// <summary>
        /// 에이전트의 remainingDistance는 경로 계산 전에 0을 돌려주는 구간이 있어
        /// 도착 판정에 쓰면 문 앞에서 즉시 "도착"해버린다. 실제 평면 거리로 판정한다.
        /// </summary>
        public bool Arrived
        {
            get
            {
                Vector3 delta = transform.position - destination;
                delta.y = 0f;
                return delta.sqrMagnitude <= ArriveRadius * ArriveRadius;
            }
        }

        /// <summary>
        /// 창고·옆방에 깔린 NavMesh 영역. 예전에는 그곳에 NavMesh를 아예 굽지 않아
        /// 손님을 막았는데, 그러면 <b>반려견도 못 들어간다.</b> 걸을 수 있게 굽고
        /// 손님만 마스크로 뺀다 — 앞으로 직원·배달 NPC가 생겨도 같은 방식으로 쓴다.
        /// </summary>
        public const int StaffArea = 3;

        /// <summary>손님이 다닐 수 있는 영역. 직원 구역만 빠진다.</summary>
        public static int WalkableAreas => NavMesh.AllAreas & ~(1 << StaffArea);

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.areaMask = WalkableAreas;
            baseSpeed = agent.speed;
            baseAcceleration = agent.acceleration;
            baseAngularSpeed = agent.angularSpeed;
            destination = transform.position;
        }

        /// <summary>
        /// 겉모습을 붙인다. 손님 프리팹은 이동 로직만 들고 있고 몸은 런타임에 고른다 —
        /// 7종을 각각 프리팹으로 복제하면 이동 파라미터 하나 고칠 때마다 7군데를 고쳐야 한다.
        ///
        /// 캡슐과 라벨 높이는 붙인 모델을 <b>실측해서</b> 맞춘다. 아이(1.2m)와 어른(1.8m)이
        /// 같은 캡슐을 쓰면 아이는 허공을 클릭해야 계산이 된다.
        /// </summary>
        public void SetAppearance(GameObject modelPrefab)
        {
            if (modelPrefab == null) return;

            GameObject model = Instantiate(modelPrefab, transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            float height = MeasureHeight(model);
            LabelHeight = height + LabelClearance;

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.radius = BodyRadius;
                capsule.height = height;
                capsule.center = new Vector3(0f, height * 0.5f, 0f);
            }

            if (agent != null) agent.height = height;

            // 모델을 붙인 뒤에 달아야 Awake에서 Animator를 찾는다
            gameObject.AddComponent<DogShop.Player.CharacterAnimatorDriver>();
        }

        /// <summary>
        /// SkinnedMeshRenderer.bounds는 바인드 포즈 기준이라 실제 몸보다 크게 나온다.
        /// 현재 포즈를 구워서 정점 최고/최저를 직접 잰다.
        /// </summary>
        static float MeasureHeight(GameObject model)
        {
            float low = float.MaxValue;
            float high = float.MinValue;

            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh baked = new Mesh();
                skin.BakeMesh(baked, true);

                foreach (Vector3 vertex in baked.vertices)
                {
                    float y = skin.transform.TransformPoint(vertex).y - model.transform.position.y;
                    if (y < low) low = y;
                    if (y > high) high = y;
                }

                Destroy(baked);
            }

            return high > low ? high - low : 1.7f;
        }

        /// <summary>
        /// 대기 중에 바라볼 방향. NavMeshAgent는 멈추면 회전을 놓기 때문에
        /// 이게 없으면 줄이 제각각 딴 데를 보고 서 있다.
        /// </summary>
        public void FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            facing = direction.normalized;
        }

        void Update()
        {
            if (facing.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), TurnSpeed * Time.deltaTime);
        }

        public void MoveTo(Vector3 target)
        {
            destination = target;
            TravelTime = 0f;
            facing = Vector3.zero;   // 다시 걷기 시작하면 에이전트가 회전을 가져간다
            if (agent == null || !agent.isOnNavMesh) return;
            agent.SetDestination(target);
        }

        /// <summary>
        /// 배속을 따라간다 — 다만 <b>상한이 있다</b>.
        ///
        /// 16배속에서 속도를 그대로 16배(28.8m/s)로 주면 한 프레임에 0.48m 를 건너뛴다.
        /// 손님 반경이 0.28 이라 자기 몸통 두 개씩 순간이동하는 셈이고,
        /// NavMeshAgent 의 회피와 도착 판정이 그 단위에서 무너진다 —
        /// 계산대를 뚫고 지나가고 손실률이 11%에서 21%로 뛰었다(10차 측정).
        ///
        /// 시계는 16배로 가되 사람은 이 배수까지만 빨라진다. 가게가 6m 남짓이라
        /// 5배(9m/s)면 한 시간(16배속 기준 3.3초) 안에 충분히 오간다.
        /// </summary>
        const int MaxSpeedMultiplier = 5;

        public void ApplySpeed(int multiplier)
        {
            if (agent == null) return;

            int scaled = Mathf.Min(multiplier, MaxSpeedMultiplier);
            agent.speed = baseSpeed * scaled;
            agent.acceleration = baseAcceleration * scaled;
            agent.angularSpeed = baseAngularSpeed * scaled;
        }
    }
}
