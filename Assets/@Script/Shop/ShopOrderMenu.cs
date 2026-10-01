using System.Collections.Generic;
using DogShop.Core;
using DogShop.Data;
using DogShop.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Shop
{
    /// <summary>
    /// 계산대에서 여는 상점창. 대금은 즉시 지불하고 다음날 09:00에 입고된다.
    ///
    /// 예전에는 전 상품이 한 줄짜리 글로 쭉 늘어서 있었다. 이름과 숫자만 보고 고르니
    /// <b>무엇을 파는 가게인지가 창에서 사라졌고</b>, 해금 안 된 상품은 아예 숨어서
    /// 레벨을 올릴 이유도 창에 없었다. 지금은 넷으로 나눈 칸을 넘겨 가며 사진으로 고른다.
    ///
    /// - 사진은 진열대에 실제로 올라가는 그 모델을 찍은 것이다(<see cref="EditorTools"/> 의 사진 굽기)
    /// - 해금 전 상품도 회색으로 보여 준다 — 몇 레벨에 무엇이 열리는지가 승급의 이유다
    /// - 가구 칸은 <b>아직 배달이 없다</b>. 살 수 있게 되는 건 가게 앞 배달이 생기고 나서다
    ///
    /// 카메라 피벗에 붙는다(자식에서 Camera를 GetComponent로 찾기 위해).
    /// </summary>
    public class ShopOrderMenu : MonoBehaviour, IPointerMenu
    {
        const float RefreshInterval = 0.25f;

        const float Pad = 16f;
        const float Gap = 10f;
        const int Columns = 4;
        const float CardWidth = 132f;
        const float CardHeight = 230f;

        // 카드 안에서 줄이 시작하는 높이. 잠긴 상품은 창고·버튼 자리에 해금 레벨이 대신 들어간다
        const float PhotoHeight = 98f;
        const float NameY = 110f;
        const float PriceY = 132f;
        const float RetailY = 158f;
        const float StockY = 177f;
        const float ActionY = 196f;
        const float HeaderHeight = 30f;
        const float TabHeight = 34f;
        const float FooterHeight = 24f;

        static readonly int[] Quantities = { 1, 5, 10 };

        /// <summary>
        /// 가구 목록. 상품 카탈로그와 따로인 이유는 <see cref="FurnitureCatalog"/> 에 적어 두었다.
        /// 비어 있으면 가구 칸은 준비 중이라고만 뜬다.
        /// </summary>
        [SerializeField] FurnitureCatalog furniture;

        /// <summary>랜덤 상자 버튼에 얹을 그림.</summary>
        [SerializeField] Texture2D boxIcon;

        IPointerMenu[] menus;
        bool open;
        float refreshTimer;

        int tab;

        /// <summary>
        /// 지금 칸에 보일 상품 번호를 <b>싼 것부터</b> 담는다. 칸을 바꿀 때마다 다시 모은다.
        ///
        /// 카탈로그 배열 순서로 그리지 않는 이유: 그 순서는 세이브 배열과 진열대 칸이 쓰는
        /// <b>상품 번호</b>라 화면 사정으로 바꿀 수 없다. 정렬은 여기서만 한다.
        /// </summary>
        readonly List<int> visible = new List<int>();

        /// <summary>가구도 같은 규칙으로 싼 것부터. 가구 카탈로그 순서는 건드리지 않는다.</summary>
        readonly List<int> furnitureOrder = new List<int>();

        string money = "";
        string footer = "";

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

        /// <summary>PlayerInteraction이 E로 호출한다. 창이 커서 클릭한 자리가 아니라 화면 가운데에 띄운다.</summary>
        public void Open(Vector2 screenPos)
        {
            open = true;
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

        // ---- 내용 ----

        ProductCategory Category => ProductCategories.Tabs[Mathf.Clamp(tab, 0, ProductCategories.Tabs.Length - 1)];
        bool IsFurnitureTab => Category == ProductCategory.Furniture;

        void Rebuild()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            EnsureCart(inv);

            ProductCatalog catalog = inv.Catalog;

            visible.Clear();
            if (!IsFurnitureTab)
            {
                for (int i = 0; i < catalog.Count; i++)
                    if (catalog.Get(i).category == Category) visible.Add(i);

                visible.Sort((a, b) => catalog.Get(a).wholesale.CompareTo(catalog.Get(b).wholesale));
            }

            furnitureOrder.Clear();
            if (furniture != null)
            {
                for (int i = 0; i < furniture.Count; i++) furnitureOrder.Add(i);
                furnitureOrder.Sort((a, b) => furniture.Get(a).price.CompareTo(furniture.Get(b).price));
            }

            int incomingCost = 0;
            for (int i = 0; i < catalog.Count; i++)
                incomingCost += inv.IncomingOf(i) * catalog.Get(i).wholesale;

            money = "보유 " + GameManager.Instance.Money.ToString("N0") + "원";

            int outside = inv.DeliveredTotal;
            int furnitureOnWay = FurnitureShop.Instance != null ? FurnitureShop.Instance.OrderedCount : 0;

            if (IsFurnitureTab && furnitureOnWay > 0)
                footer = "가구 " + furnitureOnWay + "개가 내일 아침 가게 앞에 온다     F 로 들어 옮긴다";
            else if (outside > 0)
                footer = "가게 앞에 " + outside + "개가 와 있다     들여놓고 시킬 것";
            else if (inv.RushDelivery)
                footer = "승급 특급 입고     오늘 주문한 것도 가게 앞으로 바로 온다";
            else
                footer = "내일 09:00 가게 앞 배달 " + incomingCost.ToString("N0") + "원     리드타임 1일";
        }

        /// <summary>지금 탭에 놓일 줄 수.</summary>
        int RowsNow()
        {
            int count = IsFurnitureTab ? (furniture != null ? furniture.Count : 0) : visible.Count;
            return Mathf.Max(1, Mathf.CeilToInt(count / (float)Columns));
        }

        /// <summary>가장 물건이 많은 탭의 줄 수. 창의 <b>윗변</b>을 붙박아 두는 데 쓴다.</summary>
        int RowsMost()
        {
            InventoryManager inv = InventoryManager.Instance;
            int most = furniture != null ? furniture.Count : 0;

            if (inv != null)
            {
                ProductCatalog catalog = inv.Catalog;
                for (int t = 0; t < ProductCategories.Tabs.Length; t++)
                {
                    int count = 0;
                    for (int i = 0; i < catalog.Count; i++)
                        if (catalog.Get(i).category == ProductCategories.Tabs[t]) count++;
                    if (count > most) most = count;
                }
            }

            return Mathf.Max(1, Mathf.CeilToInt(most / (float)Columns));
        }

        static float HeightFor(int rows) =>
            Pad * 2f + HeaderHeight + 8f + TabHeight + 12f
            + rows * (CardHeight + Gap) - Gap + 12f + FooterHeight;

        /// <summary>
        /// 높이는 지금 탭에 맞추되 <b>윗변은 가장 큰 탭 기준으로 붙박는다.</b>
        ///
        /// 창을 통째로 화면 가운데에 두면 5개짜리 동물사료에서 2개짜리 애견용품으로 넘길 때
        /// 창이 줄면서 위로 올라가, 방금 누른 탭 단추가 커서 밑에서 달아난다.
        /// 그렇다고 높이까지 최대로 고정하면 물건 둘인 탭에 검은 여백이 한 화면 남는다.
        /// </summary>
        Rect WindowRect()
        {
            float width = Pad * 2f + Columns * CardWidth + (Columns - 1) * Gap + CartWidth + Gap;

            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - HeightFor(RowsMost())) * 0.5f,
                width,
                HeightFor(RowsNow()));
        }

        // ---- 장바구니 ----

        /// <summary>오른쪽 장바구니 칸의 너비.</summary>
        const float CartWidth = 236f;

        /// <summary>
        /// 담아 둔 수량. 첨자는 <b>상품 번호</b>다 — 탭을 넘겨도 담은 것이 그대로 남는다.
        ///
        /// 예전에는 +5 를 누르는 순간 대금이 빠져나갔다. 잘못 누르면 되돌릴 방법이 없었고,
        /// 여러 품목을 시킬 때마다 따로 결제되어 얼마를 쓰는지 합이 보이지 않았다.
        /// </summary>
        int[] cart = new int[0];

        /// <summary>담아 둔 가구. 첨자는 가구 카탈로그 번호다.</summary>
        int[] furnitureCart = new int[0];

        void EnsureCart(InventoryManager inv)
        {
            if (cart.Length != inv.Catalog.Count) cart = new int[inv.Catalog.Count];

            int count = furniture != null ? furniture.Count : 0;
            if (furnitureCart.Length != count) furnitureCart = new int[count];
        }

        int CartCount()
        {
            int sum = 0;
            for (int i = 0; i < cart.Length; i++) sum += cart[i];
            for (int i = 0; i < furnitureCart.Length; i++) sum += furnitureCart[i];
            return sum;
        }

        /// <summary>담긴 가구 수. 긴급 배송이 되는지 가르는 기준이다.</summary>
        int FurnitureCount()
        {
            int sum = 0;
            for (int i = 0; i < furnitureCart.Length; i++) sum += furnitureCart[i];
            return sum;
        }

        void ClearCart()
        {
            for (int i = 0; i < cart.Length; i++) cart[i] = 0;
            for (int i = 0; i < furnitureCart.Length; i++) furnitureCart[i] = 0;
        }

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (!open) return false;

            Rect window = WindowRect();
            return window.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y));
        }

        // ---- 화면 ----

        void OnGUI()
        {
            if (!open) return;

            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            EnsureCart(inv);

            Rect window = WindowRect();
            GUI.Box(window, GUIContent.none, UiSkin.Panel_);

            float x = window.x + Pad;
            float y = window.y + Pad;
            float inner = window.width - Pad * 2f;

            // 머리 — 제목과 보유 금액
            GUI.Label(new Rect(x, y + 2f, 60f, HeaderHeight), "상점", UiSkin.Title);
            GUI.Label(new Rect(x + 58f, y + 2f, inner * 0.6f, HeaderHeight), "오늘 주문하면 내일 아침에 온다", LeftCaption);
            GUI.Label(new Rect(x + inner * 0.6f, y + 2f, inner * 0.4f - 34f, HeaderHeight), money, RightCaption);
            if (GUI.Button(new Rect(window.xMax - Pad - 30f, y - 2f, 30f, 28f), "✕", UiSkin.Button(UiSkin.Coral))) open = false;
            y += HeaderHeight + 8f;

            // 칸 고르기
            float tabWidth = (inner - Gap * (ProductCategories.Tabs.Length - 1)) / ProductCategories.Tabs.Length;
            for (int i = 0; i < ProductCategories.Tabs.Length; i++)
            {
                Color tint = i == tab ? UiSkin.Sky : UiSkin.Cream;
                Rect rect = new Rect(x + i * (tabWidth + Gap), y, tabWidth, TabHeight);
                if (GUI.Button(rect, ProductCategories.NameOf(ProductCategories.Tabs[i]), UiSkin.Button(tint)) && i != tab)
                {
                    tab = i;
                    Rebuild();
                }
            }
            y += TabHeight + 12f;

            // 물건들
            if (IsFurnitureTab) DrawFurniture(x, y);
            else DrawProducts(inv, x, y);

            // 장바구니는 가구 칸에서도 그린다 — 담아 둔 채로 칸을 넘길 수 있어야 한다
            DrawCart(inv, window.xMax - Pad - CartWidth, y, window.yMax - Pad - FooterHeight - 8f);

            float footerY = window.yMax - Pad - FooterHeight;
            DrawBoxButton(x, footerY, inner);
            GUI.Label(new Rect(x, footerY, inner * 0.62f, FooterHeight), footer, UiSkin.Caption);
        }

        /// <summary>
        /// 오른쪽 장바구니. 담은 것과 합계를 보여 주고, 여기서만 돈이 빠진다.
        ///
        /// 버튼 셋을 세로로 쌓는다 — 주문(초록)·취소(빨강)가 한 쌍이고,
        /// 긴급은 그 아래 따로 둔다. 할증을 무는 것이라 같은 줄에 두면 잘못 누른다.
        /// </summary>
        void DrawCart(InventoryManager inv, float left, float top, float bottom)
        {
            const float RowHeight = 20f;
            const float ButtonHeight = 34f;

            Rect panel = new Rect(left, top, CartWidth, bottom - top);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            float x = panel.x + 10f;
            float w = panel.width - 20f;
            float y = panel.y + 10f;

            int items = CartCount();
            GUI.Label(new Rect(x, y, w, 22f), "장바구니" + (items > 0 ? "   " + items + "개" : ""), UiSkin.Title);
            y += 26f;

            if (items == 0)
            {
                GUI.Label(new Rect(x, y, w, 40f), "+1 / +5 / +10 으로 담는다.\n담은 뒤 한 번에 결제한다.", UiSkin.Caption);
                return;
            }

            ProductCatalog catalog = inv.Catalog;
            float listBottom = panel.yMax - ButtonHeight * 3f - 18f - 26f;

            for (int i = 0; i < cart.Length && y < listBottom; i++)
            {
                if (cart[i] <= 0) continue;

                ProductDef p = catalog.Get(i);
                GUI.Label(new Rect(x, y, w - 72f, RowHeight), p.nameKo + " x" + cart[i], UiSkin.Caption);
                GUI.Label(new Rect(x + w - 72f, y, 72f, RowHeight),
                          (p.wholesale * cart[i]).ToString("N0") + "원", RightCaption);
                y += RowHeight;
            }

            for (int i = 0; i < furnitureCart.Length && y < listBottom; i++)
            {
                if (furnitureCart[i] <= 0) continue;

                FurnitureDef f = furniture.Get(i);
                GUI.Label(new Rect(x, y, w - 72f, RowHeight), f.nameKo + " x" + furnitureCart[i], UiSkin.Caption);
                GUI.Label(new Rect(x + w - 72f, y, 72f, RowHeight),
                          (f.price * furnitureCart[i]).ToString("N0") + "원", RightCaption);
                y += RowHeight;
            }

            FurnitureShop shop = FurnitureShop.Instance;
            int furnitureCost = shop != null ? shop.CostOf(furnitureCart) : 0;

            // 가구는 할증 대상이 아니다 — 실물이 트럭에 실려 오는 것이라 한 시간 만에 못 온다
            int plain = inv.CostOf(cart, false) + furnitureCost;
            int express = inv.CostOf(cart, true) + furnitureCost;
            int wallet = GameManager.Instance.Money;

            y = panel.yMax - ButtonHeight * 3f - 18f - 22f;
            GUI.Label(new Rect(x, y, w, 22f), "합계 " + plain.ToString("N0") + "원", UiSkin.Title);
            y += 26f;

            bool canOrder = TimeManager.Instance == null || !TimeManager.Instance.IsDayOver;

            // 주문한다 — 내일 아침
            GUI.enabled = canOrder && wallet >= plain;
            if (GUI.Button(new Rect(x, y, w, ButtonHeight),
                           "주문한다   " + plain.ToString("N0") + "원", UiSkin.Button(UiSkin.Green)))
                Submit(inv, false);
            y += ButtonHeight + 5f;

            // 취소한다 — 담은 것만 비운다. 돈은 아직 안 나갔으므로 되돌릴 것이 없다
            GUI.enabled = true;
            if (GUI.Button(new Rect(x, y, w, ButtonHeight), "취소한다", UiSkin.Button(UiSkin.Coral)))
                ClearCart();
            y += ButtonHeight + 8f;

            // 긴급 주문 — 할증을 물고 한 시간 뒤. 하루 한 번뿐이고 가구는 안 된다
            bool usedUp = inv.ExpressUsedToday;
            bool hasFurniture = FurnitureCount() > 0;

            GUI.enabled = canOrder && !usedUp && !hasFurniture && wallet >= express;
            if (GUI.Button(new Rect(x, y, w, ButtonHeight),
                           usedUp ? "긴급주문 (오늘 완료)"
                           : hasFurniture ? "긴급주문 (가구는 불가)"
                           : "긴급주문   " + express.ToString("N0") + "원",
                           UiSkin.Button(UiSkin.Sky)))
                Submit(inv, true);
            GUI.enabled = true;

            GUI.Label(new Rect(x, y + ButtonHeight + 1f, w, 18f),
                      usedUp ? "내일 다시 쓸 수 있다"
                      : hasFurniture ? "가구를 빼면 긴급으로 보낼 수 있다"
                      : "1.5배 · " + InventoryManager.ExpressHours + "시간 뒤 · 하루 한 번",
                      UiSkin.Caption);
        }

        /// <summary>
        /// 담은 것을 결제한다. 상품과 가구가 지갑은 하나이므로 <b>합계를 먼저 본다</b> —
        /// 각자 결제하게 두면 상품은 샀는데 가구에서 돈이 떨어져 반만 주문된 채로 끝난다.
        /// </summary>
        void Submit(InventoryManager inv, bool expressed)
        {
            FurnitureShop shop = FurnitureShop.Instance;
            int furnitureCost = shop != null ? shop.CostOf(furnitureCart) : 0;
            int total = inv.CostOf(cart, expressed) + furnitureCost;

            if (GameManager.Instance.Money < total)
            {
                ActionRunner.Reject("재화 부족 — " + GameManager.Instance.Money + " / " + total);
                return;
            }

            string reason;
            if (CartCount() - FurnitureCount() > 0 && !inv.TryOrderCart(cart, expressed, out reason))
            {
                ActionRunner.Reject(reason);
                return;
            }

            if (furnitureCost > 0 && !shop.TryBuyCart(furnitureCart, out reason))
            {
                ActionRunner.Reject(reason);
                return;
            }

            ClearCart();
            Rebuild();
        }

        /// <summary>
        /// 랜덤 상자. 탭으로 만들지 않는다 — 상품 카테고리가 아니라 <b>도박</b>이고,
        /// ProductCategory 에 끼워 넣으면 수요가중치·진열 칸 계산까지 따라 들어간다.
        /// </summary>
        void DrawBoxButton(float left, float top, float inner)
        {
            RandomBox box = RandomBox.Instance;
            if (box == null || !box.IsUnlocked) return;

            string reason;
            bool can = box.CanOpen(out reason);

            var rect = new Rect(left + inner - 210f, top - 10f, 210f, FooterHeight + 20f);
            if (GUI.Button(rect, GUIContent.none, UiSkin.Button(can ? UiSkin.Coral : UiSkin.Cream)) && can)
                box.Open();

            // 상자 그림을 버튼 왼쪽에 얹는다. 글자만 있으면 발주 목록에 묻힌다
            if (boxIcon != null)
            {
                float side = rect.height - 6f;
                Color prev = GUI.color;
                if (!can) GUI.color = new Color(1f, 1f, 1f, 0.45f);
                GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + 3f, side, side), boxIcon, ScaleMode.ScaleToFit, true);
                GUI.color = prev;
            }

            var label = new GUIStyle(UiSkin.Caption) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            label.normal.textColor = UiSkin.Ink;
            GUI.Label(new Rect(rect.x + rect.height + 4f, rect.y, rect.width - rect.height - 10f, rect.height),
                "랜덤 상자  " + box.Price + "원", label);

            // 결과 창이 떠 있을 때는 사유를 적지 않는다 — 그 창이 이미 같은 말을 하고 있고,
            // 하단 안내와 글자가 겹친다
            if (!can && !box.HasResult && !string.IsNullOrEmpty(reason))
                GUI.Label(new Rect(rect.x - 156f, top, 152f, FooterHeight), reason, RightCaption);
        }

        void DrawProducts(InventoryManager inv, float left, float top)
        {
            ProductCatalog catalog = inv.Catalog;
            int wallet = GameManager.Instance.Money;

            // 마감한 뒤에는 ActionRunner 가 모든 행동을 막는다. 눌러 봐야 거절만 뜨므로 아예 끈다
            bool canOrder = TimeManager.Instance == null || !TimeManager.Instance.IsDayOver;

            for (int i = 0; i < visible.Count; i++)
            {
                int index = visible[i];
                ProductDef product = catalog.Get(index);
                bool unlocked = inv.IsUnlocked(index);

                Rect card = CardAt(left, top, i);
                GUI.Box(card, GUIContent.none, UiSkin.Panel_);

                DrawPhoto(card, product.icon, unlocked);
                GUI.Label(new Rect(card.x + 4f, card.y + NameY, card.width - 8f, 20f), product.nameKo, UiSkin.Caption);
                DrawTag(card, PriceY, "도매 " + product.wholesale + "원", UiSkin.Cream);
                GUI.Label(new Rect(card.x + 4f, card.y + RetailY, card.width - 8f, 18f),
                          "판매 " + product.retail + "원", UiSkin.Caption);

                if (!unlocked)
                {
                    DrawTag(card, ActionY, "Lv " + product.unlockLevel + " 에 열린다", UiSkin.Sky);
                    continue;
                }

                // 문 앞에 놓인 것을 안 보여 주면 이미 산 물건을 또 시킨다.
                // 칸이 좁으므로 지금 가서 들일 수 있는 쪽(앞)을 내일 올 것(+N)보다 앞세운다
                string stock = "창고 " + inv.StorageOf(index);
                if (inv.DeliveredOf(index) > 0) stock += "  앞 " + inv.DeliveredOf(index);
                else if (inv.IncomingOf(index) > 0) stock += "  +" + inv.IncomingOf(index);

                // 담아 둔 것은 창고 줄 끝에 붙인다. 카드마다 몇 개 담았는지가 안 보이면
                // 오른쪽 장바구니와 눈을 왕복해야 한다
                if (cart[index] > 0) stock += "   담음 " + cart[index];
                GUI.Label(new Rect(card.x + 4f, card.y + StockY, card.width - 8f, 18f), stock, UiSkin.Caption);

                float buttonWidth = (card.width - 16f - 8f) / Quantities.Length;
                for (int q = 0; q < Quantities.Length; q++)
                {
                    GUI.enabled = canOrder;
                    Rect rect = new Rect(card.x + 8f + q * (buttonWidth + 4f), card.y + ActionY, buttonWidth, 26f);

                    // 담기만 한다. 돈은 <주문한다>를 누를 때 한 번에 빠진다
                    if (GUI.Button(rect, "+" + Quantities[q], UiSkin.Button(UiSkin.Green)))
                        cart[index] += Quantities[q];

                    GUI.enabled = true;
                }

                // 담은 카드에는 그 품목만 비우는 길을 준다. 장바구니를 통째로 비우지 않고
                // 하나만 무르고 싶은 쪽이 훨씬 흔하다
                if (cart[index] > 0
                    && GUI.Button(new Rect(card.x + card.width - 26f, card.y + 4f, 22f, 22f),
                                  "✕", UiSkin.Button(UiSkin.Coral)))
                    cart[index] = 0;
            }
        }

        void DrawFurniture(float left, float top)
        {
            if (furniture == null || furniture.Count == 0)
            {
                GUI.Label(new Rect(left, top + 40f, Columns * CardWidth + (Columns - 1) * Gap, 22f),
                          "가구는 아직 들여놓을 수 없다", UiSkin.Caption);
                return;
            }

            for (int i = 0; i < furnitureOrder.Count; i++)
            {
                int index = furnitureOrder[i];
                FurnitureDef item = furniture.Get(index);

                FurnitureShop shop = FurnitureShop.Instance;
                string reason;
                bool canBuy = shop != null && shop.CanBuy(index, out reason);
                bool unlocked = shop == null || shop.IsUnlocked(index);

                Rect card = CardAt(left, top, i);
                GUI.Box(card, GUIContent.none, UiSkin.Panel_);

                DrawPhoto(card, item.icon, unlocked);
                GUI.Label(new Rect(card.x + 4f, card.y + NameY, card.width - 8f, 20f), item.nameKo, UiSkin.Caption);
                DrawTag(card, PriceY, item.price.ToString("N0") + "원", UiSkin.Cream);
                GUI.Label(new Rect(card.x + 4f, card.y + RetailY, card.width - 8f, 18f), item.note, UiSkin.Caption);

                if (!unlocked)
                {
                    DrawTag(card, ActionY, "Lv " + item.unlockLevel + " 에 열린다", UiSkin.Sky);
                    continue;
                }

                // 담긴 수를 적는다. 상품 카드의 "담음 N" 과 같은 자리다
                if (furnitureCart[index] > 0)
                    GUI.Label(new Rect(card.x + 4f, card.y + StockY, card.width - 8f, 18f),
                              "담음 " + furnitureCart[index], UiSkin.Caption);

                // 몇 개든 살 수 있다. 자리와 돈이 유일한 한계다.
                // 돈은 장바구니에서 한 번에 빠지므로 여기서는 잔고를 보지 않는다
                GUI.enabled = canBuy || unlocked;
                if (GUI.Button(new Rect(card.x + 8f, card.y + ActionY, card.width - 16f, 26f),
                               "담기", UiSkin.Button(UiSkin.Green)))
                    furnitureCart[index]++;
                GUI.enabled = true;

                if (furnitureCart[index] > 0
                    && GUI.Button(new Rect(card.x + card.width - 26f, card.y + 4f, 22f, 22f),
                                  "✕", UiSkin.Button(UiSkin.Coral)))
                    furnitureCart[index] = 0;
            }
        }

        Rect CardAt(float left, float top, int slot) =>
            new Rect(left + (slot % Columns) * (CardWidth + Gap),
                     top + (slot / Columns) * (CardHeight + Gap),
                     CardWidth, CardHeight);

        /// <summary>사진은 칸 폭에 맞춰 비율을 지킨다. 늘려 채우면 병이 납작해진다.</summary>
        static void DrawPhoto(Rect card, Texture2D icon, bool lit)
        {
            Rect frame = new Rect(card.x + 8f, card.y + 8f, card.width - 16f, PhotoHeight);
            if (icon == null)
            {
                GUI.Label(frame, "사진 없음", UiSkin.Caption);
                return;
            }

            Color before = GUI.color;
            if (!lit) GUI.color = new Color(1f, 1f, 1f, 0.45f);   // 못 사는 물건은 흐리게
            GUI.DrawTexture(frame, icon, ScaleMode.ScaleToFit, true);
            GUI.color = before;
        }

        static void DrawTag(Rect card, float offsetY, string text, Color tint)
        {
            const float width = 108f;
            GUI.Label(new Rect(card.x + (card.width - width) * 0.5f, card.y + offsetY, width, 24f), text, UiSkin.Tag(tint));
        }

        static GUIStyle rightCaption;
        static GUIStyle leftCaption;

        static GUIStyle RightCaption
        {
            get
            {
                if (rightCaption != null && rightCaption.font == UiSkin.Font) return rightCaption;
                rightCaption = new GUIStyle(UiSkin.Caption) { alignment = TextAnchor.MiddleRight };
                return rightCaption;
            }
        }

        static GUIStyle LeftCaption
        {
            get
            {
                if (leftCaption != null && leftCaption.font == UiSkin.Font) return leftCaption;
                leftCaption = new GUIStyle(UiSkin.Caption) { alignment = TextAnchor.MiddleLeft };
                leftCaption.normal.textColor = new Color(0.66f, 0.70f, 0.74f);
                return leftCaption;
            }
        }
    }
}
