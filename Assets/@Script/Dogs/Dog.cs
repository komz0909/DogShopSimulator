using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 반려견 1마리. 스탯은 DogStats, 애니메이션은 DogAnimator가 담당한다.
    ///
    /// 이 게임에 강아지는 <b>한 마리뿐이다.</b> 판매견을 함께 키우던 구조를 걷어냈다 —
    /// 훈련비의 10%만 판매가로 돌아와(성장 +4당 16원) 훈련시키는 쪽이 언제나 손해였고,
    /// 매입 수단도 없어 선택으로 작동한 적이 없다. 한 마리에 몰입하는 쪽이 스토리와도 맞는다.
    /// </summary>
    public class Dog : MonoBehaviour
    {
        public string BreedKo { get; private set; } = "";
        public int BreedIndex { get; private set; }

        /// <summary>메인 화면에서 지어 준 이름. 비워 두면 견종 이름으로 부른다.</summary>
        public string Name { get; private set; } = "";
        public string DisplayName => string.IsNullOrEmpty(Name) ? BreedKo : Name;

        /// <summary>함께 지낸 날수. 값어치가 아니라 엔딩에 쓰는 기록이다.</summary>
        public int DaysOwned { get; private set; }

        public DogStats Stats { get; private set; }
        public DogAnimator Animator { get; private set; }

        void Awake()
        {
            Stats = GetComponent<DogStats>();
            Animator = GetComponent<DogAnimator>();
        }

        /// <summary>
        /// 주인과는 <b>몸이 부딪히지 않는다.</b> 강아지가 졸졸 따라다니다 앞에 서면
        /// 좁은 문간·진열대 사이에서 주인 길을 통째로 막았다.
        ///
        /// 콜라이더는 그대로 둔다 — E 로 강아지를 조준하는 레이캐스트는 계속 맞아야 한다.
        /// 끄는 건 플레이어 CharacterController 와의 충돌뿐이다.
        /// </summary>
        void Start()
        {
            Player.PlayerCarry owner = FindAnyObjectByType<Player.PlayerCarry>();
            CharacterController body = owner != null ? owner.GetComponent<CharacterController>() : null;
            if (body == null) return;

            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(body, c, true);
        }

        public void Initialize(string breedKo, int breedIndex)
        {
            BreedKo = breedKo;
            BreedIndex = breedIndex;
            DaysOwned = 0;
        }

        /// <summary>이름은 6자까지. 머리 위 이름표(DogNameTag)가 이 길이에 맞춰 글자를 줄인다.</summary>
        public const int MaxNameLength = 6;

        public void SetName(string name)
        {
            string n = (name ?? "").Trim();
            Name = n.Length > MaxNameLength ? n.Substring(0, MaxNameLength) : n;
        }

        public void RestoreState(int daysOwned) => DaysOwned = daysOwned;

        public void AdvanceDay() => DaysOwned++;

        /// <summary>유지 스탯 상태를 애니메이션으로 드러낸다. 컨디션이 좋으면 꼬리를 흔든다.</summary>
        public void SyncMood()
        {
            if (Animator == null) return;

            if (Stats.GrowthBlocked) Animator.Play(DogAnim.Angry);
            else if (Stats.UpkeepAverage >= 80) Animator.Play(DogAnim.WagTail);
            else Animator.Play(DogAnim.Idle);
        }
    }
}
