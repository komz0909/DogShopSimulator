using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    /// <summary>
    /// 계산대. 클릭 대상을 표시하는 마커이자, <b>대기 줄의 기준점</b>이다 —
    /// 태그·이름 비교를 쓰지 않기 위해 컴포넌트로 둔다.
    ///
    /// 줄 자리를 좌표로 박아 두지 않는 이유: 계산대를 옮길 수 있게 됐기 때문이다.
    /// 예전에는 <c>CustomerManager</c> 안에 (5.50, 0, 1.00) 같은 네 자리가 상수로 있었고,
    /// 그건 계산대가 (6.6, 0.9)에 영원히 서 있다는 전제였다. 계산대를 한 번 옮기면
    /// 손님이 허공에 줄을 서고 계산대는 저만치 떨어져 있게 된다.
    /// </summary>
    public class ShopCounter : MonoBehaviour
    {
        /// <summary>
        /// 계산대 중심에서 첫 손님까지의 거리.
        ///
        /// NavMesh가 반경 0.5로 구워져 장애물에서 0.5m가 깎인다 — 계산대 면(중심에서 0.395)에서
        /// 0.71m 떨어진 자리가 손님이 설 수 있는 가장 앞이다(몸 앞면과 계산대 사이 0.43m).
        /// 옮기기 전 좌표 (5.50)가 중심 (6.60)에서 1.10 떨어져 있던 것과 같은 값이다.
        /// </summary>
        const float FirstGap = 1.10f;

        /// <summary>줄 간격. 손님 어깨너비보다 조금 넓다.</summary>
        const float Spacing = 0.70f;

        public static ShopCounter Instance { get; private set; }

        /// <summary>
        /// 직원이 서는 쪽. 계산대 모델의 정면이 직원을 향하게 놓여 있다(씬 실측 Y 90° → +x).
        /// </summary>
        public Vector3 StaffSide => transform.forward;

        /// <summary>손님이 서는 쪽 — 직원 반대편.</summary>
        public Vector3 CustomerSide => -transform.forward;

        /// <summary>줄에 선 손님이 바라볼 방향. 계산대를 향한다.</summary>
        public Vector3 CustomerFacing => StaffSide;

        /// <summary>바닥에 내린 계산대 중심.</summary>
        public Vector3 Origin => new Vector3(transform.position.x, 0f, transform.position.z);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 줄의 <paramref name="index"/>번째 자리. 계산대 앞에서 시작해 손님 쪽으로 곧게 이어진다.
        /// 몇 명이 오든 끊기지 않게 자리 수에 상한을 두지 않는다.
        /// </summary>
        public Vector3 QueueSlot(int index)
        {
            return Origin + CustomerSide * (FirstGap + Mathf.Max(0, index) * Spacing);
        }

        /// <summary>비워 둘 줄 자리 수. 하루 손님이 가장 많을 때(Lv10 36명) 한꺼번에 서는 줄이 이 정도다.</summary>
        const int QueueLaneSlots = 8;

        /// <summary>
        /// 계산 줄이 서는 띠(계산대 앞에서 손님 쪽으로 <see cref="QueueLaneSlots"/> 자리, 폭 ±0.6m). 바닥 XZ 사각형.
        /// 여기에 가구를 세우면 줄이 끊긴다 — 57차 측정에서 Lv7 에 산 진열대 둘이 줄 위에 서서 하루 손님 27명 중 26명을 놓쳤다.
        /// </summary>
        public Rect QueueLane
        {
            get
            {
                Vector3 a = QueueSlot(0), b = QueueSlot(QueueLaneSlots - 1);
                const float Half = 0.6f;
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - Half, Mathf.Min(a.z, b.z) - Half,
                                       Mathf.Max(a.x, b.x) + Half, Mathf.Max(a.z, b.z) + Half);
            }
        }

        /// <summary>직원 구역이 계산대 중심에서 직원 쪽으로 시작하는 거리와 끝나는 거리, 계산대 양옆 여유.</summary>
        const float StaffZoneNear = 0.4f, StaffZoneFar = 3.0f, StaffZoneHalfWidth = 1.2f;

        /// <summary>
        /// 계산대 뒤 직원 구역(주인이 서서 계산하는 쪽). 바닥 XZ 사각형.
        /// 여기에 진열대를 세우면 손님이 물건을 집으러 계산대 뒤로 비집고 들어오다 막힌다 —
        /// 66차 측정에서 봇이 계산대 오른쪽 앞벽에 벽 진열대를 세워 하루 1~2명씩 21명을 놓쳤다.
        /// </summary>
        public Rect StaffZone
        {
            get
            {
                Vector3 across = Vector3.Cross(Vector3.up, StaffSide).normalized * StaffZoneHalfWidth;
                Vector3 near = Origin + StaffSide * StaffZoneNear, far = Origin + StaffSide * StaffZoneFar;
                Vector3 p0 = near + across, p1 = near - across, p2 = far + across, p3 = far - across;
                return Rect.MinMaxRect(Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x)),
                                       Mathf.Min(Mathf.Min(p0.z, p1.z), Mathf.Min(p2.z, p3.z)),
                                       Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x)),
                                       Mathf.Max(Mathf.Max(p0.z, p1.z), Mathf.Max(p2.z, p3.z)));
            }
        }

        /// <summary>놓을 수 있는지 볼 때 확인하는 줄 자리 수.</summary>
        const int RoomSlots = 3;

        /// <summary>
        /// 자리가 이만큼 넘게 끌려오면 설 자리가 없는 것으로 본다.
        /// 줄 간격(0.70)의 절반보다 작아야 두 사람이 한 자리에 겹치는 것을 걸러낸다.
        /// </summary>
        const float RoomTolerance = 0.3f;

        /// <summary>
        /// 거기에 놓으면 줄이 설 수 있는가. <b>계산대를 벽에 대고 벽을 보게</b> 놓으면
        /// 줄 자리가 벽 안으로 들어간다.
        ///
        /// <see cref="CustomerManager.RefreshQueue"/>가 자리를 NavMesh 위로 끌어다 놓기는 하지만,
        /// 공간이 모자라면 여러 자리가 <b>같은 곳으로 끌려와</b> 손님이 겹쳐 선다.
        /// 끌려온 거리를 재서, 자리가 제자리에 못 잡히면 놓지 못하게 한다.
        ///
        /// 놓기 전이라 NavMesh 는 아직 다시 굽기 전이다. 벽과 바닥은 그대로이므로
        /// 막히는 경우는 이것으로 다 걸리고, 계산대 자신의 두께만큼은 느슨하게 본다.
        /// </summary>
        public static bool HasQueueRoom(Vector3 origin, Quaternion rotation, out string reason)
        {
            Vector3 customerSide = rotation * Vector3.back;
            Vector3 ground = new Vector3(origin.x, 0f, origin.z);

            for (int i = 0; i < RoomSlots; i++)
            {
                Vector3 want = ground + customerSide * (FirstGap + i * Spacing);

                NavMeshHit hit;
                if (NavMesh.SamplePosition(want, out hit, RoomTolerance, Customer.WalkableAreas)) continue;

                reason = "계산대 앞에 줄 설 자리가 없다: " + (i + 1) + "번째가 막힌다";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
