using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// 18:00 마감 패널. 정산 요약 → (5일마다) 모의 심사 → 저녁 이벤트 카드 1장 선택 → 다음 날.
    /// D30에는 카드 대신 챔피언십 결과와 최종 랭크가 나오고 게임이 끝난다.
    /// 새 씬·이동 영역·애니메이션 없음 — 이것이 가게 밖 맵을 대체하는 설계다.
    /// </summary>
    public class EveningEventMenu : MonoBehaviour
    {
        const float Width = 620f;
        const float RowHeight = 26f;
        const float Pad = 12f;

        static readonly string[] CardTitles =
        {
            "유기견 센터 기부 — 사료 1개를 내주고 명성 +8",
            "유기견 센터 봉사 — 명성 +4",
            "폐품 수집 — 상품 재고 +3",
            "휴식 — 반려견 청결·건강 +12"
        };

        bool open;
        bool isFinal;
        bool finished;

        string settlementLine = "";
        string mockLine = "";
        string resultLine = "";
        string gradeLine = "";

        GUIStyle headerStyle;
        GUIStyle rowStyle;
        GUIStyle dimStyle;
        GUIStyle bigStyle;

        void Start()
        {
            GameManager.Instance.OnDaySettled += HandleSettled;
            if (SaveManager.Instance != null) SaveManager.Instance.OnLoaded += HandleLoaded;
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnDaySettled -= HandleSettled;
            if (SaveManager.Instance != null) SaveManager.Instance.OnLoaded -= HandleLoaded;
        }

        /// <summary>
        /// 마감된 상태로 저장한 세이브를 불러오면 카드를 <b>다시 띄운다</b>.
        ///
        /// 이 카드는 이벤트로만 열렸고 저장되지 않는다. 그래서 카드가 떠 있을 때 저장한 사람이
        /// 불러오면 "하루는 끝났는데 고를 카드가 없는" 상태가 됐다 — 날짜를 넘기는 길은
        /// 카드뿐이고 그 사이 모든 행동은 "영업 종료"로 거절되므로 <b>게임이 멈춘다</b>.
        ///
        /// 명성 정산액은 저장된 그날 매출에서 다시 계산한다. 정산 자체는 마감 때 이미 끝났고
        /// 여기서는 같은 수를 화면에 다시 적을 뿐이라, 명성이 두 번 붙지 않는다.
        /// </summary>
        void HandleLoaded()
        {
            if (TimeManager.Instance == null || !TimeManager.Instance.IsDayOver) return;

            HandleSettled(GameManager.Instance.DailyRevenue / GameManager.RevenuePerReputation);
        }

        void HandleSettled(int reputationGained)
        {
            CustomerManager c = CustomerManager.Instance;
            GameManager g = GameManager.Instance;
            ChampionshipManager champ = ChampionshipManager.Instance;

            settlementLine = "Day " + g.Day + " 마감    매출 " + g.DailyRevenue
                           + "    판매 " + c.SoldToday + "건    놓침 " + c.LostToday + "건"
                           + "    명성 +" + reputationGained + " (누적 " + g.Reputation + ")";

            Dog hero = DogManager.Instance.Hero;
            isFinal = champ.IsFinalDay(g.Day);
            mockLine = "";
            resultLine = "";
            gradeLine = "";

            if (isFinal) BuildFinalResult(hero, champ, g);
            else if (champ.IsMockDay(g.Day)) BuildMockResult(hero, champ);

            open = true;


            PointerMenus.SetOpen(this, true);
        }

        void BuildMockResult(Dog hero, ChampionshipManager champ)
        {
            string reason;
            if (!champ.CanEnter(hero, out reason))
            {
                mockLine = "모의 심사 — " + reason;
                return;
            }

            int score = champ.Score(hero, false);
            int rank = champ.Rank(score);
            mockLine = "모의 심사 — 총점 " + score + " → 챔피언십에서 " + rank + "위 예상"
                     + "   (1위 기준선 " + champ.RivalScore(0) + ", 심사 " + champ.WeightText + ")";
        }

        void BuildFinalResult(Dog hero, ChampionshipManager champ, GameManager g)
        {
            int level = ShopLevelManager.Instance.Level;

            string reason;
            if (!champ.CanEnter(hero, out reason))
            {
                resultLine = "챔피언십 출전 불가 — " + reason;
                gradeLine = "최종 랭크  " + champ.FinalGrade(champ.RivalCount + 1, level, g.Money);
                return;
            }

            int score = champ.Score(hero, true);
            int rank = champ.Rank(score);

            resultLine = "챔피언십 결과 — 총점 " + score + "   " + rank + "위 / " + (champ.RivalCount + 1) + "명"
                       + "   (심사 " + champ.WeightText + ")";
            gradeLine = "최종 랭크  " + champ.FinalGrade(rank, level, g.Money)
                      + "     가게 Lv " + level + "     자산 " + g.Money + "원"
                      + "     " + hero.BreedKo + " 미모 " + hero.Stats.Beauty + " / 훈련도 " + hero.Stats.Training;
        }

        /// <summary>계측 모드(MeasurementMode)가 무인 진행을 위해 호출한다.</summary>
        public void PickCard(int index)
        {
            DogManager dm = DogManager.Instance;

            if (index == 0)
            {
                // 재고를 내주는 카드. 봉사(공짜 +4)보다 명성이 크지만 팔 물건이 줄어든다 —
                // 카드 넷 중 유일하게 값을 치르는 선택이다.
                bool donated = InventoryManager.Instance.TryConsumeStorage(0);
                if (donated) GameManager.Instance.AddReputation(8);

                settlementLine = donated
                    ? "사료 한 포대를 기부했다 — 명성 +8"
                    : "창고에 내줄 사료가 없었다";
            }
            else if (index == 1)
            {
                GameManager.Instance.AddReputation(4);
            }
            else if (index == 2)
            {
                InventoryManager inv = InventoryManager.Instance;
                int level = ShopLevelManager.Instance.Level;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int i = Random.Range(0, inv.Catalog.Count);
                    if (inv.Catalog.Get(i).unlockLevel > level) continue;
                    inv.Grant(i, 3);
                    break;
                }
            }
            else if (dm.Hero != null)
            {
                dm.Hero.Stats.RecoverCleanliness(12);
                dm.Hero.Stats.RecoverHealth(12);
            }

            open = false;


            PointerMenus.SetOpen(this, false);
            GameManager.Instance.BeginNextDay();
        }

        void OnGUI()
        {
            if (!open) return;

            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
                headerStyle.normal.textColor = new Color(1f, 0.9f, 0.55f);
                rowStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
                dimStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                dimStyle.normal.textColor = new Color(0.8f, 0.83f, 0.79f);
                bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                bigStyle.normal.textColor = new Color(0.6f, 1f, 0.8f);
            }

            int cardCount = isFinal ? 0 : CardTitles.Length;
            float height = Pad * 4f + RowHeight * (3 + cardCount) + (isFinal ? RowHeight : 0f);
            float x = (Screen.width - Width) * 0.5f;
            float y = Mathf.Max(140f, (Screen.height - height) * 0.4f);

            GUI.Box(new Rect(x, y, Width, height), GUIContent.none);

            float ix = x + Pad;
            float iw = Width - Pad * 2f;
            float iy = y + Pad;

            GUI.Label(new Rect(ix, iy, iw, RowHeight), settlementLine, headerStyle);
            iy += RowHeight;

            if (mockLine.Length > 0)
            {
                GUI.Label(new Rect(ix, iy, iw, RowHeight), mockLine, dimStyle);
                iy += RowHeight;
            }

            if (isFinal)
            {
                GUI.Label(new Rect(ix, iy, iw, RowHeight), resultLine, dimStyle);
                iy += RowHeight;
                GUI.Label(new Rect(ix, iy, iw, RowHeight + 4f), gradeLine, bigStyle);
                iy += RowHeight + Pad;

                if (!finished && GUI.Button(new Rect(ix, iy, iw, RowHeight), "게임 종료 — 30일 완주", rowStyle))
                {
                    finished = true;
                    open = false;
                    PointerMenus.SetOpen(this, false);
                }
                return;
            }

            iy += Pad;
            GUI.Label(new Rect(ix, iy - RowHeight * 0.7f, iw, RowHeight), "저녁에 무엇을 할까", dimStyle);

            for (int i = 0; i < CardTitles.Length; i++)
            {
                if (GUI.Button(new Rect(ix, iy, iw, RowHeight - 3f), CardTitles[i], rowStyle)) PickCard(i);
                iy += RowHeight;
            }
        }
    }
}
