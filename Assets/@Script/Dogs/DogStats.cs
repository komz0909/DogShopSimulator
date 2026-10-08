using System;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 강아지 5스탯. 유지(청결·건강)는 감소형이고 재고를 써서 회복한다.
    /// 성장(미모·훈련도·어질리티)은 누적형이다 — 미모·훈련도는 훈련 슬롯 + 재화, 어질리티는 운동(재화)으로 오른다.
    /// 유지 스탯이 임계 미만이면 그날 성장 상승분이 0이다 — 이것이 결품의 이빨이다.
    ///
    /// 성장량에는 견종 특성(<see cref="BreedTraits"/>)을 곱한다. +1 에 1.1배처럼 정수로 안 떨어지는 몫은
    /// 축마다 남겨 두었다가 다음 성장에 보탠다(저장도 한다) — 버리면 작은 훈련에서 견종 차이가 사라진다.
    /// </summary>
    public class DogStats : MonoBehaviour
    {
        public const int GrowthBlockThreshold = 40;
        public const int MaxUpkeep = 100;

        public int Cleanliness { get; private set; } = 100;
        public int Health { get; private set; } = 100;
        public int Beauty { get; private set; }
        public int Training { get; private set; }
        public int Agility { get; private set; }

        // 견종 배율을 곱하고 남은 소수 몫
        public float BeautyCarry { get; private set; }
        public float TrainingCarry { get; private set; }
        public float AgilityCarry { get; private set; }

        /// <summary>가게 레벨 조건에 쓰는 성장 합. 어질리티는 넣지 않는다 — 넣으면 레벨이 지금보다 빨리 열린다.</summary>
        public int GrowthTotal => Beauty + Training;
        public int UpkeepAverage => (Cleanliness + Health) / 2;
        public bool GrowthBlocked => Cleanliness < GrowthBlockThreshold || Health < GrowthBlockThreshold;

        public event Action OnChanged;

        Dog dog;
        int Breed => (dog != null ? dog : dog = GetComponent<Dog>()) != null ? dog.BreedIndex : -1;

        public void DecayDaily(int amount)
        {
            Cleanliness = Mathf.Max(0, Cleanliness - amount);
            Health = Mathf.Max(0, Health - amount);
            OnChanged?.Invoke();
        }

        public void RecoverCleanliness(int amount)
        {
            Cleanliness = Mathf.Min(MaxUpkeep, Cleanliness + amount);
            OnChanged?.Invoke();
        }

        public void RecoverHealth(int amount)
        {
            Health = Mathf.Min(MaxUpkeep, Health + amount);
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 성장 스탯 상승(견종 배율 적용). 유지 스탯이 낮으면 아무것도 오르지 않고 false를 돌려준다.
        /// <paramref name="gained"/> 는 실제로 오른 양(Both 면 한 축 몫).
        /// </summary>
        public bool AddGrowth(GrowthAxis axis, int amount, out int gained)
        {
            gained = 0;
            if (GrowthBlocked || amount <= 0) return false;

            // Both 은 나눠 갖지 않는다. 한 슬롯으로 두 축을 같이 올리는 것이 이 훈련의 값이다
            if (axis == GrowthAxis.Beauty || axis == GrowthAxis.Both)
            {
                float carry = BeautyCarry;
                Beauty += gained = Grow(amount, GrowthAxis.Beauty, ref carry);
                BeautyCarry = carry;
            }
            if (axis == GrowthAxis.Training || axis == GrowthAxis.Both)
            {
                float carry = TrainingCarry;
                Training += gained = Grow(amount, GrowthAxis.Training, ref carry);
                TrainingCarry = carry;
            }
            if (axis == GrowthAxis.Agility)
            {
                float carry = AgilityCarry;
                Agility += gained = Grow(amount, GrowthAxis.Agility, ref carry);
                AgilityCarry = carry;
            }

            OnChanged?.Invoke();
            return true;
        }

        public bool AddGrowth(GrowthAxis axis, int amount) => AddGrowth(axis, amount, out _);

        int Grow(int amount, GrowthAxis axis, ref float carry)
        {
            float v = amount * BreedTraits.Multiplier(Breed, axis) + carry;
            int whole = Mathf.FloorToInt(v + 1e-4f);
            carry = Mathf.Max(0f, v - whole);
            return whole;
        }

        public void Restore(int cleanliness, int health, int beauty, int training, int agility = 0,
                            float beautyCarry = 0f, float trainingCarry = 0f, float agilityCarry = 0f)
        {
            Cleanliness = cleanliness;
            Health = health;
            Beauty = beauty;
            Training = training;
            Agility = agility;
            BeautyCarry = beautyCarry;
            TrainingCarry = trainingCarry;
            AgilityCarry = agilityCarry;
            OnChanged?.Invoke();
        }
    }
}
