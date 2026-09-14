using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Shop
{
    /// <summary>
    /// 계산대를 클릭하면 발주 메뉴가 열린다. 대금은 즉시 지불하고 다음날 09:00에 입고된다.
    /// 카메라 피벗에 붙는다(자식에서 Camera를 GetComponent로 찾기 위해).
    /// ponytail: 지금은 OnGUI다. D23-25에 Canvas UI로 교체하되 상호작용 모델은 그대로 유지한다.
    /// </summary>
    public class ShopOrderMenu : MonoBehaviour, IPointerMenu
    {
        const float RefreshInterval = 0.25f;
        const float Width = 460f;
        const float RowHeight = 24f;
        const float Pad = 8f;

        static readonly int[] Quantities = { 1, 5, 10 };

        IPointerMenu[] menus;
        bool open;
        Vector2 anchor;
        float refreshTimer;

        string footer = "";
        string[] rowLabels = new string[0];
        bool[] rowVisible = new bool[0];
        bool[,] buttonEnabled;

        GUIStyle rowStyle;
        GUIStyle headerStyle;
        GUIStyle dimStyle;

        public bool IsOpen => open;

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
        public void Open(Vector2 screenPos)
        {
            open = true;
            anchor = new Vector2(
                Mathf.Min(screenPos.x + 12f, Screen.width - Width - Pad),
                Mathf.Max(Screen.height - screenPos.y - 12f, Pad));
            Rebuild();
        }

        public void Close() => open = false;

        void Update()
        {
            // 메뉴 밖을 클릭하면 닫는다
            Mouse mouse = Mouse.current;
            if (open && mouse != null && mouse.leftButton.wasPressedThisFrame
                && !PointerOverAnyMenu(PointerMenus.PickPosition()))
                open = false;

            PointerMenus.SetOpen(this, open);


            if (!open) return;


            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= RefreshInterval)
            {
                refreshTimer = 0f;
                Rebuild();
            }
        }

        void Rebuild()
        {
            InventoryManager inv = InventoryManager.Instance;
            ProductCatalog cat = inv.Catalog;
            int money = GameManager.Instance.Money;

            if (rowLabels.Length != cat.Count)
            {
                rowLabels = new string[cat.Count];
                rowVisible = new bool[cat.Count];
                buttonEnabled = new bool[cat.Count, Quantities.Length];
            }

            int incomingCost = 0;

            for (int i = 0; i < cat.Count; i++)
            {
                ProductDef p = cat.Get(i);
                rowVisible[i] = inv.IsUnlocked(i);
                incomingCost += inv.IncomingOf(i) * p.wholesale;
                if (!rowVisible[i]) continue;

                rowLabels[i] = p.nameKo
                             + "   창고 " + inv.StorageOf(i)
                             + (inv.IncomingOf(i) > 0 ? " (+" + inv.IncomingOf(i) + ")" : "")
                             + "   도매 " + p.wholesale + "원";

                for (int q = 0; q < Quantities.Length; q++)
                    buttonEnabled[i, q] = money >= p.wholesale * Quantities[q];
            }

            footer = "보유 " + money + "원     내일 입고 대금 " + incomingCost + "원     리드타임 1일";
        }

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (!open) return false;

            float guiY = Screen.height - screenPos.y;
            return guiY >= anchor.y && guiY <= anchor.y + MenuHeight()
                && screenPos.x >= anchor.x && screenPos.x <= anchor.x + Width;
        }

        float MenuHeight()
        {
            int rows = 1;
            for (int i = 0; i < rowVisible.Length; i++) if (rowVisible[i]) rows++;
            return rows * RowHeight + RowHeight + Pad * 4f;
        }

        void OnGUI()
        {
            if (!open) return;

            if (rowStyle == null)
            {
                rowStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
                headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                headerStyle.normal.textColor = new Color(0.6f, 0.9f, 1f);
                dimStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                dimStyle.normal.textColor = new Color(0.75f, 0.78f, 0.74f);
            }

            float h = MenuHeight();
            GUI.Box(new Rect(anchor.x, anchor.y, Width, h), GUIContent.none);

            float x = anchor.x + Pad;
            float y = anchor.y + Pad;
            float w = Width - Pad * 2f;

            GUI.Label(new Rect(x, y, w, RowHeight), "발주 — 내일 09:00 입고", headerStyle);
            y += RowHeight + Pad;

            float buttonWidth = 44f;
            float labelWidth = w - (buttonWidth + 4f) * Quantities.Length;

            for (int i = 0; i < rowVisible.Length; i++)
            {
                if (!rowVisible[i]) continue;

                GUI.Label(new Rect(x, y, labelWidth, RowHeight), rowLabels[i], dimStyle);

                float bx = x + labelWidth;
                for (int q = 0; q < Quantities.Length; q++)
                {
                    GUI.enabled = buttonEnabled[i, q];
                    if (GUI.Button(new Rect(bx, y, buttonWidth, RowHeight - 3f), "+" + Quantities[q], rowStyle))
                    {
                        ActionRunner.TryRun(new InventoryManager.OrderAction(i, Quantities[q]));
                        Rebuild();
                    }
                    bx += buttonWidth + 4f;
                }
                GUI.enabled = true;
                y += RowHeight;
            }

            y += Pad;
            GUI.Label(new Rect(x, y, w, RowHeight), footer, dimStyle);
        }
    }
}
