using System;
using UnityEngine;

namespace DogShop.Data
{
    public enum GrowthAxis { Training, Beauty }

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
        public int cost = 20;
        public int gain = 1;
        [Min(1)] public int unlockLevel = 1;
    }

    [CreateAssetMenu(menuName = "DogShop/Training Catalog", fileName = "TrainingCatalog")]
    public class TrainingCatalog : ScriptableObject
    {
        [SerializeField] TrainingDef[] entries = new TrainingDef[0];

        public int Count => entries.Length;
        public TrainingDef Get(int index) => entries[index];
    }
}
