using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// <b>마스코트 등급</b>. 강아지는 가게의 얼굴이다 — 미모·훈련도·어질리티 총합이 구간을 넘을 때마다 등급이 오르고,
    /// 가게 레벨업 일부는 이 등급을 요구한다(<see cref="Data.ShopLevelDef.heroStatGate"/> = 그 등급의 문턱 총합).
    /// "강아지가 커야 가게가 큰다"를 규칙으로 보장하는 장치다. 매출은 직접 건드리지 않는다(훈련 경제 균형).
    /// </summary>
    public static class MascotGrade
    {
        /// <summary>등급별 문턱(성장 스탯 총합). 가게 레벨 표의 heroStatGate 값과 같은 숫자를 쓴다.</summary>
        public static readonly int[] Thresholds = { 0, 40, 150, 260, 400 };
        public static readonly string[] Names = { "새내기", "동네 강아지", "인기 강아지", "스타 강아지", "챔피언 후보" };

        /// <summary>등급이 오를 때 띄우는 한 줄. 손님들 반응으로 말한다.</summary>
        public static readonly string[] PromoteLines =
        {
            "",
            "동네 손님들이 이름을 부르기 시작했다",
            "손님들이 강아지를 보러 찾아온다",
            "가게 앞에 사진 찍는 사람이 생겼다",
            "온 동네가 챔피언십을 응원한다",
        };

        public static readonly Color[] Colors =
        {
            new Color(0.78f, 0.72f, 0.64f),
            new Color(0.55f, 0.78f, 0.52f),
            new Color(0.42f, 0.66f, 0.92f),
            new Color(0.96f, 0.72f, 0.25f),
            new Color(0.93f, 0.45f, 0.62f),
        };

        static Texture2D[] badges;

        /// <summary>등급 배지 그림(바르코, Resources/T_UI_Mascot_0~4). 없으면 null — 색 띠만 그린다.</summary>
        public static Texture2D Badge(int grade)
        {
            if (badges == null)
            {
                badges = new Texture2D[Names.Length];
                for (int i = 0; i < badges.Length; i++) badges[i] = Resources.Load<Texture2D>("T_UI_Mascot_" + i);
            }
            return grade >= 0 && grade < badges.Length ? badges[grade] : null;
        }

        public static int Total(DogStats s) => s == null ? 0 : s.Beauty + s.Training + s.Agility;

        public static int GradeOf(int total)
        {
            int g = 0;
            for (int i = 1; i < Thresholds.Length; i++) if (total >= Thresholds[i]) g = i;
            return g;
        }

        public static bool IsMax(int grade) => grade >= Thresholds.Length - 1;

        /// <summary>그 문턱 총합을 넘는 첫 등급의 이름. 레벨업 조건 문구에 쓴다.</summary>
        public static string NameForThreshold(int threshold) => Names[GradeOf(threshold)];
    }
}
