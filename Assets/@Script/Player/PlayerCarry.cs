using System;
using DogShop.Shop;
using UnityEngine;


namespace DogShop.Player
{
    /// <summary>
    /// 상자를 주워 들고 다니고 내려놓는다. 상자는 월드 오브젝트이므로 어디에 두었는지가 기억된다.
    /// 진열이 메뉴 클릭이 아니라 <b>동선</b>이 되므로 가게가 넓어지는 것이 실제 비용이 된다.
    ///
    /// 줍기·내려놓기 입력은 PlayerInteraction이 처리한다(마우스 조준 + E).
    /// </summary>
    public class PlayerCarry : MonoBehaviour
    {
        /// <summary>상자를 들면 이 비율로 느려진다.</summary>
        const float LoadedSpeedFactor = 0.82f;

        /// <summary>
        /// 상자 자리(몸 기준). 상자 피벗은 바닥 가운데라 y 는 상자 밑면.
        /// 손은 IK 로 맞추지 않는다(해 봤더니 손이 뒤집혀 두 번 뺐다). 대신 <b>들고 있는 자세 클립</b>(Owner_Carry, 팔만 덮는
        /// Carry 층 — <see cref="CharacterAnimatorDriver"/>)이 양손을 상자 옆면 아래쪽에 고정해 두고, 상자를 그 손 사이에 놓는다.
        /// 자세는 이 자리에 맞춰 구운 것이라 둘을 같이 옮겨야 한다. 팔이 짧아(어깨→손 약 0.41m) 예전 자리 (0, 0.72, 0.45)는
        /// 손이 닿지 않아 상자가 떠 보였다.
        /// </summary>
        [SerializeField] Vector3 holdOffset = new Vector3(0f, 0.58f, 0.36f);

        Transform holdAnchor;
        CharacterController controller;

        public CarryCrate Held { get; private set; }

        public bool IsHolding => Held != null;
        public float SpeedFactor => IsHolding ? LoadedSpeedFactor : 1f;

        public event Action OnChanged;

        void Awake()
        {
            controller = GetComponent<CharacterController>();

            GameObject anchor = new GameObject("CarryAnchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = holdOffset;
            holdAnchor = anchor.transform;
        }

        // ---- 상자가 가슴을 따라간다 ----
        //
        // 걷기 클립은 상체를 좌우로 비튼다. 손(들기 자세)은 어깨에 달려 그 비틀림을 따라가는데 상자는 몸통(캡슐)에 고정돼 있어,
        // 걸음마다 손이 상자 옆면 안으로 3cm 파묻혔다가 5cm 떨어졌다. 상자 자리를 가슴 뼈 기준으로 옮겨 손과 같이 움직이게 한다.
        // 가슴 뼈에 직접 붙이지 않는 건 뼈 배율(173)이 상자 크기까지 키우기 때문이다 — 자리만 매 프레임 따라간다.

        Animator animator;
        Transform chest, leftHand, rightHand;
        int carryLayer = -1;
        Vector3 chestOffsetPos;
        Quaternion chestOffsetRot = Quaternion.identity;
        /// <summary>두 손 사이에 상자를 맞춰 가슴 기준 자리를 잡았나. 들기 자세는 가슴에 대해 고정이라 한 번이면 된다.</summary>
        bool calibrated;

        /// <summary>손이 상자 밑면보다 이만큼 위, 상자 가운데보다 이만큼 뒤를 받친다(들기 자세를 구울 때의 자리).</summary>
        const float HandAboveBottom = 0.14f;
        const float HandBehindCenter = 0.11f;

        void Start()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            carryLayer = animator.GetLayerIndex("Carry");
        }

        void LateUpdate()
        {
            if (chest == null || !IsHolding) return;
            if (!calibrated)
            {
                // 들기 자세가 다 들어간 첫 프레임에 두 손 사이 가운데로 상자를 맞추고, 그 자리를 가슴 기준으로 기억한다
                if (carryLayer < 0 || leftHand == null || rightHand == null || animator.GetLayerWeight(carryLayer) < 0.999f) return;
                Vector3 across = rightHand.position - leftHand.position;
                across.y = 0f;
                if (across.sqrMagnitude < 0.0001f) return;
                Vector3 forward = Vector3.Cross(across.normalized, Vector3.up);
                Vector3 mid = (leftHand.position + rightHand.position) * 0.5f;
                Vector3 bottom = mid + forward * HandBehindCenter - Vector3.up * HandAboveBottom;
                chestOffsetPos = chest.InverseTransformPoint(bottom);
                chestOffsetRot = Quaternion.Inverse(chest.rotation) * Quaternion.LookRotation(forward, Vector3.up);
                calibrated = true;
            }
            holdAnchor.SetPositionAndRotation(chest.TransformPoint(chestOffsetPos), chest.rotation * chestOffsetRot);
        }


        public string Describe() => IsHolding ? Held.Describe() : "빈손";

