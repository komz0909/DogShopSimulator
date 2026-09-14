using DogShop.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Player
{
    /// <summary>
    /// 플레이어의 자식 피벗에 붙는다. 리그 하나로 1인칭·3인칭을 모두 낸다 — <b>V</b>로 전환한다.
    ///
    /// 1인칭 — 커서를 잠그고 버튼 없이 마우스룩. 조준은 화면 중앙.
    /// 3인칭 — 커서를 살려두고 오른쪽 버튼을 누른 채 끌어 회전. 조준은 마우스 위치.
    ///
    /// 조준 좌표는 PointerMenus.PickPosition()이 커서 잠금 상태를 보고 알아서 고르므로
    /// 상호작용 코드(PlayerInteraction)는 시점을 몰라도 된다.
    /// 메뉴가 열려 있는 동안에는 회전을 멈추고 커서를 풀어준다 — OnGUI 메뉴를 눌러야 하므로.
    ///
    /// 클래스 이름은 씬 참조가 걸려 있어 그대로 둔다.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        /// <summary>기본은 3인칭. 1인칭은 게임 씬이 완성된 뒤에 판단한다.</summary>
        [SerializeField] bool firstPerson = false;

        [Header("3인칭")]
        [SerializeField] float distance = 4.5f;
        [SerializeField] float minDistance = 1.2f;
        [SerializeField] float pivotHeight = 1.5f;

        [Header("1인칭")]
        /// <summary>눈높이. 좁은 가게에서 답답하지 않게 시야각을 넓힌다.</summary>
        [SerializeField] float eyeHeight = 1.62f;
        [SerializeField] float firstPersonFov = 72f;

        [Header("감도")]
        /// <summary>3인칭 드래그용. 버튼을 누른 채 끌 때만 돌기 때문에 커도 된다.</summary>
        [SerializeField] float sensitivity = 0.18f;

        /// <summary>
        /// 1인칭용. 드래그가 아니라 마우스를 움직이는 내내 계속 돌기 때문에
        /// 같은 값이면 3인칭보다 훨씬 빠르게 느껴진다 — 멀미의 원인.
        /// </summary>
        [SerializeField] float firstPersonSensitivity = 0.06f;

        /// <summary>pitch는 <b>양수가 아래</b>다(Quaternion.Euler의 X축). 3인칭용 한계.</summary>
        [SerializeField] float minPitch = -20f;
        [SerializeField] float maxPitch = 70f;

        /// <summary>
        /// 1인칭은 발밑 상자를 봐야 한다. 캡슐 반지름 0.3 때문에 바닥 상자에 0.46m까지만
        /// 붙을 수 있고 그 거리에서 상자를 보려면 75°를 내려다봐야 하는데,
        /// 3인칭 한계 70°로는 닿지 않아 상자를 조준할 수 없었다.
        /// </summary>
        const float FirstPersonMaxPitch = 88f;

        /// <summary>1인칭은 높은 선반도 올려다봐야 한다.</summary>
        const float FirstPersonMinPitch = -70f;

        Camera cam;
        Transform camT;
        Renderer[] body;
        float baseFov;
        float yaw;
        float pitch = 20f;

        /// <summary>Esc로 커서를 풀어둔 상태. 게임 화면을 다시 클릭하면 잠긴다.</summary>
        bool looking = true;

        public bool IsFirstPerson => firstPerson;

        void Awake()
        {
            cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                camT = cam.transform;
                baseFov = cam.fieldOfView;
            }

            // 운반 상자를 잡기 전에 캡처하므로 몸통 렌더러만 잡힌다(상자는 나중에 자식으로 붙는다).
            Transform root = transform.parent != null ? transform.parent : transform;
            body = root.GetComponentsInChildren<Renderer>(true);

            yaw = transform.eulerAngles.y;
            ApplyMode();
        }

        void OnDisable() => Cursor.lockState = CursorLockMode.None;

        void LateUpdate()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.vKey.wasPressedThisFrame)
            {
                firstPerson = !firstPerson;
                ApplyMode();
            }

            SyncCursor();

            if (!PointerMenus.AnyOpen) ApplyLook();

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (camT == null) return;

            Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
            camT.rotation = look;

            if (firstPerson)
            {
                camT.position = transform.position + Vector3.up * eyeHeight;
                return;
            }

            Vector3 pivot = transform.position + Vector3.up * pivotHeight;
            Vector3 dir = look * Vector3.back;

            float applied = distance;
            if (Physics.SphereCast(pivot, 0.25f, dir, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
                applied = Mathf.Max(minDistance, hit.distance - 0.1f);

            camT.position = pivot + dir * applied;
        }

        /// <summary>시점을 바꿀 때만 하는 일 — 시야각, 몸통 표시, 피치 재클램프.</summary>
        void ApplyMode()
        {
            if (cam != null) cam.fieldOfView = firstPerson ? firstPersonFov : baseFov;

            // 1인칭에서는 캡슐이 화면을 가린다. 플레이어 모델이 생기면 머리만 끄면 된다.
            for (int i = 0; i < body.Length; i++)
                if (body[i] != null) body[i].enabled = !firstPerson;

            pitch = Mathf.Clamp(pitch, MinPitch(), MaxPitch());
        }

        float MinPitch() => firstPerson ? FirstPersonMinPitch : minPitch;
        float MaxPitch() => firstPerson ? FirstPersonMaxPitch : maxPitch;

        /// <summary>
        /// 1인칭은 커서를 잠근다. 메뉴가 열리면 눌러야 하므로 풀어준다.
        /// Esc로 풀 수 있어야 한다 — 매 프레임 다시 잠그면 에디터 게임뷰에 커서가 갇힌다.
        /// 창을 벗어나도(alt-tab) 풀린다.
        /// </summary>
        void SyncCursor()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) looking = false;
            else if (!looking && mouse != null && mouse.leftButton.wasPressedThisFrame) looking = true;

            bool locked = firstPerson && looking && Application.isFocused && !PointerMenus.AnyOpen;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>1인칭은 버튼 없이, 3인칭은 오른쪽 버튼을 누른 채 끌 때만 돈다.</summary>
        void ApplyLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            if (!firstPerson && !mouse.rightButton.isPressed) return;
            if (firstPerson && Cursor.lockState != CursorLockMode.Locked) return;

            float s = firstPerson ? firstPersonSensitivity : sensitivity;
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * s;
            pitch = Mathf.Clamp(pitch - delta.y * s, MinPitch(), MaxPitch());
        }
    }
}
