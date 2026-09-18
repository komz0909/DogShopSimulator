using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using UnityEngine;
using UnityEngine.InputSystem;


namespace DogShop.Debugging
{
    /// <summary>
    /// 상점 상태 HUD. 강아지 상호작용은 DogContextMenu(강아지 클릭)가 담당한다.
    /// 발주는 ShopOrderMenu(계산대 클릭)가 담당한다.
    /// 1/2/3 배속 · R 하루리셋 · L 레벨업 · F5/F9 저장/로드
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        const float RefreshInterval = 0.25f;

        string line1 = "";
        string line2 = "";
        string line3 = "";
        string line4 = "";
        string line5 = "";
        string notice = "";
        float refreshTimer;
        float noticeTimer;

        /// <summary>
        /// 디버그 수치는 기본으로 숨긴다 — 이제 GameHud 가 플레이용 정보를 맡는다.
        /// 단축키(배속·저장·레벨업)는 숨겨도 계속 동작한다. 측정 중에 필요하다.
        /// </summary>
        bool visible;
        GUIStyle style;
        GUIStyle noticeStyle;

        readonly ShopLevelManager.UpgradeAction upgrade = new ShopLevelManager.UpgradeAction();

        void Start()
        {
            ActionRunner.OnExecuted += HandleExecuted;
            ActionRunner.OnRejected += HandleRejected;
            ShopLevelManager.Instance.OnLevelUp += HandleLevelUp;
            Refresh();
        }

        void OnDestroy()
        {
            ActionRunner.OnExecuted -= HandleExecuted;
            ActionRunner.OnRejected -= HandleRejected;
            if (ShopLevelManager.Instance != null) ShopLevelManager.Instance.OnLevelUp -= HandleLevelUp;
        }

        void Update()
        {
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= RefreshInterval)
            {
                refreshTimer = 0f;
                Refresh();
            }

