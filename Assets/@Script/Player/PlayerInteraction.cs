using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using DogShop.Shop;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Player
{
    /// <summary>
    /// 유일한 상호작용 버튼. <b>마우스로 조준하고 E</b>를 누른다.
    ///
    /// 상자 → 줍기 / 들고 있으면 바닥을 보고 E로 내려놓기
    /// 창고 선반 → 1개 담기      진열대 → 1개 진열
    /// 손님 → 계산               바닥 오염 → 청소
    /// 계산대 → 발주 메뉴         강아지 → 케어·훈련·판매 메뉴
    ///
    /// 왼쪽 클릭은 메뉴 버튼 조작에만 쓰인다 — 허공을 클릭해 놓치는 일이 없다.
    /// 조준 대상까지의 거리는 카메라가 아니라 <b>플레이어</b> 기준으로 잰다.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        const float RayRange = 60f;
        const float InteractRange = 3.5f;
        const float LabelWidth = 200f;
        const float LabelRange = 6f;

        Camera cam;
        Transform player;
        PlayerCarry carry;
        DogContextMenu dogMenu;
        ShopOrderMenu orderMenu;
        IPointerMenu[] menus;

        /// <summary>월드 라벨(가격표·힌트) 폰트. HUD와 같은 서체를 써야 한 화면처럼 보인다.</summary>
        [SerializeField] Font uiFont;

        GUIStyle labelStyle;
        GUIStyle urgentStyle;
        GUIStyle crateStyle;
        GUIStyle hintStyle;

        string hint = "";

        /// <summary>배치 모드가 켜져 있으면 E를 그쪽에 넘긴다 — 진열하다 진열대를 들면 안 된다.</summary>
        FurniturePlacer placer;

        void Awake()
        {
            Camera found = GetComponentInChildren<Camera>();
            if (found != null) cam = found;

            carry = GetComponentInParent<PlayerCarry>();
            player = carry != null ? carry.transform : transform;
            dogMenu = GetComponent<DogContextMenu>();
            placer = GetComponent<FurniturePlacer>();
            orderMenu = GetComponent<ShopOrderMenu>();
            menus = GetComponents<IPointerMenu>();
        }

        void Update()
        {
            if (placer != null && placer.Active) { hint = ""; return; }

            RefreshHint();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.eKey.wasPressedThisFrame) return;

            Interact();
        }

        /// <summary>조준선에 걸린 것을 거리 안에서 처리한다.</summary>
        void Interact()
        {
            // 허공을 조준했으면 아무것도 하지 않는다 — 예전에는 여기서 상자를 놓아버렸다.
            RaycastHit hit;
            if (!Aim(out hit)) return;

            bool inRange = Vector3.Distance(player.position, hit.point) <= InteractRange;

            CarryCrate crate = hit.collider.GetComponentInParent<CarryCrate>();
            if (crate != null)
            {
                if (!inRange) { Reject("너무 멀다 — 가까이 갈 것"); return; }
                ActionRunner.TryRun(new PickUpAction(carry, crate));
                return;
            }

            StorageRack rack = hit.collider.GetComponentInParent<StorageRack>();
            if (rack != null)
            {
                if (!inRange) { Reject("너무 멀다 — 창고 선반에 가까이 갈 것"); return; }
                ActionRunner.TryRun(new TakeOneAction(carry, rack.ProductIndex));
                return;
            }

            ShelfTable shelf = hit.collider.GetComponentInParent<ShelfTable>();
            if (shelf != null)
            {
                if (!inRange) { Reject("너무 멀다 — 진열대에 가까이 갈 것"); return; }

                // 어느 칸인지는 <b>조준한 높이</b>가 정한다. 벽 진열대처럼 위아래 두 칸이면
                // 플레이어가 보는 칸에 놓인다 — 칸 고르는 키를 따로 만들 이유가 없다.
                int slot = shelf.SlotNear(hit.point.y);

                // 그 칸에 뭔가 있으면 회수, 비어 있으면 상자에서 진열.
                // 둘이 동시에 성립하지 않으므로 키를 나눌 필요가 없다.
                if (shelf.CountAt(slot) > 0 && !HoldsProduct(shelf.ProductAt(slot)))
                    ActionRunner.TryRun(new TakeFromShelfAction(carry, shelf, slot));
                else
                    ActionRunner.TryRun(new PlaceOneAction(carry, shelf, slot));
                return;
            }

            Customer customer = hit.collider.GetComponentInParent<Customer>();
            if (customer != null)
            {
                if (!inRange) { Reject("너무 멀다 — 계산대로 갈 것"); return; }
                CustomerManager.Instance.Checkout(customer);
                return;
            }

            DirtSpot dirt = hit.collider.GetComponentInParent<DirtSpot>();
            if (dirt != null)
            {
                if (!inRange) { Reject("너무 멀다 — 가까이 갈 것"); return; }
                CleanlinessManager.Instance.Clean(dirt);
                return;
            }

            if (hit.collider.GetComponentInParent<ShopCounter>() != null)
            {
                if (!inRange) { Reject("너무 멀다 — 계산대로 갈 것"); return; }
                if (orderMenu != null) orderMenu.Open(ScreenPointOf(hit.point));
                return;
            }

            Dog dog = hit.collider.GetComponentInParent<Dog>();
            if (dog != null)
            {
                if (!inRange) { Reject("너무 멀다 — 강아지에게 가까이 갈 것"); return; }
                if (dogMenu != null) dogMenu.Open(dog, ScreenPointOf(hit.point));
                return;
            }

            Bed bed = hit.collider.GetComponentInParent<Bed>();
            if (bed != null)
            {
                if (!inRange) { Reject("너무 멀다 — 침대로 갈 것"); return; }
                bed.Open(ScreenPointOf(hit.point));
                return;
            }

            // 남은 것은 지형이다. 상자는 바닥을 조준했을 때만 내려놓는다 —
            // 벽을 스쳐도 놓아버리면 창고 랙에 담는 도중 상자를 계속 잃는다.
            if (carry == null || !carry.IsHolding) return;

            if (!IsGround(hit)) { Reject("바닥에만 내려놓을 수 있다 — 발 앞 바닥을 볼 것"); return; }
            if (!inRange) { Reject("너무 멀다 — 발 앞 바닥을 볼 것"); return; }

            carry.DropAt(hit.point);
        }

        /// <summary>바닥 높이 허용 오차. 매장·창고 바닥은 둘 다 y=0 이다.</summary>
        const float GroundLevel = 0.06f;

        /// <summary>
        /// <b>진짜 바닥</b>만 인정한다. 위를 향한 면이면서 높이가 바닥면이어야 한다.
        /// 법선만 보면 계산대 상판(0.63)·진열대(0.48)·선반 위, 심지어 손님 머리까지
        /// 위를 향하므로 상자를 그 위에 얹을 수 있게 되어 버그처럼 보인다.
        /// </summary>
        static bool IsGround(RaycastHit hit) => hit.normal.y > 0.7f && hit.point.y <= GroundLevel;

        bool Aim(out RaycastHit hit)
        {
            hit = default;
            if (cam == null) return false;

            Vector2 screenPos = PointerMenus.PickPosition();
            return Physics.Raycast(cam.ScreenPointToRay(screenPos), out hit, RayRange, ~0, QueryTriggerInteraction.Collide);
        }

        Vector2 ScreenPointOf(Vector3 worldPoint)
        {
            Vector3 screen = cam.WorldToScreenPoint(worldPoint);
            return new Vector2(screen.x, screen.y);
        }

        static void Reject(string reason) => ActionRunner.Reject(reason);

        /// <summary>조준 대상에 따라 화면 하단에 무엇을 할 수 있는지 띄운다.</summary>
        void RefreshHint()
        {
            hint = "";

            RaycastHit hit;
            if (!Aim(out hit)) return;
            if (Vector3.Distance(player.position, hit.point) > InteractRange) return;

            InventoryManager inv = InventoryManager.Instance;

            if (hit.collider.GetComponentInParent<CarryCrate>() != null) hint = "[E] 상자 들기";
            else if (hit.collider.GetComponentInParent<StorageRack>() != null) hint = "[E] 상자에 1개 담기";
            else if (hit.collider.GetComponentInParent<ShelfTable>() != null)
            {
                ShelfTable aimed = hit.collider.GetComponentInParent<ShelfTable>();
                int slot = aimed.SlotNear(hit.point.y);
                hint = aimed.CountAt(slot) > 0 && !HoldsProduct(aimed.ProductAt(slot))
                     ? "[E] 1개 회수" : "[E] 1개 진열";
            }
            else if (hit.collider.GetComponentInParent<Customer>() != null) hint = "[E] 계산";
            else if (hit.collider.GetComponentInParent<DirtSpot>() != null) hint = "[E] 청소";
            else if (hit.collider.GetComponentInParent<ShopCounter>() != null) hint = "[E] 발주";
            else if (hit.collider.GetComponentInParent<Dog>() != null) hint = "[E] 강아지 관리";
            else if (hit.collider.GetComponentInParent<Bed>() != null) hint = "[E] 잠자기";
            else if (carry != null && carry.IsHolding && IsGround(hit)) hint = "[E] 여기에 상자 내려놓기";
        }

        // ---- 액션 ----

        sealed class PickUpAction : IPlayerAction
        {
            readonly PlayerCarry carry;
            readonly CarryCrate crate;

            public PickUpAction(PlayerCarry carry, CarryCrate crate)
            {
                this.carry = carry;
                this.crate = crate;
            }

            public bool CanExecute(out string reason)
            {
                if (carry == null) { reason = "플레이어 없음"; return false; }
                if (carry.IsHolding) { reason = "이미 상자를 들고 있다 — 바닥을 보고 E로 내려놓기"; return false; }

                reason = null;
                return true;
            }

            public void Execute() => carry.PickUp(crate);
        }

        sealed class TakeOneAction : IPlayerAction
        {
            readonly PlayerCarry carry;
            readonly int productIndex;

            public TakeOneAction(PlayerCarry carry, int productIndex)
            {
                this.carry = carry;
                this.productIndex = productIndex;
            }

            public bool CanExecute(out string reason)
            {
                if (carry == null || !carry.IsHolding) { reason = "상자를 들고 와야 한다 (E)"; return false; }
                if (!carry.Held.Accepts(productIndex))
                {
                    reason = "칸이 부족하다 — " + CarryCrate.SlotCostOf(productIndex)
                           + "칸 필요, 남은 " + carry.Held.Room + "칸";
                    return false;
                }
                if (InventoryManager.Instance.StorageOf(productIndex) <= 0)
                {
                    reason = "창고 재고 없음 — 계산대에서 발주할 것";
                    return false;
                }

                reason = null;
                return true;
            }

            public void Execute()
            {
                if (!InventoryManager.Instance.TryConsumeStorage(productIndex)) return;
                if (!carry.Held.AddOne(productIndex))
                    InventoryManager.Instance.Grant(productIndex, 1);
            }
        }

        /// <summary>
        /// 상자에서 <b>그 칸</b>으로 1개 옮긴다. 칸이 이미 다른 상품에 배정돼 있으면 거절한다 —
        /// 그 규칙이 "자리를 바꾸려면 먼저 비워야 한다"를 만든다.
        /// </summary>
        sealed class PlaceOneAction : IPlayerAction
        {
            readonly PlayerCarry carry;
            readonly ShelfTable table;
            readonly int slot;

            public PlaceOneAction(PlayerCarry carry, ShelfTable table, int slot)
            {
                this.carry = carry;
                this.table = table;
                this.slot = slot;
            }

            int Pick()
            {
                int bound = table.ProductAt(slot);
                if (bound >= 0) return bound;

                // 빈 칸이면 상자에 든 것 중 이 진열대가 받는 첫 상품을 올린다
                CarryCrate crate = carry.Held;
                for (int i = 0; i < InventoryManager.Instance.Catalog.Count; i++)
                    if (crate.CountOf(i) > 0 && table.AcceptsBulk(i)) return i;

                return -1;
            }

            public bool CanExecute(out string reason)
            {
                if (carry == null || !carry.IsHolding) { reason = "상자를 들고 와야 한다 (E)"; return false; }

                CarryCrate crate = carry.Held;
                if (crate.IsEmpty) { reason = "상자가 비었다 — 창고에서 담아올 것"; return false; }

                int productIndex = Pick();
                if (productIndex < 0)
                {
                    reason = table.AcceptedBulk == 1
                        ? "작은 물건만 올릴 수 있다 — " + crate.Describe()
                        : "사료처럼 부피 큰 물건만 올릴 수 있다 — " + crate.Describe();
                    return false;
                }

                if (crate.CountOf(productIndex) <= 0)
                {
                    reason = "상자에 " + InventoryManager.Instance.Catalog.Get(productIndex).nameKo + "이(가) 없다";
                    return false;
                }

                return table.CanPlace(slot, productIndex, out reason);
            }

            public void Execute()
            {
                int productIndex = Pick();
                if (productIndex < 0) return;
                if (!carry.Held.RemoveOne(productIndex)) return;

                table.Place(slot, productIndex);
            }
        }

        bool HoldsProduct(int productIndex)
        {
            return carry != null && carry.IsHolding && carry.Held.CountOf(productIndex) > 0;
        }

        /// <summary>
        /// 진열대에서 상자로 도로 담는다. 진열대를 옮기려면 먼저 비워야 하므로
        /// <b>되돌리는 경로가 반드시 있어야 한다</b> — 없으면 한 번 진열한 진열대는 영영 못 옮긴다.
        /// </summary>
        sealed class TakeFromShelfAction : IPlayerAction
        {
            readonly PlayerCarry carry;
            readonly ShelfTable table;
            readonly int slot;

            public TakeFromShelfAction(PlayerCarry carry, ShelfTable table, int slot)
            {
                this.carry = carry;
                this.table = table;
                this.slot = slot;
            }

            public bool CanExecute(out string reason)
            {
                if (carry == null || !carry.IsHolding) { reason = "상자를 들고 와야 한다 (E)"; return false; }

                int productIndex = table.ProductAt(slot);
                if (productIndex < 0 || table.CountAt(slot) <= 0) { reason = "이 칸은 비었다"; return false; }
                if (!carry.Held.Accepts(productIndex)) { reason = "상자가 가득 찼다 — " + carry.Held.Describe(); return false; }

                reason = null;
                return true;
            }

            public void Execute()
            {
                int productIndex = table.ProductAt(slot);
                if (!table.Take(slot)) return;
                carry.Held.AddOne(productIndex);
            }
        }

        // ---- 월드 라벨 ----

        void OnGUI()
        {
            if (cam == null) return;

            EnsureStyles();
            DrawCustomerLabels();
            DrawSlotLabels();
            DrawCrateLabels();
            DrawHint();
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;

            labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 14 };
            labelStyle.normal.textColor = Color.white;
            if (uiFont != null) labelStyle.font = uiFont;

            urgentStyle = new GUIStyle(labelStyle);
            urgentStyle.normal.textColor = new Color(1f, 0.55f, 0.45f);

            crateStyle = new GUIStyle(labelStyle);
            crateStyle.normal.textColor = new Color(0.75f, 1f, 0.8f);

            hintStyle = new GUIStyle(GUI.skin.box) { fontSize = 18 };
            hintStyle.normal.textColor = new Color(1f, 0.93f, 0.6f);
            if (uiFont != null) hintStyle.font = uiFont;
        }

        void DrawHint()
        {
            // 1인칭은 커서가 없으므로 조준점을 그려준다. 3인칭은 마우스 커서가 곧 조준점이다.
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 aim = PointerMenus.PickPosition();
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                GUI.DrawTexture(new Rect(aim.x - 2f, Screen.height - aim.y - 2f, 4f, 4f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (hint.Length == 0) return;

            GUI.Label(new Rect((Screen.width - 260f) * 0.5f, Screen.height - 92f, 260f, 28f), hint, hintStyle);
        }

        void DrawCustomerLabels()
        {
            CustomerManager cm = CustomerManager.Instance;
            if (cm == null) return;

            for (int i = 0; ; i++)
            {
                Customer c = cm.QueuedAt(i);
                if (c == null) break;
                if (c.State != CustomerState.Waiting || c.Label.Length == 0) continue;

                // 정가를 다 못 받게 된 손님은 깎인 값과 명성 손실을 같이 보여준다 —
                // 왜 손해인지가 보이지 않으면 계산대를 지킬 이유가 안 생긴다.
                string text = c.Label;
                if (c.Overtime > 0f)
                {
                    text = c.Label + "  ->  " + cm.PayoutOf(c) + "원";
                    int penalty = cm.ReputationPenaltyOf(c);
                    if (penalty > 0) text += "  명성 -" + penalty;
                }

                bool urgent = c.WaitRemaining < CustomerManager.Patience * 0.35f;
                DrawLabel(c.transform.position + Vector3.up * c.LabelHeight, text, urgent ? urgentStyle : labelStyle, false);
            }
        }

        void DrawSlotLabels()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            ShelfManager shelves = ShelfManager.Instance;
            if (shelves != null)
            {
                // 칸마다 한 줄씩 띄운다 — 벽 진열대는 위아래가 다른 상품일 수 있다
                for (int i = 0; i < shelves.Count; i++)
                {
                    ShelfTable table = shelves.Get(i);
                    for (int slot = 0; slot < table.SlotCount; slot++)
                    {
                        int index = table.ProductAt(slot);
                        Vector3 at = table.transform.position + Vector3.up * (table.HeightOf(slot) + 0.34f);

                        string text = index >= 0
                            ? inv.Catalog.Get(index).nameKo + "  " + table.CountAt(slot) + " / " + table.CapacityPerSlot
                            : (table.AcceptedBulk == 1 ? "빈 칸 (작은 물건)" : table.AcceptedBulk == 2 ? "빈 칸 (큰 물건)" : "빈 칸");

                        DrawLabel(at, text, index >= 0 ? labelStyle : crateStyle, true);
                    }
                }
            }

            StorageManager storage = StorageManager.Instance;
            if (storage != null)
            {
                for (int i = 0; i < storage.Count; i++)
                {
                    StorageRack rack = storage.Get(i);
                    ProductDef def = inv.Catalog.Get(rack.ProductIndex);
                    string slots = CarryCrate.SlotCostOf(rack.ProductIndex) > 1 ? " (2칸)" : "";
                    DrawLabel(rack.transform.position + Vector3.up * 1.15f,
                        def.nameKo + "  창고 " + inv.StorageOf(rack.ProductIndex) + slots,
                        labelStyle, true);
                }
            }
        }

        void DrawCrateLabels()
        {
            if (carry == null) return;

            CarryCrate[] crates = Object.FindObjectsByType<CarryCrate>(FindObjectsSortMode.None);
            for (int i = 0; i < crates.Length; i++)
            {
                if (carry.Held == crates[i]) continue;
                DrawLabel(crates[i].transform.position + Vector3.up * 0.5f, crates[i].Describe(), crateStyle, true);
            }
        }

        void DrawLabel(Vector3 worldPosition, string text, GUIStyle style, bool rangeLimited)
        {
            if (rangeLimited && Vector3.Distance(player.position, worldPosition) > LabelRange) return;

            Vector3 screen = cam.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) return;

            GUI.Label(new Rect(screen.x - LabelWidth * 0.5f, Screen.height - screen.y - 24f, LabelWidth, 22f),
                text, style);
        }
    }
}
