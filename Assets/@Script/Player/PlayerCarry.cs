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

        [SerializeField] Vector3 holdOffset = new Vector3(0f, 0.95f, 0.42f);

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

        /// <summary>조준한 지점에 내려놓는다. 바닥 높이는 레이캐스트로 맞춘다.</summary>
        public bool DropAt(Vector3 position)
        {
            if (!IsHolding) return false;

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