        public bool PickUp(CarryCrate crate)
        {
            if (crate == null || IsHolding) return false;

            Held = crate;
            crate.transform.SetParent(holdAnchor, false);
            crate.transform.localPosition = Vector3.zero;
            crate.transform.localRotation = Quaternion.identity;

            SetPhysicsActive(crate, false);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 조준한 지점에 내려놓는다. 바닥 높이는 레이캐스트로 맞춘다.
        /// 그 자리가 벽·가구와 겹치거나 벽 너머면 가까운 빈 바닥으로 옮겨 놓고, 빈 곳이 없으면 놓지 않는다(false) —
        /// 예전엔 맞은 점에 그대로 놓아 상자가 벽을 뚫거나 가구에 박혔다(2026-11-04).
        /// </summary>
        public bool DropAt(Vector3 position)
        {
            if (!IsHolding) return false;

            Vector3 spot;
            if (!FindDropSpot(Held, position, out spot)) return false;
            position = spot;

            CarryCrate crate = Held;
            Held = null;

            // 씬 루트가 아니라 CrateManager 밑으로 돌려놓는다 — 루트에 관리 대상을 남기지 않는다
            Transform owner = CrateManager.Instance != null ? CrateManager.Instance.transform : null;
            crate.transform.SetParent(owner, true);

            Vector3 target = position;
            target.y = GroundHeight(target);

            crate.transform.position = target;
            crate.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            SetPhysicsActive(crate, true);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 위치를 건드리지 않고 손에서만 뗀다. 세이브 복원용 —
        /// Drop 계열은 바닥으로 옮기기 때문에 복원 중에 쓰면 저장된 위치를 잃는다.
        /// </summary>
        public void ForceRelease()
        {
            if (Held == null) return;

            CarryCrate released = Held;
            Held = null;
            SetPhysicsActive(released, true);
            OnChanged?.Invoke();
        }

        /// <summary>들고 있는 동안에는 상자 콜라이더를 끈다 — 플레이어 자신과 부딪히지 않게.</summary>
        static void SetPhysicsActive(CarryCrate crate, bool active)
        {
            foreach (Collider collider in crate.GetComponentsInChildren<Collider>(true))
                collider.enabled = active;
        }

        static readonly Collider[] overlapHits = new Collider[16];

        /// <summary>바닥으로 치는 높이(매장·창고 바닥 y=0). 이보다 높으면 가구 위다.</summary>
        const float FloorTolerance = 0.06f;

        /// <summary>
        /// 상자를 놓을 빈 바닥을 찾는다. 조준점부터 보고, 막혔으면 주인 쪽으로 당기며, 그래도 안 되면 둘레를 돈다.
        /// 빈 곳 = 바닥 높이이고, 상자 크기 상자 안에 벽·가구·다른 상자가 없고, 주인 몸에서 그 자리까지 막힘이 없다(벽 너머가 아니다).
        /// </summary>
        bool FindDropSpot(CarryCrate crate, Vector3 aim, out Vector3 spot)
        {
            // 상자 크기 — 들고 있는 동안 콜라이더는 꺼져 있으므로 그림 크기로 잰다. 돌려 놓아도 맞게 정사각으로 본다
            Bounds b = new Bounds(crate.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in crate.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float side = any ? Mathf.Max(b.extents.x, b.extents.z) * 0.92f : 0.25f;
            float halfH = any ? Mathf.Max(0.08f, b.extents.y * 0.9f) : 0.15f;
            var half = new Vector3(side, halfH, side);

            Vector3 me = transform.position; me.y = aim.y;
            Vector3 toMe = me - aim;
            float back = toMe.magnitude;
            Vector3 dirToMe = back > 0.001f ? toMe / back : Vector3.zero;

            // 1) 조준점  2) 주인 쪽으로 15cm 씩  3) 조준점 둘레 두 바퀴
            if (TrySpot(aim, half, out spot)) return true;
            for (float d = 0.15f; d < back - 0.3f; d += 0.15f)
                if (TrySpot(aim + dirToMe * d, half, out spot)) return true;
            for (int ring = 1; ring <= 2; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 0.25f;
                    if (TrySpot(aim + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.22f * ring), half, out spot)) return true;
                }
            spot = aim;
            return false;
        }

        bool TrySpot(Vector3 at, Vector3 half, out Vector3 spot)
        {
            spot = at;
            spot.y = GroundHeight(at);
            if (spot.y > FloorTolerance) return false;   // 가구 위(또는 주인 몸 위)

            Vector3 center = spot + Vector3.up * (half.y + 0.04f);   // 바닥 면은 안 닿게 살짝 띄운다
            int n = Physics.OverlapBoxNonAlloc(center, half, overlapHits, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = overlapHits[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;   // 주인 몸은 상관없다
                return false;
            }

            // 벽 너머가 아닌가 — 주인 허리에서 그 자리까지 막힘이 없어야 한다
            RaycastHit hit;
            Vector3 from = transform.position + Vector3.up * 0.5f;
            if (Physics.Linecast(from, center, out hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                return false;
            return true;
        }

        float GroundHeight(Vector3 position)
        {
            RaycastHit hit;
            Vector3 origin = position + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return controller != null ? transform.position.y - controller.height * 0.5f : 0f;
        }
    }
}
