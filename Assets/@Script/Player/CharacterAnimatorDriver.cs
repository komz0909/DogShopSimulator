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
        static readonly int WaveHash = Animator.StringToHash("Wave");

        /// <summary>
        /// 손을 흔든다(주인이 T 로 강아지를 부를 때). 컨트롤러에 Wave 상태가 있는 캐릭터만 — 지금은 주인뿐이다.
        /// 끝나면 대기로 돌아가고, 도중에 걸으면 걷기로 넘어간다(AC_Owner 의 전이).
        /// </summary>
        public void Wave()
        {
            if (animator != null && animator.HasState(0, WaveHash))
                animator.CrossFadeInFixedTime(WaveHash, 0.15f);
        }

        /// <summary>한 프레임 변위는 튀기 쉬워서 부드럽게 만든다.</summary>
        const float Smoothing = 10f;

        [SerializeField] Animator animator;

        Vector3 lastPosition;
        float speed;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            // 움직임은 CharacterController·NavMeshAgent 가 낸다. 루트 모션이 켜져 있으면 걷기 클립이 자식 Model 을
            // 바라보는 쪽으로 따로 밀어내, 몸이 캡슐에서 떨어져 나가 돌 때마다 반원을 그렸고(휙휙)
            // 캡슐 기준으로 붙는 운반 상자가 몸 한가운데나 등 뒤에 보였다. 씬·셋업 툴이 다시 켜도 여기서 끈다
            if (animator != null) animator.applyRootMotion = false;
            lastPosition = transform.position;

            // 상자 드는 자세 층(주인만 있다). 손님 컨트롤러엔 없어 -1 이다
            carry = GetComponentInParent<PlayerCarry>();
            carryLayer = animator != null ? animator.GetLayerIndex(CarryLayerName) : -1;
            tapeLayer = animator != null ? animator.GetLayerIndex(TapeLayerName) : -1;
        }

        const string CarryLayerName = "Carry";
        const string TapeLayerName = "Tape";
        int tapeLayer = -1;
        float tapeWeight;
        /// <summary>상자를 들고 놓을 때 팔이 자세로 옮겨 가는 빠르기(초당 가중치). 0.15초쯤에 다 들어간다.</summary>
        const float CarryBlendSpeed = 7f;

        PlayerCarry carry;
        int carryLayer = -1;
        float carryWeight;

        /// <summary>상자를 들고 있으면 팔만 "두 손으로 받친" 자세로 덮는다(걷기 다리는 그대로).</summary>
        void UpdateCarryPose()
        {
            if (carry == null) return;   // 주인만
            if (carryLayer >= 0)
            {
                float want = carry.IsHolding ? 1f : 0f;
                carryWeight = Mathf.MoveTowards(carryWeight, want, CarryBlendSpeed * Time.deltaTime);
                animator.SetLayerWeight(carryLayer, carryWeight);
            }
            // 포장대에서 테이프를 들면 오른팔을 가슴 앞으로(테이프는 오른손 뼈에 붙어 따라온다)
            if (tapeLayer >= 0)
            {
                float want = Shop.PackingStation.PlayerHoldingTape ? 1f : 0f;
                tapeWeight = Mathf.MoveTowards(tapeWeight, want, CarryBlendSpeed * Time.deltaTime);
                animator.SetLayerWeight(tapeLayer, tapeWeight);
            }
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
            UpdateCarryPose();
        }
    }
}
