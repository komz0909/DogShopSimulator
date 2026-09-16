using DogShop.Dogs;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// D30 챔피언십 1회. 심사 가중치는 게임 시작 시 결정되어 공개되고, 30일 내내 육성 목표가 된다.
    /// 5일마다 열리는 모의 심사는 같은 채점 함수를 그대로 호출하고 보상은 주지 않는다 — 학습 기회만 준다.
    /// </summary>
    public class ChampionshipManager : MonoBehaviour
    {
        public const int FinalDay = 30;
        public const int MockInterval = 5;
        public const int HealthGate = 70;
        public const int CleanlinessPenaltyBelow = 50;
        public const float CleanlinessPenalty = 0.2f;

        /// <summary>±3%. ±10%면 실력 마진이 노이즈에 묻혀 동전던지기가 된다.</summary>
        public const float RandomSpread = 0.03f;

        public static ChampionshipManager Instance { get; private set; }

        /// <summary>
        /// NPC 기준선. <b>11차 무인 측정 실측</b>에 맞춰 내렸다 — 30일 자동 플레이가
        /// L5 도달, 성장 155(점수 78)였는데 옛 기준선 210~100 으로는 전원에게 졌다.
        ///
        /// 지금 값의 의도:
        ///   L1에 머무는 플레이(점수 ~60)  -> 전원 패배. "레벨을 올려야 이긴다"
        ///   훈련을 10일 놓친 자동 플레이(78) -> 5위
        ///   훈련을 놓치지 않는 플레이(120~140) -> 2~3위
        ///   레벨과 훈련 둘 다 최적(150+)  -> 우승
        ///
        /// 아직 <b>임시값</b>이다. 사람이 직접 플레이할 수 있게 되면 그때 실측으로 다시 맞춘다.
        /// </summary>
        static readonly int[] RivalScores = { 150, 125, 100, 80, 62 };

        /// <summary>50/50은 두 축 구분을 무의미하게 만들므로 후보에서 제외한다.</summary>
        static readonly int[] BeautyWeightOptions = { 70, 60, 40, 30 };

        public int BeautyWeight { get; private set; } = 70;
        public int TrainingWeight => 100 - BeautyWeight;
        public int RivalCount => RivalScores.Length;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            BeautyWeight = BeautyWeightOptions[Random.Range(0, BeautyWeightOptions.Length)];
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public string WeightText => "미모 " + BeautyWeight + " / 훈련도 " + TrainingWeight;

        public bool IsMockDay(int day) => day % MockInterval == 0 && day < FinalDay;
        public bool IsFinalDay(int day) => day >= FinalDay;

        public bool CanEnter(Dog dog, out string reason)
        {
            if (dog == null) { reason = "출전견 없음"; return false; }
            if (dog.Stats.Health < HealthGate)
            {
                reason = "건강 " + dog.Stats.Health + " — " + HealthGate + " 미만은 출전 불가";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>총점 = 미모 x 가중치 + 훈련도 x 가중치. 성장 스탯만 심사한다.</summary>
        public int Score(Dog dog, bool applyRandom)
        {
            if (dog == null) return 0;

            DogStats st = dog.Stats;
            float raw = st.Beauty * BeautyWeight * 0.01f + st.Training * TrainingWeight * 0.01f;

            if (st.Cleanliness < CleanlinessPenaltyBelow) raw *= 1f - CleanlinessPenalty;
            if (applyRandom) raw *= 1f + Random.Range(-RandomSpread, RandomSpread);

            return Mathf.RoundToInt(raw);
        }

        public int Rank(int score)
        {
            int rank = 1;
            for (int i = 0; i < RivalScores.Length; i++)
                if (RivalScores[i] > score) rank++;
            return rank;
        }

        public int RivalScore(int index) => RivalScores[Mathf.Clamp(index, 0, RivalScores.Length - 1)];

        /// <summary>엔딩 랭크 = 챔피언십 순위 + 최종 가게 레벨 + 총 자산.</summary>
        public string FinalGrade(int rank, int shopLevel, int money)
        {
            int points = 0;

            if (rank == 1) points += 3;
            else if (rank == 2) points += 2;
            else if (rank == 3) points += 1;

            if (shopLevel >= 10) points += 3;
            else if (shopLevel >= 8) points += 2;
            else if (shopLevel >= 6) points += 1;

            if (money >= 5000) points += 2;
            else if (money >= 2000) points += 1;

            if (points >= 7) return "S";
            if (points >= 5) return "A";
            if (points >= 3) return "B";
            return "C";
        }
    }
}
