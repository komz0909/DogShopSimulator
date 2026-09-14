using System;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 강아지 4스탯. 유지(청결·건강)는 감소형이고 재고를 써서 회복한다.
    /// 성장(미모·훈련도)은 누적형이고 훈련 슬롯 + 재화를 쓴다.
    /// 유지 스탯이 임계 미만이면 그날 성장 상승분이 0이다 — 이것이 결품의 이빨이다.
    /// </summary>
    public class DogStats : MonoBehaviour
    {
        public const int GrowthBlockThreshold = 40;
        public const int MaxUpkeep = 100;

        public int Cleanliness { get; private set; } = 100;
        public int Health { get; private set; } = 100;
        public int Beauty { get; private set; }
        public int Training { get; private set; }

        public int GrowthTotal => Beauty + Training;
        public int UpkeepAverage => (Cleanliness + Health) / 2;
        public bool GrowthBlocked => Cleanliness < GrowthBlockThreshold || Health < GrowthBlockThreshold;

        public event Action OnChanged;

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

        /// <summary>성장 스탯 상승. 유지 스탯이 낮으면 아무것도 오르지 않고 false를 돌려준다.</summary>
        public bool AddGrowth(GrowthAxis axis, int amount)
        {
            if (GrowthBlocked || amount <= 0) return false;

            if (axis == GrowthAxis.Beauty) Beauty += amount;
            else Training += amount;

            OnChanged?.Invoke();
            return true;
        }

        public void Restore(int cleanliness, int health, int beauty, int training)
        {
            Cleanliness = cleanliness;
            Health = health;
            Beauty = beauty;
            Training = training;
            OnChanged?.Invoke();
        }
    }
}
