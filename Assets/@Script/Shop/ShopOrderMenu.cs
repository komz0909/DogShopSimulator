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
            if (outside > 0)
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
            float width = Pad * 2f + Columns * CardWidth + (Columns - 1) * Gap;

            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - HeightFor(RowsMost())) * 0.5f,
                width,
                HeightFor(RowsNow()));
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

            float footerY = window.yMax - Pad - FooterHeight;
            GUI.Label(new Rect(x, footerY, inner, FooterHeight), footer, UiSkin.Caption);
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
                GUI.Label(new Rect(card.x + 4f, card.y + StockY, card.width - 8f, 18f), stock, UiSkin.Caption);

                float buttonWidth = (card.width - 16f - 8f) / Quantities.Length;
                for (int q = 0; q < Quantities.Length; q++)
                {
                    GUI.enabled = canOrder && wallet >= product.wholesale * Quantities[q];
                    Rect rect = new Rect(card.x + 8f + q * (buttonWidth + 4f), card.y + ActionY, buttonWidth, 26f);
                    if (GUI.Button(rect, "+" + Quantities[q], UiSkin.Button(UiSkin.Green)))
                    {
                        ActionRunner.TryRun(new InventoryManager.OrderAction(index, Quantities[q]));
                        Rebuild();
                    }
                    GUI.enabled = true;
                }
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
                FurnitureDef item = furniture.Get(furnitureOrder[i]);

                Rect card = CardAt(left, top, i);
                GUI.Box(card, GUIContent.none, UiSkin.Panel_);

                DrawPhoto(card, item.icon, true);
                GUI.Label(new Rect(card.x + 4f, card.y + NameY, card.width - 8f, 20f), item.nameKo, UiSkin.Caption);
                DrawTag(card, PriceY, item.price.ToString("N0") + "원", UiSkin.Cream);
                GUI.Label(new Rect(card.x + 4f, card.y + RetailY, card.width - 8f, 18f), item.note, UiSkin.Caption);

                // 가구는 가게 앞으로 배달 와서 직접 놓는 것이라, 받을 자리가 생겨야 살 수 있다.
                // 눌리지 않는 버튼 대신 딱지를 둔다 — 못 누르는 버튼은 고장 난 것처럼 보인다
                DrawTag(card, ActionY, "배달 준비 중", UiSkin.Sky);
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
