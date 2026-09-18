using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    /// <summary>
    /// 플레이어가 들어서 옮길 수 있는 가구. 진열대·창고 선반·계산대에 붙는다.
    ///
    /// 배치 판정에 쓰는 <b>발자국</b>을 자기 콜라이더에서 한 번 재어 들고 있는다.
    /// 회전하면 발자국도 같이 도는데, 그건 월드 회전을 곱해서 그때그때 계산한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlaceableFurniture : MonoBehaviour
    {
        /// <summary>손님이 설 자리를 찾을 때 가구 표면에서 이만큼 떨어진 곳을 본다.</summary>
        public const float ApproachClearance = 0.65f;

        /// <summary>겹침 판정에 이 값을 빼서 잰다 — 딱 붙여 놓는 것까지 막으면 답답하다.</summary>
        const float OverlapSlack = 0.04f;

        [SerializeField] string displayName = "가구";

        /// <summary>
        /// 벽걸이형인가. 켜면 아무 데나 놓이지 않고 <b>가장 가까운 벽을 찾아 등을 붙인다</b>.
        /// 뒤판이 평면인 가구를 매장 한가운데 세워 두면 뒤가 뻥 뚫려 보인다.
        /// </summary>
        [SerializeField] bool wallMounted;

        /// <summary>벽을 찾는 반경. 이보다 먼 곳을 조준하면 붙일 벽이 없다고 본다.</summary>
        public const float WallSearchRadius = 1.6f;

        Collider body;
        NavMeshObstacle obstacle;
        Vector3 localCenter;
        Vector3 halfExtents;

        public string DisplayName => displayName;
        public bool WallMounted => wallMounted;

        /// <summary>바닥 기준 높이. 내려놓을 때 발자국이 바닥에 닿게 맞추는 데 쓴다.</summary>
        public float BottomOffset { get; private set; }

        void Awake()
        {
            body = GetComponentInChildren<Collider>();
            if (body == null) return;

            // Collider.bounds 는 <b>월드 축에 정렬된</b> 상자라 회전이 섞여 들어온다.
            // 벽에 붙이려고 90도 돌려 세운 진열대는 폭과 깊이가 뒤바뀐 값이 나왔고
            // (1.17 x 0.40 를 0.40 x 1.17 로), 그래서 다시 붙일 때 깊이의 절반이 아니라
            // 폭의 절반만큼 밀려나 벽에서 38cm 떠서 놓였다.
            // 회전을 잠깐 풀고 재면 회전과 무관한 진짜 크기가 나온다.
            Quaternion spin = transform.rotation;
            transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();

            Bounds bounds = body.bounds;
            localCenter = transform.InverseTransformPoint(bounds.center);   // 반드시 회전을 푼 상태에서

            transform.rotation = spin;
            Physics.SyncTransforms();

            // lossyScale 을 되돌려 크기를 로컬 값으로 환산한다
            Vector3 scale = transform.lossyScale;
            halfExtents = new Vector3(
                bounds.extents.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                bounds.extents.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                bounds.extents.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));

            BottomOffset = transform.position.y - bounds.min.y;

            EnsureObstacle(bounds);
        }

        /// <summary>
        /// 손님이 가구를 통과하지 못하게 NavMesh를 깎는다. 프리팹에 없으면 여기서 붙인다 —
        /// <b>앞으로 추가될 가구도 이 컴포넌트만 달면 자동으로 막힌다.</b>
        /// 구운 NavMesh를 고치는 게 아니라 실시간 카빙이라 옮겨도 따라온다.
        /// </summary>
        void EnsureObstacle(Bounds bounds)
        {
            obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();

            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = localCenter;

            Vector3 s = transform.lossyScale;
            obstacle.size = new Vector3(
                bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(s.z)));

            obstacle.carving = true;
        }

        /// <summary>들고 다니는 동안에는 카빙을 끈다 — 매 프레임 NavMesh를 다시 깎을 이유가 없다.</summary>
        public void SetCarving(bool on)
        {
            if (obstacle != null) obstacle.enabled = on;
        }

        /// <summary>주어진 위치·회전에 놓았을 때 발자국의 월드 중심.</summary>
        public Vector3 CenterAt(Vector3 position, Quaternion rotation)
        {
            Vector3 scaled = Vector3.Scale(localCenter, transform.lossyScale);
            return position + rotation * scaled;
        }

        /// <summary>여유를 빼지 않은 진짜 절반 크기. 벽에 붙일 거리를 잴 때 쓴다.</summary>
        public Vector3 RawHalfExtents
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return new Vector3(
                    halfExtents.x * Mathf.Abs(s.x),
                    halfExtents.y * Mathf.Abs(s.y),
                    halfExtents.z * Mathf.Abs(s.z));
            }
        }

        /// <summary>겹침 판정용 절반 크기. 여유만큼 줄여 딱 붙이는 것을 허용한다.</summary>
        public Vector3 HalfExtents
        {
            get
            {
                return RawHalfExtents - Vector3.one * OverlapSlack;
            }
        }

        /// <summary>
        /// 그 자리에 놓을 수 있는가. 벽·다른 가구·사람과 겹치면 안 된다.
        /// 자기 자신과 들고 있는 플레이어는 제외한다.
        /// </summary>
        public bool Fits(Vector3 position, Quaternion rotation, Transform holder, out string reason)
        {
            Vector3 center = CenterAt(position, rotation);
            Collider[] hits = Physics.OverlapBox(center, HalfExtents, rotation, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Transform t = hits[i].transform;
                if (t.IsChildOf(transform)) continue;
                if (holder != null && t.IsChildOf(holder)) continue;

                // 바닥은 발자국 아래로 깔려 있으니 당연히 걸린다
                if (hits[i].GetComponentInParent<PlaceableFurniture>() == null
                    && hits[i].bounds.max.y <= position.y + 0.02f) continue;

                reason = hits[i].name + " 와 겹친다";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 조준한 바닥 근처에서 가장 가까운 벽을 찾아, 등을 붙였을 때의 위치와 회전을 돌려준다.
        ///
        /// 벽을 직접 레이캐스트로 때리지 않고 <b>여덟 방향으로 쏘아</b> 가장 가까운 면을 고른다 —
        /// 플레이어가 벽을 정확히 조준할 필요 없이 벽 앞 바닥만 대충 보면 된다.
        /// </summary>
        public bool TrySnapToWall(Vector3 floorPoint, out Vector3 position, out Quaternion rotation)
        {
            position = floorPoint;
            rotation = transform.rotation;

            float probeHeight = floorPoint.y + Mathf.Max(0.35f, HalfExtents.y);
            Vector3 origin = new Vector3(floorPoint.x, probeHeight, floorPoint.z);

            float nearest = float.MaxValue;
            Vector3 hitPoint = Vector3.zero;
            Vector3 hitNormal = Vector3.zero;

            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                RaycastHit hit;
                if (!Physics.Raycast(origin, dir, out hit, WallSearchRadius, ~0, QueryTriggerInteraction.Ignore)) continue;

                // 벽만 본다. 사람·가구·상자에 등을 붙이면 안 된다 —
                // 실측에서 매장 한가운데를 조준했을 때 근처에 서 있던 플레이어에게 붙었다.
                if (hit.collider.GetComponentInParent<PlaceableFurniture>() != null) continue;
                if (hit.collider.GetComponentInParent<CharacterController>() != null) continue;
                if (hit.collider.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null) continue;
                if (hit.collider.attachedRigidbody != null) continue;
                if (Mathf.Abs(hit.normal.y) > 0.3f) continue;   // 바닥·천장 제외
                if (hit.distance >= nearest) continue;

                nearest = hit.distance;
                hitPoint = hit.point;
                hitNormal = hit.normal;
            }

            if (nearest == float.MaxValue) return false;

            // 벽 법선이 매장 안쪽을 향하므로 가구의 <b>정면</b>이 그 방향을 보게 한다
            Vector3 normal = new Vector3(hitNormal.x, 0f, hitNormal.z).normalized;
            rotation = Quaternion.LookRotation(normal, Vector3.up);

            // 벽을 따라가는 위치는 <b>조준점을 그대로 쓰고</b>, 벽에서 떨어진 거리만 고친다.
            // 히트 지점을 그대로 쓰면 가구가 광선이 닿은 자리로 끌려가 문틀 안으로 들어갔다.
            // 원점이 발자국 한가운데가 아닌 가구(뒤판만 있는 선반 따위)도 있으므로
            // 절반 깊이에서 중심이 밀린 만큼을 뺀다 — 기준은 원점이 아니라 <b>뒷면</b>이다.
            float centerZ = Vector3.Scale(localCenter, transform.lossyScale).z;
            float gap = Vector3.Dot(floorPoint - hitPoint, normal);
            position = floorPoint + normal * (RawHalfExtents.z - centerZ - gap);
            position.y = floorPoint.y + BottomOffset;

            return true;
        }

        /// <summary>
        /// 손님이 설 자리를 가구 둘레에서 찾는다. 자유 배치라 통로가 어디 생길지 미리 알 수 없으므로
        /// <b>여덟 방향을 실제로 NavMesh에 물어본다</b>. 입구에 가까운 쪽을 먼저 본다.
        /// </summary>
        public Vector3 FindApproach(Vector3 entrance)
        {
            Vector3 center = CenterAt(transform.position, transform.rotation);
            center.y = 0f;

            float reach = Mathf.Max(HalfExtents.x, HalfExtents.z) + ApproachClearance;

            Vector3 best = center;
            float bestScore = float.MaxValue;

            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 candidate = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * reach;

                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidate + Vector3.up * 0.3f, out hit, 0.4f, Customer.WalkableAreas)) continue;
                if (Mathf.Abs(hit.position.x - candidate.x) > 0.3f || Mathf.Abs(hit.position.z - candidate.z) > 0.3f) continue;

                float score = Vector3.Distance(candidate, entrance);
                if (score >= bestScore) continue;

                bestScore = score;
                best = new Vector3(candidate.x, 0f, candidate.z);
            }

            return best;
        }
    }
}
