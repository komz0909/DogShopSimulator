using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using DogShop.UI;
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

        bool open;
        bool isFinal;
        bool finished;

        string mockLine = "";
        string resultLine = "";
        string gradeLine = "";

        /// <summary>엔딩 컷신 글에 채울 값. 최종일 결과를 만들 때 같이 적는다.</summary>
        readonly System.Collections.Generic.Dictionary<string, string> endingValues =
            new System.Collections.Generic.Dictionary<string, string>();

        // ---- 정산서 그림(바르코) ----
        [Header("정산서")]
        [SerializeField] Texture2D board;        // 클립보드 종이(UI_SettlementBoard)
        [SerializeField] Texture2D iconIncome;   // 돈 주머니
        [SerializeField] Texture2D iconSpend;    // 지갑과 영수증
        [SerializeField] Texture2D iconDelivery; // 배송 트럭
        [SerializeField] Texture2D iconReputation; // 별
        [SerializeField] Texture2D iconCustomers;  // 손님 얼굴

        GUIStyle titleStyle, labelStyle, valueStyle, subStyle, noteStyle, netStyle, recommendStyle;

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

            // 정산서 숫자는 그릴 때마다 각 매니저에서 바로 읽는다(OnGUI) — 하루 끝 취소 벌점처럼
            // 이 이벤트 뒤에 붙는 변화까지 맞게 보이도록

            Dog hero = DogManager.Instance.Hero;
            isFinal = champ.IsFinalDay(g.Day);
            mockLine = "";
            resultLine = "";
            gradeLine = "";

            if (isFinal) BuildFinalResult(hero, champ, g);
            else if (champ.IsMockDay(g.Day)) BuildMockResult(hero, champ);

            // D30 에 도그쇼를 실제로 치렀으면(순위표를 이미 봤으면) 정산서는 건너뛰고 바로 엔딩 그림으로 간다 —
            // 순위 발표 뒤에 하루 정산서가 끼어들어 흐름이 끊겼다. 쇼를 건너뛴 경우(무인 측정·출전 불가)만 정산서로 결과를 보인다
            if (isFinal && champ.HasShowResult && !finished && Cutscene.Instance != null)
            {
                finished = true;
                Cutscene.Instance.PlayEnding(endingValues);
                return;
            }

            Sound.Play(Sfx.Settlement);
            open = true;


            PointerMenus.SetOpen(this, true);
        }

        void BuildMockResult(Dog hero, ChampionshipManager champ)
        {
            string reason;
            if (!champ.CanEnter(hero, out reason))
            {
                mockLine = "모의 심사: " + reason;
                return;
            }

            // 챔피언십 순위는 D30 도그쇼 총점으로만 정한다 — 여기서는 미니게임에 쓰일 몸 상태만 보여 준다
            mockLine = "모의 심사: 미모 " + hero.Stats.Beauty + " / 훈련도 " + hero.Stats.Training + " / 어질리티 " + hero.Stats.Agility + "  (도그쇼 난이도 반영)";
        }

        void BuildFinalResult(Dog hero, ChampionshipManager champ, GameManager g)
        {
            int level = ShopLevelManager.Instance.Level;

            // 도그쇼를 실제로 치렀으면 출전 자격은 따지지 않는다 — 순위표까지 본 뒤 "무대에 서지 못했다"가 나왔다
            string reason;
            if (!champ.HasShowResult && !champ.CanEnter(hero, out reason))
            {
                resultLine = "챔피언십 출전 불가: " + reason;
                gradeLine = "최종 랭크  " + champ.FinalGrade(champ.RivalCount + 1, level, g.Money);
                FillEnding(-1, champ.FinalGrade(champ.RivalCount + 1, level, g.Money), level, g.Money);
                return;
            }

            int rank;
            if (champ.HasShowResult)
            {
                rank = champ.ShowRank;
                resultLine = "도그쇼 챔피언십 결과: 총점 " + champ.ShowScore + "점   " + rank + "위 / " + champ.ShowBoard.Count + "마리";
            }
            else
            {
                // 도그쇼를 건너뛰는 무인 측정만 이쪽 — 스탯으로 어림한다
                int score = champ.Score(hero, true);
                rank = champ.Rank(score);
                resultLine = "챔피언십 결과(스탯 추정): 총점 " + score + "   " + rank + "위 / " + (champ.RivalCount + 1) + "명";
            }
            FillEnding(rank, champ.FinalGrade(rank, level, g.Money), level, g.Money);
            gradeLine = "최종 랭크  " + champ.FinalGrade(rank, level, g.Money)
                      + "     가게 Lv " + level + "     자산 " + g.Money + "원"
                      + "     " + hero.DisplayName + " 미모 " + hero.Stats.Beauty + " / 훈련도 " + hero.Stats.Training + " / 어질리티 " + hero.Stats.Agility;
        }

        /// <summary>
        /// 엔딩 둘째 장은 순위로 갈린다. 우승이면 트로피 그림, 아니면 "다음 대회를 노리자" 그림
        /// (<see cref="Cutscene"/> 가 won 값으로 고른다)과 그에 맞는 한 줄.
        /// </summary>
        void FillEnding(int rank, string grade, int level, int money)
        {
            string place;
            if (rank == 1) place = "심사위원의 손끝이 우리를 가리켰다. 챔피언이다!";
            else if (rank >= 2 && rank <= 3) place = rank + "위! 우승까지 딱 한 걸음. 다음 대회에선 꼭 정상에 서자, 화이팅!";
            else if (rank > 3) place = rank + "위. 이번엔 아쉬웠지만 더 노력해서 다음 대회를 노려보자, 화이팅!";
            else place = "무대에는 서지 못했다. 그래도 " + ChampionshipManager.LastDay + "일을 함께 버틴 우리는 이미 한 팀이다. 다음엔 같이 서자!";

            endingValues["won"] = rank == 1 ? "1" : "0";
            endingValues["place"] = place;
            endingValues["grade"] = grade;
            endingValues["level"] = level.ToString();
            endingValues["money"] = money.ToString("N0");
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

            // 진열대 이름표 같은 다른 OnGUI 보다 위에 — 정산서 위로 월드 라벨이 비쳐 보였다
            GUI.depth = -100;
            Styles();

            Matrix4x4 keep = GUI.matrix;
            float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            // 뒤를 살짝 어둡게 — 하루가 끝났다는 느낌, 정산서에 눈이 가게
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 클립보드 — 화면 높이의 92%. 그림 비율(720:1029)을 지킨다
            float bh = Mathf.Min(sh * 0.92f, 1000f);
            float bw = board != null ? bh * board.width / board.height : bh * 0.7f;
            var b = new Rect((sw - bw) * 0.5f, (sh - bh) * 0.5f, bw, bh);
            if (board != null) GUI.DrawTexture(b, board, ScaleMode.StretchToFill, true);
            else GUI.Box(b, GUIContent.none, UiSkin.Tag(new Color(0.98f, 0.95f, 0.87f)));

            // 종이 안쪽(그림 기준 비율): 좌우 9%, 위 14%(클립 아래), 아래 7%
            float x = b.x + bw * 0.10f, w = bw * 0.80f, y = b.y + bh * 0.135f, bottom = b.yMax - bh * 0.075f;

            GameManager g = GameManager.Instance;
            CustomerManager c = CustomerManager.Instance;
            InventoryManager inv = InventoryManager.Instance;

            GUI.Label(new Rect(x, y, w, 50f), "Day " + g.Day + " 마감 정산", titleStyle);
            y += 64f;

            // ---- 번 돈 ----
            int online = 0, shipped = 0, cancelled = 0;
            if (OnlineOrderManager.Instance != null) OnlineOrderManager.Instance.TodaySummary(out shipped, out online, out cancelled);
            int store = Mathf.Max(0, g.DailyRevenue - online);
            int earned = g.DailyRevenue + g.BonusToday;
            string earnedSub = "매장 판매 " + store.ToString("N0") + "원";
            if (online > 0) earnedSub += "  /  온라인 " + online.ToString("N0") + "원";
            if (g.BonusToday > 0) earnedSub += "  /  보상 " + g.BonusToday.ToString("N0") + "원";
            Row(ref y, x, w, iconIncome, "오늘 번 돈", "+" + earned.ToString("N0") + "원", Good, earnedSub);

            // ---- 쓴 돈 ----
            int spent = g.SpentTodayTotal;
            string spentSub = SpendPart("발주", GameManager.SpendKind.Stock) + SpendPart("가구", GameManager.SpendKind.Furniture)
                            + SpendPart("강아지", GameManager.SpendKind.Dog) + SpendPart("기타", GameManager.SpendKind.Other);
            Row(ref y, x, w, iconSpend, "오늘 쓴 돈", "-" + spent.ToString("N0") + "원", Bad,
                spentSub.Length > 0 ? spentSub.Substring(0, spentSub.Length - 5) : "쓴 돈 없음");

            // ---- 순이익 ----
            int net = earned - spent;
            Line(new Rect(x, y, w, 2f));
            netStyle.normal.textColor = net >= 0 ? Good : Bad;
            GUI.Label(new Rect(x, y + 8f, w * 0.5f, 36f), "오늘 남긴 돈", labelStyle);
            GUI.Label(new Rect(x + w * 0.4f, y + 6f, w * 0.6f, 40f), (net >= 0 ? "+" : "") + net.ToString("N0") + "원", netStyle);
            GUI.Label(new Rect(x, y + 46f, w, 26f), "가진 돈 " + g.Money.ToString("N0") + "원", subStyle);
            y += 86f;

            // ---- 배송 ----
            var arriving = new System.Text.StringBuilder();
            int kinds = 0;
            if (inv != null)
                for (int i = 0; i < inv.Catalog.Count; i++)
                {
                    int q = inv.IncomingOf(i);
                    if (q <= 0) continue;
                    if (kinds < 3) arriving.Append(kinds > 0 ? ", " : "").Append(inv.Catalog.Get(i).nameKo).Append(" x").Append(q);
                    kinds++;
                }
            if (kinds > 3) arriving.Append(" 외 ").Append(kinds - 3).Append("종");
            string deliveryValue = kinds > 0 ? "내일 아침 도착" : "발주 없음";
            string deliverySub = kinds > 0 ? arriving.ToString() : "오늘 시킨 물건이 없다. 내일 팔 재고를 확인하자";
            int unopened = inv != null ? inv.ParcelCount : 0;
            if (unopened > 0) deliverySub += "\n가게 앞에 안 뜯은 상자 " + unopened + "개";
            else if (kinds == 0) deliverySub += "";
            if (shipped + cancelled > 0) deliverySub += "\n온라인 주문 배송 " + shipped + "건" + (cancelled > 0 ? " / 취소 " + cancelled + "건" : "");
            Row(ref y, x, w, iconDelivery, "발주와 배송", deliveryValue, Ink, deliverySub);

            // ---- 명성 ----
            int repGain = g.Reputation - g.ReputationAtDayStart;
            ShopLevelDef next = ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Next : null;
            string repSub = "누적 " + g.Reputation + (next != null ? "  /  다음 레벨까지 " + Mathf.Max(0, next.requiredReputation - g.Reputation) : "  /  최대 레벨");
            Row(ref y, x, w, iconReputation, "오늘 오른 명성", (repGain >= 0 ? "+" : "") + repGain, repGain >= 0 ? Gold : Bad, repSub);

            // ---- 손님 ----
            string custSub = c != null ? "판매 " + c.SoldToday + "건  /  놓친 손님 " + c.LostToday + "명" : "";
            Row(ref y, x, w, iconCustomers, "다녀간 손님", (c != null ? c.VisitorsToday : 0) + "명", Ink, custSub);

            // 내일의 추천 상품 — 오늘 밤 발주로 넉넉히 들여 둘지 정하게
            if (!isFinal && inv != null && inv.RecommendedTomorrow >= 0 && InventoryManager.ShopOpensTomorrow)
            {
                string pick = "내일 추천 상품: " + inv.Catalog.Get(inv.RecommendedTomorrow).nameKo
                            + "  (판매가 +" + Mathf.RoundToInt(InventoryManager.RecommendBonus * 100f) + "%)";
                GUI.Label(new Rect(x, y, w, 30f), pick, recommendStyle ?? (recommendStyle = new GUIStyle(noteStyle) { fontSize = noteStyle.fontSize + 4, fontStyle = FontStyle.Bold, normal = { textColor = Gold } }));
                y += 34f;
            }

            if (mockLine.Length > 0)
            {
                GUI.Label(new Rect(x, y, w, 40f), mockLine, noteStyle);
                y += 44f;
            }

            if (isFinal)
            {
                GUI.Label(new Rect(x, y, w, 24f), resultLine, noteStyle);
                y += 26f;
                GUI.Label(new Rect(x, y, w, 46f), gradeLine, noteStyle);

                if (!finished && UiSkin.Click(new Rect(x, bottom - 56f, w, 50f), (Exhibition.Active ? "시연 종료" : "게임 종료: 30일 완주"), Big(UiSkin.Coral)))
                {
                    finished = true;
                    open = false;
                    PointerMenus.SetOpen(this, false);

                    // 엔딩 3장을 보고 타이틀로 돌아간다
                    if (Cutscene.Instance != null) Cutscene.Instance.PlayEnding(endingValues);
                }
                GUI.matrix = keep;
                return;
            }

            if (UiSkin.Click(new Rect(x, bottom - 56f, w, 50f), "다음 날 아침으로  Day " + (g.Day + 1), Big(UiSkin.Green))) Continue();
            GUI.matrix = keep;
        }

        static readonly Color Ink = new Color(0.33f, 0.2f, 0.1f);
        static readonly Color Good = new Color(0.2f, 0.55f, 0.3f);
        static readonly Color Bad = new Color(0.8f, 0.32f, 0.28f);
        static readonly Color Gold = new Color(0.82f, 0.55f, 0.08f);
        const float IconSize = 74f;

        void Styles()
        {
            if (titleStyle != null && titleStyle.font == UiSkin.Font) return;
            titleStyle = new GUIStyle { font = UiSkin.Font, fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = Ink;
            labelStyle = new GUIStyle { font = UiSkin.Font, fontSize = 27, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = Ink;
            valueStyle = new GUIStyle { font = UiSkin.Font, fontSize = 31, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            netStyle = new GUIStyle(valueStyle) { fontSize = 36 };
            subStyle = new GUIStyle { font = UiSkin.Font, fontSize = 19, alignment = TextAnchor.UpperLeft, wordWrap = true };
            subStyle.normal.textColor = new Color(0.5f, 0.38f, 0.28f);
            noteStyle = new GUIStyle(subStyle) { fontSize = 19, alignment = TextAnchor.UpperCenter };
            noteStyle.normal.textColor = Ink;
        }

        static GUIStyle Big(Color tint) => new GUIStyle(UiSkin.Button(tint)) { fontSize = 26 };

        string SpendPart(string name, GameManager.SpendKind kind)
        {
            int v = GameManager.Instance.SpentToday(kind);
            return v > 0 ? name + " " + v.ToString("N0") + "원  /  " : "";
        }

        /// <summary>정산서 한 칸: 아이콘 · 항목 이름 · 오른쪽 큰 숫자 · 아래 작은 설명(여러 줄 가능).</summary>
        void Row(ref float y, float x, float w, Texture2D icon, string label, string value, Color valueColor, string sub)
        {
            if (icon != null) GUI.DrawTexture(new Rect(x, y, IconSize, IconSize), icon, ScaleMode.ScaleToFit, true);
            float tx = x + IconSize + 12f, tw = w - IconSize - 12f;
            GUI.Label(new Rect(tx, y + 2f, tw * 0.6f, 36f), label, labelStyle);
            valueStyle.normal.textColor = valueColor;
            GUI.Label(new Rect(tx + tw * 0.4f, y, tw * 0.6f, 40f), value, valueStyle);
            float subH = subStyle.CalcHeight(new GUIContent(sub), tw);
            GUI.Label(new Rect(tx, y + 40f, tw, subH), sub, subStyle);
            y += Mathf.Max(IconSize, 40f + subH) + 14f;
        }

        /// <summary>종이 위 구분선. 연한 갈색 한 줄.</summary>
        static void Line(Rect r)
        {
            GUI.color = new Color(0.55f, 0.38f, 0.22f, 0.3f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
