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
        string sellLabel = "";
        bool sellEnabled;

        readonly string[] careLabels = new string[3];
        readonly bool[] careEnabled = new bool[3];
        string[] trainLabels = new string[0];
        bool[] trainEnabled = new bool[0];
        bool[] trainVisible = new bool[0];

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
            header = target.BreedKo + (target.IsHero ? "  ★ 주인공견" : "  판매견")
                   + "   " + target.DaysOwned + "일";
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
            }

            for (int i = 0; i < cat.Count; i++)
            {
                TrainingDef def = cat.Get(i);
                trainVisible[i] = tm.IsUnlocked(i);
                if (!trainVisible[i]) continue;

                string reason;
                trainEnabled[i] = tm.CanTrain(target, i, out reason);
                trainLabels[i] = def.nameKo
                               + "   " + def.cost + "원"
                               + "   " + (def.axis == GrowthAxis.Beauty ? "미모" : "훈련도") + " +" + def.gain;
            }

            sellEnabled = target.CanSell;
            sellLabel = target.CanSell ? "판매   " + target.SalePrice + "원" : "주인공견은 판매 불가";
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
            int rows = 2 + 3 + 1 + 1;
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
                if (GUI.Button(new Rect(x, y, w, RowHeight - 2f), trainLabels[i], rowStyle))
                    ActionRunner.TryRun(new TrainingManager.TrainAction(target, i));
                y += RowHeight;
            }

            GUI.enabled = true;
            y += Pad;

            GUI.enabled = sellEnabled;
            if (GUI.Button(new Rect(x, y, w, RowHeight - 2f), sellLabel, rowStyle))
            {
                Dog sold = target;
                if (DogManager.Instance.Sell(sold)) target = null;
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
