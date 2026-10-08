using DogShop.Data;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 견종별 성장 특성. 훈련·운동으로 오르는 양에 <b>곱한다</b>(+10% 면 1.1배).
    /// 견종 고르기 카드의 별과 특성 글도 여기서 읽는다 — 수치와 보이는 것이 어긋나지 않게 한 곳에 둔다.
    ///
    /// 순서는 <see cref="DogManager"/> 의 견종 순서(닥스훈트, 비글, 시바견, 웰시코기, 잭 러셀 테리어, 프렌치 불독)와 같다.
    /// 건강·청결은 견종 보정이 없다.
    /// </summary>
    public static class BreedTraits
    {
        /// <summary>견종마다 { 미모, 훈련도, 어질리티 } 보정(%).</summary>
        static readonly float[,] Bonus =
        {
            {  7.5f,  7.5f, -5f   },   // 닥스훈트
            { -5f,    5f,   10f   },   // 비글
            { 10f,   -5f,    5f   },   // 시바견
            {  7.5f, -5f,    7.5f },   // 웰시코기
            { -2.5f, -2.5f, 15f   },   // 잭 러셀 테리어
            { 10f,    5f,   -5f   },   // 프렌치 불독
        };

        static readonly string[] Traits =
        {
            "외모와 훈련에 강하지만 운동은 조금 약합니다",
            "운동과 훈련에 강하지만 외모 관리는 조금 약합니다",
            "외모에 강하지만 훈련에 조금 약합니다",
            "외모와 운동에 강하지만 훈련에 조금 약합니다",
            "운동 능력이 최고지만 외모와 훈련은 조금 약합니다",
            "외모에 아주 강하고 훈련도 잘하지만 운동은 조금 약합니다",
        };

        /// <summary>별 5개가 되는 보정(%). 0% 가 별 3개, 그 사이는 직선으로.</summary>
        public const float FullStarBonus = 15f;

        public static float BonusPercent(int breed, GrowthAxis axis)
        {
            if (breed < 0 || breed >= Bonus.GetLength(0)) return 0f;
            switch (axis)
            {
                case GrowthAxis.Beauty: return Bonus[breed, 0];
                case GrowthAxis.Training: return Bonus[breed, 1];
                case GrowthAxis.Agility: return Bonus[breed, 2];
                default: return 0f;
            }
        }

        public static float Multiplier(int breed, GrowthAxis axis) => 1f + BonusPercent(breed, axis) * 0.01f;

        /// <summary>보정 → 별 개수(0.5 단위). 0% = 3개, +15% = 5개, -15% = 1개.</summary>
        public static float Stars(float bonusPercent) =>
            Mathf.Clamp(Mathf.Round((3f + bonusPercent / FullStarBonus * 2f) * 2f) * 0.5f, 0.5f, 5f);

        public static string TraitText(int breed) => breed >= 0 && breed < Traits.Length ? Traits[breed] : "";

        /// <summary>"+7.5%" / "-5%" / "0%".</summary>
        public static string Format(float bonusPercent) =>
            (bonusPercent > 0f ? "+" : "") + bonusPercent.ToString("0.#") + "%";
    }
}
