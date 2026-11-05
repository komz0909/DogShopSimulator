using System;
using UnityEngine;

namespace DogShop.Data
{
    /// <summary>
    /// 훈련이 올리는 성장 스탯. <see cref="Both"/>는 <b>두 축을 각각</b> 올린다 —
    /// 획득량을 나눠 갖는 게 아니라 미모와 훈련도에 같은 값이 따로 붙는다.
    ///
    /// 값을 <b>뒤에만</b> 붙인다. 직렬화가 정수라 순서를 바꾸면 기존 카탈로그의 축이 뒤집힌다.
    /// <see cref="Agility"/> 는 훈련 카드가 아니라 운동(<see cref="DogShop.Dogs.TrainingManager"/> 의 운동)으로만 오른다.
    /// </summary>
    public enum GrowthAxis { Training, Beauty, Both, Agility }

    /// <summary>
    /// 훈련 1종. 훈련 슬롯 1개와 재화를 소모해 성장 스탯을 올린다.
    /// 원당효율(cost/gain)과 슬롯당효율(gain)이 반드시 역순이어야 파레토 지배가 생기지 않는다.
    /// gain은 절대 건드리지 말고 조정은 cost로만 할 것.
    /// </summary>
    [Serializable]
    public class TrainingDef
    {
        public string nameKo = "";
        public GrowthAxis axis = GrowthAxis.Training;

        /// <summary>1단계 비용. 단계가 오르면 <see cref="TrainingStages.Step"/>배씩 붙는다.</summary>
        public int cost = 20;

        /// <summary>1단계 획득량. 비용과 <b>같은 배수</b>로 오른다.</summary>
        public int gain = 1;

        [Min(1)] public int unlockLevel = 1;
    }

    /// <summary>
    /// 훈련 단계. 같은 훈련을 계속 쓰면 단계를 올려 <b>슬롯 하나가 내는 성장</b>을 키운다.
    ///
    /// (시연판 규칙) 비용과 획득량이 같은 배수로 오르므로 <b>원당 효율은 단계와 무관하다</b>.
    /// 본편은 2026-11-05 부터 돈으로 강화하고 단계마다 1점당 값이 싸진다 — 아래 클래스 머리말 참고.
    /// 단계를 올려서 얻는 것은 효율이 아니라 <b>슬롯당 처리량</b>이다 —
    /// 하루 슬롯이 정해져 있으니, 고점을 노리려면 단계를 올리는 수밖에 없다.
    /// 값은 시간으로 치른다(2~4단계 하루, 5단계 이틀 동안 그 훈련을 못 쓴다).
    /// </summary>
    public static class TrainingStages
    {
        // ---- 본편 강화(2026-11-05): 돈을 내고 올리고, 올릴수록 효율이 좋아진다. 10단계 ----
        // 후반에 돈이 남아돌았다(측정: 20일차 뒤 3,000~5,800원이 쓸 곳 없이 쌓임). 훈련 칸이 하루 3개로 정해져 있어
        // 돈이 있어도 강아지를 더 키울 수 없었다. 이제 강화를 돈으로 사고, 강화가 1점당 값을 낮춰 준다.
        // 단계는 시즌이 바뀌어도 남는다 — 다섯 시즌 동안 돈을 쓸 곳이다.
        // 시연판(Exhibition.Boosted)은 예전 규칙(5단계, 비용과 획득이 같이 1.5배, 시간으로 치름) 그대로다.

        /// <summary>돈으로 강화하는 본편 규칙인가(본편, 튜토리얼). 시연판은 false.</summary>
        public static bool Paid => !Core.Exhibition.Boosted;

        public static int Max => Paid ? 10 : 5;

        /// <summary>시연판: 단계당 비용, 획득 배수.</summary>
        public const float Step = 1.5f;

        /// <summary>본편: 단계마다 획득 +30%, 1회 값 +10%. 10단계면 획득 3.7배, 값 1.9배라 1점당 값이 처음의 절반쯤이다.</summary>
        public const float GainPerStage = 0.3f, CostPerStage = 0.1f;

        /// <summary>본편: 강화비 = 1단계 1회 값 x 이것 x 지금 단계. 전문 훈련(550원)은 1,100원부터 9,900원까지, 10단계까지 49,500원.</summary>
        public const int UpgradeCostFactor = 2;

        public static float GainMultiplier(int stage) => 1f + GainPerStage * (Mathf.Clamp(stage, 1, Max) - 1);
        public static float CostMultiplier(int stage) => 1f + CostPerStage * (Mathf.Clamp(stage, 1, Max) - 1);
        public static int UpgradeCost(int baseCost, int stage) => baseCost * UpgradeCostFactor * Mathf.Clamp(stage, 1, Max);

        /// <summary>그 단계로 올리는 데 걸리는 날. 본편은 돈을 내면 바로 오른다(0). 시연판은 하루, 마지막 단계만 이틀.</summary>
        public static int DaysToReach(int stage) => Paid ? 0 : stage >= Max ? 2 : 1;

        /// <summary>
        /// 그 단계에서 몇 번 훈련해야 다음 단계로 올릴 수 있는가.
        /// 본편은 지금 단계 수만큼(1, 2, 3 ... 9번, 10단계까지 모두 45번). 시연판은 1 / 2 / 4 / 8.
        /// 써 본 만큼만 올라가므로, 주력으로 쓰는 훈련이 자연히 먼저 커진다.
        /// </summary>
        public static int RepsToAdvance(int stage) => Paid ? Mathf.Clamp(stage, 1, Max - 1) : 1 << (Mathf.Clamp(stage, 1, Max - 1) - 1);

        /// <summary>시연판: 단계 값(비용과 획득에 같은 배수).</summary>
        public static int ValueAt(int baseValue, int stage) =>
            Mathf.Max(1, Mathf.RoundToInt(baseValue * Mathf.Pow(Step, Mathf.Clamp(stage, 1, Max) - 1)));
    }

    [CreateAssetMenu(menuName = "DogShop/Training Catalog", fileName = "TrainingCatalog")]
    public class TrainingCatalog : ScriptableObject
    {
        [SerializeField] TrainingDef[] entries = new TrainingDef[0];

        public int Count => entries.Length;
        public TrainingDef Get(int index) => entries[index];
    }
}