            if (noticeTimer > 0f)
            {
                noticeTimer -= Time.unscaledDeltaTime;
                if (noticeTimer <= 0f) notice = "";
            }

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f1Key.wasPressedThisFrame) visible = !visible;

            if (kb.digit1Key.wasPressedThisFrame) TimeManager.Instance.SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) TimeManager.Instance.SetSpeed(4);
            if (kb.digit3Key.wasPressedThisFrame) TimeManager.Instance.SetSpeed(16);

            if (kb.rKey.wasPressedThisFrame) { TimeManager.Instance.StartNewDay(); Show("하루 리셋 — 09:00"); }
            if (kb.lKey.wasPressedThisFrame) ActionRunner.TryRun(upgrade);

            if (kb.nKey.wasPressedThisFrame && CleanlinessManager.Instance != null)
            {
                CleanlinessManager.Instance.ForceSpawn(3);
                Show("오염 +3 (테스트) — 청결 " + CleanlinessManager.Instance.Cleanliness);
            }

            if (kb.f5Key.wasPressedThisFrame) SaveManager.Instance.Save();
            if (kb.f9Key.wasPressedThisFrame) SaveManager.Instance.Load();
        }

        void HandleExecuted(IPlayerAction action) => Refresh();

        void HandleRejected(IPlayerAction action, string reason) => Show("거절 — " + reason);

        void HandleLevelUp(int level) => Show("레벨 " + level + " — " + ShopLevelManager.Instance.Current.unlockKo);


        void Show(string message)
        {
            notice = message;
            noticeTimer = 3.5f;
            Refresh();
        }

        void Refresh()
        {
            TimeManager t = TimeManager.Instance;
            GameManager g = GameManager.Instance;
            ShopLevelManager s = ShopLevelManager.Instance;
            CustomerManager c = CustomerManager.Instance;
            InventoryManager inv = InventoryManager.Instance;
            if (t == null || g == null || s == null || c == null || inv == null) return;

            ShopLevelDef next = s.Next;

            line1 = "Day " + g.Day + "  " + t.ClockText + "  x" + t.SpeedMultiplier
                  + "     Lv " + s.Level
                  + "     돈 " + g.Money
                  + "     명성 " + g.Reputation + (next != null ? " / " + next.requiredReputation : " MAX");

            CleanlinessManager clean = CleanlinessManager.Instance;

            line2 = "매출 " + c.RevenueToday + "   놓침 " + c.LostToday
                  + "   바스켓 " + c.AverageBasket + "(목표 " + s.Current.basketPriceTarget + ")"
                  + "   장내 " + c.InStore + "명  대기 " + c.QueueLength + "명"
                  + (clean != null
                        ? "     청결 " + clean.Cleanliness + "  오염 " + clean.SpotCount + "개  손님 x"
                          + clean.CustomerFactor.ToString("0.00")
                        : "");

            line3 = inv.RushDelivery ? "재고 [승급일 — 발주 즉시 입고]  " : "재고  ";
            for (int i = 0; i < inv.Catalog.Count; i++)
            {
                ProductDef p = inv.Catalog.Get(i);
                if (p.unlockLevel > s.Level) continue;

                line3 += p.nameKo + " 창고" + inv.StorageOf(i) + "/진열" + inv.ShelfOf(i);
                if (inv.IncomingOf(i) > 0) line3 += "(+" + inv.IncomingOf(i) + ")";
                line3 += "  ";
            }

            TrainingManager tm = TrainingManager.Instance;
            DogManager dm = DogManager.Instance;
            line4 = tm != null && dm != null && dm.Hero != null
                ? "훈련 슬롯 " + tm.SlotsUsed + "/" + tm.SlotsTotal
                  + "   " + dm.Hero.BreedKo + " 미모 " + dm.Hero.Stats.Beauty + " / 훈련도 " + dm.Hero.Stats.Training
                : "";

            line4 += UpgradeAdvice(s, inv, g);

            EconomyMonitor eco = EconomyMonitor.Instance;
            line5 = eco != null ? eco.StatusLine() : "";
        }

        /// <summary>
        /// 승격이 가능할 때 <b>승격 후에 필요한 운전자본</b>을 함께 알린다.
        /// 명성만 보고 올리면 손님은 늘어나는데 채울 돈이 없어 가게가 마른다 —
        /// 실측에서 L2 승격 직후 매일 손님 6~7명을 놓치는 나선에 빠졌다.
        /// 막지는 않는다. 언제 올릴지는 플레이어의 판단이다.
        /// </summary>
        static string UpgradeAdvice(ShopLevelManager s, InventoryManager inv, GameManager g)
        {
            if (s == null || inv == null || g == null || s.IsMaxLevel) return "";

            string reason;
            if (!s.CanLevelUp(out reason)) return "";

            ShopLevelDef next = s.Next;
            int needed = inv.DailyRestockCost(s.Level + 1, next.customersPerDay) * 2;
            int after = g.Money - next.upgradeCost;

            return "      [L] Lv" + (s.Level + 1) + " 승격 가능 — 비용 " + next.upgradeCost
                 + ", 승격 후 남는 돈 " + after + " / 권장 운전자본 " + needed
                 + (after < needed ? "  ※자금 부족 — 더 모으고 올릴 것" : "  OK")
                 + "  (승급 당일 발주는 즉시 입고)";
        }

        void OnGUI()
        {
            if (!visible) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                style.normal.textColor = Color.white;
                noticeStyle = new GUIStyle(style) { fontSize = 18 };
                noticeStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);
            }

            GUI.Box(new Rect(8f, 8f, 1160f, 174f), GUIContent.none);
            GUI.Label(new Rect(18f, 12f, 1140f, 20f), line1, style);
            GUI.Label(new Rect(18f, 34f, 1140f, 20f), line2, style);
            GUI.Label(new Rect(18f, 56f, 1140f, 20f), line3, style);
            GUI.Label(new Rect(18f, 78f, 1140f, 20f), line4, style);
            GUI.Label(new Rect(18f, 100f, 1140f, 20f), line5, style);
            GUI.Label(new Rect(18f, 122f, 1140f, 20f),
                "조준 + [E] 상호작용   ·   V 시점 전환(1인칭/3인칭)   ·   3인칭은 오른쪽 버튼 드래그로 시점   ·   WASD 이동", style);
            GUI.Label(new Rect(18f, 142f, 1140f, 20f),
                "F1 디버그 표시   ·   1/2/3 배속   R 하루리셋   L 레벨업   N 오염+3   F5/F9 저장/로드", style);
            if (notice.Length > 0) GUI.Label(new Rect(18f, 162f, 1140f, 22f), notice, noticeStyle);
        }
    }
}
