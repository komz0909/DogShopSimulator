using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 강아지 1마리의 정체성과 몸값. 스탯은 DogStats, 애니메이션은 DogAnimator가 담당한다.
    /// 주인공견은 판매할 수 없고 챔피언십에만 나간다.
    /// </summary>
    public class Dog : MonoBehaviour
    {
        // ponytail: 판매가 공식의 계수는 D26 밸런싱 대상인 시작값이다.
        const int BasePrice = 100;
        const int UpkeepWeight = 2;
        const int GrowthWeight = 4;
        const int DayWeight = 15;

        public string BreedKo { get; private set; } = "";
        public int BreedIndex { get; private set; }
        public bool IsHero { get; private set; }
        public int DaysOwned { get; private set; }

        public DogStats Stats { get; private set; }
        public DogAnimator Animator { get; private set; }

        public bool CanSell => !IsHero;

        public int SalePrice =>
            BasePrice
            + Stats.UpkeepAverage * UpkeepWeight
            + Stats.GrowthTotal * GrowthWeight
            + DaysOwned * DayWeight;

        void Awake()
        {
            Stats = GetComponent<DogStats>();
            Animator = GetComponent<DogAnimator>();
        }

        public void Initialize(string breedKo, int breedIndex, bool isHero)
        {
            BreedKo = breedKo;
            BreedIndex = breedIndex;
            IsHero = isHero;
            DaysOwned = 0;
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
