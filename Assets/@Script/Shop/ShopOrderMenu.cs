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

        // 창 안 좌표는 1920x1080 기준 설계값이다. 화면이 작으면 GUI.matrix 로 통째로 줄인다
        const float Gap = 10f;
        const int Columns = 4;
        const float CardWidth = 132f;
        const float CardHeight = 240f;

        // 카드 안에서 줄이 시작하는 높이. 잠긴 상품은 창고·버튼 자리에 해금 레벨이 대신 들어간다.
        // 카드 그림 아래 띠(발바닥)가 21 이라 버튼은 214 에서 끝난다
        const float PhotoY = 12f;
        const float PhotoHeight = 86f;
        const float NameY = 100f;
        const float PriceY = 122f;
        const float RetailY = 148f;
        const float StockY = 166f;
        const float ActionY = 188f;
        const float HeaderHeight = 56f;   // 안내·보유 금액 줄. 오른쪽은 장바구니 메모장 윗부분(집게)이 차지한다
        const float TabHeight = 40f;
        const float FooterHeight = 30f;

        // 차양 틀 그림(1200x896)의 9-slice 경계(텍셀)와 배율.
        // 위 215 는 나무 테두리+차양 끝까지, 아래 125 는 아래 모서리 발바닥 혹까지다
        const float FrameScale = 0.68f;
        const float FrameLeft = 130f, FrameTop = 215f, FrameRight = 130f, FrameBottom = 125f;

        /// <summary>틀 안쪽 크림색 판이 시작하는 여백. 위는 차양 아래, 아래는 모서리 혹 위다.</summary>
        const float SideInset = 54f;
        const float TopInset = 150f;
        const float BottomInset = 90f;

        // 장바구니 메모장(766x1358): 위는 집게, 아래는 발바닥 도장까지 늘리지 않는다
        const float CartLeft = 50f, CartTop = 150f, CartRight = 50f, CartBottom = 230f;

        // 상품 카드(816x1088): 아래 띠에 발바닥이 있다
        const float CardBorderSide = 48f, CardBorderTop = 50f, CardBorderBottom = 130f;

        /// <summary>탭 그림(536x240) 양 끝 둥근 부분.</summary>
        const float TabEnd = 120f;

        [Header("그림 (바르코)")]
        [SerializeField] Texture2D frameTex;
        [SerializeField] Texture2D cartTex;
        [SerializeField] Texture2D cardTex;
        [SerializeField] Texture2D tabTex;
        [SerializeField] Texture2D tabOnTex;
        [Tooltip("오른쪽 위 귀퉁이 닫기 발바닥(T_UI_ShopClosePaw). 틀 그림에서 지운 그 발바닥이다")]
        [SerializeField] Texture2D closePawTex;

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
            // ESC 로도 닫는다(같은 ESC 로 설정 창이 열리지 않게 표시)
            Keyboard keyboard = Keyboard.current;
            if (open && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                open = false;
                PointerMenus.TakeEscape();
            }

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

                // 해금 레벨 순 — 레벨이 오를 때마다 새 물건이 맨 끝(다음 칸)에 붙는다. 같은 레벨이면 싼 것부터
                visible.Sort((a, b) =>
                {
                    int byLevel = catalog.Get(a).unlockLevel.CompareTo(catalog.Get(b).unlockLevel);
                    if (byLevel != 0) return byLevel;
                    int byPrice = catalog.Get(a).wholesale.CompareTo(catalog.Get(b).wholesale);
                    return byPrice != 0 ? byPrice : a.CompareTo(b);
                });
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

        /// <summary>가장 물건이 많은 탭의 줄 수. 창 크기를 이것으로 붙박는다.</summary>
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
            TopInset + HeaderHeight + 6f + TabHeight + 12f
            + rows * (CardHeight + Gap) - Gap + 12f + FooterHeight + BottomInset;

        const float ProductsWidth = Columns * CardWidth + (Columns - 1) * Gap;

        /// <summary>창이 화면 높이(설계 1080)에서 위아래로 이만큼은 비우고 들어가야 한다.</summary>
        const float MaxWindowHeight = 1020f;

        /// <summary>
        /// 설계 좌표 한 칸이 화면 몇 픽셀인지. 1080p 에서 1 — 단 창이 화면보다 길면 그만큼 통째로 줄인다.
        /// 가구 탭이 9개(창고 선반 추가)가 되며 카드가 세 줄로 늘어, 창이 화면 위아래로 잘려 나갔다.
        /// </summary>
        float UiScale
        {
            get
            {
                // UiSkin.Boost 배로 키우되 창 전체(가장 큰 탭 기준)가 화면 안(높이는 1020/1080 = 94%)에 들어가게
                float width = SideInset * 2f + ProductsWidth + Gap + CartWidth;
                return UiSkin.ScaleFor(width, HeightFor(RowsMost()), MaxWindowHeight / 1080f);
            }
        }

        /// <summary>
        /// 창은 <b>가장 큰 탭 기준으로 크기를 붙박는다</b>(설계 좌표).
        ///
        /// 예전엔 높이를 지금 탭에 맞춰 줄였지만, 차양 틀 그림은 높이가 바뀌면 나무 테두리가
        /// 늘었다 줄었다 해서 탭을 넘길 때마다 창이 꿈틀거린다. 지금 탭은 모두 두 줄 안에 든다.
        /// </summary>
        Rect WindowRect()
        {
            float s = UiScale;
            float width = SideInset * 2f + ProductsWidth + Gap + CartWidth;
            float height = HeightFor(RowsMost());

            return new Rect((Screen.width / s - width) * 0.5f, (Screen.height / s - height) * 0.5f, width, height);
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

            float s = UiScale;
            Rect window = WindowRect();
            return window.Contains(new Vector2(screenPos.x / s, (Screen.height - screenPos.y) / s));
        }

        // ---- 화면 ----

        void OnGUI()
        {
            if (!open) return;

            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            EnsureCart(inv);

            Styles.Ensure();

            Matrix4x4 keep = GUI.matrix;
            float s = UiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            Rect window = WindowRect();
            if (frameTex != null)
                UiSkin.Draw9(window, frameTex, FrameLeft, FrameTop, FrameRight, FrameBottom, FrameScale);
            else
                GUI.Box(window, GUIContent.none, UiSkin.Panel_);

            float x = window.x + SideInset;
            float y = window.y + TopInset;

            // 간판 — 틀 윗변 나무 위에 얹는다. 차양 아래 크림 판은 상품 자리로 아낀다
            GUI.Label(new Rect(window.x, window.y + 1f, window.width, 34f), "상점", Styles.sign);

            // 머리 — 안내와 보유 금액은 상품 쪽. 닫기는 틀 오른쪽 위 빨간 발바닥 하나뿐이다
            // (<상점 닫기> 버튼도 있었는데 닫기가 두 개라 뺐고, 그 자리는 장바구니가 쓴다)
            float headerTop = y;
            GUI.Label(new Rect(x, y, ProductsWidth * 0.36f, HeaderHeight), "오늘 주문하면 내일 아침에 온다", Styles.soft);
            DrawLevelUpButton(new Rect(x + ProductsWidth * 0.37f, y + 7f, ProductsWidth * 0.34f, HeaderHeight - 14f));
            GUI.Label(new Rect(x + ProductsWidth * 0.72f, y, ProductsWidth * 0.28f, HeaderHeight), money, Styles.money);
            y += HeaderHeight + 6f;

            // 장바구니는 가구 칸에서도 그린다 — 담아 둔 채로 칸을 넘길 수 있어야 한다.
            // 머리 줄부터 하단 안내 줄 끝까지 오른쪽 한 칸을 통째로 쓴다
            DrawCart(inv, x + ProductsWidth + Gap, headerTop, window.yMax - BottomInset);

            // 칸 고르기 — 상품 쪽 너비만
            float tabWidth = (ProductsWidth - Gap * (ProductCategories.Tabs.Length - 1)) / ProductCategories.Tabs.Length;
            for (int i = 0; i < ProductCategories.Tabs.Length; i++)
            {
                Rect rect = new Rect(x + i * (tabWidth + Gap), y, tabWidth, TabHeight);
                if (DrawTab(rect, ProductCategories.NameOf(ProductCategories.Tabs[i]), i == tab) && i != tab)
                {
                    tab = i;
                    Rebuild();
                }
            }
            y += TabHeight + 12f;

            // 물건들
            if (IsFurnitureTab) DrawFurniture(x, y);
            else DrawProducts(inv, x, y);

            float footerY = window.yMax - BottomInset - FooterHeight;
            DrawBoxButton(x, footerY, ProductsWidth);
            GUI.Label(new Rect(x + 4f, footerY, ProductsWidth * 0.62f, FooterHeight), footer, Styles.footer);


            // 오른쪽 위 귀퉁이 발바닥도 닫기다(텍셀 1103,27 67x61). 틀 그림에서는 그 발바닥을 지워 두었고,
            // 9-slice 오른쪽 위 조각은 FrameScale 로 오른쪽·위 변에 붙어 그려진다
            if (frameTex != null)
            {
                var spot = new Rect(window.xMax - (1200f - 1103f) * FrameScale, window.y + 27f * FrameScale,
                                    67f * FrameScale, 61f * FrameScale);
                if (UiSkin.PawClose(spot, closePawTex)) open = false;
            }
            GUI.matrix = keep;
        }

        /// <summary>
        /// 가게 레벨업 — <b>사야 오른다</b>. 명성이 문턱을 넘어도 저절로 오르지 않고, 여기서 비용을 내고 눌러야 한다.
        /// 조건이 안 되면 무엇이 모자란지 버튼에 적고 눌리지 않는다. 오르면 HUD 가 해금 내용을 알린다.
        /// </summary>
        void DrawLevelUpButton(Rect rect)
        {
            ShopLevelManager levels = ShopLevelManager.Instance;
            if (levels == null) return;

            if (levels.IsMaxLevel)
            {
                GUI.enabled = false;
                GUI.Button(rect, "가게 최대 레벨", UiSkin.Button(UiSkin.Cream));
                GUI.enabled = true;
                return;
            }

            ShopLevelDef next = levels.Next;
            string reason;
            bool can = levels.CanLevelUp(out reason);
            int rep = GameManager.Instance.Reputation;
            string label = can
                ? "Lv" + (levels.Level + 1) + " 레벨업  " + next.upgradeCost.ToString("N0") + "원"
                : rep < next.requiredReputation
                    ? "Lv" + (levels.Level + 1) + " 레벨업  명성 " + rep + "/" + next.requiredReputation
                    : "Lv" + (levels.Level + 1) + " 레벨업  " + next.upgradeCost.ToString("N0") + "원 (부족)";
            if (!can && rep >= next.requiredReputation && next.heroStatGate > 0 && levels.HeroGrowthStat < next.heroStatGate)
                label = "Lv" + (levels.Level + 1) + " 레벨업  " + Dogs.MascotGrade.NameForThreshold(next.heroStatGate) + " 필요";

            // 조건이 안 돼도 글자는 또렷하게 — GUI.enabled 로 끄면 무엇이 모자란지가 흐려서 안 읽혔다.
            // 대신 눌러도 오르지 않고 사유만 알린다(ActionRunner 가 거절 사유를 띄운다)
            var style = new GUIStyle(UiSkin.Button(can ? UiSkin.Green : UiSkin.Cream)) { fontSize = 15 };
            if (UiSkin.Click(rect, label, style))
            {
                ActionRunner.TryRun(new ShopLevelManager.UpgradeAction());
                Rebuild();   // 새로 풀린 상품·가구가 바로 보이게
            }
        }

        /// <summary>탭 하나. 고른 탭은 꿀색, 나머지는 크림색 알약. 그림이 없으면 예전 색 버튼.</summary>
        bool DrawTab(Rect rect, string label, bool on)
        {
            Texture2D tex = on ? tabOnTex : tabTex;
            if (tex == null)
                return UiSkin.Click(rect, label, UiSkin.Button(on ? UiSkin.Sky : UiSkin.Cream));

            bool hover = !on && rect.Contains(Event.current.mousePosition);
            GUI.color = hover ? new Color(1f, 0.97f, 0.88f) : Color.white;
            UiSkin.Draw9(rect, tex, TabEnd, 0f, TabEnd, 0f, rect.height / tex.height);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), label, on ? Styles.tabOn : Styles.tab);
            return UiSkin.Click(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>
        /// 오른쪽 장바구니. 담은 것과 합계를 보여 주고, 여기서만 돈이 빠진다.
        ///
        /// 버튼 셋을 세로로 쌓는다 — 주문(초록)·취소(빨강)가 한 쌍이고,
        /// 긴급은 그 아래 따로 둔다. 할증을 무는 것이라 같은 줄에 두면 잘못 누른다.
        /// </summary>
        void DrawCart(InventoryManager inv, float left, float top, float bottom)
        {
            const float RowHeight = 21f;
            const float ButtonHeight = 34f;
            // 합계 줄(32) + 주문(34+5) + 취소(34+8) + 긴급(34) + 한 줄 설명(1+18)
            const float CartFooter = 32f + ButtonHeight * 3f + 13f + 19f;

            // 메모장 아래 발바닥 도장(아래에서 20~62)은 가리지 않는다 — 비어 있을 때 메모장다워 보이는 자리다
            float stamp = cartTex != null ? 66f : 10f;

            Rect panel = new Rect(left, top, CartWidth, bottom - top);
            if (cartTex != null)
                UiSkin.Draw9(panel, cartTex, CartLeft, CartTop, CartRight, CartBottom, CartWidth / cartTex.width);
            else
                GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            float x = panel.x + 22f;
            float w = panel.width - 44f;
            float y = panel.y + (cartTex != null ? 46f : 10f);   // 집게 아래부터

            int items = CartCount();
            GUI.Label(new Rect(x, y, w, 24f), "장바구니" + (items > 0 ? "   " + items + "개" : ""), Styles.heading);
            y += 26f;
            Line(new Rect(x, y, w, 2f));
            y += 8f;

            if (items == 0)
            {
                GUI.Label(new Rect(x, y, w, 44f), "+1 / +5 / +10 으로 담는다.\n담은 뒤 한 번에 결제한다.", Styles.softWrap);
                return;
            }

            ProductCatalog catalog = inv.Catalog;
            float listBottom = panel.yMax - stamp - CartFooter - RowHeight;

            for (int i = 0; i < cart.Length && y < listBottom; i++)
            {
                if (cart[i] <= 0) continue;

                // 이름 옆 X — 돈이 모자라게 담았을 때 그 품목만 뺀다(상품 카드까지 찾아가지 않아도 되게)
                ProductDef p = catalog.Get(i);
                if (UiSkin.Click(new Rect(x, y + 1f, RowHeight - 2f, RowHeight - 2f), "X", Styles.Remove))
                {
                    cart[i] = 0;
                    continue;
                }
                GUI.Label(new Rect(x + RowHeight + 2f, y, w - 74f - RowHeight, RowHeight), p.nameKo + " x" + cart[i], Styles.item);
                GUI.Label(new Rect(x + w - 72f, y, 72f, RowHeight),
                          (p.wholesale * cart[i]).ToString("N0") + "원", Styles.itemRight);
                y += RowHeight;
            }

            for (int i = 0; i < furnitureCart.Length && y < listBottom; i++)
            {
                if (furnitureCart[i] <= 0) continue;

                FurnitureDef f = furniture.Get(i);
                if (UiSkin.Click(new Rect(x, y + 1f, RowHeight - 2f, RowHeight - 2f), "X", Styles.Remove))
                {
                    furnitureCart[i] = 0;
                    continue;
                }
                GUI.Label(new Rect(x + RowHeight + 2f, y, w - 74f - RowHeight, RowHeight), f.nameKo + " x" + furnitureCart[i], Styles.item);
                GUI.Label(new Rect(x + w - 72f, y, 72f, RowHeight),
                          (f.price * furnitureCart[i]).ToString("N0") + "원", Styles.itemRight);
                y += RowHeight;
            }

            FurnitureShop shop = FurnitureShop.Instance;
            int furnitureCost = shop != null ? shop.CostOf(furnitureCart) : 0;

            // 가구는 할증 대상이 아니다 — 실물이 트럭에 실려 오는 것이라 한 시간 만에 못 온다
            int plain = inv.CostOf(cart, false) + furnitureCost;
            int express = inv.CostOf(cart, true) + furnitureCost;
            int wallet = GameManager.Instance.Money;

            y = panel.yMax - stamp - CartFooter;
            Line(new Rect(x, y, w, 2f));
            GUI.Label(new Rect(x, y + 4f, w, 24f), "합계 " + plain.ToString("N0") + "원", Styles.heading);
            y += 32f;

            bool canOrder = TimeManager.Instance == null || !TimeManager.Instance.IsDayOver;

            // 주문한다 — 내일 아침
            GUI.enabled = canOrder && wallet >= plain;
            if (UiSkin.Click(new Rect(x, y, w, ButtonHeight),
                           "주문한다   " + plain.ToString("N0") + "원", UiSkin.Button(UiSkin.Green)))
                Submit(inv, false);
            y += ButtonHeight + 5f;

            // 취소한다 — 담은 것만 비운다. 돈은 아직 안 나갔으므로 되돌릴 것이 없다
            GUI.enabled = true;
            if (UiSkin.Click(new Rect(x, y, w, ButtonHeight), "취소한다", UiSkin.Button(UiSkin.Coral)))
                ClearCart();
            y += ButtonHeight + 8f;

            // 긴급 주문 — 할증을 물고 한 시간 뒤. 하루 한 번뿐이고 가구는 안 된다
            bool usedUp = inv.ExpressUsedToday;
            bool hasFurniture = FurnitureCount() > 0;
            bool inHours = InventoryManager.ExpressOpenNow;   // 10:00~17:00 만

            GUI.enabled = canOrder && inHours && !usedUp && !hasFurniture && wallet >= express;
            if (UiSkin.Click(new Rect(x, y, w, ButtonHeight),
                           usedUp ? "긴급주문 (오늘 완료)"
                           : !inHours ? "긴급주문 (10:00 ~ 17:00)"
                           : hasFurniture ? "긴급주문 (가구는 불가)"
                           : "긴급주문   " + express.ToString("N0") + "원",
                           UiSkin.Button(UiSkin.Sky)))
                Submit(inv, true);
            GUI.enabled = true;

            GUI.Label(new Rect(x, y + ButtonHeight + 1f, w, 18f),
                      usedUp ? "내일 다시 쓸 수 있다"
                      : !inHours ? "긴급주문은 10:00 ~ 17:00 에만 받는다"
                      : hasFurniture ? "가구를 빼면 긴급으로 보낼 수 있다"
                      : "1.5배 / " + InventoryManager.ExpressHours + "시간 뒤 / 하루 한 번",
                      Styles.softCenter);
        }

        /// <summary>메모장 위 구분선. 연한 갈색 한 줄.</summary>
        static void Line(Rect r)
        {
            GUI.color = new Color(0.55f, 0.38f, 0.22f, 0.25f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
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
                ActionRunner.Reject("재화 부족: " + GameManager.Instance.Money + " / " + total);
                return;
            }

            string reason;
            bool hasProducts = CartCount() - FurnitureCount() > 0;
            if (hasProducts)
            {
                if (!inv.TryOrderCart(cart, expressed, out reason))
                {
                    ActionRunner.Reject(reason);
                    return;
                }
                // 상품은 결제가 끝났다 — 뒤의 가구 결제가 실패해도 다시 누를 때 상품이 두 번 주문되지 않게 상품 칸만 먼저 비운다
                for (int i = 0; i < cart.Length; i++) cart[i] = 0;
                GameEvents.RaiseStockOrdered();
            }

            if (furnitureCost > 0 && !shop.TryBuyCart(furnitureCart, out reason))
            {
                ActionRunner.Reject(reason);
                Rebuild();
                return;
            }

            ClearCart();
            Sound.Play(Sfx.ShopBuy);
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

            var rect = new Rect(left + inner - 210f, top - 5f, 210f, FooterHeight + 10f);
            if (UiSkin.Click(rect, GUIContent.none, UiSkin.Button(can ? UiSkin.Coral : UiSkin.Cream)) && can)
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
                GUI.Label(new Rect(rect.x - 156f, top, 152f, FooterHeight), reason, Styles.warnRight);
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
                DrawCard(card);

                DrawPhoto(card, product.icon, unlocked);
                GUI.Label(new Rect(card.x + 4f, card.y + NameY, card.width - 8f, 20f), product.nameKo, Styles.name);
                DrawTag(card, PriceY, "도매 " + product.wholesale + "원", UiSkin.Cream);
                GUI.Label(new Rect(card.x + 4f, card.y + RetailY, card.width - 8f, 18f),
                          "판매 " + product.retail + "원", Styles.small);

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
                GUI.Label(new Rect(card.x + 4f, card.y + StockY, card.width - 8f, 18f), stock, Styles.small);

                float buttonWidth = (card.width - 20f - 6f) / Quantities.Length;
                for (int q = 0; q < Quantities.Length; q++)
                {
                    GUI.enabled = canOrder;
                    Rect rect = new Rect(card.x + 10f + q * (buttonWidth + 3f), card.y + ActionY, buttonWidth, 26f);

                    // 담기만 한다. 돈은 <주문한다>를 누를 때 한 번에 빠진다
                    if (UiSkin.Click(rect, "+" + Quantities[q], UiSkin.Button(UiSkin.Green)))
                        cart[index] += Quantities[q];

                    GUI.enabled = true;
                }

                // 담은 카드에는 그 품목만 비우는 길을 준다. 장바구니를 통째로 비우지 않고
                // 하나만 무르고 싶은 쪽이 훨씬 흔하다
                if (cart[index] > 0
                    && UiSkin.Click(new Rect(card.xMax - 38f, card.y + 10f, 28f, 28f),
                                  "X", Styles.Remove))
                    cart[index] = 0;

                // 내일 추천 상품 — 시킨 물건은 내일 아침 오니 내일 것을 표시한다(오른쪽 위, 담은 X 가 있으면 그 옆)
                if (index == inv.RecommendedTomorrow && InventoryManager.ShopOpensTomorrow)
                {
                    float bx = cart[index] > 0 ? card.xMax - 38f - 50f : card.xMax - 56f;
                    GUI.Label(new Rect(bx, card.y + 10f, 46f, 24f), "추천", UiSkin.Tag(UiSkin.Coral));
                }
            }
        }

        void DrawFurniture(float left, float top)
        {
            if (furniture == null || furniture.Count == 0)
            {
                GUI.Label(new Rect(left, top + 40f, Columns * CardWidth + (Columns - 1) * Gap, 22f),
                          "가구는 아직 들여놓을 수 없다", Styles.small);
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
                DrawCard(card);

                DrawPhoto(card, item.icon, unlocked);
                GUI.Label(new Rect(card.x + 4f, card.y + NameY, card.width - 8f, 20f), item.nameKo, Styles.name);
                DrawTag(card, PriceY, item.price.ToString("N0") + "원", UiSkin.Cream);
                // 장식 가구는 쓰임새 대신 효과를 적는다 — 고르는 기준이 그것뿐이다
                GUI.Label(new Rect(card.x + 4f, card.y + RetailY, card.width - 8f, 18f), item.IsDecor ? item.DecorText : item.note, Styles.small);

                if (!unlocked)
                {
                    DrawTag(card, ActionY, "Lv " + item.unlockLevel + " 에 열린다", UiSkin.Sky);
                    continue;
                }

                // 담긴 수를 적는다. 상품 카드의 "담음 N" 과 같은 자리다
                if (furnitureCart[index] > 0)
                    GUI.Label(new Rect(card.x + 4f, card.y + StockY, card.width - 8f, 18f),
                              "담음 " + furnitureCart[index], Styles.small);

                // 명성 장식은 종류마다 하나만 — 이미 가졌으면 담을 수 없고, 담기도 한 번만
                bool oneOnly = shop != null && shop.OneOnly(index);
                if (oneOnly && shop.OwnedCount(index) > 0)
                {
                    DrawTag(card, ActionY, "보유 중", UiSkin.Cream);
                    continue;
                }

                // 그 밖의 가구는 몇 개든 살 수 있다. 자리와 돈이 유일한 한계다.
                // 돈은 장바구니에서 한 번에 빠지므로 여기서는 잔고를 보지 않는다
                bool full = oneOnly && furnitureCart[index] >= 1;
                GUI.enabled = (canBuy || unlocked) && !full;
                if (UiSkin.Click(new Rect(card.x + 10f, card.y + ActionY, card.width - 20f, 26f),
                               full ? "1개만" : "담기", UiSkin.Button(UiSkin.Green)))
                    furnitureCart[index]++;
                GUI.enabled = true;

                if (furnitureCart[index] > 0
                    && UiSkin.Click(new Rect(card.xMax - 38f, card.y + 10f, 28f, 28f),
                                  "X", Styles.Remove))
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
            Rect frame = new Rect(card.x + 12f, card.y + PhotoY, card.width - 24f, PhotoHeight);
            if (icon == null)
            {
                GUI.Label(frame, "사진 없음", Styles.small);
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

        /// <summary>상품 카드 한 장. 강아지 창 훈련 카드와 같은 나무 테 그림이다.</summary>
        void DrawCard(Rect card)
        {
            if (cardTex == null) { GUI.Box(card, GUIContent.none, UiSkin.Panel_); return; }
            UiSkin.Draw9(card, cardTex, CardBorderSide, CardBorderTop, CardBorderSide, CardBorderBottom,
                         card.width / cardTex.width);
        }

        /// <summary>
        /// 크림색 종이 위 글씨. 예전 판이 검은 반투명이라 글씨가 밝은 회색이었는데,
        /// 바탕이 밝아지면서 전부 진한 갈색으로 바꿨다(강아지 창과 같은 잉크색).
        /// </summary>
        static class Styles
        {
            static readonly Color Ink = new Color(0.33f, 0.2f, 0.1f);
            static readonly Color Soft = new Color(0.47f, 0.36f, 0.26f);
            static readonly Color Warn = new Color(0.8f, 0.3f, 0.25f);

            public static GUIStyle sign, soft, softWrap, softCenter, money, heading, item, itemRight,
                                   name, small, footer, warnRight, tab, tabOn;

            static Font built;
            static GUIStyle remove;

            /// <summary>카드 귀퉁이 X. 기본 버튼은 안쪽 여백(14)이 커서 작은 칸에 X 가 안 들어간다. 판 모서리(14x2)보다 작으면 찌그러지므로 28 칸에 쓴다.</summary>
            public static GUIStyle Remove
            {
                get
                {
                    GUIStyle baseStyle = UiSkin.Button(UiSkin.Coral);
                    if (remove != null && remove.normal.background == baseStyle.normal.background) return remove;
                    remove = new GUIStyle(baseStyle) { padding = new RectOffset(0, 0, 0, 1), fontSize = 13 };
                    return remove;
                }
            }

            public static void Ensure()
            {
                if (sign != null && built == UiSkin.Font) return;
                built = UiSkin.Font;

                sign = Make(22, new Color(1f, 0.96f, 0.86f), TextAnchor.MiddleCenter, true);
                soft = Make(15, Soft, TextAnchor.MiddleLeft, false);
                softWrap = Make(14, Soft, TextAnchor.UpperLeft, false);
                softWrap.wordWrap = true;
                softCenter = Make(13, Soft, TextAnchor.MiddleCenter, false);
                money = Make(18, Ink, TextAnchor.MiddleRight, true);
                heading = Make(17, Ink, TextAnchor.MiddleLeft, true);
                item = Make(14, Ink, TextAnchor.MiddleLeft, false);
                itemRight = Make(14, Ink, TextAnchor.MiddleRight, false);
                name = Make(15, Ink, TextAnchor.MiddleCenter, true);
                small = Make(13, Soft, TextAnchor.MiddleCenter, false);
                footer = Make(14, Soft, TextAnchor.MiddleLeft, false);
                warnRight = Make(13, Warn, TextAnchor.MiddleRight, false);
                tab = Make(16, Soft, TextAnchor.MiddleCenter, true);
                tabOn = Make(16, Ink, TextAnchor.MiddleCenter, true);
            }

            static GUIStyle Make(int size, Color color, TextAnchor anchor, bool bold)
            {
                var s = new GUIStyle
                {
                    font = UiSkin.Font, fontSize = size, alignment = anchor, wordWrap = false,
                    fontStyle = bold ? FontStyle.Bold : FontStyle.Normal
                };
                s.normal.textColor = color;
                return s;
            }
        }
    }
}
