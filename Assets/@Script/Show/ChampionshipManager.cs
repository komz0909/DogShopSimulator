using System.Collections.Generic;
using DogShop.Core;
using DogShop.Dogs;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// 시즌마다 챔피언십 1회(시즌 = <see cref="FinalDay"/>일, 시즌 1 은 30일차·시즌 2 는 60일차…).
    /// 엔딩 뒤 [다음 시즌 도전하기]로 이어 가고, 시즌마다 라이벌 도그쇼 점수가 오른다(<see cref="RivalBonus"/>).
    /// 심사 가중치는 시즌 시작 때 결정되어 공개되고, 그 시즌 내내 육성 목표가 된다.
    /// 5일마다 열리는 모의 심사는 같은 채점 함수를 그대로 호출하고 보상은 주지 않는다 — 학습 기회만 준다.
    /// </summary>
    public class ChampionshipManager : MonoBehaviour, ISaveParticipant
    {
        /// <summary>한 시즌의 길이(일). 시즌 1 챔피언십 날이기도 하다.</summary>
        public const int FinalDay = 30;
        public const int MockInterval = 5;
        public const int HealthGate = 70;
        public const int CleanlinessPenaltyBelow = 50;
        public const float CleanlinessPenalty = 0.2f;

        /// <summary>±3%. ±10%면 실력 마진이 노이즈에 묻혀 동전던지기가 된다.</summary>
        public const float RandomSpread = 0.03f;

        public static ChampionshipManager Instance { get; private set; }

        /// <summary>
        /// NPC 기준선. <b>43차 무인 측정 실측</b>에 맞춰 1.79배로 올렸다 —
        /// 훈련 단계 체계가 들어오면서 30일 자동 플레이의 점수가 242에서 <b>433</b>이 됐다.
        /// 옛 기준선(1위 150)으로는 완벽한 플레이가 2.9배로 이겨서 승부가 아니었다.
        ///
        /// 비율은 그대로 뒀다 — 1위가 자동 플레이 점수의 62%다. 그래야
        /// "둘 다 최적이면 이기고 대충 하면 진다"가 성립한다.
        ///
        /// 지금 값의 의도:
        ///   L1에 머무는 플레이        -> 전원 패배. "레벨을 올려야 이긴다"
        ///   훈련을 자주 놓친 플레이     -> 4~5위
        ///   훈련을 놓치지 않는 플레이    -> 2~3위
        ///   레벨·훈련·단계 모두 최적(270+) -> 우승
        ///
        /// 아직 <b>임시값</b>이다. 봇은 손실률 1.0%로 진열을 완벽히 하지만 사람은 그러지
        /// 못한다. 직접 플레이할 수 있게 되면 그때 다시 맞춘다 — 빡빡하면 내리면 된다.
        /// </summary>
        static readonly int[] RivalScores = { 270, 225, 180, 145, 110 };

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

        public bool IsMockDay(int day) => day % MockInterval == 0 && day < LastDay;
        /// <summary>이번 시즌의 도그쇼 날. 본편은 시즌 × 30일차(30·60·90…), 시연 모드 3일차.</summary>
        public static int LastDay => Exhibition.Active ? Exhibition.Days : (Instance != null ? Instance.Season : 1) * FinalDay;

        // ---- 시즌(2026-10-30) ----

        /// <summary>지금 시즌(1부터).</summary>
        public int Season { get; private set; } = 1;

        /// <summary>시즌별 챔피언십 순위(-1 = 출전하지 못함). 지난 시즌까지만 들어 있다.</summary>
        readonly List<int> seasonRanks = new List<int>();
        public IReadOnlyList<int> SeasonRanks => seasonRanks;
        public int Trophies { get { int n = 0; foreach (int r in seasonRanks) if (r == 1) n++; return n; } }

        /// <summary>시즌마다 라이벌 도그쇼 점수가 이만큼 오른다. 1위 라이벌 86 → 90 → 94 → 98 에서 멈춘다.</summary>
        public const int RivalBonusPerSeason = 4;
        public const int MaxRivalBonus = 12;
        public int RivalBonus => Exhibition.Active ? 0 : Mathf.Min(MaxRivalBonus, (Season - 1) * RivalBonusPerSeason);

        /// <summary>
        /// 도그쇼가 능력치를 0~1 로 펼 때 쓰는 만점 기준. 시즌 1 은 240(46차 측정: 30일 최적 플레이가 미모·훈련도 각 220 안팎),
        /// 시즌마다 +80, 시즌 4 부터 480. 능력치 자체에는 상한이 없지만 만점을 넘긴 몫은 도그쇼에 안 들어가므로,
        /// 만점이 오르면 같은 강아지라도 무대가 다시 어려워져 계속 키워야 한다. 시연은 240.
        /// </summary>
        public const int BaseStatFull = 240;
        public const int StatFullPerSeason = 80;
        public const int MaxStatFull = 480;
        public static int StatFullFor(int season) => Mathf.Min(MaxStatFull, BaseStatFull + (Mathf.Max(1, season) - 1) * StatFullPerSeason);
        public static float StatFull => Exhibition.Active || Instance == null ? BaseStatFull : StatFullFor(Instance.Season);

        // ---- 우승 혜택(명예의 벽) ----

        /// <summary>우승한 시즌의 기록. 가게 뒷벽 액자(<see cref="ChampionWall"/>)에 걸린다.</summary>
        [System.Serializable]
        public class ChampionRecord
        {
            public int season;
            public int breedIndex;
            public string dogName;
        }

        readonly List<ChampionRecord> champions = new List<ChampionRecord>();
        public IReadOnlyList<ChampionRecord> Champions => champions;

        /// <summary>우승 상금 — 우승한 그 자리에서 바로 받는다.</summary>
        public const int ChampionPrize = 1000;

        /// <summary>트로피 하나당 매장 판매가 보너스. 최대 <see cref="MaxBonusTrophies"/>개까지(+15%).</summary>
        public const float SaleBonusPerTrophy = 0.05f;
        public const int MaxBonusTrophies = 3;

        /// <summary>지금 매장 판매가 보너스(0.05 = +5%). 시연에는 없다.</summary>
        public static float SaleBonus => Exhibition.Active || Instance == null ? 0f
            : Mathf.Min(MaxBonusTrophies, Instance.Trophies) * SaleBonusPerTrophy;

        /// <summary>판매가에 챔피언 보너스를 얹는다(CustomerManager.PayoutOf).</summary>
        public static int WithSaleBonus(int retail)
        {
            float bonus = SaleBonus;
            return bonus > 0f ? Mathf.RoundToInt(retail * (1f + bonus)) : retail;
        }

        /// <summary>
        /// 이번 시즌 순위를 적는다(시즌 결산 판이 뜰 때). 한 시즌에 한 번만.
        /// 우승이면 명예의 벽에 액자를 걸고 상금을 준다.
        /// </summary>
        public void RecordSeason(int rank)
        {
            if (seasonRanks.Count >= Season) return;
            seasonRanks.Add(rank);
            if (rank != 1 || Exhibition.Active) return;

            Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            champions.Add(new ChampionRecord
            {
                season = Season,
                breedIndex = hero != null ? hero.BreedIndex : 0,
                dogName = hero != null ? hero.DisplayName : "강아지",
            });
            if (GameManager.Instance != null) GameManager.Instance.AddMoney(ChampionPrize);
        }

        /// <summary>다음 시즌으로. 가게·강아지는 그대로, 순위표·심사 가중치만 새로.</summary>
        public void BeginNextSeason()
        {
            RecordSeason(HasShowResult ? ShowRank : -1);
            Season++;
            ShowBoard = null;
            ShowRank = 0;
            ShowScore = 0;
            BeautyWeight = BeautyWeightOptions[Random.Range(0, BeautyWeightOptions.Length)];
        }

        public void CaptureInto(SaveData data)
        {
            data.season = Season;
            data.seasonRanks = seasonRanks.ToArray();
            data.champions = champions.ToArray();
        }

        public void RestoreFrom(SaveData data)
        {
            // 시즌이 없던 세이브는 0 — 날짜로 맞춘다(30일차까지 시즌 1)
            Season = Mathf.Max(Mathf.Max(1, data.season), (data.day - 1) / FinalDay + 1);
            seasonRanks.Clear();
            if (data.seasonRanks != null) seasonRanks.AddRange(data.seasonRanks);
            champions.Clear();
            if (data.champions != null) champions.AddRange(data.champions);
        }

        public bool IsFinalDay(int day) => day >= LastDay;

        public bool CanEnter(Dog dog, out string reason)
        {
            if (dog == null) { reason = "출전견 없음"; return false; }
            if (dog.Stats.Health < HealthGate)
            {
                reason = "건강 " + dog.Stats.Health + " (" + HealthGate + " 미만은 출전 불가)";
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

        // ---- D30 도그쇼 순위 ----
        // 순위는 도그쇼 총점(100점 만점)으로만 정한다 — 잘 키운 강아지를 무대에서 평가받는 게임이다.
        // 육성은 미니게임 쪽으로 들어간다(미모 → 포즈 판정 폭, 훈련도 → 물어오기 정확도, 어질리티 → 어질리티 속도·점프, 셋의 평균 → 장애물 달리기 속도).
        // 위의 스탯 점수(Score·Rank·RivalScores)는 도그쇼를 건너뛰는 무인 측정에서만 쓴다.

        public class ShowEntry
        {
            public string name;
            public string breed;
            public int breedIndex;
            public int score;
            public bool hero;
        }

        /// <summary>라이벌 다섯 자리의 기준 점수(±<see cref="RivalShowSpread"/>). <b>임시값</b> — 직접 해 보고 맞춘다.</summary>
        static readonly int[] RivalShowScores = { 86, 79, 71, 62, 52 };
        const int RivalShowSpread = 3;

        static readonly string[] RivalNames =
            { "초코", "콩이", "보리", "두부", "몽이", "코코", "루이", "별이", "탄이", "호두", "밤이", "쿠키", "모카", "뭉치", "까미", "해피" };

        /// <summary>도그쇼 순위표(1위부터). 쇼를 마치기 전에는 null.</summary>
        public List<ShowEntry> ShowBoard { get; private set; }
        public int ShowRank { get; private set; }
        public int ShowScore { get; private set; }
        public bool HasShowResult => ShowBoard != null;

        /// <summary>
        /// 도그쇼 총점으로 순위표를 만든다. 주인공 견종을 뺀 나머지 견종이 하나씩 나오고,
        /// 이름은 임의로, 점수는 기준 점수에 조금씩 흔들림을 준다. 같은 점수면 주인공이 앞.
        /// </summary>
        public void SetShowResult(Dog hero, int score)
        {
            DogManager dm = DogManager.Instance;
            var board = new List<ShowEntry>
            {
                new ShowEntry { name = hero.DisplayName, breed = hero.BreedKo, breedIndex = hero.BreedIndex, score = score, hero = true },
            };

            var names = new List<string>(RivalNames);
            names.Remove(hero.DisplayName);
            var breeds = new List<int>();
            for (int b = 0; b < dm.BreedCount; b++) if (b != hero.BreedIndex) breeds.Add(b);
            Shuffle(names);
            Shuffle(breeds);   // 어느 견종이 1위 자리에 서는지는 판마다 다르다

            for (int i = 0; i < breeds.Count; i++)
            {
                int[] rivals = Exhibition.Active ? Exhibition.RivalShowScores : RivalShowScores;
                int baseScore = rivals[Mathf.Min(i, rivals.Length - 1)];
                board.Add(new ShowEntry
                {
                    name = names[i % names.Count],
                    breed = dm.BreedName(breeds[i]),
                    breedIndex = breeds[i],
                    score = Mathf.Clamp(baseScore + RivalBonus + Random.Range(-RivalShowSpread, RivalShowSpread + 1), 0, 100),
                });
            }

            board.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : b.hero.CompareTo(a.hero));
            if (Exhibition.Active) KeepPodium(board);
            ShowBoard = board;
            ShowScore = score;
            ShowRank = board.FindIndex(e => e.hero) + 1;
        }

        /// <summary>
        /// 시연은 최소 <see cref="Exhibition.MinShowRank"/>등에 든다 — 처음 해 본 관람객이 꼴찌로 끝나면 게임 인상이 나빠진다.
        /// 주인공보다 위에 있는 라이벌 중 메달권 밖으로 밀려날 자리를 주인공 점수보다 조금 낮게 바꾼다. 1등은 실력으로만.
        /// </summary>
        static void KeepPodium(List<ShowEntry> board)
        {
            int heroAt = board.FindIndex(e => e.hero);
            int keepAbove = Exhibition.MinShowRank - 1;
            if (heroAt <= keepAbove) return;
            int heroScore = board[heroAt].score;
            for (int i = keepAbove; i < heroAt; i++)
                board[i].score = Mathf.Max(0, heroScore - 1 - (i - keepAbove) * 2 - Random.Range(0, 2));   // 2점 간격이라 서로 겹치지 않는다
            board.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : b.hero.CompareTo(a.hero));
        }

        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
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
