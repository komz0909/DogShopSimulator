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
        /// 매일의 선택이 사라진다. 슬롯 수만큼 <b>서로 다른</b> 훈련을 골라야 해서
        /// "무엇을 어떤 순서로 쓸까"가 남는다.
        /// </summary>
        bool[] usedToday;

        /// <summary>
        /// 훈련마다의 단계(1~5). 슬롯 하나가 내는 성장을 키운다.
        ///
        /// 반복으로는 오르지 않는다 — <see cref="BeginUpgrade"/>로만 올라가고,
        /// 그동안 그 훈련을 못 쓴다. 하루 한 번 제한은 그대로다.
        /// </summary>
        int[] stage;

        /// <summary>강화가 끝나기까지 남은 날. 0보다 크면 그 훈련을 쓸 수 없다.</summary>
        int[] upgradeDays;

        /// <summary>지금 단계에서 그 훈련을 몇 번 했는가. 단계가 오르면 0으로 돌아간다.</summary>
        int[] reps;
        public int SlotsTotal => ShopLevelManager.Instance.Current.trainingSlots;
        public int SlotsLeft => Mathf.Max(0, SlotsTotal - SlotsUsed);

        /// <summary>그날 축별 훈련 지출. 슬롯이 남는지(비율 하한 위반)를 실측으로 보려면 필요하다.</summary>
        public int SpentTodayTraining { get; private set; }
        public int SpentTodayBeauty { get; private set; }

        // ---- 단계 ----

        public int StageOf(int index) =>
            stage != null && index >= 0 && index < stage.Length ? stage[index] : 1;

        /// <summary>지금 단계의 비용.</summary>
        public int CostOf(int index) => TrainingStages.ValueAt(catalog.Get(index).cost, StageOf(index));

        /// <summary>지금 단계의 획득량.</summary>
        public int GainOf(int index) => TrainingStages.ValueAt(catalog.Get(index).gain, StageOf(index));

        /// <summary>강화 중이면 남은 날, 아니면 0.</summary>
        public int UpgradeDaysLeft(int index) =>
            upgradeDays != null && index >= 0 && index < upgradeDays.Length ? upgradeDays[index] : 0;

        public bool IsUpgrading(int index) => UpgradeDaysLeft(index) > 0;

        /// <summary>지금 단계에서 한 훈련 횟수.</summary>
        public int RepsOf(int index) =>
            reps != null && index >= 0 && index < reps.Length ? reps[index] : 0;

        /// <summary>다음 단계로 올리려면 지금 단계에서 몇 번 해야 하는가.</summary>
        public int RepsNeeded(int index) => TrainingStages.RepsToAdvance(StageOf(index));

        public bool CanUpgrade(int index, out string reason)
        {
            if (!IsUnlocked(index)) { reason = "미해금 훈련"; return false; }
            if (IsUpgrading(index)) { reason = "강화 중: " + UpgradeDaysLeft(index) + "일 남음"; return false; }
            if (StageOf(index) >= TrainingStages.Max) { reason = "최고 단계"; return false; }

            int need = RepsNeeded(index);
            if (RepsOf(index) < need)
            {
                reason = "더 해 봐야 한다: " + RepsOf(index) + " / " + need + "회";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 단계 강화를 시작한다. 값은 돈이 아니라 <b>시간</b>이다 —
        /// 끝날 때까지 그 훈련을 못 쓰므로, 주력 훈련을 올리는 동안은 다른 것으로 버텨야 한다.
        /// </summary>
        public bool BeginUpgrade(int index)
        {
            string reason;
            if (!CanUpgrade(index, out reason)) return false;

            upgradeDays[index] = TrainingStages.DaysToReach(stage[index] + 1);
            OnSlotsChanged?.Invoke();
            return true;
        }

        void AdvanceUpgrades()
        {
            if (upgradeDays == null) return;

            for (int i = 0; i < upgradeDays.Length; i++)
            {
                if (upgradeDays[i] <= 0) continue;

                upgradeDays[i]--;
                if (upgradeDays[i] != 0 || stage[i] >= TrainingStages.Max) continue;

                stage[i]++;
                reps[i] = 0;   // 새 단계에서 다시 쌓는다
            }
        }

        /// <summary>
        /// 그 훈련이 이 축을 올리는가. 두 축을 같이 올리는 훈련(놀아주기)은 양쪽 모두에 해당한다.
        /// </summary>
        public static bool Covers(GrowthAxis trainingAxis, GrowthAxis wanted) =>
            trainingAxis == wanted || trainingAxis == GrowthAxis.Both;

        /// <summary>해금된 훈련 중 그 축의 최고가. 지금 단계 기준이다.</summary>
        public int TopCostOf(GrowthAxis axis)
        {
            int top = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (!Covers(catalog.Get(i).axis, axis) || !IsUnlocked(i)) continue;
                if (CostOf(i) > top) top = CostOf(i);
            }
            return top;
        }

        /// <summary>
        /// 하루에 훈련으로 쓸 수 있는 <b>최대 금액</b>. 하루 한 번 제한이 있으므로
        /// 슬롯 수만큼 <b>서로 다른</b> 훈련 중 비싼 것부터 고른 합이다.
        ///
        /// "슬롯 x 최고가"가 아니다 — 같은 훈련을 반복할 수 없어서, 쓸 수 있는 훈련이
        /// 슬롯보다 적으면 슬롯을 다 못 채운다. 강화 중인 훈련도 못 쓰므로 뺀다.
        /// </summary>
        public int DailyCapacity
        {
            get
            {
                int slots = SlotsTotal;
                if (catalog == null || slots <= 0) return 0;

                int total = 0, taken = 0, ceiling = int.MaxValue;
                while (taken < slots)
                {
                    int best = 0;
                    for (int i = 0; i < catalog.Count; i++)
                    {
                        if (!IsUnlocked(i) || IsUpgrading(i)) continue;
                        if (CostOf(i) < ceiling && CostOf(i) > best) best = CostOf(i);
                    }
                    if (best <= 0) break;

                    // 같은 값이 여러 종일 수 있다(축이 둘이라 비용표가 겹친다)
                    for (int i = 0; i < catalog.Count && taken < slots; i++)
                    {
                        if (!IsUnlocked(i) || IsUpgrading(i) || CostOf(i) != best) continue;
                        total += best;
                        taken++;
                    }
                    ceiling = best;
                }
                return total;
            }
        }

        public event Action OnSlotsChanged;

        // ---- 운동(어질리티) ----
        // 훈련 슬롯을 쓰지 않고 돈만 낸다. 하루 한 번, 해금된 운동 중 하나를 고른다.
        // 비싼 운동일수록 한 번에 많이 오르지만 1점당 값은 조금씩 비싸진다(40 → 42 → 45원) — 싼 운동이 죽지 않게.
        // 30일 동안 레벨을 제때 올리며 매일 하면 대략 200 안팎(도그쇼 만점 기준 240)에 닿는다. 임시값이다.

        public struct ExerciseDef
        {
            public string nameKo;
            public int cost, gain, unlockLevel;
        }

        public static readonly ExerciseDef[] Exercises =
        {
            new ExerciseDef { nameKo = "공놀이", cost = 120, gain = 3, unlockLevel = 1 },
            new ExerciseDef { nameKo = "달리기", cost = 250, gain = 6, unlockLevel = 3 },
            new ExerciseDef { nameKo = "장애물", cost = 450, gain = 10, unlockLevel = 5 },
        };

        public bool ExercisedToday { get; private set; }
        public int SpentTodayAgility { get; private set; }

        /// <summary>운동을 마쳤다(강아지, 운동 번호, 실제로 오른 어질리티). 화면 가운데 "운동했다" 창이 받는다.</summary>
        public event Action<Dog, int, int> OnExercised;

        public bool IsExerciseUnlocked(int index) =>
            index >= 0 && index < Exercises.Length && Exercises[index].unlockLevel <= ShopLevelManager.Instance.Level;

        public bool CanExercise(Dog dog, int index, out string reason)
        {
            if (dog == null) { reason = "대상 없음"; return false; }
            if (!IsExerciseUnlocked(index)) { reason = "Lv " + Exercises[Mathf.Clamp(index, 0, Exercises.Length - 1)].unlockLevel + " 해금"; return false; }
            if (ExercisedToday) { reason = "오늘 운동 끝, 내일 다시"; return false; }
            if (dog.Stats.GrowthBlocked)
            {
                reason = "유지 스탯 부족: 청결 " + dog.Stats.Cleanliness + " / 건강 " + dog.Stats.Health;
                return false;
            }
            int cost = Exercises[index].cost;
            if (GameManager.Instance.Money < cost)
            {
                reason = "재화 부족: " + GameManager.Instance.Money + " / " + cost;
                return false;
            }
            reason = null;
            return true;
        }

        public bool Exercise(Dog dog, int index)
        {
            if (!CanExercise(dog, index, out _)) return false;
            ExerciseDef def = Exercises[index];
            if (!GameManager.Instance.TrySpend(def.cost)) return false;

            dog.Stats.AddGrowth(GrowthAxis.Agility, def.gain, out int gained);
            ExercisedToday = true;
            SpentTodayAgility += def.cost;

            DogRoamer roamer = dog.GetComponent<DogRoamer>();
            if (roamer != null) roamer.Show(DogAnim.Run, ShowSeconds);
            else dog.Animator.Play(DogAnim.Run);

            OnSlotsChanged?.Invoke();
            OnExercised?.Invoke(dog, index, gained);
            return true;
        }

        public sealed class ExerciseAction : IPlayerAction
        {
            readonly Dog dog;
            readonly int index;

            public ExerciseAction(Dog dog, int index)
            {
                this.dog = dog;
                this.index = index;
            }

            public bool CanExecute(out string reason) => Instance.CanExercise(dog, index, out reason);
            public void Execute() => Instance.Exercise(dog, index);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            int count = catalog != null ? catalog.Count : 0;
            usedToday = new bool[count];
            stage = new int[count];
            upgradeDays = new int[count];
            reps = new int[count];
            for (int i = 0; i < count; i++) stage[i] = 1;
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
            SpentTodayAgility = 0;
            ExercisedToday = false;
            if (usedToday != null) Array.Clear(usedToday, 0, usedToday.Length);

            // 강화는 날짜로 흐른다. 하루가 시작될 때 하루씩 깎는다
            AdvanceUpgrades();

            OnSlotsChanged?.Invoke();
        }

        public void CaptureInto(SaveData data)
        {
            data.trainingSlotsUsed = SlotsUsed;
            data.exercisedToday = ExercisedToday;
            data.trainingUsedToday = usedToday != null ? (bool[])usedToday.Clone() : new bool[0];
            data.trainingStage = stage != null ? (int[])stage.Clone() : new int[0];
            data.trainingUpgradeDays = upgradeDays != null ? (int[])upgradeDays.Clone() : new int[0];
            data.trainingReps = reps != null ? (int[])reps.Clone() : new int[0];
        }

        public void RestoreFrom(SaveData data)
        {
            SlotsUsed = Mathf.Max(0, data.trainingSlotsUsed);
            ExercisedToday = data.exercisedToday;

            // 쿨타임도 같이 복원한다. 안 그러면 저장하고 불러오는 것만으로 하루 쿨이 풀린다
            for (int i = 0; usedToday != null && i < usedToday.Length; i++)
                usedToday[i] = data.trainingUsedToday != null && i < data.trainingUsedToday.Length
                    && data.trainingUsedToday[i];

            // 단계와 강화 진행도 같이 복원한다. 안 그러면 불러오는 것만으로 강화가 끝나거나
            // 어렵게 올린 단계가 1로 돌아간다. 옛 세이브에는 없으므로 1단계로 본다
            for (int i = 0; stage != null && i < stage.Length; i++)
            {
                stage[i] = data.trainingStage != null && i < data.trainingStage.Length
                    ? Mathf.Clamp(data.trainingStage[i], 1, TrainingStages.Max)
                    : 1;

                upgradeDays[i] = data.trainingUpgradeDays != null && i < data.trainingUpgradeDays.Length
                    ? Mathf.Max(0, data.trainingUpgradeDays[i])
                    : 0;

                reps[i] = data.trainingReps != null && i < data.trainingReps.Length
                    ? Mathf.Max(0, data.trainingReps[i])
                    : 0;
            }

            OnSlotsChanged?.Invoke();
        }

        public bool IsUnlocked(int index) =>
            catalog.Get(index).unlockLevel <= ShopLevelManager.Instance.Level;

        public bool CanTrain(Dog dog, int index, out string reason)
        {
            if (dog == null) { reason = "대상 없음"; return false; }
            if (!IsUnlocked(index)) { reason = "미해금 훈련"; return false; }
            if (IsUpgrading(index)) { reason = "강화 중: " + UpgradeDaysLeft(index) + "일 남음"; return false; }
            if (UsedToday(index)) { reason = "오늘 이미 했다, 내일 다시"; return false; }
            if (SlotsLeft <= 0) { reason = "훈련 슬롯 소진: " + SlotsUsed + "/" + SlotsTotal; return false; }

            if (dog.Stats.GrowthBlocked)
            {
                reason = "유지 스탯 부족: 청결 " + dog.Stats.Cleanliness + " / 건강 " + dog.Stats.Health;
                return false;
            }

            int cost = CostOf(index);
            if (GameManager.Instance.Money < cost)
            {
                reason = "재화 부족: " + GameManager.Instance.Money + " / " + cost;
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
            int cost = CostOf(index);
            if (!GameManager.Instance.TrySpend(cost)) return false;

            dog.Stats.AddGrowth(def.axis, GainOf(index));
            ShowTraining(dog, def);

            SlotsUsed++;
            if (usedToday != null && index < usedToday.Length) usedToday[index] = true;
            if (reps != null && index < reps.Length) reps[index]++;
            // 두 축을 올리는 훈련은 지출을 반씩 나눠 적는다. 양쪽에 전액을 적으면
            // 두 열의 합이 실제 지출을 넘어 감시비율의 분자가 부풀려진다
            if (def.axis == GrowthAxis.Both)
            {
                SpentTodayBeauty += cost / 2;
                SpentTodayTraining += cost - cost / 2;
            }
            else if (def.axis == GrowthAxis.Beauty) SpentTodayBeauty += cost;
            else SpentTodayTraining += cost;
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
                if (Covers(other.axis, def.axis) && other.cost < def.cost) tier++;
            }
            return tier;
        }

        static DogAnim ShowFor(GrowthAxis axis, int tier)
        {
            // 놀아주기는 훈련이라기보다 노는 것이다. 언제나 꼬리를 흔든다
            if (axis == GrowthAxis.Both) return DogAnim.WagTail;

            // 짖기(Angry)는 비싼 훈련에만 준다 — 매번 으르렁대면 사나운 개로 보인다
            if (axis == GrowthAxis.Training)
            {
                switch (tier)
                {
                    case 0: return DogAnim.Run;       // 산책
                    case 1: return DogAnim.Sit;       // 고급 훈련
                    default: return DogAnim.Angry;    // 전문 훈련
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
