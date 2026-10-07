using DogShop.Core;
using DogShop.Data;
using DogShop.Shop;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Dogs
{
    /// <summary>
    /// 강아지를 E 로 부르면 뜨는 <b>훈련장 창</b>. 상태·돌보기·훈련을 한 판에 보여 준다.
    /// 카메라 피벗에 붙는다(자식에서 Camera를 GetComponent로 찾기 위해).
    ///
    /// 예전엔 클릭한 자리 옆에 글자 버튼만 줄줄이 뜨는 메뉴였다 — 무엇이 훈련이고 무엇이 돌보기인지,
    /// 단계·쿨타임이 어디 적혀 있는지 한눈에 안 들어왔다. 지금은 화면 가운데 나무 액자 창에
    ///   위: 이름·함께한 날 / 청결·건강 게이지 / 미모·훈련도 수치
    ///   가운데: 돌보기 3종(상품 사진 + 창고 개수)
    ///   아래: 훈련 카드(아이콘·단계 별·효과·비용·상태 도장) + 단계상승 버튼, 오늘 훈련 슬롯
    /// 를 그린다. 규칙(<see cref="TrainingManager"/>·<see cref="DogCare"/>)은 그대로다.
    ///
    /// 창 그림·카드·아이콘은 VARCO 로 뽑았다(<c>UI/UI_TrainWindow</c>, <c>UI/UI_TrainCard</c>, <c>UI/TrainIcons</c>).
    /// </summary>
    public class DogContextMenu : MonoBehaviour, IPointerMenu
    {
        const float RefreshInterval = 0.25f;

        // 설계 좌표. 창 그림(1116x777)과 같은 비율이고, 화면에 맞춰 통째로 줄인다
        const float DesignW = 1000f;
        const float DesignH = 696f;

        [Header("VARCO UI")]
        [SerializeField] Texture2D windowTex;
        [SerializeField] Texture2D cardTex;
        [SerializeField] Texture2D closeTex;

        /// <summary>훈련 카탈로그 순서(산책·털관리·고급 훈련·스타일링·놀아주기·전문 훈련·전문 미용).</summary>
        [SerializeField] Texture2D[] trainIcons = new Texture2D[0];
        [SerializeField] Texture2D upgradeIcon;
        [SerializeField] Texture2D cleanIcon;
        [SerializeField] Texture2D healthIcon;
        [SerializeField] Texture2D beautyIcon;
        [SerializeField] Texture2D trainingIcon;

        IPointerMenu[] menus;
        Dog target;
        float refreshTimer;

        // ---- 그릴 내용(0.25초마다 다시 계산) ----
        string title = "";
        string subtitle = "";
        int clean, health, beauty, training;
        bool blocked;

        readonly string[] careName = { "밥 주기", "목욕", "약 먹이기" };
        readonly int[] careProduct = { DogCare.FoodIndex, DogCare.ShampooIndex, DogCare.MedicineIndex };
        readonly bool[] careEnabled = new bool[3];
        readonly int[] careStock = new int[3];

        struct Card
        {
            public bool unlocked, enabled, used, upgrading, canUpgrade;
            public int stage, cost, gain, unlockLevel, daysLeft;
            public string name, axis, upgradeLabel, reason;
            public GrowthAxis axisKind;
        }
        Card[] cards = new Card[0];

        GUIStyle titleStyle, subStyle, labelStyle, smallStyle, numStyle, cardName, cardSmall, stampStyle, starStyle, btnStyle;

        public bool IsOpen => target != null;
        public Dog Target => target;

        void Awake()
        {
            menus = GetComponents<IPointerMenu>();
        }

        bool PointerOverAnyMenu(Vector2 screenPos)
        {
            for (int i = 0; i < menus.Length; i++)
                if (menus[i].ContainsPoint(screenPos)) return true;
            return false;
        }

        /// <summary>PlayerInteraction이 E로 호출한다. 창은 화면 가운데에 뜨므로 위치는 쓰지 않는다.</summary>
        public void Open(Dog dog, Vector2 screenPos)
        {
            target = dog;

            // 말을 걸었으면 이쪽을 본다. 등을 보인 채로 메뉴가 뜨면 무시당하는 것처럼 보인다
            DogRoamer roamer = dog != null ? dog.GetComponent<DogRoamer>() : null;
            if (roamer != null) roamer.FaceOwner();

            Rebuild();
        }

        public void Close() => target = null;

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (target != null && mouse != null && mouse.leftButton.wasPressedThisFrame
                && !PointerOverAnyMenu(PointerMenus.PickPosition()))
                target = null;

            if (target != null && target.gameObject == null) target = null;

            // ESC 로도 닫는다(설정 창이 같은 ESC 로 열리지 않게 표시)
            Keyboard keyboard = Keyboard.current;
            if (target != null && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                target = null;
                PointerMenus.TakeEscape();
            }

            PointerMenus.SetOpen(this, IsOpen);
            if (target == null) return;

            DogRoamer roamer = target.GetComponent<DogRoamer>();
            if (roamer != null) roamer.FaceOwner();

            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= RefreshInterval)
            {
                refreshTimer = 0f;
                Rebuild();
            }
        }

        void Rebuild()
        {
            if (target == null) return;

            DogStats st = target.Stats;
            title = target.DisplayName;
            subtitle = (string.IsNullOrEmpty(target.Name) ? "" : target.BreedKo + "  ·  ") + "함께한 지 " + target.DaysOwned + "일";
            clean = st.Cleanliness;
            health = st.Health;
            beauty = st.Beauty;
            training = st.Training;
            blocked = st.GrowthBlocked;

            InventoryManager inv = InventoryManager.Instance;
            DogCare.CareAction[] care = { DogCare.CareAction.Feed(target), DogCare.CareAction.Bath(target), DogCare.CareAction.Medicine(target) };
            for (int i = 0; i < 3; i++)
            {
                string reason;
                careEnabled[i] = care[i].CanExecute(out reason);
                careStock[i] = inv.StorageOf(careProduct[i]);
            }

            TrainingManager tm = TrainingManager.Instance;
            TrainingCatalog cat = tm.Catalog;
            if (cards.Length != cat.Count) cards = new Card[cat.Count];

            for (int i = 0; i < cat.Count; i++)
            {
                TrainingDef def = cat.Get(i);
                Card c = new Card
                {
                    name = def.nameKo,
                    axisKind = def.axis,
                    axis = AxisLabel(def.axis),
                    unlockLevel = def.unlockLevel,
                    unlocked = tm.IsUnlocked(i)
                };

                if (c.unlocked)
                {
                    string reason;
                    c.enabled = tm.CanTrain(target, i, out reason);
                    // 카드에는 앞부분만 — "재화 부족 — 80 / 100" 에서 "재화 부족"
                    c.reason = string.IsNullOrEmpty(reason) ? "" : reason.Split('—')[0].Trim();
                    c.stage = tm.StageOf(i);
                    c.cost = tm.CostOf(i);
                    c.gain = tm.GainOf(i);
                    c.used = tm.UsedToday(i);
                    c.upgrading = tm.IsUpgrading(i);
                    c.daysLeft = tm.UpgradeDaysLeft(i);
                    c.canUpgrade = tm.CanUpgrade(i, out reason);

                    // 아직 덜 했으면 몇 번 남았는지를 적는다 — 단계가 있는 줄 알게
                    c.upgradeLabel = c.stage >= TrainingStages.Max ? "MAX"
                        : tm.RepsOf(i) < tm.RepsNeeded(i) ? "단계업 " + tm.RepsOf(i) + "/" + tm.RepsNeeded(i)
                        : "단계업 " + TrainingStages.DaysToReach(c.stage + 1) + "일";
                }
                cards[i] = c;
            }
        }

        /// <summary>두 축을 올리는 훈련은 양쪽을 다 적는다 — 같은 +4라도 값이 두 배다.</summary>
        static string AxisLabel(GrowthAxis axis)
        {
            if (axis == GrowthAxis.Both) return "미모·훈련도";
            return axis == GrowthAxis.Beauty ? "미모" : "훈련도";
        }

        // ---- 배치 ----

        /// <summary>창을 화면에 맞추는 배율. 설계 크기보다 작은 화면이면 통째로 줄인다.</summary>
        float Scale => Mathf.Min(1f, Screen.width * 0.94f / DesignW, Screen.height * 0.94f / DesignH);

        Vector2 Origin => new Vector2((Screen.width - DesignW * Scale) * 0.5f, (Screen.height - DesignH * Scale) * 0.5f);

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (target == null) return false;
            float s = Scale;
            Vector2 o = Origin;
            float guiY = Screen.height - screenPos.y;
            return screenPos.x >= o.x && screenPos.x <= o.x + DesignW * s
                && guiY >= o.y && guiY <= o.y + DesignH * s;
        }

        // ---- 그리기 ----

        void OnGUI()
        {
            if (target == null) return;
            EnsureStyles();

            GUI.depth = -10;
            Matrix4x4 keep = GUI.matrix;
            Vector2 o = Origin;
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(new Vector3(o.x, o.y, 0f), Quaternion.identity, new Vector3(s, s, 1f));

            if (windowTex != null) GUI.DrawTexture(new Rect(0f, 0f, DesignW, DesignH), windowTex, ScaleMode.StretchToFill);
            else GUI.Box(new Rect(0f, 0f, DesignW, DesignH), GUIContent.none, UI.UiSkin.Panel_);

            // 머리 판자(창 그림의 x 0.28~0.72, y 0.03~0.17)
            GUI.Label(new Rect(300f, 46f, 400f, 40f), title, titleStyle);   // 리본(위 가운데)을 피해 조금 내린다
            GUI.Label(new Rect(300f, 82f, 400f, 22f), subtitle, subStyle);

            // 닫기(발바닥) — 설정 창 발바닥(화면 72px)과 같은 크기로 보이게, 창이 줄어든 만큼 키워 그린다
            float paw = UI.SettingsPanel.PawSize / s;
            var close = new Rect(DesignW - 30f - paw, 22f, paw, paw);
            if (closeTex != null) GUI.DrawTexture(close, closeTex, ScaleMode.ScaleToFit);
            if (GUI.Button(close, closeTex != null ? GUIContent.none : new GUIContent("X"), GUIStyle.none)) target = null;
            if (target == null) { GUI.matrix = keep; return; }

            DrawStats(new Rect(62f, 112f, 876f, 78f));
            DrawCare(new Rect(62f, 202f, 876f, 70f));
            DrawTraining(new Rect(62f, 284f, 876f, 330f));

            GUI.matrix = keep;
        }

        void DrawStats(Rect r)
        {
            float w = (r.width - 3f * 12f) / 4f;
            Gauge(new Rect(r.x, r.y, w, r.height), cleanIcon, "청결", clean, 100f, true);
            Gauge(new Rect(r.x + (w + 12f), r.y, w, r.height), healthIcon, "건강", health, 100f, true);
            Gauge(new Rect(r.x + (w + 12f) * 2f, r.y, w, r.height), beautyIcon, "미모", beauty, 240f, false);
            Gauge(new Rect(r.x + (w + 12f) * 3f, r.y, w, r.height), trainingIcon, "훈련도", training, 240f, false);

            if (blocked)
            {
                var warn = new Rect(r.x + r.width * 0.5f - 210f, r.yMax - 4f, 420f, 22f);
                GUI.Box(warn, "성장 정지 — 청결·건강을 40 이상으로 올리자", UI.UiSkin.Tag(UI.UiSkin.Coral));
            }
        }

        /// <summary>아이콘 + 이름·수치 + 막대. 청결·건강은 0~100 컨디션, 미모·훈련도는 쌓이는 값이다.</summary>
        void Gauge(Rect r, Texture2D icon, string label, int value, float full, bool upkeep)
        {
            Panel(r, new Color(1f, 1f, 1f, 0.55f));
            var ic = new Rect(r.x + 8f, r.y + (r.height - 50f) * 0.5f, 50f, 50f);
            if (icon != null) GUI.DrawTexture(ic, icon, ScaleMode.ScaleToFit);

            float tx = ic.xMax + 8f, tw = r.xMax - tx - 10f;
            GUI.Label(new Rect(tx, r.y + 8f, tw, 22f), label, labelStyle);
            GUI.Label(new Rect(tx, r.y + 8f, tw, 22f), value.ToString(), numStyle);

            var bar = new Rect(tx, r.y + 40f, tw, 14f);
            Color fill = upkeep
                ? (value >= 70 ? new Color(0.45f, 0.78f, 0.45f) : value >= 40 ? new Color(0.95f, 0.75f, 0.3f) : new Color(0.9f, 0.4f, 0.35f))
                : label == "미모" ? new Color(0.93f, 0.5f, 0.72f) : new Color(0.38f, 0.6f, 0.9f);
            Bar(bar, Mathf.Clamp01(value / full), fill);
        }

        void DrawCare(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y - 2f, 120f, 22f), "돌보기", sectionStyle);
            InventoryManager inv = InventoryManager.Instance;
            float w = (r.width - 120f - 2f * 12f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(r.x + 120f + i * (w + 12f), r.y, w, r.height - 8f);
                bool on = careEnabled[i];
                bool hover = on && b.Contains(Event.current.mousePosition);
                Panel(b, hover ? new Color(1f, 0.97f, 0.86f, 1f) : new Color(1f, 1f, 1f, on ? 0.75f : 0.35f));

                Texture2D icon = inv.Catalog.Get(careProduct[i]).icon;
                var ic = new Rect(b.x + 8f, b.y + 4f, b.height - 8f, b.height - 8f);
                GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                if (icon != null) GUI.DrawTexture(ic, icon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;

                GUI.Label(new Rect(ic.xMax + 8f, b.y + 6f, b.width - ic.width - 20f, 24f), careName[i], labelStyle);
                GUI.Label(new Rect(ic.xMax + 8f, b.y + 32f, b.width - ic.width - 20f, 20f),
                    inv.Catalog.Get(careProduct[i]).nameKo + "  창고 " + careStock[i] + "개",
                    careStock[i] > 0 ? smallStyle : warnSmall);

                GUI.enabled = on;
                if (GUI.Button(b, GUIContent.none, GUIStyle.none)) RunCare(i);
                GUI.enabled = true;
            }
        }

        void DrawTraining(Rect r)
        {
            TrainingManager tm = TrainingManager.Instance;
            GUI.Label(new Rect(r.x, r.y, 200f, 24f), "훈련", sectionStyle);

            // 오늘 훈련 슬롯 — 발바닥 도장으로
            if (tm != null)
            {
                string slots = "오늘 남은 훈련  " + tm.SlotsLeft + " / " + tm.SlotsTotal;
                GUI.Label(new Rect(r.xMax - 360f, r.y, 220f, 24f), slots, rightStyle);
                for (int i = 0; i < tm.SlotsTotal; i++)
                {
                    var pip = new Rect(r.xMax - 132f + i * 22f, r.y + 2f, 20f, 20f);
                    GUI.color = i < tm.SlotsLeft ? Color.white : new Color(1f, 1f, 1f, 0.25f);
                    if (closeTex != null) GUI.DrawTexture(pip, closeTex, ScaleMode.ScaleToFit);
                }
                GUI.color = Color.white;
            }

            int n = cards.Length;
            if (n == 0) return;
            const float gap = 8f;
            float cw = (r.width - gap * (n - 1)) / n;
            float ch = 200f;   // 그림 비율(0.75)보다 길게 늘린다 — 아이콘·이름·별·효과·비용이 다 들어가야 한다
            float top = r.y + 30f;

            for (int i = 0; i < n; i++)
            {
                var c = new Rect(r.x + i * (cw + gap), top, cw, ch);
                DrawCard(i, c);

                var up = new Rect(c.x, c.yMax + 6f, c.width, 30f);
                Card d = cards[i];
                if (!d.unlocked) continue;
                GUI.enabled = d.canUpgrade;
                var label = new GUIContent(d.upgradeLabel, d.canUpgrade ? upgradeIcon : null);
                if (GUI.Button(up, label, btnStyle))
                {
                    tm.BeginUpgrade(i);
                    Rebuild();
                }
                GUI.enabled = true;
            }

            GUI.Label(new Rect(r.x, top + ch + 44f, r.width, 22f),
                "카드를 누르면 훈련 · 같은 훈련을 여러 번 하면 단계업이 열린다(강화 중엔 그 훈련을 쉰다)", hintStyle);
        }

        void DrawCard(int i, Rect c)
        {
            Card d = cards[i];
            bool hover = d.unlocked && d.enabled && c.Contains(Event.current.mousePosition);

            // 살짝 떠오르는 느낌
            if (hover) c.y -= 4f;

            GUI.color = !d.unlocked ? new Color(0.55f, 0.55f, 0.55f, 1f)
                      : !d.enabled ? new Color(0.72f, 0.72f, 0.72f, 1f) : Color.white;
            if (cardTex != null) GUI.DrawTexture(c, cardTex, ScaleMode.StretchToFill);
            else GUI.Box(c, GUIContent.none);

            // 카드 안쪽(그림의 x 0.05~0.95, y 0.04~0.88)
            var inner = new Rect(c.x + c.width * 0.05f, c.y + c.height * 0.04f, c.width * 0.9f, c.height * 0.84f);

            Texture2D icon = i < trainIcons.Length ? trainIcons[i] : null;
            float isz = inner.width * 0.62f;
            var ir = new Rect(inner.center.x - isz * 0.5f, inner.y + 6f, isz, isz);
            if (icon != null) GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit);

            float y = ir.yMax + 2f;
            GUI.Label(new Rect(inner.x, y, inner.width, 22f), d.name, cardName);
            y += 22f;

            if (!d.unlocked)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(inner.x, y + 10f, inner.width, 24f), "Lv " + d.unlockLevel + " 해금", stampStyle);
                return;
            }

            // 단계 별(★ 채움 / ☆ 빈칸)
            string stars = new string('★', d.stage) + new string('☆', Mathf.Max(0, TrainingStages.Max - d.stage));
            GUI.Label(new Rect(inner.x, y, inner.width, 18f), stars, starStyle);
            y += 20f;

            Color chip = d.axisKind == GrowthAxis.Beauty ? new Color(0.93f, 0.5f, 0.72f)
                       : d.axisKind == GrowthAxis.Training ? new Color(0.38f, 0.6f, 0.9f) : new Color(0.62f, 0.48f, 0.86f);
            var chipR = new Rect(inner.x + 4f, y, inner.width - 8f, 20f);
            GUI.color = Color.white;
            GUI.Box(chipR, d.axis + " +" + d.gain, UI.UiSkin.Tag(chip));
            y += 24f;

            // 못 하는 이유가 따로 있으면(돈·슬롯·컨디션) 비용 자리에 빨갛게 적는다. 완료·강화는 도장이 말해 준다
            bool showReason = !d.enabled && !d.used && !d.upgrading && d.reason.Length > 0;
            GUI.Label(new Rect(inner.x - 4f, y, inner.width + 8f, 20f), showReason ? d.reason : d.cost.ToString("N0") + "원", showReason ? cardWarn : cardSmall);

            // 상태 도장
            string stamp = d.upgrading ? "강화 중 " + d.daysLeft + "일" : d.used ? "오늘 완료" : "";
            if (stamp.Length > 0)
            {
                var sr = new Rect(c.x + 4f, c.y + c.height * 0.30f, c.width - 8f, 26f);
                GUI.color = new Color(1f, 1f, 1f, 0.92f);
                GUI.Box(sr, stamp, UI.UiSkin.Tag(d.upgrading ? UI.UiSkin.Sky : UI.UiSkin.Green));
            }
            GUI.color = Color.white;

            GUI.enabled = d.enabled;
            if (GUI.Button(c, GUIContent.none, GUIStyle.none))
            {
                ActionRunner.TryRun(new TrainingManager.TrainAction(target, i));
                Rebuild();
            }
            GUI.enabled = true;
        }

        // ---- 그리기 도우미 ----

        static void Panel(Rect r, Color tint)
        {
            GUI.color = tint;
            GUI.Box(r, GUIContent.none, roundBox);
            GUI.color = Color.white;
        }

        static void Bar(Rect r, float t, Color fill)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.15f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * t, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static GUIStyle roundBox, sectionStyle, rightStyle, hintStyle, warnSmall, cardWarn;

        void EnsureStyles()
        {
            if (titleStyle != null) return;

            Color ink = new Color(0.33f, 0.2f, 0.1f);
            Color cream = new Color(1f, 0.96f, 0.86f);

            titleStyle = new GUIStyle(UI.UiSkin.Title) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = cream;
            subStyle = new GUIStyle(UI.UiSkin.Label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            subStyle.normal.textColor = new Color(0.45f, 0.3f, 0.18f);

            labelStyle = new GUIStyle(UI.UiSkin.Label) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = ink;
            numStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleRight, fontSize = 19 };
            smallStyle = new GUIStyle(UI.UiSkin.Label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            smallStyle.normal.textColor = new Color(0.45f, 0.35f, 0.25f);
            warnSmall = new GUIStyle(smallStyle);
            warnSmall.normal.textColor = new Color(0.8f, 0.3f, 0.25f);

            sectionStyle = new GUIStyle(UI.UiSkin.Title) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            sectionStyle.normal.textColor = ink;
            rightStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleRight, fontSize = 15 };
            hintStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };

            cardName = new GUIStyle(UI.UiSkin.Title) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            cardName.normal.textColor = ink;
            cardSmall = new GUIStyle(UI.UiSkin.Label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            cardSmall.normal.textColor = new Color(0.45f, 0.32f, 0.2f);
            starStyle = new GUIStyle(UI.UiSkin.Label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            starStyle.normal.textColor = new Color(0.92f, 0.66f, 0.12f);
            stampStyle = new GUIStyle(cardSmall) { fontSize = 15 };
            cardWarn = new GUIStyle(cardSmall) { fontSize = 12 };
            cardWarn.normal.textColor = new Color(0.82f, 0.25f, 0.2f);

            btnStyle = new GUIStyle(UI.UiSkin.Button(UI.UiSkin.Cream)) { fontSize = 13, imagePosition = ImagePosition.ImageLeft };
            btnStyle.padding = new RectOffset(4, 4, 3, 3);

            roundBox = new GUIStyle(UI.UiSkin.Tag(Color.white));
        }

        void RunCare(int slot)
        {
            if (slot == 0) ActionRunner.TryRun(DogCare.CareAction.Feed(target));
            else if (slot == 1) ActionRunner.TryRun(DogCare.CareAction.Bath(target));
            else ActionRunner.TryRun(DogCare.CareAction.Medicine(target));

            Rebuild();
        }
    }
}
