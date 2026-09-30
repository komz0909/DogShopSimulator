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
            ShowTraining(dog, def);

            SlotsUsed++;
            if (usedToday != null && index < usedToday.Length) usedToday[index] = true;
            if (def.axis == GrowthAxis.Beauty) SpentTodayBeauty += def.cost;
            else SpentTodayTraining += def.cost;
            OnSlotsChanged?.Invoke();
            return true;
        }

        /// <summary>훈련 한 번이 화면에 보이는 시간. 클립 한 바퀴(1.6초)보다 조금 길게.</summary>
        const float ShowSeconds = 2.2f;

        /// <summary>
        /// 훈련하면 그 자리에 서서 <b>훈련마다 다른 동작</b>을 한다.
        ///
        /// 예전에는 여기서 <c>Animator.Play</c>만 불렀는데, 다음 프레임에
        /// <see cref="DogRoamer"/>가 속력을 보고 Idle 로 덮어써서 아무것도 안 보였다.
        /// 이제 로머에게 "이 동작을 몇 초 동안 해라"라고 맡긴다.
        ///
        /// 어떤 동작을 할지는 <b>그 축에서 몇 번째로 비싼 훈련인가</b>로 고른다.
        /// 카탈로그 첨자로 박으면 훈련을 하나 끼워 넣는 순간 전부 어긋난다.
        /// </summary>
        void ShowTraining(Dog dog, TrainingDef def)
        {
            DogRoamer roamer = dog.GetComponent<DogRoamer>();
            DogAnim anim = ShowFor(def.axis, TierOf(def));

            if (roamer != null) roamer.Show(anim, ShowSeconds);
            else dog.Animator.Play(anim);   // 로머가 없는 강아지(테스트용)라면 그냥 재생한다
        }

        /// <summary>같은 축에서 이 훈련보다 싼 것이 몇 개인가 — 0이 가장 싼 훈련이다.</summary>
        int TierOf(TrainingDef def)
        {
            int tier = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                TrainingDef other = catalog.Get(i);
                if (other.axis == def.axis && other.cost < def.cost) tier++;
            }
            return tier;
        }

        static DogAnim ShowFor(GrowthAxis axis, int tier)
        {
            // 짖기(Angry)는 비싼 훈련에만 준다 — 매번 으르렁대면 사나운 개로 보인다
            if (axis == GrowthAxis.Training)
            {
                switch (tier)
                {
                    case 0: return DogAnim.Run;       // 산책
                    case 1: return DogAnim.Sit;       // 복종 훈련
                    case 2: return DogAnim.Run;       // 어질리티 특훈
                    default: return DogAnim.Angry;    // 전문·마스터 훈련
                }
            }

            // 미용은 얌전한 동작만. 꼬리를 흔들거나 앉아서 받는다
            return tier % 2 == 0 ? DogAnim.WagTail : DogAnim.Sit;
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
