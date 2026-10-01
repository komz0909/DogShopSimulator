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

                reason = "계산대 앞에 줄 설 자리가 없다 — " + (i + 1) + "번째가 막힌다";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
