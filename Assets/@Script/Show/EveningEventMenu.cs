using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// 잠든 뒤 뜨는 마감 패널. 정산 요약 → (5일마다) 모의 심사 → 다음 날 아침.
    /// D30에는 챔피언십 결과와 최종 랭크가 나오고 게임이 끝난다.
    ///
    /// 예전에는 여기서 저녁 이벤트 카드(기부·봉사·폐품·휴식)를 한 장 골랐다. 명성이
    /// 손님 방문으로 쌓이게 바뀌면서 뺐다 — 매일 같은 카드를 누르는 의례였고,
    /// 명성을 사는 버튼이 따로 있으면 손님을 받는 것과 명성이 따로 놀았다.
    /// </summary>
    public class EveningEventMenu : MonoBehaviour
    {
        const float Width = 620f;
        const float RowHeight = 26f;
        const float Pad = 12f;

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
        /// 명성은 낮 동안 손님마다 이미 붙었고 여기서는 오늘 쌓인 양(저장됨)을 다시 적을 뿐이라,
        /// 명성이 두 번 붙지 않는다.
        /// </summary>
        void HandleLoaded()
        {
            if (TimeManager.Instance == null || !TimeManager.Instance.IsDayOver) return;

            HandleSettled(CustomerManager.Instance != null ? CustomerManager.Instance.ReputationToday : 0);
        }

        void HandleSettled(int reputationGained)
        {
            CustomerManager c = CustomerManager.Instance;
            GameManager g = GameManager.Instance;
            ChampionshipManager champ = ChampionshipManager.Instance;

            settlementLine = "Day " + g.Day + " 마감    매출 " + g.DailyRevenue
                           + "    판매 " + c.SoldToday + "건    놓침 " + c.LostToday + "건"
                           + "    방문 " + c.VisitorsToday + "명 · 명성 +" + reputationGained + " (누적 " + g.Reputation + ")";

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

        /// <summary>다음 날 아침으로. 버튼과 계측 모드(MeasurementMode)가 부른다.</summary>
        public void Continue()
        {
            if (!open) return;

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

            // 정산 한 줄 + (모의 심사) + 버튼. 최종일은 결과 두 줄이 더 붙는다
            int rows = 2 + (mockLine.Length > 0 ? 1 : 0) + (isFinal ? 2 : 0);
            float height = Pad * 3f + RowHeight * rows + (isFinal ? Pad : 0f);
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
            if (GUI.Button(new Rect(ix, iy, iw, RowHeight), "다음 날 아침으로 — Day " + (GameManager.Instance.Day + 1), rowStyle)) Continue();
        }
    }
}
