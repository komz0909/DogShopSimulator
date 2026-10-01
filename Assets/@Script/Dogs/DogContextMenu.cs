using DogShop.Core;
using DogShop.Data;
using DogShop.Shop;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Dogs
{
    /// <summary>
    /// 강아지를 클릭하면 상호작용 메뉴가 열린다. 케어·훈련·판매를 여기서 전부 처리한다.
    /// 카메라 피벗에 붙는다(자식에서 Camera를 GetComponent로 찾기 위해).
    /// ponytail: 지금은 OnGUI다. D23-25에 Canvas UI로 교체하되 상호작용 모델은 그대로 유지한다.
    /// </summary>
    public class DogContextMenu : MonoBehaviour, IPointerMenu
    {
        const float RefreshInterval = 0.25f;
        const float Width = 300f;
        const float RowHeight = 22f;
        const float Pad = 8f;

        IPointerMenu[] menus;
        Dog target;
        Vector2 anchor;
        float refreshTimer;

        string header = "";
        string statLine = "";

        readonly string[] careLabels = new string[3];
        readonly bool[] careEnabled = new bool[3];
        string[] trainLabels = new string[0];
        bool[] trainEnabled = new bool[0];
        bool[] trainVisible = new bool[0];

        /// <summary>단계상승 버튼. 훈련 줄 오른쪽에 붙는다.</summary>
        string[] upgradeLabels = new string[0];
        bool[] upgradeEnabled = new bool[0];

        /// <summary>단계상승 버튼 너비. 훈련 줄에서 이만큼을 떼어 쓴다.</summary>
        const float UpgradeWidth = 54f;

        GUIStyle rowStyle;
        GUIStyle headerStyle;
        GUIStyle dimStyle;

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

        /// <summary>PlayerInteraction이 E로 호출한다.</summary>
        public void Open(Dog dog, Vector2 screenPos)
        {
            target = dog;
            anchor = new Vector2(
                Mathf.Min(screenPos.x + 12f, Screen.width - Width - Pad),
                Mathf.Max(Screen.height - screenPos.y - 12f, Pad));

            // 말을 걸었으면 이쪽을 본다. 등을 보인 채로 메뉴가 뜨면 무시당하는 것처럼 보인다
            DogRoamer roamer = dog != null ? dog.GetComponent<DogRoamer>() : null;
            if (roamer != null) roamer.FaceOwner();

            Rebuild();
        }

        public void Close() => target = null;

        void Update()
        {
            // 메뉴 밖을 클릭하면 닫는다
            Mouse mouse = Mouse.current;
            if (target != null && mouse != null && mouse.leftButton.wasPressedThisFrame
                && !PointerOverAnyMenu(PointerMenus.PickPosition()))
                target = null;

            // 강아지가 팔려서 사라졌으면 메뉴를 닫는다
            if (target != null && target.gameObject == null) target = null;

            PointerMenus.SetOpen(this, IsOpen);
            if (target == null) return;

            // 메뉴가 열려 있는 동안은 계속 이쪽을 본다. 하던 동작(훈련 연출)은 건드리지 않는다
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
            header = target.BreedKo + "   함께한 지 " + target.DaysOwned + "일";
            statLine = "청결 " + st.Cleanliness + "   건강 " + st.Health
                     + "   미모 " + st.Beauty + "   훈련도 " + st.Training
                     + (st.GrowthBlocked ? "   ※성장정지" : "");

            InventoryManager inv = InventoryManager.Instance;
            BuildCare(0, DogCare.CareAction.Feed(target), "밥 주기", DogCare.FoodIndex, inv);
            BuildCare(1, DogCare.CareAction.Bath(target), "목욕", DogCare.ShampooIndex, inv);
            BuildCare(2, DogCare.CareAction.Medicine(target), "약 먹이기", DogCare.MedicineIndex, inv);

            TrainingManager tm = TrainingManager.Instance;
            TrainingCatalog cat = tm.Catalog;
            if (trainLabels.Length != cat.Count)
            {
                trainLabels = new string[cat.Count];
                trainEnabled = new bool[cat.Count];
                trainVisible = new bool[cat.Count];
                upgradeLabels = new string[cat.Count];
                upgradeEnabled = new bool[cat.Count];
            }

            for (int i = 0; i < cat.Count; i++)
            {
                TrainingDef def = cat.Get(i);
                trainVisible[i] = tm.IsUnlocked(i);
                if (!trainVisible[i]) continue;

                string reason;
                trainEnabled[i] = tm.CanTrain(target, i, out reason);

                // 왜 못 누르는지 줄 안에서 바로 보여 준다. 슬롯이 남았는데 회색인 이유가
                // 쿨타임인지 강화 중인지 알 길이 없으면 고장으로 보인다
                string mark = tm.IsUpgrading(i) ? "   (강화 " + tm.UpgradeDaysLeft(i) + "일)"
                            : tm.UsedToday(i) ? "   (오늘 완료)" : "";

                trainLabels[i] = def.nameKo + " " + tm.StageOf(i) + "단계"
                               + "   " + tm.CostOf(i) + "원"
                               + "   " + AxisLabel(def.axis) + " +" + tm.GainOf(i)
                               + mark;

                string why;
                upgradeEnabled[i] = tm.CanUpgrade(i, out why);

                // 아직 덜 했으면 <b>몇 번 남았는지</b>를 버튼에 적는다.
                // "왜 회색인지"를 따로 찾아보게 하면 단계가 있는 줄도 모른다
                upgradeLabels[i] = tm.StageOf(i) >= TrainingStages.Max ? "MAX"
                    : tm.RepsOf(i) < tm.RepsNeeded(i) ? tm.RepsOf(i) + "/" + tm.RepsNeeded(i)
                    : "▲" + TrainingStages.DaysToReach(tm.StageOf(i) + 1) + "일";
            }
        }

        /// <summary>두 축을 올리는 훈련은 양쪽을 다 적는다 — 같은 +4라도 값이 두 배다.</summary>
        static string AxisLabel(GrowthAxis axis)
        {
            if (axis == GrowthAxis.Both) return "미모·훈련도";
            return axis == GrowthAxis.Beauty ? "미모" : "훈련도";
        }

        void BuildCare(int slot, DogCare.CareAction action, string label, int productIndex, InventoryManager inv)
        {
            string reason;
            careEnabled[slot] = action.CanExecute(out reason);
            careLabels[slot] = label + "   " + inv.Catalog.Get(productIndex).nameKo
                             + " 창고 " + inv.StorageOf(productIndex) + "개";
        }

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (target == null) return false;

            float guiY = Screen.height - screenPos.y;
            return guiY >= anchor.y && guiY <= anchor.y + MenuHeight()
                && screenPos.x >= anchor.x && screenPos.x <= anchor.x + Width;
        }

        float MenuHeight()
        {
            int rows = 2 + 3 + 1;   // 머리말·스탯 / 관리 3종 / 슬롯 표시 (판매 줄 제거)
            for (int i = 0; i < trainVisible.Length; i++) if (trainVisible[i]) rows++;
            return rows * RowHeight + Pad * 4f;
        }

        void OnGUI()
        {
            if (target == null) return;

            if (rowStyle == null)
            {
                rowStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
                headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                headerStyle.normal.textColor = new Color(1f, 0.9f, 0.5f);
                dimStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                dimStyle.normal.textColor = new Color(0.75f, 0.78f, 0.74f);
            }

            float h = MenuHeight();
            GUI.Box(new Rect(anchor.x, anchor.y, Width, h), GUIContent.none);

            float y = anchor.y + Pad;
            float x = anchor.x + Pad;
            float w = Width - Pad * 2f;

            GUI.Label(new Rect(x, y, w, RowHeight), header, headerStyle);
            y += RowHeight;
            GUI.Label(new Rect(x, y, w, RowHeight), statLine, dimStyle);
            y += RowHeight + Pad;

            TrainingManager tm = TrainingManager.Instance;
            GUI.Label(new Rect(x, y - Pad * 0.5f, w, 12f), "", dimStyle);

            for (int i = 0; i < 3; i++)
            {
                GUI.enabled = careEnabled[i];
                if (GUI.Button(new Rect(x, y, w, RowHeight - 2f), careLabels[i], rowStyle)) RunCare(i);
                y += RowHeight;
            }

            GUI.enabled = true;
            y += Pad;

            for (int i = 0; i < trainVisible.Length; i++)
            {
                if (!trainVisible[i]) continue;

                GUI.enabled = trainEnabled[i];
                if (GUI.Button(new Rect(x, y, w - UpgradeWidth - 2f, RowHeight - 2f), trainLabels[i], rowStyle))
                    ActionRunner.TryRun(new TrainingManager.TrainAction(target, i));

                // 단계상승은 돈이 아니라 시간을 낸다. 누르면 그 훈련이 며칠 잠긴다
                GUI.enabled = upgradeEnabled[i];
                if (GUI.Button(new Rect(x + w - UpgradeWidth, y, UpgradeWidth, RowHeight - 2f),
                        upgradeLabels[i], rowStyle))
                {
                    tm.BeginUpgrade(i);
                    Rebuild();
                }

                y += RowHeight;
            }

            GUI.enabled = true;

            if (tm != null)
            {
                GUI.Label(new Rect(x, anchor.y + h - RowHeight - 2f, w, RowHeight),
                    "훈련 슬롯 " + tm.SlotsUsed + " / " + tm.SlotsTotal, dimStyle);
            }
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
