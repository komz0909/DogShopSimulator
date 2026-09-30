using UnityEngine;

namespace DogShop.Data
{
    /// <summary>
    /// 물건 등급. 랜덤박스에서만 나온다 — 발주로 들여온 물건은 등급이 없다(<see cref="ItemGrade.None"/>).
    ///
    /// 등급은 <b>같은 상품의 값을 바꾼다.</b> 기본 사료 S등급은 기본 사료 정가의 5배에 팔린다.
    /// 다른 상품이 되는 것이 아니라 "좋은 개체"가 나온 것이다.
    /// </summary>
    public enum ItemGrade
    {
        None = -1,
        F = 0,
        E = 1,
        D = 2,
        C = 3,
        B = 4,
        A = 5,
        S = 6
    }

    public static class ItemGrades
    {
        /// <summary>등급 수(None 제외).</summary>
        public const int Count = 7;

        /// <summary>F부터 S까지 뽑힐 확률(%). 합이 100이다.</summary>
        static readonly float[] Chance = { 50f, 25f, 10f, 7f, 5f, 2.5f, 0.5f };

        /// <summary>
        /// 정가에 곱하는 배수. 1.0인 등급이 없으므로 발주품(None)과 섞이지 않는다.
        ///
        /// 위쪽을 눌러 놓았다(A 3배 -> 2.5배, S 5배 -> 3배). S 하나가 매출을 통째로
        /// 흔들면 상자가 "가게를 키우는 여러 선택지 중 하나"가 아니라 정답이 된다.
        /// </summary>
        static readonly float[] Multiplier = { 0.6f, 0.8f, 1.1f, 1.5f, 2f, 2.5f, 3f };

        /// <summary>등급 색. 값이 아니라 <b>희소함</b>을 나타내므로 위로 갈수록 뜨거워진다.</summary>
        static readonly Color[] Tint =
        {
            new Color(0.62f, 0.64f, 0.66f),   // F 회색
            new Color(0.60f, 0.72f, 0.60f),   // E 풀색
            new Color(0.55f, 0.72f, 0.85f),   // D 하늘
            new Color(0.55f, 0.60f, 0.90f),   // C 파랑
            new Color(0.72f, 0.55f, 0.90f),   // B 보라
            new Color(0.95f, 0.72f, 0.35f),   // A 주황
            new Color(1.00f, 0.42f, 0.42f),   // S 빨강
        };

        /// <summary>이 등급부터 반짝인다. 아래 등급까지 반짝이면 반짝임이 신호 노릇을 못 한다.</summary>
        public const ItemGrade SparkleFrom = ItemGrade.B;

        public static string NameOf(ItemGrade grade) =>
            grade == ItemGrade.None ? "" : grade.ToString();

        public static float MultiplierOf(ItemGrade grade) =>
            grade == ItemGrade.None ? 1f : Multiplier[(int)grade];

        public static Color ColorOf(ItemGrade grade) =>
            grade == ItemGrade.None ? Color.white : Tint[(int)grade];

        public static float ChanceOf(ItemGrade grade) =>
            grade == ItemGrade.None ? 0f : Chance[(int)grade];

        public static bool Sparkles(ItemGrade grade) =>
            grade != ItemGrade.None && grade >= SparkleFrom;

        /// <summary>
        /// 값이 비싼 쪽이 큰 값. 등급 <b>열거형 순서로 비교하면 안 된다</b> —
        /// F(0.6배)와 E(0.8배)는 발주품(1.0배)보다 싸므로, 열거형으로 재면
        /// 손님이 85원짜리를 두고 51원짜리를 먼저 집어 간다.
        /// </summary>
        public static float ValueOf(ItemGrade grade) => MultiplierOf(grade);

        /// <summary>
        /// 레벨당 위쪽 등급에 실리는 가중치. 등급 한 칸 올라갈 때마다 곱해진다.
        ///
        /// 0.04 는 <b>조금씩</b>이다 — L10 에서 S가 0.5%에서 2% 남짓이 된다.
        /// 더 키우면 후반에 상자가 뽑기가 아니라 확정 수입이 되고,
        /// 더 줄이면 가게를 키운 보람이 상자에 안 나타난다.
        /// </summary>
        const float TiltPerLevel = 0.04f;

        /// <summary>그 레벨에서 각 등급이 뽑힐 상대 가중치.</summary>
        public static float WeightOf(ItemGrade grade, int shopLevel)
        {
            if (grade == ItemGrade.None) return 0f;

            float step = 1f + TiltPerLevel * Mathf.Max(0, shopLevel - 1);
            return Chance[(int)grade] * Mathf.Pow(step, (int)grade);
        }

        /// <summary>그 레벨에서 각 등급이 뽑힐 확률(%).</summary>
        public static float ChanceOf(ItemGrade grade, int shopLevel)
        {
            float total = 0f;
            for (int i = 0; i < Count; i++) total += WeightOf((ItemGrade)i, shopLevel);

            return total > 0f ? WeightOf(grade, shopLevel) / total * 100f : 0f;
        }

        /// <summary>
        /// 확률표대로 하나 뽑는다. 표를 더하며 훑으므로 확률을 고쳐도 여기는 손댈 필요가 없다.
        ///
        /// <b>가게 레벨이 오르면 위쪽 등급이 조금씩 잘 나온다.</b> 상자 값도 레벨에 따라
        /// 오르므로, 값만 오르고 나오는 것은 그대로면 후반에 상자가 순수한 손해가 된다.
        /// </summary>
        public static ItemGrade Roll(int shopLevel = 1)
        {
            float total = 0f;
            for (int i = 0; i < Count; i++) total += WeightOf((ItemGrade)i, shopLevel);

            float pick = Random.value * total;
            for (int i = 0; i < Count; i++)
            {
                pick -= WeightOf((ItemGrade)i, shopLevel);
                if (pick <= 0f) return (ItemGrade)i;
            }
            return ItemGrade.F;
        }

        /// <summary>그 등급이 붙었을 때의 판매가. 반올림은 여기 한 곳에서만 한다.</summary>
        public static int PriceOf(int retail, ItemGrade grade) =>
            Mathf.Max(1, Mathf.RoundToInt(retail * MultiplierOf(grade)));
    }
}
