using UnityEngine;

namespace DogShop.Player
{
    /// <summary>
    /// 이동 속도를 Animator의 Speed 파라미터로 넘겨 대기/걷기를 전환한다.
    /// 플레이어와 손님이 같은 컨트롤러(AC_Humanoid)를 쓰므로 이 하나로 둘 다 처리한다.
    ///
    /// 속도는 <b>변위에서 직접 잰다</b> — CharacterController와 NavMeshAgent 중
    /// 무엇이 움직이든 상관없이 같은 코드가 동작한다.
    /// </summary>
    public class CharacterAnimatorDriver : MonoBehaviour
    {
        static readonly int SpeedHash = Animator.StringToHash("Speed");

        /// <summary>한 프레임 변위는 튀기 쉬워서 부드럽게 만든다.</summary>
        const float Smoothing = 10f;

        [SerializeField] Animator animator;

        Vector3 lastPosition;
        float speed;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            lastPosition = transform.position;
        }

        void Update()
        {
            if (animator == null) return;

            // 수평 변위만 본다. 중력으로 인한 낙하가 걷기로 오인되지 않게.
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;

            float raw = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            speed = Mathf.Lerp(speed, raw, Time.deltaTime * Smoothing);

            animator.SetFloat(SpeedHash, speed);
        }
    }
}
