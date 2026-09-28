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

        /// <summary>
        /// 오늘 이미 쓴 훈련. 같은 훈련은 <b>하루에 한 번</b>이다.
        ///
        /// 이게 없으면 슬롯을 전부 같은 훈련으로 채우는 게 언제나 정답이라
        /// (돈만 있으면 최고가 하나만 반복) 매일의 선택이 사라진다. 하루 쿨타임을 주면
        /// 슬롯 수만큼 <b>서로 다른</b> 훈련을 골라야 해서, 돈이 넉넉해져도
        /// "무엇을 어떤 순서로 쓸까"가 남는다.
        /// </summary>
        bool[] usedToday;
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

        /// <summary>
        /// 하루에 훈련으로 쓸 수 있는 <b>최대 금액</b>. 슬롯 수만큼 <b>서로 다른</b> 훈련 중
        /// 비싼 것부터 고른 합이다.
        ///
        /// 하루 쿨타임이 생기면서 "슬롯 × 최고가"는 더 이상 상한이 아니다 —
        /// L4 는 슬롯이 4개인데 해금된 훈련이 축마다 3종뿐이라, 같은 것을 반복할 수 없으면
        /// 두 축을 섞어야만 슬롯을 다 쓴다. 감시 지표가 재는 "훈련이 흡수할 수 있는 돈"의
        /// 진짜 크기는 이 값이다.
        /// </summary>
        public int DailyCapacity
        {
            get
            {
                int slots = SlotsTotal;
                if (catalog == null || slots <= 0) return 0;

                // 해금된 훈련의 비용을 내림차순으로 slots 개만 더한다
                int total = 0;
                int taken = 0;
                int ceiling = int.MaxValue;

                while (taken < slots)
                {
                    int best = 0;
                    for (int i = 0; i < catalog.Count; i++)
                    {
                        if (!IsUnlocked(i)) continue;
                        int cost = catalog.Get(i).cost;
                        if (cost < ceiling && cost > best) best = cost;
                    }
                    if (best <= 0) break;

                    // 같은 값이 여러 종일 수 있다(축이 둘이라 비용표가 겹친다)
                    for (int i = 0; i < catalog.Count && taken < slots; i++)
                    {
                        if (!IsUnlocked(i) || catalog.Get(i).cost != best) continue;
                        total += best;
                        taken++;
                    }
                    ceiling = best;
                }
                return total;
            }
        }

        public event Action OnSlotsChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            usedToday = new bool[catalog != null ? catalog.Count : 0];
        }

        /// <summary>오늘 이미 쓴 훈련인가. 하루가 바뀌면 풀린다.</summary>
        public bool UsedToday(int index) =>
            usedToday != null && index >= 0 && index < usedToday.Length && usedToday[index];

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
            if (usedToday != null) Array.Clear(usedToday, 0, usedToday.Length);
            OnSlotsChanged?.Invoke();
        }

        public void CaptureInto(SaveData data)
        {
            data.trainingSlotsUsed = SlotsUsed;
            data.trainingUsedToday = usedToday != null ? (bool[])usedToday.Clone() : new bool[0];
        }

        public void RestoreFrom(SaveData data)
        {
            SlotsUsed = Mathf.Max(0, data.trainingSlotsUsed);

            // 쿨타임도 같이 복원한다. 안 그러면 저장하고 불러오는 것만으로 하루 쿨이 풀린다
            if (usedToday != null)
            {
                Array.Clear(usedToday, 0, usedToday.Length);
                if (data.trainingUsedToday != null)
                    for (int i = 0; i < usedToday.Length && i < data.trainingUsedToday.Length; i++)
                        usedToday[i] = data.trainingUsedToday[i];
            }

            OnSlotsChanged?.Invoke();
        }

        public bool IsUnlocked(int index) =>
            catalog.Get(index).unlockLevel <= ShopLevelManager.Instance.Level;

        public bool CanTrain(Dog dog, int index, out string reason)
        {
            if (dog == null) { reason = "대상 없음"; return false; }
            if (!IsUnlocked(index)) { reason = "미해금 훈련"; return false; }
            if (UsedToday(index)) { reason = "오늘 이미 했다 — 내일 다시"; return false; }
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
            if (usedToday != null && index < usedToday.Length) usedToday[index] = true;
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
