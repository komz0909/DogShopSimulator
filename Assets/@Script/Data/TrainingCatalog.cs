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
    /// 비용과 획득량이 같은 배수로 오르므로 <b>원당 효율은 단계와 무관하다</b>.
    /// 단계를 올려서 얻는 것은 효율이 아니라 <b>슬롯당 처리량</b>이다 —
    /// 하루 슬롯이 정해져 있으니, 고점을 노리려면 단계를 올리는 수밖에 없다.
    /// 값은 시간으로 치른다(2~4단계 하루, 5단계 이틀 동안 그 훈련을 못 쓴다).
    /// </summary>
    public static class TrainingStages
    {
        public const int Max = 5;

        /// <summary>단계당 비용·획득 배수.</summary>
        public const float Step = 1.5f;

        /// <summary>그 단계로 올리는 데 걸리는 날. 마지막 단계만 이틀이다.</summary>
        public static int DaysToReach(int stage) => stage >= Max ? 2 : 1;

        /// <summary>
        /// 그 단계에서 <b>몇 번 훈련해야</b> 다음 단계로 올릴 수 있는가. 1 / 2 / 4 / 8 로 배가 된다.
        ///
        /// 단계를 돈이나 시간만으로 올리면 "일단 다 올리고 본다"가 되어 단계가 선택이 아니게 된다.
        /// 써 본 만큼만 올라가므로, 주력으로 쓰는 훈련이 자연히 먼저 커진다.
        /// </summary>
        public static int RepsToAdvance(int stage) => 1 << (Mathf.Clamp(stage, 1, Max - 1) - 1);

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
