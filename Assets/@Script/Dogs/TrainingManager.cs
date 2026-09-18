using System;
using DogShop.Core;
using DogShop.Data;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 하루 훈련 슬롯. 슬롯은 강아지별로 나뉘지 않고 총량으로 공유된다 —
    /// 반려견은 한 마리뿐이다. 슬롯은 <b>미모와 훈련도 중 어디에 쓸까</b>로 갈린다 —
    /// 챔피언십 심사 가중치가 시작 시점에 공개되므로 그 배분이 매일의 선택이다.
    /// </summary>
    public class TrainingManager : MonoBehaviour, ISaveParticipant
    {
        public static TrainingManager Instance { get; private set; }

        [SerializeField] TrainingCatalog catalog;

        public TrainingCatalog Catalog => catalog;
        public int SlotsUsed { get; private set; }
        public int SlotsTotal => ShopLevelManager.Instance.Current.trainingSlots;
        public int SlotsLeft => Mathf.Max(0, SlotsTotal - SlotsUsed);

        /// <summary>그날 축별 훈련 지출. 슬롯이 남는지(비율 하한 위반)를 실측으로 보려면 필요하다.</summary>
        public int SpentTodayTraining { get; private set; }
        public int SpentTodayBeauty { get; private set; }

        /// <summary>해금된 훈련 중 그 축의 최고가. 감시 지표의 분모다.</summary>
        public int TopCostOf(GrowthAxis axis)
        {
            int top = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                TrainingDef def = catalog.Get(i);
                if (def.axis != axis || !IsUnlocked(i)) continue;
                if (def.cost > top) top = def.cost;
            }
            return top;
        }

        public event Action OnSlotsChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnDayStarted += ResetSlots;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= ResetSlots;
            if (Instance == this) Instance = null;
        }

        void ResetSlots()
        {
            SlotsUsed = 0;
            SpentTodayTraining = 0;
            SpentTodayBeauty = 0;
            OnSlotsChanged?.Invoke();
        }

        public void CaptureInto(SaveData data) => data.trainingSlotsUsed = SlotsUsed;

        public void RestoreFrom(SaveData data)
        {
            SlotsUsed = Mathf.Max(0, data.trainingSlotsUsed);
            OnSlotsChanged?.Invoke();
        }

        public bool IsUnlocked(int index) =>
            catalog.Get(index).unlockLevel <= ShopLevelManager.Instance.Level;

        public bool CanTrain(Dog dog, int index, out string reason)
        {
            if (dog == null) { reason = "대상 없음"; return false; }
            if (!IsUnlocked(index)) { reason = "미해금 훈련"; return false; }
            if (SlotsLeft <= 0) { reason = "훈련 슬롯 소진 — " + SlotsUsed + "/" + SlotsTotal; return false; }

            if (dog.Stats.GrowthBlocked)
            {
                reason = "유지 스탯 부족 — 청결 " + dog.Stats.Cleanliness + " / 건강 " + dog.Stats.Health;
                return false;
            }

            int cost = catalog.Get(index).cost;
            if (GameManager.Instance.Money < cost)
            {
                reason = "재화 부족 — " + GameManager.Instance.Money + " / " + cost;
                return false;
            }

            reason = null;
            return true;
        }

        public bool Train(Dog dog, int index)
        {
            string reason;
            if (!CanTrain(dog, index, out reason)) return false;

            TrainingDef def = catalog.Get(index);
            if (!GameManager.Instance.TrySpend(def.cost)) return false;

            dog.Stats.AddGrowth(def.axis, def.gain);
            dog.Animator.Play(def.axis == GrowthAxis.Training ? DogAnim.Run : DogAnim.WagTail);

            SlotsUsed++;
            if (def.axis == GrowthAxis.Beauty) SpentTodayBeauty += def.cost;
            else SpentTodayTraining += def.cost;
            OnSlotsChanged?.Invoke();
            return true;
        }

        public sealed class TrainAction : IPlayerAction
        {
            readonly Dog dog;
            readonly int index;

            public TrainAction(Dog dog, int index)
            {
                this.dog = dog;
                this.index = index;
            }

            public bool CanExecute(out string reason) => Instance.CanTrain(dog, index, out reason);
            public void Execute() => Instance.Train(dog, index);
        }
    }
}
