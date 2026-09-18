using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>3D Stylized Animated Dogs Kit의 AnimationID 값.</summary>
    public enum DogAnim
    {
        Idle = 0,
        WagTail = 1,
        Walk = 2,
        WalkAlt = 3,
        Run = 4,
        Eat = 5,
        Angry = 6,
        Sit = 7
    }

    /// <summary>Dog_Animator_Controler를 AnimationID(int) 하나로 구동한다. Start/Cycle/End 전이는 컨트롤러가 처리한다.</summary>
    public class DogAnimator : MonoBehaviour
    {
        static readonly int AnimationIdParam = Animator.StringToHash("AnimationID");

        Animator animator;

        public DogAnim Current { get; private set; } = DogAnim.Idle;

        void Awake() => animator = GetComponentInChildren<Animator>();

        public void Play(DogAnim anim)
        {
            if (animator == null || Current == anim) return;
            Current = anim;
            animator.SetInteger(AnimationIdParam, (int)anim);
        }

        /// <summary>
        /// 재생 배속. 이 킷의 클립에는 <b>루트 모션이 없어</b>(averageSpeed 0) 제자리에서만 움직인다 —
        /// 이동 속도와 다리 회전이 따로 놀면 미끄러져 보이므로, 실제 속력에 맞춰 여기서 맞춘다.
        /// </summary>
        public void SetPlaybackSpeed(float speed)
        {
            if (animator == null) return;
            animator.speed = Mathf.Clamp(speed, 0.5f, 2f);
        }
    }
}
