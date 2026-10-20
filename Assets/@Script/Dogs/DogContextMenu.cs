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
    /// 단계·쿨타임이 어디 적혀 있는지 한눈에 안 들어왔다. 지금은 화면 가운데 나무 액자 창을 둘로 나눠
    ///   머리 판자: 이름 · 견종 / 함께한 날
    ///   왼쪽 "상태": 초상화 · 컨디션 한 줄 · 스탯 다섯 줄(막대, 견종 보정) · 견종 특성
    ///   오른쪽 "할 것": 돌보기 3 · 운동 3(하루 1회) · 훈련 목록(한 줄에 하나: 단계·효과·값·[훈련]·[단계업])
    /// 를 그린다. 규칙(<see cref="TrainingManager"/>·<see cref="DogCare"/>)은 그대로다.
    ///
    /// 창 그림·아이콘은 VARCO 로 뽑았다(<c>UI/UI_TrainWindow</c>, <c>UI/TrainIcons</c>).
    /// </summary>
    public class DogContextMenu : MonoBehaviour, IPointerMenu
    {
        const float RefreshInterval = 0.25f;

        // 설계 좌표. 창 그림(1116x777)과 같은 비율이고, 화면에 맞춰 통째로 줄인다
        const float DesignW = 1000f;
        const float DesignH = 696f;

        [Header("VARCO UI")]
        [SerializeField] Texture2D windowTex;
        [Tooltip("훈련 남은 횟수 발바닥 표시")]
        [SerializeField] Texture2D closeTex;
        [Tooltip("오른쪽 위 귀퉁이 닫기 발바닥(T_UI_DogClosePaw). 창 그림에서 지운 그 발바닥이다")]
        [SerializeField] Texture2D closePawTex;
        [Tooltip("견종 번호 순서. 상태 칸 맨 위 초상화")]
        [SerializeField] Texture2D[] breedPortraits = new Texture2D[0];

        /// <summary>훈련 카탈로그 순서(산책·털관리·고급 훈련·스타일링·놀아주기·전문 훈련·전문 미용).</summary>
        [SerializeField] Texture2D[] trainIcons = new Texture2D[0];
        [SerializeField] Texture2D upgradeIcon;
        [SerializeField] Texture2D cleanIcon;
        [SerializeField] Texture2D healthIcon;
        [SerializeField] Texture2D beautyIcon;
        [SerializeField] Texture2D trainingIcon;
        [SerializeField] Texture2D agilityIcon;

        [Header("운동")]
        [Tooltip("TrainingManager.Exercises 순서(공놀이·달리기·장애물)")]
        [SerializeField] Texture2D[] exerciseIcons = new Texture2D[0];
        [Tooltip("견종 번호 순서. 운동하면 화면 가운데 작은 창으로 띄우는 그림")]
        [SerializeField] Texture2D[] exerciseArt = new Texture2D[0];

        IPointerMenu[] menus;
        Dog target;
        float refreshTimer;

        // ---- 그릴 내용(0.25초마다 다시 계산) ----
        string title = "";
        string subtitle = "";
        int clean, health, beauty, training, agility;
        int breed;
        bool blocked;

        readonly bool[] exerciseEnabled = new bool[TrainingManager.Exercises.Length];
        readonly string[] exerciseReason = new string[TrainingManager.Exercises.Length];
        bool exercisedToday;

        // "운동했다" 창
        const float PopupSeconds = 2.6f;
        float popupAt = -10f;
        int popupBreed, popupGain;
        string popupName = "";
        bool PopupVisible => Time.unscaledTime - popupAt < PopupSeconds;

        readonly string[] careName = { "밥 주기", "목욕", "약 먹이기" };
        readonly int[] careProduct = { DogCare.FoodIndex, DogCare.ShampooIndex, DogCare.MedicineIndex };   // 밥·목욕은 날짜 따라 Rebuild 에서 바뀐다
        string careNotice = "";
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

        GUIStyle titleStyle, subStyle, labelStyle, smallStyle, numStyle, starStyle, btnStyle;

        public bool IsOpen => target != null;
        public Dog Target => target;

        void Awake()
        {
            menus = GetComponents<IPointerMenu>();
        }

        void Start()
        {
            if (TrainingManager.Instance != null) TrainingManager.Instance.OnExercised += ShowExercisePopup;
        }

        void OnDestroy()
        {
            if (TrainingManager.Instance != null) TrainingManager.Instance.OnExercised -= ShowExercisePopup;
        }

        void ShowExercisePopup(Dog dog, int index, int gained)
        {
            popupAt = Time.unscaledTime;
            popupBreed = dog != null ? dog.BreedIndex : 0;
            popupGain = gained;
            popupName = TrainingManager.Exercises[index].nameKo;
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
            subtitle = (string.IsNullOrEmpty(target.Name) ? "" : target.BreedKo + "  /  ") + "함께한 지 " + target.DaysOwned + "일";
            clean = st.Cleanliness;
            health = st.Health;
            beauty = st.Beauty;
            training = st.Training;
            agility = st.Agility;
            breed = target.BreedIndex;
            blocked = st.GrowthBlocked;

            InventoryManager inv = InventoryManager.Instance;
            // 10일차까지 기본, 11일차부터 고급 사료·샴푸 — 아이콘·재고도 지금 쓰는 쪽을 보여 준다
            careProduct[0] = DogCare.FoodNow;
            careProduct[1] = DogCare.ShampooNow;
            careNotice = DogCare.PremiumCare
                ? "밥/목욕에 고급 사료, 고급 샴푸 필요"
                : GameManager.Instance.Day == DogCare.PremiumFromDay - 1
                    ? "내일부터 고급 사료, 고급 샴푸 필요 (오늘 주문해 둘 것)"
                    : DogCare.PremiumFromDay + "일차부터 고급 사료, 고급 샴푸 필요";
            DogCare.CareAction[] care = { DogCare.CareAction.Feed(target), DogCare.CareAction.Bath(target), DogCare.CareAction.Medicine(target) };
            for (int i = 0; i < 3; i++)
            {
                string reason;
                careEnabled[i] = care[i].CanExecute(out reason);
                careStock[i] = inv.StorageOf(careProduct[i]);
            }

            TrainingManager tm = TrainingManager.Instance;
            exercisedToday = tm.ExercisedToday;
            for (int i = 0; i < TrainingManager.Exercises.Length; i++)
            {
                string reason;
                exerciseEnabled[i] = tm.CanExercise(target, i, out reason);
                // 버튼에는 앞부분만 — "재화 부족: 80 / 120" 에서 "재화 부족"
                exerciseReason[i] = string.IsNullOrEmpty(reason) ? "" : reason.Split(':')[0].Trim();
            }

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
            if (axis == GrowthAxis.Both) return "미모/훈련도";
            return axis == GrowthAxis.Beauty ? "미모" : "훈련도";
        }

        // ---- 배치 ----

        /// <summary>창 배율. 1080p 에서 UiSkin.Boost 배로 그리고, 화면의 94% 를 넘지 않게 줄인다.</summary>
        float Scale => UI.UiSkin.ScaleFor(DesignW, DesignH, 0.94f);

        Vector2 Origin => new Vector2((Screen.width - DesignW * Scale) * 0.5f, (Screen.height - DesignH * Scale) * 0.5f);

        public bool ContainsPoint(Vector2 screenPos)
        {
            // "운동했다" 창을 눌러 닫을 때 뒤의 훈련장 창까지 닫히지 않게
            if (PopupVisible && PopupRect().Contains(new Vector2(screenPos.x, Screen.height - screenPos.y))) return true;
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
            if (target == null && !PopupVisible) return;
            EnsureStyles();
            if (target != null) DrawWindow();
            if (PopupVisible) DrawExercisePopup();
        }

        void DrawWindow()
        {

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

            // 닫기 — 창 그림(원본 1116x777) 오른쪽 위 발바닥을 지운 자리(1027,73 60x51, 메운 자리까지 덮는 크기)가 버튼이다. 그림에서는 그 발바닥을 지워 두었다
            Rect pawSpot = windowTex != null
                ? UI.UiSkin.TexelRect(new Rect(0f, 0f, DesignW, DesignH), 1116f, 777f, 1027f, 73f, 60f, 51f)
                : new Rect(DesignW - 90f, 20f, 60f, 60f);
            if (UI.UiSkin.PawClose(pawSpot, closePawTex)) target = null;
            if (target == null) { GUI.matrix = keep; return; }

            // 머리 판자(설계 y 22~121) 아래를 둘로 나눈다 — 왼쪽은 "지금 상태", 오른쪽은 "할 수 있는 것".
            // 예전엔 스탯 5칸·돌보기·운동·훈련 카드 7장이 한 판에 가로로 꽉 차서 어디부터 봐야 할지 몰랐다
            DrawStatus(new Rect(70f, 132f, 290f, 500f));
            Divider(new Rect(378f, 140f, 2f, 484f));
            DrawActions(new Rect(396f, 132f, 534f, 500f));

            GUI.matrix = keep;
        }

        static readonly Color BeautyColor = new Color(0.93f, 0.5f, 0.72f);
        static readonly Color TrainingColor = new Color(0.38f, 0.6f, 0.9f);
        static readonly Color AgilityColor = new Color(0.98f, 0.6f, 0.22f);

        static Color UpkeepColor(int v) =>
            v >= 70 ? new Color(0.45f, 0.78f, 0.45f) : v >= 40 ? new Color(0.95f, 0.75f, 0.3f) : new Color(0.9f, 0.4f, 0.35f);

        /// <summary>왼쪽: 초상화 + 한 줄 컨디션 + 스탯 다섯 줄 + 견종 특성.</summary>
        void DrawStatus(Rect r)
        {
            // 초상화와 컨디션 — 지금 이 아이가 괜찮은지가 맨 먼저 보이게
            Texture2D face = breed >= 0 && breed < breedPortraits.Length ? breedPortraits[breed] : null;
            var pic = new Rect(r.x, r.y, 96f, 96f);
            Panel(pic, new Color(1f, 1f, 1f, 0.6f));
            if (face != null) GUI.DrawTexture(new Rect(pic.x + 4f, pic.y + 4f, 88f, 88f), face, ScaleMode.ScaleToFit);

            float tx = pic.xMax + 12f, tw = r.xMax - tx;
            string mood = blocked ? "성장 정지" : Mathf.Min(clean, health) >= 70 ? "컨디션 좋음" : "돌봐 줄 때가 됐다";
            GUI.Box(new Rect(tx, r.y + 2f, tw, 26f), mood,
                    UI.UiSkin.Tag(blocked ? UI.UiSkin.Coral : Mathf.Min(clean, health) >= 70 ? UI.UiSkin.Green : UI.UiSkin.Cream));

            // 마스코트 등급 — 강아지가 가게의 얼굴이다. 총합이 오르면 등급이 오르고 가게 레벨업 조건이 풀린다
            int total = beauty + training + agility;
            int grade = MascotGrade.GradeOf(total);
            Texture2D badgeArt = MascotGrade.Badge(grade);
            float bx = badgeArt != null ? tx + 46f : tx;   // 배지 그림이 있으면 왼쪽에 두고 띠·막대를 오른쪽으로
            if (badgeArt != null) GUI.DrawTexture(new Rect(tx - 2f, r.y + 30f, 46f, 46f), badgeArt, ScaleMode.ScaleToFit);
            var badge = new Rect(bx, r.y + 34f, r.xMax - bx, 28f);
            Color gc = MascotGrade.Colors[grade];
            GUI.DrawTexture(badge, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, new Color(gc.r, gc.g, gc.b, 0.9f), 0f, 14f);
            GUI.Label(badge, MascotGrade.Names[grade], mascotStyle);
            if (MascotGrade.IsMax(grade))
                GUI.Label(new Rect(bx, r.y + 64f, r.xMax - bx, 18f), "최고 등급", mascotSmall);
            else
            {
                int lo = MascotGrade.Thresholds[grade], hi = MascotGrade.Thresholds[grade + 1];
                Bar(new Rect(bx, r.y + 68f, r.xMax - bx, 8f), Mathf.Clamp01((total - lo) / (float)(hi - lo)), gc);
                GUI.Label(new Rect(tx, r.y + 78f, tw, 18f), MascotGrade.Names[grade + 1] + "까지 " + total + "/" + hi, mascotSmall);
            }
            if (blocked) GUI.Label(new Rect(r.x, r.y + 98f, r.width, 18f), "청결/건강을 40 이상으로 (성장 정지)", warnSmall);

            // 스탯 다섯 줄: 아이콘 / 이름 / 수치, 그 아래 막대. 성장 스탯은 견종 보정을 이름 옆에
            float y = r.y + 112f;
            StatRow(ref y, r, healthIcon, "건강", health, 100f, UpkeepColor(health), GrowthAxis.Both);
            StatRow(ref y, r, cleanIcon, "청결", clean, 100f, UpkeepColor(clean), GrowthAxis.Both);
            y += 6f;
            StatRow(ref y, r, beautyIcon, "미모", beauty, 240f, BeautyColor, GrowthAxis.Beauty);
            StatRow(ref y, r, trainingIcon, "훈련도", training, 240f, TrainingColor, GrowthAxis.Training);
            StatRow(ref y, r, agilityIcon, "어질리티", agility, 240f, AgilityColor, GrowthAxis.Agility);

            // 견종 특성 한 줄 — 어느 쪽을 키우기 좋은지
            string trait = BreedTraits.TraitText(breed);
            if (trait.Length > 0)
                GUI.Label(new Rect(r.x, y + 6f, r.width, 40f), trait, traitStyle);
        }

        void StatRow(ref float y, Rect r, Texture2D icon, string label, int value, float full, Color fill, GrowthAxis axis)
        {
            const float h = 60f;   // 다섯 줄 + 특성 한 줄이 상태 칸(500) 아래까지 고르게 차는 높이
            var ic = new Rect(r.x, y + 7f, 36f, 36f);
            if (icon != null) GUI.DrawTexture(ic, icon, ScaleMode.ScaleToFit);

            float tx = ic.xMax + 10f, tw = r.xMax - tx;
            GUI.Label(new Rect(tx, y + 2f, tw, 22f), label, labelStyle);
            if (axis != GrowthAxis.Both)
            {
                float bonus = BreedTraits.BonusPercent(breed, axis);
                if (bonus != 0f)
                    GUI.Label(new Rect(tx + 74f, y + 4f, 80f, 20f), BreedTraits.Format(bonus), bonus > 0f ? bonusUp : bonusDown);
            }
            GUI.Label(new Rect(tx, y + 2f, tw, 22f), value.ToString(), numStyle);
            Bar(new Rect(tx, y + 30f, tw, 10f), Mathf.Clamp01(value / full), fill);
            y += h;
        }

        /// <summary>오른쪽: 돌보기 / 운동 / 훈련 — 위에서 아래로 "자주 → 계획해서" 순서.</summary>
        void DrawActions(Rect r)
        {
            InventoryManager inv = InventoryManager.Instance;
            const float bgap = 8f, rowH = 54f;
            float bw = (r.width - 2f * bgap) / 3f;
            float y = r.y;

            // 돌보기 — 창고 재고를 쓴다
            GUI.Label(new Rect(r.x, y, 200f, 26f), "돌보기", sectionStyle);
            GUI.Label(new Rect(r.x, y, r.width, 26f), "창고 물건을 쓴다", rightSmall);
            y += 30f;
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(r.x + i * (bw + bgap), y, bw, rowH);
                Texture2D icon = inv.Catalog.Get(careProduct[i]).icon;
                if (ActionButton(b, icon, careName[i], "창고 " + careStock[i] + "개", careEnabled[i], careStock[i] <= 0))
                    RunCare(i);
            }
            y += rowH + 2f;
            GUI.Label(new Rect(r.x + 4f, y, r.width - 4f, 18f), careNotice, DogCare.PremiumCare ? warnSmall : smallStyle);
            y += 20f;

            // 운동 — 돈, 하루 한 번(셋 중 하나)
            GUI.Label(new Rect(r.x, y, 200f, 26f), "운동", sectionStyle);
            GUI.Label(new Rect(r.x + 52f, y + 1f, 60f, 24f), (exercisedToday ? 1 : 0) + " / 1", exercisedToday ? exerciseDone : exerciseCount);
            GUI.Label(new Rect(r.x, y, r.width, 26f), "하루 한 번, 어질리티", rightSmall);
            y += 30f;
            for (int i = 0; i < TrainingManager.Exercises.Length; i++)
            {
                TrainingManager.ExerciseDef def = TrainingManager.Exercises[i];
                var b = new Rect(r.x + i * (bw + bgap), y, bw, rowH);
                bool unlocked = TrainingManager.Instance.IsExerciseUnlocked(i);
                string line = !unlocked ? "Lv " + def.unlockLevel + " 해금"
                            : exercisedToday ? "오늘 완료"
                            : exerciseEnabled[i] ? def.cost.ToString("N0") + "원 / +" + TrainingManager.ExerciseGainOf(i)
                            : exerciseReason[i];
                bool warn = unlocked && !exercisedToday && !exerciseEnabled[i];
                Texture2D icon = i < exerciseIcons.Length ? exerciseIcons[i] : null;
                if (ActionButton(b, icon, def.nameKo, line, exerciseEnabled[i], warn))
                {
                    ActionRunner.TryRun(new TrainingManager.ExerciseAction(target, i));
                    Rebuild();
                }
            }
            y += rowH + 14f;

            DrawTrainingList(new Rect(r.x, y, r.width, r.yMax - y));
        }

        static void Divider(Rect r)
        {
            GUI.color = new Color(0.45f, 0.3f, 0.18f, 0.18f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>아이콘 + 이름 + 한 줄 설명을 담은 작은 버튼. 누르면 true.</summary>
        bool ActionButton(Rect b, Texture2D icon, string name, string line, bool on, bool warn)
        {
            bool hover = on && b.Contains(Event.current.mousePosition);
            Panel(b, hover ? new Color(1f, 0.97f, 0.86f, 1f) : new Color(1f, 1f, 1f, on ? 0.75f : 0.35f));

            var ic = new Rect(b.x + 5f, b.y + (b.height - 40f) * 0.5f, 40f, 40f);
            GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            if (icon != null) GUI.DrawTexture(ic, icon, ScaleMode.ScaleToFit);
            GUI.color = Color.white;

            float tx = ic.xMax + 4f, tw = b.xMax - tx - 3f;
            GUI.Label(new Rect(tx, b.y + 8f, tw, 22f), name, btnName);
            GUI.Label(new Rect(tx, b.y + 34f, tw, 18f), line, warn ? warnSmall : btnLine);

            GUI.enabled = on;
            bool pressed = UI.UiSkin.Click(b, GUIContent.none, GUIStyle.none);
            GUI.enabled = true;
            return pressed;
        }

        // ---- "운동했다" 창 ----

        Rect PopupRect()
        {
            float w = Mathf.Min(520f, Screen.width * 0.5f);
            float h = w * 0.75f + 96f;
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        /// <summary>화면 가운데 작은 창: 그 견종이 공원에서 뛰노는 그림 + "운동했다!" + 오른 어질리티. 잠깐 떴다 사라진다.</summary>
        void DrawExercisePopup()
        {
            float t = Time.unscaledTime - popupAt;
            float a = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((PopupSeconds - t) / 0.35f);
            float pop = Mathf.Lerp(0.9f, 1f, Mathf.Clamp01(t / 0.15f));

            Matrix4x4 keep = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -20;
            Rect r = PopupRect();
            r = new Rect(r.center.x - r.width * pop * 0.5f, r.center.y - r.height * pop * 0.5f, r.width * pop, r.height * pop);

            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Box(r, GUIContent.none, UI.UiSkin.Panel_);
            var img = new Rect(r.x + 12f, r.y + 12f, r.width - 24f, (r.width - 24f) * 0.75f);
            Texture2D art = popupBreed >= 0 && popupBreed < exerciseArt.Length ? exerciseArt[popupBreed] : null;
            if (art == null && exerciseArt.Length > 0) art = exerciseArt[0];
            if (art != null) GUI.DrawTexture(img, art, ScaleMode.ScaleAndCrop);

            float bonus = BreedTraits.BonusPercent(popupBreed, GrowthAxis.Agility);
            GUI.Label(new Rect(r.x, img.yMax + 6f, r.width, 36f), popupName + " 운동했다!", popupTitle);
            GUI.Label(new Rect(r.x, img.yMax + 44f, r.width, 26f),
                      "어질리티 +" + popupGain + (bonus != 0f ? "   (견종 " + BreedTraits.Format(bonus) + ")" : ""), popupLine);
            GUI.color = Color.white;

            // 누르면 바로 닫힌다
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                popupAt = -10f;
                Event.current.Use();
            }
            GUI.matrix = keep;
        }

        /// <summary>
        /// 훈련을 <b>한 줄에 하나씩</b> 늘어놓는다: 아이콘 · 이름 · 단계 별 · 효과 · 비용 · [훈련] · [단계업].
        /// 예전 카드 7장은 그림이 커서 한눈에 비교가 안 됐다 — 줄로 세우면 효과와 값을 위아래로 견줄 수 있다.
        /// </summary>
        void DrawTrainingList(Rect r)
        {
            TrainingManager tm = TrainingManager.Instance;
            GUI.Label(new Rect(r.x, r.y, 200f, 26f), "훈련", sectionStyle);

            // 오늘 남은 훈련 — 글 + 발바닥 도장
            if (tm != null)
            {
                float pipsW = tm.SlotsTotal * 22f;
                GUI.Label(new Rect(r.xMax - pipsW - 230f, r.y, 222f, 26f), "오늘 남은 훈련  " + tm.SlotsLeft + " / " + tm.SlotsTotal, rightStyle);
                for (int i = 0; i < tm.SlotsTotal; i++)
                {
                    GUI.color = i < tm.SlotsLeft ? Color.white : new Color(1f, 1f, 1f, 0.25f);
                    if (closeTex != null) GUI.DrawTexture(new Rect(r.xMax - pipsW + i * 22f, r.y + 3f, 20f, 20f), closeTex, ScaleMode.ScaleToFit);
                }
                GUI.color = Color.white;
            }

            int n = cards.Length;
            if (n == 0) return;
            float top = r.y + 32f;
            float rowH = Mathf.Min(40f, (r.yMax - top) / n);
            for (int i = 0; i < n; i++) DrawTrainingRow(i, new Rect(r.x, top + i * rowH, r.width, rowH - 3f));
        }

        void DrawTrainingRow(int i, Rect row)
        {
            Card d = cards[i];
            TrainingManager tm = TrainingManager.Instance;
            bool hover = d.unlocked && row.Contains(Event.current.mousePosition);
            Panel(row, new Color(1f, 1f, 1f, !d.unlocked ? 0.18f : hover ? 0.7f : 0.45f));

            float cy = row.y + row.height * 0.5f;
            GUI.color = d.unlocked ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            Texture2D icon = i < trainIcons.Length ? trainIcons[i] : null;
            if (icon != null) GUI.DrawTexture(new Rect(row.x + 6f, cy - 15f, 30f, 30f), icon, ScaleMode.ScaleToFit);
            GUI.color = Color.white;

            GUI.Label(new Rect(row.x + 42f, row.y, 100f, row.height), d.name, d.unlocked ? rowName : rowNameOff);

            if (!d.unlocked)
            {
                GUI.Label(new Rect(row.x + 150f, row.y, row.width - 156f, row.height), "Lv " + d.unlockLevel + " 에 열린다", rowLocked);
                return;
            }

            // 단계(★ 채움 / ☆ 빈칸)
            string stars = new string('★', d.stage) + new string('☆', Mathf.Max(0, TrainingStages.Max - d.stage));
            GUI.Label(new Rect(row.x + 142f, row.y, 66f, row.height), stars, starStyle);

            // 효과 칩(축 색)
            Color chip = d.axisKind == GrowthAxis.Beauty ? BeautyColor
                       : d.axisKind == GrowthAxis.Training ? TrainingColor : new Color(0.62f, 0.48f, 0.86f);
            GUI.Box(new Rect(row.x + 210f, cy - 11f, 92f, 22f), d.axis + " +" + d.gain, UI.UiSkin.Tag(chip));

            // 값 — 못 하는 까닭(돈·슬롯·컨디션)이 있으면 그 자리에 빨갛게
            bool showReason = !d.enabled && !d.used && !d.upgrading && d.reason.Length > 0;
            GUI.Label(new Rect(row.x + 304f, row.y, 80f, row.height), showReason ? d.reason : d.cost.ToString("N0") + "원", showReason ? rowWarn : rowCost);

            // [훈련] — 오늘 했거나 강화 중이면 그 상태를 버튼 글로
            var trainBtn = new Rect(row.x + 388f, cy - 14f, 64f, 28f);
            GUI.enabled = d.enabled;
            string trainLabel = d.upgrading ? "강화 " + d.daysLeft + "일" : d.used ? "완료" : "훈련";
            if (UI.UiSkin.Click(trainBtn, trainLabel, d.enabled ? trainBtnStyle : trainBtnOff))
            {
                ActionRunner.TryRun(new TrainingManager.TrainAction(target, i));
                Rebuild();
            }

            // [단계업 n/m] — 여러 번 하면 열린다
            var upBtn = new Rect(row.x + 458f, cy - 14f, row.xMax - row.x - 462f, 28f);
            GUI.enabled = d.canUpgrade;
            if (UI.UiSkin.Click(upBtn, new GUIContent(d.upgradeLabel, d.canUpgrade ? upgradeIcon : null), btnStyle))
            {
                tm.BeginUpgrade(i);
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

        static GUIStyle roundBox, sectionStyle, rightStyle, rightSmall, warnSmall, traitStyle, mascotStyle, mascotSmall;
        static GUIStyle bonusUp, bonusDown, btnName, btnLine, popupTitle, popupLine, exerciseCount, exerciseDone;
        static GUIStyle rowName, rowNameOff, rowLocked, rowCost, rowWarn, trainBtnStyle, trainBtnOff;

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
            rightSmall = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleRight };
            traitStyle = new GUIStyle(smallStyle) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft };
            mascotStyle = new GUIStyle(UI.UiSkin.Title) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            mascotStyle.normal.textColor = new Color(0.22f, 0.13f, 0.06f);
            mascotSmall = new GUIStyle(smallStyle) { fontSize = 12, alignment = TextAnchor.MiddleLeft };

            starStyle = new GUIStyle(UI.UiSkin.Label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            starStyle.normal.textColor = new Color(0.92f, 0.66f, 0.12f);

            btnStyle = new GUIStyle(UI.UiSkin.Button(UI.UiSkin.Cream)) { fontSize = 13, imagePosition = ImagePosition.ImageLeft };
            btnStyle.padding = new RectOffset(4, 4, 3, 3);
            trainBtnStyle = new GUIStyle(UI.UiSkin.Button(UI.UiSkin.Green)) { fontSize = 14 };
            trainBtnOff = new GUIStyle(UI.UiSkin.Button(UI.UiSkin.Cream)) { fontSize = 13 };

            rowName = new GUIStyle(UI.UiSkin.Title) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            rowName.normal.textColor = ink;
            rowNameOff = new GUIStyle(rowName);
            rowNameOff.normal.textColor = new Color(ink.r, ink.g, ink.b, 0.45f);
            rowLocked = new GUIStyle(smallStyle) { fontSize = 13 };
            rowLocked.normal.textColor = new Color(0.45f, 0.35f, 0.25f, 0.7f);
            rowCost = new GUIStyle(UI.UiSkin.Label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            rowCost.normal.textColor = new Color(0.45f, 0.32f, 0.2f);
            rowWarn = new GUIStyle(rowCost) { fontSize = 12 };
            rowWarn.normal.textColor = new Color(0.82f, 0.25f, 0.2f);

            roundBox = new GUIStyle(UI.UiSkin.Tag(Color.white));

            bonusUp = new GUIStyle(smallStyle) { fontSize = 12 };
            bonusUp.normal.textColor = new Color(0.2f, 0.55f, 0.25f);
            bonusDown = new GUIStyle(smallStyle) { fontSize = 12 };
            bonusDown.normal.textColor = new Color(0.78f, 0.3f, 0.25f);
            btnName = new GUIStyle(labelStyle) { fontSize = 15 };
            btnLine = new GUIStyle(smallStyle) { fontSize = 12 };
            warnSmall.fontSize = 12;
            exerciseCount = new GUIStyle(labelStyle) { fontSize = 15 };
            exerciseDone = new GUIStyle(exerciseCount);
            exerciseDone.normal.textColor = new Color(0.8f, 0.3f, 0.25f);

            popupTitle = new GUIStyle(UI.UiSkin.Title) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            popupTitle.normal.textColor = cream;   // 창 바탕(Panel_)이 어두워 진한 글씨는 안 읽혔다
            popupLine = new GUIStyle(UI.UiSkin.Label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            popupLine.normal.textColor = new Color(1f, 0.78f, 0.35f);
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
