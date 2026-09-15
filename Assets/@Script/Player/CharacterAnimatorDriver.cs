using UnityEngine;

namespace DogShop.Player
{
    /// <summary>
    /// 이동 속도를 Animator의 Speed 파라미터로 넘겨 대기/걷기를 전환한다.
    /// 컨트롤러는 캐릭터마다 다르지만(Generic 리그라 클립이 골격 전용이다)
    /// Speed 파라미터 이름은 같으므로 이 하나로 주인과 손님 8종을 모두 처리한다.
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
