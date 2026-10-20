using DogShop.Core;
using DogShop.Data;
using DogShop.Shop;
using UnityEngine;
using UnityEngine.UI;

namespace DogShop.UI
{
    /// <summary>
    /// 플레이용 HUD. 디버그 수치가 아니라 <b>판단에 필요한 것만</b> 띄운다 —
    /// 지금 몇 시인지, 돈이 얼마인지, 계산대에 사람이 몇이나 서 있는지.
    ///
    /// 계층은 코드로 짓는다. 씬이 바이너리로 직렬화돼 있어 손으로 짠 UI는 diff에 남지 않고,
    /// 진열대·창고 선반도 이미 런타임 생성이라 방식이 일관된다.
    ///
    /// 폰트는 OnGUI가 쓰던 내장 폰트를 그대로 쓴다. TMP로 가려면 한글 글리프가 든
    /// 폰트 에셋이 필요한데, 그건 배포 라이선스가 걸린 별개 결정이다(D28 빌드 검증 항목).
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        /// <summary>매 프레임 문자열을 새로 만들 이유가 없다. 초당 4번이면 눈에 끊기지 않는다.</summary>
        const float RefreshInterval = 0.25f;

        const float ToastSeconds = 3.5f;

        static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.72f);
        static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f);
        static readonly Color MoneyColor = new Color(1f, 0.84f, 0.38f);
        static readonly Color WarnColor = new Color(1f, 0.45f, 0.40f);
        static readonly Color CalmColor = new Color(0.62f, 0.66f, 0.72f);
        static readonly Color BarFill = new Color(0.42f, 0.72f, 1f);
        static readonly Color PanelEdge = new Color(1f, 1f, 1f, 0.16f);

        /// <summary>
        /// 둥근 모서리 스프라이트. AI로 뽑은 이미지는 9-slice 가장자리가 제각각이라
        /// 늘리면 곡률이 뭉개진다 — 여긴 기하학적 정밀도가 필요한 자리라 직접 그린다.
        /// 한 장 만들어 모든 패널이 색만 바꿔 쓴다.
        /// </summary>
        static Sprite roundedSprite;

        /// <summary>
        /// HUD 폰트. 비워 두면 내장 폰트로 떨어진다 — 씬 참조가 끊겨도 글자는 나와야 한다.
        /// </summary>
        [SerializeField] Font uiFont;

        /// <summary>순서: 돈 · 명성 · 청결 · 손님 · 시계. 씬에서 물린다.</summary>
        [SerializeField] Sprite iconMoney;
        [SerializeField] Sprite iconReputation;
        [SerializeField] Sprite iconClean;
        [SerializeField] Sprite iconCustomer;
        [SerializeField] Sprite iconClock;

        /// <summary>온라인 주문 알림 뼈다귀(UI_Bone). 스프라이트로 임포트돼 있지 않아 RawImage 로 그린다.</summary>
        [SerializeField] Texture2D boneTexture;

        /// <summary>튜토리얼 퀘스트 별(Ico_QuestStar). 뼈다귀처럼 RawImage 로 그린다.</summary>
        [SerializeField] Texture2D questStarTexture;

        Text clockText;
        Text speedText;
        Text moneyText;
        Text levelText;
        Text reputationText;
        Text mascotText;
        RawImage mascotBadge;
        Text mascotNeedText;
        Image reputationBar;
        Text todayText;
        Text cleanText;
        Text deliveryText;
        Text queueText;
        Text toastText;
        RectTransform orderAlert;
        CanvasGroup orderAlertGroup;

        RectTransform questPanel;      // 바깥 판(테두리) — 높이를 여기서 바꾼다
        Text questHeader;
        Text questObjective;
        Text questDetail;
        Text questToggleLabel;
        RectTransform questToggle;
        RectTransform questClaim;      // 목표를 채우면 뜨는 [퀘스트 완료] 버튼
        Image questClaimImage;
        bool questExpanded = true;
        int questVersion = -1;

        float refreshTimer;
        float toastTimer;

        void Awake()
        {
            // 클릭 메뉴(OnGUI)들도 같은 글꼴을 쓰게 넘겨 준다 — 폰트 참조를 들고 있는 건 여기뿐이다
            if (uiFont != null) UiSkin.Font = uiFont;

            Build();
        }

        void Start()
        {
            ActionRunner.OnRejected += HandleRejected;
            ShopLevelManager.Instance.OnLevelUp += HandleLevelUp;
            Refresh();
        }

        void OnDestroy()
        {
            ActionRunner.OnRejected -= HandleRejected;
            if (ShopLevelManager.Instance != null) ShopLevelManager.Instance.OnLevelUp -= HandleLevelUp;
        }

        void Update()
        {
            if (toastTimer > 0f)
            {
                toastTimer -= Time.unscaledDeltaTime;
                if (toastTimer <= 0f) toastText.text = "";
            }

            UpdateQuest();
            UpdateOrderAlert();

            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer < RefreshInterval) return;

            refreshTimer = 0f;
            Refresh();
        }

        // ---- 갱신 ----

        void Refresh()
        {
            TimeManager t = TimeManager.Instance;
            GameManager g = GameManager.Instance;
            ShopLevelManager s = ShopLevelManager.Instance;
            CustomerManager c = CustomerManager.Instance;
            if (t == null || g == null || s == null || c == null) return;

            // 시연 모드는 몇째 날인지 3일 중으로 보여 준다(Day 2 / 3)
            clockText.text = "Day " + g.Day + (Exhibition.Active ? " / " + Exhibition.Days : "") + "   " + t.ClockText;
            speedText.text = "x" + t.SpeedMultiplier;
            speedText.color = t.SpeedMultiplier > 1 ? MoneyColor : CalmColor;

            moneyText.text = g.Money.ToString("N0") + "원";
            levelText.text = "가게 Lv " + s.Level;

            ShopLevelDef next = s.Next;
            if (next != null)
            {
                reputationText.text = "명성 " + g.Reputation + " / " + next.requiredReputation;
                reputationBar.fillAmount = next.requiredReputation > 0
                    ? Mathf.Clamp01(g.Reputation / (float)next.requiredReputation)
                    : 1f;
            }
            else
            {
                reputationText.text = "명성 " + g.Reputation + "   최대 레벨";
                reputationBar.fillAmount = 1f;
            }

            // 마스코트 등급과, 다음 레벨업이 요구하는 등급(모자랄 때만)
            Dogs.Dog hero = Dogs.DogManager.Instance != null ? Dogs.DogManager.Instance.Hero : null;
            if (hero != null)
            {
                int total = Dogs.MascotGrade.Total(hero.Stats);
                int grade = Dogs.MascotGrade.GradeOf(total);
                Texture2D art = Dogs.MascotGrade.Badge(grade);
                mascotText.text = art != null ? Dogs.MascotGrade.Names[grade] : "마스코트  " + Dogs.MascotGrade.Names[grade];   // 배지 그림이 "마스코트"를 대신한다(오른쪽 "~ 필요"와 안 겹치게)
                mascotText.color = Color.Lerp(TextColor, Dogs.MascotGrade.Colors[grade], 0.6f);
                mascotBadge.texture = art;
                mascotBadge.enabled = art != null;
                bool need = next != null && next.heroStatGate > 0 && total < next.heroStatGate;
                mascotNeedText.text = need ? Dogs.MascotGrade.NameForThreshold(next.heroStatGate) + " 필요" : "";
                mascotNeedText.color = new Color(1f, 0.62f, 0.5f);
            }
            else { mascotText.text = ""; mascotNeedText.text = ""; mascotBadge.enabled = false; }

            todayText.text = "오늘 매출 " + c.RevenueToday.ToString("N0") + "원"
                           + "     판매 " + c.SoldToday
                           + "     놓침 " + c.LostToday;

            // 청결은 조용히 손님 수를 최대 40% 깎는다. 화면에 없으면 방치하는 이유가 없다
            CleanlinessManager clean = CleanlinessManager.Instance;
            if (clean != null)
            {
                cleanText.text = "청결 " + clean.Cleanliness + "     손님 x" + clean.CustomerFactor.ToString("0.00");
                cleanText.color = clean.Cleanliness < 50 ? WarnColor : (clean.Cleanliness < 80 ? MoneyColor : CalmColor);
            }

            // 줄이 길어지는 것이 플레이어가 계산대로 가야 한다는 유일한 신호다
            int waiting = c.QueueLength;
            queueText.text = waiting > 0 ? "계산 대기 " + waiting + "명" : "계산 대기 없음";
            queueText.color = waiting >= 3 ? WarnColor : (waiting > 0 ? MoneyColor : CalmColor);

            // 배달은 가게 밖에 쌓인다. 화면 안에 없는 것은 잊어버리므로 남은 개수를 적어 둔다
            InventoryManager inv = InventoryManager.Instance;
            int waitingOutside = inv != null ? inv.DeliveredTotal : 0;
            deliveryText.text = waitingOutside > 0 ? "가게 앞에 배달 " + waitingOutside + "개" : "";
        }

        void HandleRejected(IPlayerAction action, string reason) => Toast(reason, WarnColor);

        void HandleLevelUp(int level) =>
            Toast("가게 레벨 " + level + ": " + ShopLevelManager.Instance.Current.unlockKo, MoneyColor);

        public void Toast(string message, Color color)
        {
            toastText.text = message;
            toastText.color = color;
            toastTimer = ToastSeconds;
        }

        // ---- 계층 생성 ----

        void Build()
        {
            Font font = uiFont
                     ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            var canvasGo = new GameObject("HudCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;   // OnGUI 디버그 오버레이보다 아래에 깔리지 않게

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 기준 해상도를 줄이면 그만큼 크게 그려진다 — UiSkin.Boost 배(글자가 작다는 피드백, 2026-10-17)
            scaler.referenceResolution = new Vector2(1920f, 1080f) / UiSkin.Boost;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 클릭을 가로채면 조준이 막힌다. HUD는 보여주기만 한다
            canvasGo.GetComponent<GraphicRaycaster>().enabled = false;

            RectTransform root = canvasGo.GetComponent<RectTransform>();

            BuildClock(root, font);
            BuildWallet(root, font);
            BuildToday(root, font);
            BuildToast(root, font);
            BuildQuest(root, font);
            BuildOrderAlert(root, font);
        }

        // ---- 튜토리얼 퀘스트 ----

        const float WalletBottom = 24f + WalletHeight;
        const float WalletHeight = 150f;   // 우상단 상태창 높이 — 돈, 레벨·명성, 마스코트 등급 세 줄
        const float QuestWidth = 344f;
        const float QuestRowHeight = 64f;
        const float QuestGap = 8f;
        const float QuestClaimHeight = 40f;
        static readonly Color ClaimColor = new Color(0.553f, 0.753f, 0.635f);   // UiSkin.Green — 다른 창의 확인 버튼과 같은 초록

        /// <summary>
        /// 우상단 상태창 바로 아래 — 별 · 지금 퀘스트 목표(진행도) · [자세히/접기]. 펼치면 할 일 설명이 붙는다.
        /// 다른 HUD 처럼 보여 주기만 하는 판이라 레이캐스트는 꺼 두고, 판을 누른 것은 Update 에서 직접 본다.
        /// </summary>
        void BuildQuest(RectTransform root, Font font)
        {
            RectTransform inner = Panel(root, "Quest", new Vector2(1f, 1f), new Vector2(-24f, -WalletBottom - QuestGap), new Vector2(QuestWidth, QuestRowHeight));
            questPanel = (RectTransform)inner.parent;

            var star = new GameObject("Star", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            star.transform.SetParent(inner, false);
            RawImage starImage = star.GetComponent<RawImage>();
            starImage.texture = questStarTexture;
            starImage.raycastTarget = false;
            if (questStarTexture == null) starImage.color = MoneyColor;
            Anchor(starImage.rectTransform, new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(44f, 44f));

            questHeader = Label(inner, "Header", font, 15, TextAnchor.MiddleLeft, CalmColor);
            Anchor(questHeader.rectTransform, new Vector2(0f, 1f), new Vector2(62f, -8f), new Vector2(180f, 20f));

            questObjective = Label(inner, "Objective", font, 21, TextAnchor.MiddleLeft, TextColor);
            Anchor(questObjective.rectTransform, new Vector2(0f, 1f), new Vector2(62f, -30f), new Vector2(QuestWidth - 74f, 26f));

            questToggle = Block(inner, "Toggle", new Color(1f, 1f, 1f, 0.14f));
            Anchor(questToggle, new Vector2(1f, 1f), new Vector2(-10f, -8f), new Vector2(58f, 22f));
            questToggleLabel = Label(questToggle, "Label", font, 14, TextAnchor.MiddleCenter, TextColor);
            Stretch(questToggleLabel.rectTransform, Vector2.zero, Vector2.zero);

            questDetail = Label(inner, "Detail", font, 17, TextAnchor.UpperLeft, new Color(0.85f, 0.87f, 0.9f));
            questDetail.horizontalOverflow = HorizontalWrapMode.Wrap;
            questDetail.lineSpacing = 1.1f;
            Anchor(questDetail.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -QuestRowHeight), new Vector2(QuestWidth - 28f, 20f));

            questClaim = Block(inner, "Claim", ClaimColor);
            Anchor(questClaim, new Vector2(0f, 1f), new Vector2(12f, -QuestRowHeight + 2f), new Vector2(QuestWidth - 24f, QuestClaimHeight));
            questClaimImage = questClaim.GetComponent<Image>();
            Text claimLabel = Label(questClaim, "Label", font, 20, TextAnchor.MiddleCenter, new Color(0.16f, 0.19f, 0.22f));
            Stretch(claimLabel.rectTransform, Vector2.zero, Vector2.zero);
            claimLabel.text = "퀘스트 완료   +" + TutorialQuestManager.Reward + "원 받기";
            questClaim.gameObject.SetActive(false);

            questPanel.gameObject.SetActive(false);
        }

        /// <summary>튜토리얼 완료 문구를 띄워 두는 시간(초).</summary>
        const float QuestFinishedSeconds = 6f;

        void UpdateQuest()
        {
            if (questPanel == null) return;
            TutorialQuestManager quests = TutorialQuestManager.Instance;

            bool finishedBanner = quests != null && quests.IsComplete && quests.FinishedAt >= 0f
                                  && Time.unscaledTime - quests.FinishedAt < QuestFinishedSeconds;
            // 시연 할 일은 다 끝내도 "오늘 할 일 완료"를 날이 바뀔 때까지 띄워 둔다 — 다음에 뭘 할지 알려 주는 자리라서
            bool show = quests != null && (!quests.IsComplete || finishedBanner || TutorialQuestManager.Demo);
            if (questPanel.gameObject.activeSelf != show) questPanel.gameObject.SetActive(show);
            if (!show) return;

            // 달성했으면 [퀘스트 완료]로 보상을 받고, 아니면 판을 눌러 설명을 접고 편다.
            // 1인칭에서 커서가 잠겨 있으면(화면 가운데 조준) 누른 게 아니다
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            // 다른 창(발주·강아지·설정)이 떠 있으면 그 창을 누른 것이다 — 창 밑으로 퀘스트 판이 눌려 보상이 새지 않게
            if (!quests.IsComplete && mouse != null && mouse.leftButton.wasPressedThisFrame
                && Cursor.lockState != CursorLockMode.Locked && !PointerMenus.AnyOpen)
            {
                Vector2 point = mouse.position.ReadValue();
                if (quests.ReadyToClaim)
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(questClaim, point, null)) quests.Claim();
                }
                else if (RectTransformUtility.RectangleContainsScreenPoint(questPanel, point, null))
                {
                    questExpanded = !questExpanded;
                    questVersion = -1;
                }
            }

            // 받을 보상이 있으면 버튼이 은은하게 숨 쉰다 — 눈에 띄게
            if (quests.ReadyToClaim)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                questClaimImage.color = Color.Lerp(ClaimColor, new Color(0.75f, 0.92f, 0.8f), k);
            }

            if (questVersion == quests.Version) return;
            // 새 퀘스트가 켜지면 설명을 펼쳐 보여 준다
            if (questVersion >= 0 && quests.Version != questVersion && questStepShown != quests.Step) questExpanded = true;
            questStepShown = quests.Step;
            questVersion = quests.Version;

            float height = QuestRowHeight;
            if (quests.IsComplete)
            {
                questHeader.text = quests.Header;
                questObjective.text = quests.CompleteText;
                questObjective.color = MoneyColor;
                questToggle.gameObject.SetActive(false);
                questClaim.gameObject.SetActive(false);
                string next = quests.CompleteDetail;
                questDetail.gameObject.SetActive(next != null);
                if (next != null)
                {
                    questDetail.text = next;
                    float textHeight = Mathf.Ceil(questDetail.preferredHeight);
                    questDetail.rectTransform.sizeDelta = new Vector2(QuestWidth - 28f, textHeight);
                    height += textHeight + 12f;
                }
            }
            else if (quests.ReadyToClaim)
            {
                TutorialQuestManager.Quest q = quests.Current;
                questHeader.text = quests.Header;
                questObjective.text = q.objective + " (" + quests.Progress + "/" + q.target + ")";
                questObjective.color = MoneyColor;
                questToggle.gameObject.SetActive(false);
                questDetail.gameObject.SetActive(false);
                questClaim.gameObject.SetActive(true);
                height += QuestClaimHeight + 10f;
            }
            else
            {
                TutorialQuestManager.Quest q = quests.Current;
                questHeader.text = quests.Header;
                questObjective.text = q.objective + " (" + quests.Progress + "/" + q.target + ")";
                questObjective.color = TextColor;
                questClaim.gameObject.SetActive(false);
                questToggle.gameObject.SetActive(true);
                questToggleLabel.text = questExpanded ? "접기" : "자세히";

                questDetail.gameObject.SetActive(questExpanded);
                if (questExpanded)
                {
                    questDetail.text = q.detail;
                    float textHeight = Mathf.Ceil(questDetail.preferredHeight);
                    questDetail.rectTransform.sizeDelta = new Vector2(QuestWidth - 28f, textHeight);
                    height += textHeight + 12f;
                }
            }
            questPanel.sizeDelta = new Vector2(QuestWidth, height);
        }

        int questStepShown = -1;

        /// <summary>퀘스트 판이 차지하는 아래 끝(우상단 기준, 양수). 그 아래에 다른 알림을 붙인다.</summary>
        float QuestBottom => questPanel != null && questPanel.gameObject.activeSelf
            ? WalletBottom + QuestGap + questPanel.sizeDelta.y
            : WalletBottom;

        /// <summary>
        /// 우상단 상태창 바로 아래 — 수락 안 한 온라인 주문이 있으면 뼈다귀가 깜빡이며 "주문접수중".
        /// 주문은 토스트로 한 번 알리고 끝나서, 놓치면 모니터에 갈 이유를 모른다.
        /// </summary>
        void BuildOrderAlert(RectTransform root, Font font)
        {
            var go = new GameObject("OrderAlert", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(root, false);
            orderAlert = go.GetComponent<RectTransform>();
            orderAlertGroup = go.GetComponent<CanvasGroup>();
            orderAlertGroup.blocksRaycasts = false;
            orderAlertGroup.interactable = false;
            // 상태창(폭 344, 높이 120, 위 여백 24)의 가운데 아래
            Anchor(orderAlert, new Vector2(1f, 1f), new Vector2(-24f - (344f - 220f) * 0.5f, -24f - 120f - 8f), new Vector2(220f, 105f));

            var bone = new GameObject("Bone", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            bone.transform.SetParent(orderAlert, false);
            RawImage img = bone.GetComponent<RawImage>();
            img.texture = boneTexture;
            img.raycastTarget = false;
            if (boneTexture == null) img.color = new Color(0.96f, 0.62f, 0.25f);
            Stretch(img.rectTransform, Vector2.zero, Vector2.zero);

            Text label = Label(orderAlert, "Label", font, 24, TextAnchor.MiddleCenter, new Color(0.35f, 0.16f, 0.04f));
            Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
            label.text = "주문접수중";

            go.SetActive(false);
        }

        void UpdateOrderAlert()
        {
            if (orderAlert == null) return;
            OnlineOrderManager orders = OnlineOrderManager.Instance;
            bool show = orders != null && orders.NewCount > 0;
            if (orderAlert.gameObject.activeSelf != show) orderAlert.gameObject.SetActive(show);
            if (!show) return;
            // 퀘스트 판이 있으면 그 아래로 내려간다(판은 펼치고 접을 때마다 높이가 바뀐다)
            orderAlert.anchoredPosition = new Vector2(orderAlert.anchoredPosition.x, -QuestBottom - 8f);
            // 1초에 한 번 흐려졌다 돌아온다 — 완전히 사라지면 읽기 어렵다
            orderAlertGroup.alpha = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f));
        }

        /// <summary>좌상단 — 날짜·시각·배속.</summary>
        void BuildClock(RectTransform root, Font font)
        {
            RectTransform panel = Panel(root, "Clock", new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(306f, 66f));

            Icon(panel, iconClock, new Vector2(0f, 1f), new Vector2(12f, -11f), 44f);

            clockText = Label(panel, "Clock", font, 26, TextAnchor.MiddleLeft, TextColor);
            Stretch(clockText.rectTransform, new Vector2(64f, 0f), new Vector2(-64f, 0f));

            speedText = Label(panel, "Speed", font, 22, TextAnchor.MiddleRight, CalmColor);
            Stretch(speedText.rectTransform, new Vector2(0f, 0f), new Vector2(-16f, 0f));
        }

        /// <summary>우상단 — 돈·레벨·명성.</summary>
        void BuildWallet(RectTransform root, Font font)
        {
            RectTransform panel = Panel(root, "Wallet", new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(344f, WalletHeight));

            Icon(panel, iconMoney, new Vector2(0f, 1f), new Vector2(14f, -10f), 40f);

            moneyText = Label(panel, "Money", font, 30, TextAnchor.MiddleRight, MoneyColor);
            Anchor(moneyText.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(250f, 36f));

            levelText = Label(panel, "Level", font, 20, TextAnchor.MiddleLeft, TextColor);
            Anchor(levelText.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -56f), new Vector2(140f, 24f));

            Icon(panel, iconReputation, new Vector2(1f, 1f), new Vector2(-150f, -54f), 28f);

            reputationText = Label(panel, "Reputation", font, 20, TextAnchor.MiddleRight, CalmColor);
            Anchor(reputationText.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -56f), new Vector2(126f, 24f));

            // 마스코트 등급 — 강아지가 커야 가게가 큰다. 다음 레벨업이 등급을 요구하면 오른쪽에 무엇이 필요한지
            var badgeGo = new GameObject("MascotBadge", typeof(RectTransform), typeof(RawImage));
            badgeGo.transform.SetParent(panel, false);
            mascotBadge = badgeGo.GetComponent<RawImage>();
            mascotBadge.raycastTarget = false;
            Anchor(mascotBadge.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -84f), new Vector2(32f, 32f));
            mascotText = Label(panel, "Mascot", font, 18, TextAnchor.MiddleLeft, TextColor);
            Anchor(mascotText.rectTransform, new Vector2(0f, 1f), new Vector2( 48f, -88f), new Vector2(150f, 24f));
            mascotNeedText = Label(panel, "MascotNeed", font, 18, TextAnchor.MiddleRight, CalmColor);
            Anchor(mascotNeedText.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -88f), new Vector2(170f, 24f));

            RectTransform track = Block(panel, "RepTrack", new Color(1f, 1f, 1f, 0.12f));
            track.GetComponent<Image>().sprite = null;
            Anchor(track, new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(312f, 6f));
            track.pivot = new Vector2(0f, 0.5f);

            reputationBar = Block(track, "RepFill", BarFill).GetComponent<Image>();
            reputationBar.sprite = null;                 // 6px 높이에 둥근 모서리는 뭉개진다
            Stretch(reputationBar.rectTransform, Vector2.zero, Vector2.zero);
            reputationBar.type = Image.Type.Filled;
            reputationBar.fillMethod = Image.FillMethod.Horizontal;
            reputationBar.fillOrigin = 0;
        }

        /// <summary>좌하단 — 오늘 실적, 청결, 계산 대기.</summary>
        void BuildToday(RectTransform root, Font font)
        {
            RectTransform panel = Panel(root, "Today", new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(452f, 150f));

            todayText = Label(panel, "Today", font, 20, TextAnchor.MiddleLeft, TextColor);
            Anchor(todayText.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(420f, 24f));

            Icon(panel, iconClean, new Vector2(0f, 1f), new Vector2(14f, -46f), 28f);

            cleanText = Label(panel, "Clean", font, 20, TextAnchor.MiddleLeft, CalmColor);
            Anchor(cleanText.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -48f), new Vector2(386f, 24f));

            Icon(panel, iconCustomer, new Vector2(0f, 0f), new Vector2(14f, 44f), 30f);

            queueText = Label(panel, "Queue", font, 22, TextAnchor.MiddleLeft, CalmColor);
            Anchor(queueText.rectTransform, new Vector2(0f, 0f), new Vector2(50f, 46f), new Vector2(386f, 26f));

            // 배달은 가게 밖에 있어서 화면에 안 잡힌다. 여기 없으면 아침에 나가 볼 이유를 모른다
            deliveryText = Label(panel, "Delivery", font, 20, TextAnchor.MiddleLeft, MoneyColor);
            Anchor(deliveryText.rectTransform, new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(420f, 24f));
        }

        /// <summary>상단 중앙 — 레벨업·거절 사유 같은 한 줄 알림.</summary>
        void BuildToast(RectTransform root, Font font)
        {
            // 강아지 상태창 리본(화면 위 140 근처)에 가리지 않게 조금 위로 — 훈련하다 마스코트 등급이 오르는 알림이 여기 뜬다
            toastText = Label(root, "Toast", font, 26, TextAnchor.MiddleCenter, MoneyColor);
            Anchor(toastText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -74f), new Vector2(900f, 34f));

            var outline = toastText.gameObject.AddComponent<Shadow>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            toastText.text = "";
        }

        // ---- 조립 도우미 ----

        /// <summary>
        /// 테두리 1px 을 두른 패널. 바깥 판 위에 안쪽 판을 1px 줄여 얹으면
        /// 어떤 크기에서도 선이 번지지 않는다 — 텍스처에 테두리를 구우면 늘릴 때 흐려진다.
        /// 자식은 <b>안쪽 판</b>에 붙으므로 반환값도 그쪽이다.
        /// </summary>
        static RectTransform Panel(RectTransform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            RectTransform outer = Block(parent, name, PanelEdge);
            Anchor(outer, anchor, offset, size);

            RectTransform inner = Block(outer, "Fill", PanelColor);
            Stretch(inner, new Vector2(1f, 1f), new Vector2(-1f, -1f));

            return inner;
        }

        static RectTransform Block(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.sprite = Rounded();
            image.type = Image.Type.Sliced;

            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// 반지름 <see cref="CornerRadius"/> 의 둥근 사각형을 한 번만 굽는다.
        /// 9-slice 경계를 반지름에 맞춰 잡으므로 어떤 크기로 늘려도 모서리가 그대로다.
        /// </summary>
        const int CornerRadius = 12;

        static Sprite Rounded()
        {
            if (roundedSprite != null) return roundedSprite;

            int size = CornerRadius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 모서리 중심까지의 거리로 안팎을 가른다. 가장자리 1px 은 부드럽게 깎는다
                float dx = Mathf.Max(0f, Mathf.Max(CornerRadius - x - 0.5f, x + 0.5f - (size - CornerRadius)));
                float dy = Mathf.Max(0f, Mathf.Max(CornerRadius - y - 0.5f, y + 0.5f - (size - CornerRadius)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(CornerRadius - d)));
            }
            tex.Apply();

            float b = CornerRadius;
            roundedSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            return roundedSprite;
        }

        /// <summary>
        /// 아이콘 한 장. 스프라이트가 없으면 아예 만들지 않는다 —
        /// 빈 사각형이 뜨느니 글자만 나오는 편이 낫다.
        /// </summary>
        static void Icon(RectTransform parent, Sprite sprite, Vector2 anchor, Vector2 offset, float size)
        {
            if (sprite == null) return;

            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;

            Anchor(go.GetComponent<RectTransform>(), anchor, offset, new Vector2(size, size));
        }

        static Text Label(RectTransform parent, string name, Font font, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        /// <summary>
        /// 한 점에 고정하고 크기를 준다. anchor 가 (0,1)이면 offset 은 좌상단 기준,
        /// (1,1)이면 우상단 기준이 되도록 피벗을 같이 맞춘다.
        /// </summary>
        static void Anchor(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = min;
            rect.offsetMax = max;
        }
    }
}
