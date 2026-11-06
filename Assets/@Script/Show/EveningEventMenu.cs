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
    /// 시즌 끝날(30·60·90일차…)에는 챔피언십 결과와 최종 랭크가 나오고 엔딩으로 간다.
    /// 본편은 엔딩 뒤 시즌 결산 판에서 다음 시즌으로 이어 가거나 저장하고 타이틀로 간다. 시연은 엔딩 뒤 견종 고르기.
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

            // 튜토리얼 마지막 날(2일차)이 저물었다 — 3일차는 없다. 할 일을 다 못 했어도 타이틀로(2026-11-05)
            if (Exhibition.IsTutorialLastDay(g.Day))
            {
                Exhibition.ReturnToPicker = false;
                if (Cutscene.Instance != null) Cutscene.Instance.ReturnToTitle();
                return;
            }

            Dog hero = DogManager.Instance.Hero;
            isFinal = champ.IsFinalDay(g.Day);
            mockLine = "";
            resultLine = "";
            gradeLine = "";

            if (isFinal) BuildFinalResult(hero, champ, g);
            else if (champ.IsMockDay(g.Day)) BuildMockResult(hero, champ);
            // 마스코트 등급 심사 날(10일차, 20일차)은 모의 심사 줄 대신 심사 결과를 적는다
            if (!isFinal && MascotReview.DoneDay == g.Day && MascotReview.ResultLine.Length > 0) mockLine = MascotReview.ResultLine;

            // D30 에 도그쇼를 실제로 치렀으면(순위표를 이미 봤으면) 정산서는 건너뛰고 바로 엔딩 그림으로 간다 —
            // 순위 발표 뒤에 하루 정산서가 끼어들어 흐름이 끊겼다. 쇼를 건너뛴 경우(무인 측정·출전 불가)만 정산서로 결과를 보인다
            if (isFinal && champ.HasShowResult && !finished && Cutscene.Instance != null)
            {
                finished = true;
                Cutscene.Instance.PlayEnding(endingValues, AfterEnding);
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
            mockLine = "모의 심사: 미모 " + hero.Stats.Beauty + " / 훈련도 " + hero.Stats.Training + " / 어질리티 " + hero.Stats.Agility + "  (만점 " + Mathf.RoundToInt(ChampionshipManager.StatFull) + ", 도그쇼 난이도 반영)";
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

            seasonRank = rank;
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
            if (seasonChoice) { DrawSeasonChoice(); return; }
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
            if (c != null && c.ChampionBonusToday > 0) earnedSub += "  /  챔피언 +" + c.ChampionBonusToday.ToString("N0") + "원";
            Row(ref y, x, w, iconIncome, "오늘 번 돈", "+" + earned.ToString("N0") + "원", Good, earnedSub);

            // ---- 쓴 돈 ----
            int spent = g.SpentTodayTotal;
            string spentSub = SpendPart("발주", GameManager.SpendKind.Stock) + SpendPart("가구", GameManager.SpendKind.Furniture)
                            + SpendPart("강아지", GameManager.SpendKind.Dog) + SpendPart("기타", GameManager.SpendKind.Other);
            Row(ref y, x, w, iconSpend, "오늘 쓴 돈", (spent > 0 ? "-" : "") + spent.ToString("N0") + "원", Bad,
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
            string repSub = "누적 " + g.Reputation.ToString("N0") + (next == null ? "  /  최대 레벨, 명성은 계속 쌓인다" : next.requiredReputation > g.Reputation ? "  /  다음 레벨까지 " + (next.requiredReputation - g.Reputation) : "  /  다음 레벨 명성 충분");
            Row(ref y, x, w, iconReputation, "오늘 오른 명성", (repGain >= 0 ? "+" : "") + repGain, repGain >= 0 ? Gold : Bad, repSub);

            // ---- 손님 ----
            string custSub = c != null ? "판매 " + c.SoldToday + "건  /  놓친 손님 " + c.LostToday + "명" : "";
            Row(ref y, x, w, iconCustomers, "다녀간 손님", (c != null ? c.VisitorsToday : 0) + "명", Ink, custSub);

            // 오늘의 가게 목표(일일 퀘스트) — 달성하고 안 받은 보상은 마감 때 대신 받아 준다
            DailyQuestManager daily = DailyQuestManager.Instance;
            if (daily != null && daily.Active)
            {
                GUI.Label(new Rect(x, y, w, 26f), "오늘의 가게 목표 " + daily.DoneCount + " / " + DailyQuestManager.PerDay + " 달성", noteStyle);
                y += 28f;
            }

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

                if (!finished && UiSkin.Click(new Rect(x, bottom - 56f, w, 50f), (Exhibition.Active ? "시연 종료" : "시즌 " + ChampionshipManager.Instance.Season + " 마치기"), Big(UiSkin.Coral)))
                {
                    finished = true;
                    open = false;
                    PointerMenus.SetOpen(this, false);

                    // 엔딩 3장을 보고 타이틀로 돌아간다
                    if (Cutscene.Instance != null) Cutscene.Instance.PlayEnding(endingValues, AfterEnding);
                }
                GUI.matrix = keep;
                return;
            }

            if (UiSkin.Click(new Rect(x, bottom - 56f, w, 50f), "다음 날 아침으로  Day " + (g.Day + 1), Big(UiSkin.Green))) Continue();
            GUI.matrix = keep;
        }

        // ---- 시즌 결산(본편, 챔피언십 엔딩 뒤) ----

        bool seasonChoice;
        int seasonRank = -1;

        // 다음 시즌 강아지 고르기(2026-11-04) — 시즌마다 새 강아지를 처음 능력치로 키운다
        bool dogPick;
        int pickBreed;
        string pickName = "";
        GUIStyle pickFieldStyle;
        float seasonOpenedAt, pickOpenedAt;
        float pickRejectedAt = -10f;
        DogNames.Reject pickReject;
        int hallPlace = -1;   // 마지막 시즌 결산 판: 이 판이 명예의 전당 몇 위에 드는가(0 = 밖, -1 = 아직 안 셈)

        /// <summary>엔딩을 다 본 뒤 부를 것. 본편만 시즌 결산 판, 시연은 null(견종 고르기로).</summary>
        System.Action AfterEnding => Exhibition.Active ? null : (System.Action)OpenSeasonChoice;

        void OpenSeasonChoice()
        {
            ChampionshipManager.Instance.RecordSeason(seasonRank);
            seasonChoice = true;
            seasonOpenedAt = Time.unscaledTime;
            hallPlace = -1;
            PointerMenus.SetOpen(this, true);
            Sound.Play(seasonRank == 1 ? Sfx.Fanfare : Sfx.Sparkle);
        }

        /// <summary>
        /// 시즌 결산판(2026-11-04 다시 그림). 금빛 나무 액자 위에 빨간 리본 제목, 이번 시즌 결과(트로피, 메달),
        /// 다섯 시즌 기록 칸, 다음 시즌에 달라지는 것, 그림 버튼. 마지막 시즌이면 완주 화면과 다시 시작 버튼.
        /// </summary>
        void DrawSeasonChoice()
        {
            GUI.depth = -100;
            Styles();
            if (dogPick) { DrawDogPick(); return; }

            Matrix4x4 keep = GUI.matrix;
            float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            ChampionshipManager champ = ChampionshipManager.Instance;
            bool won = seasonRank == 1;
            bool last = champ.IsLastSeason;
            SeasonArt.Backdrop(sw, sh, won || last, seasonOpenedAt);

            Texture2D art = SeasonArt.Board;
            float bh = Mathf.Min(sh * 0.96f, 1040f);
            float bw = art != null ? bh * art.width / art.height : bh * 0.7f;
            var b = new Rect((sw - bw) * 0.5f, (sh - bh) * 0.5f, bw, bh);

            float pop = SeasonArt.Pop(seasonOpenedAt);
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), b.center);
            GUI.color = new Color(1f, 1f, 1f, SeasonArt.Fade(seasonOpenedAt));

            if (art != null) GUI.DrawTexture(b, art, ScaleMode.StretchToFill, true);
            else GUI.Box(b, GUIContent.none, UiSkin.Tag(new Color(0.98f, 0.95f, 0.87f)));

            // 리본 제목: 판보다 조금 넓게, 트로피 아래 종이 윗자리에 걸친다
            float rw = bw * 1.1f, rh = rw * 0.2234f;
            SeasonArt.RibbonTitle(new Rect(b.center.x - rw * 0.5f, b.y + bh * 0.165f, rw, rh),
                last ? ChampionshipManager.MaxSeasons + "시즌 완주!" : "시즌 " + champ.Season + " / " + ChampionshipManager.MaxSeasons + " 마무리", 46);

            float x = b.x + bw * 0.12f, w = bw * 0.76f, y = b.y + bh * 0.345f, bottom = b.y + bh * 0.918f;

            // ---- 이번 시즌 결과: 트로피나 메달과 큰 글씨 ----
            Texture2D icon = SeasonArt.RankIcon(seasonRank);
            const float Ic = 124f;
            float tx = x, tw = w;
            if (icon != null)
            {
                float bob = Mathf.Sin(Time.unscaledTime * 2.4f) * 4f;
                if (won && SeasonArt.Sparkle != null)
                {
                    float twinkle = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 5f);
                    Color k = GUI.color;
                    GUI.color = new Color(1f, 0.9f, 0.4f, twinkle * k.a);
                    GUI.DrawTexture(new Rect(x - 6f, y - 4f, 40f, 40f), SeasonArt.Sparkle, ScaleMode.ScaleToFit, true);
                    GUI.DrawTexture(new Rect(x + Ic - 30f, y + Ic * 0.55f, 30f, 30f), SeasonArt.Sparkle, ScaleMode.ScaleToFit, true);
                    GUI.color = k;
                }
                GUI.DrawTexture(new Rect(x, y + bob, Ic, Ic), icon, ScaleMode.ScaleToFit, true);
                tx = x + Ic + 18f;
                tw = w - Ic - 18f;
            }

            string head, sub;
            Color headColor;
            if (won) { head = "챔피언!"; sub = "우승 상금 +" + ChampionshipManager.ChampionPrize.ToString("N0") + "원\n벽에 우승 액자가 걸렸다"; headColor = SeasonArt.Gold; }
            else if (seasonRank >= 2 && seasonRank <= 3) { head = seasonRank + "위 입상!"; sub = "우승까지 딱 한 걸음\n다음 시즌엔 꼭 정상에 서자"; headColor = seasonRank == 2 ? new Color(0.82f, 0.86f, 0.92f) : new Color(0.93f, 0.62f, 0.36f); }
            else if (seasonRank > 3) { head = seasonRank + "위"; sub = "아쉬웠지만 괜찮아\n다음 시즌을 노려 보자"; headColor = new Color(0.95f, 0.9f, 0.8f); }
            else { head = "출전 못함"; sub = "이번 시즌은 무대에 서지 못했다\n다음엔 같이 서자"; headColor = new Color(0.95f, 0.9f, 0.8f); }

            if (last && !won) sub = seasonRank > 0 ? "마지막 시즌까지 함께 달렸다\n정말 수고했어!" : "마지막 시즌까지 함께했다\n정말 수고했어!";

            var headStyle = SeasonArt.Text(SeasonArt.Fit(head, 58, tw), icon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, headColor);
            SeasonArt.Outlined(new Rect(tx, y + 2f, tw, 66f), head, headStyle, SeasonArt.Outline, 3f);
            GUI.Label(new Rect(tx, y + 70f, tw, 60f), sub, SeasonArt.Text(21, icon != null ? TextAnchor.UpperLeft : TextAnchor.UpperCenter, SeasonArt.SoftInk, true));
            y += 130f;

            Line(new Rect(x + 20f, y, w - 40f, 2f));
            y += 12f;

            // ---- 다섯 시즌 기록 칸 ----
            GUI.Label(new Rect(x, y, w, 30f), "시즌 기록", SeasonArt.Text(24, TextAnchor.MiddleCenter, SeasonArt.Ink));
            y += 36f;
            int n = ChampionshipManager.MaxSeasons;
            const float Gap = 12f;
            float slot = Mathf.Min(76f, (w - Gap * (n - 1)) / n);
            float sx = x + (w - (slot * n + Gap * (n - 1))) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var c = new Rect(sx + i * (slot + Gap), y, slot, slot);
                bool done = i < champ.SeasonRanks.Count;
                bool now = i == champ.Season - 1;
                if (now)
                {
                    float glow = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
                    Color k = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, glow * k.a);
                    GUI.Box(new Rect(c.x - 6f, c.y - 6f, c.width + 12f, c.height + 12f), GUIContent.none, UiSkin.Tag(new Color(1f, 0.76f, 0.2f)));
                    GUI.color = k;
                }
                GUI.Box(c, GUIContent.none, UiSkin.Tag(done ? SeasonArt.Cream : new Color(0.83f, 0.77f, 0.68f)));
                if (done)
                {
                    int r = champ.SeasonRanks[i];
                    Texture2D m = SeasonArt.RankIcon(r);
                    if (m != null) GUI.DrawTexture(new Rect(c.x + 8f, c.y + 8f, c.width - 16f, c.height - 16f), m, ScaleMode.ScaleToFit, true);
                    else GUI.Label(c, r > 0 ? r + "위" : "불참", SeasonArt.Text(r > 0 ? 28 : 22, TextAnchor.MiddleCenter, SeasonArt.SoftInk));
                }
                else GUI.Label(c, "?", SeasonArt.Text(36, TextAnchor.MiddleCenter, new Color(0.62f, 0.55f, 0.46f)));
                GUI.Label(new Rect(c.x - 10f, c.yMax + 4f, c.width + 20f, 24f), (i + 1) + "시즌", SeasonArt.Text(18, TextAnchor.MiddleCenter, now ? SeasonArt.DeepGold : SeasonArt.SoftInk));
            }
            y += slot + 32f;

            string chips = "우승 트로피 " + champ.Trophies + "개";
            if (ChampionshipManager.SaleBonus > 0f) chips += "     매장 판매가 +" + Mathf.RoundToInt(ChampionshipManager.SaleBonus * 100f) + "%";
            // 이번 시즌 가게 명성(2026-11-05) — 시즌마다 0 에서 다시 쌓으므로 시즌끼리 견줄 수 있다
            int fameNow = champ.SeasonLogs.Count > 0 ? champ.SeasonLogs[champ.SeasonLogs.Count - 1].shopFame : 0;
            string fameLine = "이번 시즌 가게 명성 " + fameNow.ToString("N0") + "   (합계 " + champ.TotalShopFame.ToString("N0") + ")";
            GUI.Label(new Rect(x, y, w, 30f), fameLine, SeasonArt.Text(SeasonArt.Fit(fameLine, 23, w), TextAnchor.MiddleCenter, SeasonArt.Ink));
            y += 30f;
            GUI.Label(new Rect(x, y, w, 30f), chips, SeasonArt.Text(SeasonArt.Fit(chips, 21, w), TextAnchor.MiddleCenter, SeasonArt.DeepGold));
            y += 30f;

            // 순위 보너스(2026-11-05): 1등 10%, 2등 7.5%, 3등 5% 를 시즌마다 더한다
            float rankBonus = ChampionshipManager.RankBonus;
            string bonusLine = rankBonus > 0f ? "순위 보너스  명성, 훈련, 운동 " + ChampionshipManager.RankBonusText(rankBonus) : "1~3등을 하면 명성, 훈련, 운동 보너스가 붙는다";
            GUI.Label(new Rect(x, y, w, 30f), bonusLine, SeasonArt.Text(SeasonArt.Fit(bonusLine, 21, w), TextAnchor.MiddleCenter, new Color(0.2f, 0.55f, 0.3f)));
            y += 36f;

            var box = new Rect(x, y, w, bottom - 104f - y);
            GUI.Box(box, GUIContent.none, UiSkin.Tag(new Color(0.96f, 0.9f, 0.76f)));
            float bx = box.x + 18f, bwid = box.width - 36f;

            // ---- 다섯 시즌을 다 마쳤다: 더 이어 갈 시즌이 없다 ----
            if (last)
            {
                if (hallPlace < 0) hallPlace = HallOfFame.PreviewPlace(champ.BuildHallEntry());
                string hallLine = hallPlace > 0 ? "명예의 전당 " + hallPlace + "위에 이름을 올렸다!" : "이번에는 명예의 전당 5위 안에 못 들었다";
                GUI.Label(new Rect(bx, box.y + 10f, bwid, 32f), hallLine, SeasonArt.Text(SeasonArt.Fit(hallLine, 26, bwid), TextAnchor.MiddleCenter, hallPlace > 0 ? SeasonArt.DeepGold : SeasonArt.Ink));
                GUI.Label(new Rect(bx, box.y + 48f, bwid, box.height - 54f),
                    "기록은 타이틀 화면의 명예의 전당에 남는다\n새 게임으로 다시 도전해 보자", SeasonArt.Text(20, TextAnchor.UpperCenter, SeasonArt.SoftInk, true));
                if (SeasonArt.Button(new Rect(x, bottom - 88f, w, 84f), "새 게임으로 다시 도전", true, 32)) FinishAllSeasons();
                GUI.color = Color.white;
                GUI.matrix = keep;
                return;
            }

            int nextStart = ChampionshipManager.LastDay + 1;
            GUI.Label(new Rect(bx, box.y + 8f, bwid, 32f), "다음 시즌  Day " + nextStart + " ~ " + (nextStart + ChampionshipManager.FinalDay - 1),
                SeasonArt.Text(25, TextAnchor.MiddleCenter, SeasonArt.Ink));
            GUI.Label(new Rect(bx, box.y + 38f, bwid, 26f), "새 강아지와 가게를 1레벨부터 다시 키운다", SeasonArt.Text(20, TextAnchor.MiddleCenter, SeasonArt.SoftInk));
            GUI.Label(new Rect(bx, box.y + 62f, bwid, 26f), "돈, 가구, 훈련 단계, 순위 보너스는 그대로", SeasonArt.Text(20, TextAnchor.MiddleCenter, SeasonArt.SoftInk));

            // 다음 시즌에 무엇이 어려워지는가: 만점과 라이벌
            int fullNow = ChampionshipManager.StatFullFor(champ.Season), fullNext = ChampionshipManager.StatFullFor(champ.Season + 1);
            int rivalNext = Mathf.Min(ChampionshipManager.MaxRivalBonus, champ.Season * ChampionshipManager.RivalBonusPerSeason) - champ.RivalBonus;
            string harder = (fullNext > fullNow ? "능력치 만점 " + fullNow + "에서 " + fullNext + "으로" : "능력치 만점 " + fullNow + " (최대)")
                          + (rivalNext > 0 ? "   라이벌 점수 +" + rivalNext : "   라이벌은 최고 수준");
            GUI.Label(new Rect(bx, box.y + 88f, bwid, 28f), harder,
                SeasonArt.Text(SeasonArt.Fit(harder, 20, bwid), TextAnchor.MiddleCenter, new Color(0.78f, 0.3f, 0.24f)));

            if (SeasonArt.Button(new Rect(x, bottom - 88f, w, 84f), "다음 시즌 강아지 고르기", true, 32))
            {
                dogPick = true;
                pickOpenedAt = Time.unscaledTime;
                pickBreed = DogManager.Instance != null && DogManager.Instance.Hero != null ? DogManager.Instance.Hero.BreedIndex : 0;
                pickName = "";
            }
            GUI.color = Color.white;
            GUI.matrix = keep;
        }

        /// <summary>다음 시즌 첫날 아침으로. 타이틀로 갈 때도 넘긴 뒤 저장해서, 이어하기가 다음 시즌에서 시작되게 한다.</summary>
        void StartNextSeason(bool toTitle)
        {
            seasonChoice = false;
            PointerMenus.SetOpen(this, false);
            ChampionshipManager.Instance.BeginNextSeason();
            // 새 시즌은 가게도 1레벨, 명성 0 에서 다시 키운다(2026-11-05). 넓이, 가구, 돈, 재고, 훈련 단계, 순위 보너스는 그대로
            if (ShopLevelManager.Instance != null) ShopLevelManager.Instance.ResetForNewSeason();
            if (GameManager.Instance != null) GameManager.Instance.ResetReputationForNewSeason();
            Input.imeCompositionMode = IMECompositionMode.Auto;
            if (DogManager.Instance != null) DogManager.Instance.ReplaceHero(pickBreed, (pickName ?? "").Trim());
            dogPick = false;
            finished = false;
            isFinal = false;
            open = false;
            GameManager.Instance.BeginNextDay();
            if (toTitle) StartCoroutine(SaveAndLeave());
        }

        /// <summary>다섯 시즌을 다 마친 판: 이어 할 것이 없으니 세이브를 지우고 타이틀로.</summary>
        void FinishAllSeasons()
        {
            seasonChoice = false;
            PointerMenus.SetOpen(this, false);
            // 명예의 전당에 이 판을 남긴다(세이브와 따로 저장). 타이틀은 바로 전당을 연다
            HallOfFame.Add(ChampionshipManager.Instance.BuildHallEntry());
            HallOfFame.OpenOnTitle = true;
            SaveManager.DeleteSave();
            if (Cutscene.Instance != null) Cutscene.Instance.ReturnToTitle();
        }

        /// <summary>
        /// 다음 시즌에 함께할 강아지(2026-11-04 다시 그림). 리본 제목 아래 견종 카드가 한 줄로 늘어서고,
        /// 고른 카드는 금빛 카드로 바뀌어 살짝 떠서 숨 쉰다. 아래에 이름 칸과 그림 버튼 둘.
        /// </summary>
        void DrawDogPick()
        {
            if (pickFieldStyle == null || pickFieldStyle.font != UiSkin.Font)
            {
                // 기본 입력칸 스타일을 복사하면 어두운 바탕(scaledBackgrounds)이 따라와 흰 칸 위가 검게 덮였다 — 바탕 없는 새 스타일로
                pickFieldStyle = new GUIStyle { font = UiSkin.Font, fontSize = 32, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 4, 4), clipping = TextClipping.Clip };
                pickFieldStyle.normal.textColor = pickFieldStyle.hover.textColor = pickFieldStyle.focused.textColor = pickFieldStyle.active.textColor = SeasonArt.Ink;
            }

            Matrix4x4 keep = GUI.matrix;
            float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            ChampionshipManager champ = ChampionshipManager.Instance;
            int next = champ.Season + 1;
            SeasonArt.Backdrop(sw, sh, false, pickOpenedAt);

            float pop = SeasonArt.Pop(pickOpenedAt);
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), new Vector2(sw * 0.5f, sh * 0.5f));
            GUI.color = new Color(1f, 1f, 1f, SeasonArt.Fade(pickOpenedAt));

            float rw = Mathf.Min(880f, sw * 0.62f), rh = rw * 0.2234f;
            SeasonArt.RibbonTitle(new Rect((sw - rw) * 0.5f, 20f, rw, rh), "시즌 " + next + " 함께할 강아지", 48);
            float y = 20f + rh;
            SeasonArt.Outlined(new Rect(0f, y, sw, 38f), "새 강아지와 가게를 1레벨부터 다시 키운다. 돈, 가구, 훈련 단계, 순위 보너스는 그대로",
                SeasonArt.Text(26, TextAnchor.MiddleCenter, Color.white), SeasonArt.Outline, 2f);
            // 다음 시즌 심사 경향 — 견종을 고르는 기준이 된다(2026-11-05)
            int upcoming = champ.UpcomingTrend;
            SeasonArt.Outlined(new Rect(0f, y + 36f, sw, 34f), "시즌 " + next + " 심사 경향: " + ChampionshipManager.TrendNames[upcoming] + "  (" + ChampionshipManager.TrendShares(upcoming) + ")",
                SeasonArt.Text(24, TextAnchor.MiddleCenter, SeasonArt.Gold), SeasonArt.Outline, 2f);
            y += 62f + 34f;

            // ---- 견종 카드 ----
            DogManager dogs = DogManager.Instance;
            int count = dogs != null ? dogs.BreedCount : 0;
            int rows = count <= 6 ? 1 : 2;
            int cols = Mathf.Max(1, Mathf.CeilToInt(count / (float)rows));
            const float Gap = 24f, Aspect = 1.535f;
            float availH = sh - y - 330f;
            float cardW = Mathf.Min(260f, (sw - 160f - Gap * (cols - 1)) / cols, (availH - Gap * (rows - 1)) / rows / Aspect);
            float cardH = cardW * Aspect;
            float sx = (sw - (cols * cardW + (cols - 1) * Gap)) * 0.5f;

            // 고른 카드를 마지막에 그려 옆 카드 위로 올라오게
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < count; i++)
            {
                bool on = i == pickBreed;
                if ((pass == 0) == on) continue;
                var r = new Rect(sx + (i % cols) * (cardW + Gap), y + (i / cols) * (cardH + Gap), cardW, cardH);
                bool hover = r.Contains(Event.current.mousePosition);
                float k = on ? 1.08f + 0.015f * Mathf.Sin(Time.unscaledTime * 4f) : hover ? 1.04f : 1f;
                float lift = on ? -10f : hover ? -4f : 0f;
                var d = new Rect(r.center.x - r.width * k * 0.5f, r.center.y - r.height * k * 0.5f + lift, r.width * k, r.height * k);

                Texture2D cardArt = on ? SeasonArt.PickCardOn : SeasonArt.PickCard;
                if (cardArt != null) GUI.DrawTexture(d, cardArt, ScaleMode.StretchToFill, true);
                else GUI.Box(d, GUIContent.none, UiSkin.Tag(on ? new Color(1f, 0.84f, 0.3f) : Color.white));

                Texture2D tex = ChampionWall.PortraitOf(i);
                float pic = d.width * 0.74f;
                if (tex != null) GUI.DrawTexture(new Rect(d.center.x - pic * 0.5f, d.y + d.height * 0.13f, pic, pic), tex, ScaleMode.ScaleToFit, true);

                string breed = dogs.BreedName(i);
                var nameRect = new Rect(d.x + d.width * 0.1f, d.y + d.height * 0.69f, d.width * 0.8f, d.height * 0.15f);
                GUI.Label(nameRect, breed, SeasonArt.Text(SeasonArt.Fit(breed, Mathf.RoundToInt(28 * k), nameRect.width), TextAnchor.MiddleCenter, SeasonArt.Ink));

                if (UiSkin.Click(r, GUIContent.none, GUIStyle.none) && pickBreed != i)
                {
                    pickBreed = i;
                    Sound.Play(Sfx.DogBark);
                }
            }
            y += rows * cardH + (rows - 1) * Gap + 44f;

            // ---- 이름 ----
            const float FieldW = 640f, FieldH = 72f;
            var plate = new Rect((sw - FieldW) * 0.5f, y, FieldW, FieldH);
            GUI.Box(plate, GUIContent.none, UiSkin.Tag(SeasonArt.Cream));
            GUI.Label(new Rect(plate.x + 22f, plate.y, 100f, FieldH), "이름", SeasonArt.Text(30, TextAnchor.MiddleLeft, SeasonArt.DeepGold));
            var field = new Rect(plate.x + 120f, plate.y + 10f, FieldW - 140f, FieldH - 20f);
            GUI.Box(field, GUIContent.none, UiSkin.Tag(Color.white));
            Color cursor = GUI.skin.settings.cursorColor;
            GUI.skin.settings.cursorColor = SeasonArt.Ink;
            // 한글은 IME 가 켜져 있어야 친다. 글꼴에 없는 글자, 숫자, 기호는 바로 지우고 안내한다(2026-11-05)
            GUI.SetNextControlName("SeasonDogName");
            bool typing = GUI.GetNameOfFocusedControl() == "SeasonDogName";
            Input.imeCompositionMode = typing ? IMECompositionMode.On : IMECompositionMode.Auto;
            string typed = GUI.TextField(field, pickName ?? "", Dog.MaxNameLength, pickFieldStyle);
            DogNames.Reject reject;
            pickName = DogNames.Clean(typed, out reject);
            if (reject != DogNames.Reject.None) { pickReject = reject; pickRejectedAt = Time.unscaledTime; }
            GUI.skin.settings.cursorColor = cursor;
            if (string.IsNullOrEmpty(pickName) && dogs != null)
                GUI.Label(new Rect(field.x + 14f, field.y, field.width - 28f, field.height), dogs.BreedName(pickBreed), SeasonArt.Text(30, TextAnchor.MiddleLeft, new Color(0.7f, 0.64f, 0.56f)));
            bool rejected = Time.unscaledTime - pickRejectedAt < 2.5f;
            string note = rejected ? DogNames.Message(pickReject) : "비워 두면 견종 이름으로 부른다 (최대 " + Dog.MaxNameLength + "글자)";
            SeasonArt.Outlined(new Rect(0f, plate.yMax + 6f, sw, 30f), note,
                SeasonArt.Text(21, TextAnchor.MiddleCenter, rejected ? new Color(1f, 0.62f, 0.5f) : Color.white), SeasonArt.Outline, 2f);
            y = plate.yMax + 56f;

            // ---- 버튼 ----
            const float MainW = 560f, SubW = 360f, BtnH = 92f, BtnGap = 34f;
            float bx = (sw - (MainW + SubW + BtnGap)) * 0.5f;
            if (SeasonArt.Button(new Rect(bx, y, MainW, BtnH), "이 강아지와 시즌 " + next + " 시작", true, 32)) StartNextSeason(false);
            else if (SeasonArt.Button(new Rect(bx + MainW + BtnGap, y, SubW, BtnH), "저장하고 타이틀로", false, 28)) StartNextSeason(true);

            GUI.color = Color.white;
            GUI.matrix = keep;
        }

        System.Collections.IEnumerator SaveAndLeave()
        {
            yield return null;
            yield return null;   // 아침 처리(입고·하루 초기화)가 끝난 뒤를 남긴다
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
            if (Cutscene.Instance != null) Cutscene.Instance.ReturnToTitle();
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
