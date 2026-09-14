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
    }
}
