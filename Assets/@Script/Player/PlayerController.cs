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
        Transform camPivot;
        PlayerCarry carry;
        float verticalVelocity;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            carry = GetComponent<PlayerCarry>();
            ThirdPersonCamera pivot = GetComponentInChildren<ThirdPersonCamera>();
            if (pivot != null) camPivot = pivot.transform;
        }

        void Update()
        {
            Vector2 input = ReadMove();

            Vector3 forward = camPivot != null ? camPivot.forward : transform.forward;
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
