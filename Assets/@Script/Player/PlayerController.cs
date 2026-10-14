using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Player
{
    /// <summary>카메라 피벗 기준 WASD 이동. 피벗은 자식에서 GetComponent로 찾는다.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 3.2f;
        [SerializeField] float turnSpeed = 14f;
        [SerializeField] float gravity = -18f;

        CharacterController controller;
        ThirdPersonCamera camPivot;
        PlayerCarry carry;
        float verticalVelocity;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            carry = GetComponent<PlayerCarry>();
            ThirdPersonCamera pivot = GetComponentInChildren<ThirdPersonCamera>();
            camPivot = pivot;
        }

        void Update()
        {
            Vector2 input = ReadMove();

            // 카메라 yaw 로 앞을 정한다. 예전엔 피벗(플레이어 자식)의 forward 를 읽었는데, 몸이 방향을 트는 동안
            // 피벗도 같이 돌아 앞이 흔들렸고 — 좌우로만 눌러도 몸이 도는 만큼 길이 휘어 타원을 그리며 갔다
            Vector3 forward = camPivot != null ? Quaternion.Euler(0f, camPivot.Yaw, 0f) * Vector3.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 move = right * input.x + forward * input.y;
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (move.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            verticalVelocity = controller.isGrounded ? -1f : verticalVelocity + gravity * Time.deltaTime;

            // 상자를 들면 느려진다 — 나르는 것이 비용이 되게
            float speed = moveSpeed * (carry != null ? carry.SpeedFactor : 1f);
            Vector3 velocity = move * speed;
            velocity.y = verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
        }

        static Vector2 ReadMove()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }
    }
}
