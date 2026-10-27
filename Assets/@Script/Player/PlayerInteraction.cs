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
    /// 상자 → 줍기(E) / 들고 있으면 바닥을 보고 R로 내려놓기
    /// 창고 선반 → 상자 단계만큼(1·2·3개) 담기      진열대 → 단계만큼 진열
    /// 손님 → 계산               바닥 오염 → 청소
    /// 계산대 → 발주 메뉴         강아지 → 케어·훈련·판매 메뉴
    /// 문 → 열기·닫기 (정문은 영업 중엔 열린 채 고정, 가게를 닫은 뒤에만)
    ///
    /// 왼쪽 클릭은 메뉴 버튼 조작에만 쓰인다 — 허공을 클릭해 놓치는 일이 없다.
    /// 조준 대상까지의 거리는 카메라가 아니라 <b>플레이어</b> 기준으로 잰다.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        const float RayRange = 60f;
        const float InteractRange = 3.5f;

        /// <summary>조준선이 이만큼보다 더 카메라 쪽(주인공 등 뒤)에서 맞은 것은 무시한다.</summary>
        const float BehindMargin = 1f;
        const float LabelWidth = 200f;
        readonly System.Collections.Generic.List<Rect> placedLabels = new System.Collections.Generic.List<Rect>();
        const float LabelRange = 6f;

        /// <summary>이름표 바로 앞 이만큼은 가려도 보인다. 창고 선반 이름표는 뒷벽에서 0.3m 뒤라 이보다 작아야 한다.</summary>
        const float LabelOcclusionSlack = 0.1f;
        static readonly RaycastHit[] occlusionHits = new RaycastHit[8];

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

            // 설정 창(ESC)·컷신이 시간을 멈춘 동안은 손을 못 쓴다 — 멈춰 있는데 상자가 들리면 안 된다
            if (Time.timeScale == 0f) { hint = ""; return; }

            RefreshHint();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.eKey.wasPressedThisFrame) Interact(true);
            else if (keyboard.rKey.wasPressedThisFrame) Interact(false);

            // T: 강아지 부르기 — 어디 있든 주인에게 뛰어온다
            if (keyboard.tKey.wasPressedThisFrame && !PointerMenus.AnyOpen)
            {
                Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
                DogRoamer roamer = hero != null ? hero.GetComponent<DogRoamer>() : null;
                if (roamer != null)
                {
                    roamer.Call();
                    Sound.Play(Sfx.DogCall);
                    // 부르면서 손을 흔든다 — 강아지가 뛰어오는 이유가 화면에 보이게
                    CharacterAnimatorDriver body = player.GetComponentInChildren<CharacterAnimatorDriver>();
                    if (body != null) body.Wave();
                }
            }
        }

        /// <summary>
        /// 조준선에 걸린 것을 거리 안에서 처리한다.
        ///
        /// <paramref name="load"/> 가 방향을 정한다 — <b>E는 상자에 담고, R은 상자에서 내린다.</b>
        /// 예전에는 E 하나로 "상자에 그 상품이 있으면 넣고 없으면 꺼낸다"로 갈랐는데,
        /// 창고에서 한 개를 담는 순간 "있음"이 되어 다음 E가 방금 담은 것을 도로 넣었다.
        /// 같은 키가 한 번 누를 때마다 뜻이 뒤집히면 연달아 담는 일이 아예 불가능하다.
        /// </summary>
        void Interact(bool load)
        {
            // 허공을 조준했으면 아무것도 하지 않는다 — 예전에는 여기서 상자를 놓아버렸다.
            RaycastHit hit;
            if (!Aim(out hit)) return;

            bool inRange = Vector3.Distance(player.position, hit.point) <= InteractRange;

            CarryCrate crate = hit.collider.GetComponentInParent<CarryCrate>();
            if (crate != null)
            {
                if (!load) return;   // 상자를 내려놓는 것은 바닥을 보고 R
                if (!inRange) { Reject("너무 멀다, 가까이 갈 것"); return; }
                ActionRunner.TryRun(new PickUpAction(carry, crate));
                return;
            }

            // 배달 상자 — E 로 뜯으면 곧장 창고 선반에 정리된다. 창고 선반에 자리가 없으면 뜯지 않는다
            DeliveryParcel parcel = hit.collider.GetComponentInParent<DeliveryParcel>();
            if (parcel != null)
            {
                if (!load) { Reject("상자는 뜯기만 한다 (E)"); return; }
                if (!inRange) { Reject("너무 멀다, 상자에 가까이 갈 것"); return; }
                if (DeliveryManager.Instance != null)
                {
                    string why = DeliveryManager.Instance.OpenIntoStorage(parcel);
                    if (why != null) Reject(why);
                }
                return;
            }

            DeliveryStack delivery = hit.collider.GetComponentInParent<DeliveryStack>();
            if (delivery != null)
            {
                // 배달 더미는 받기만 한다 — 트럭이 내려놓고 간 것을 되돌릴 데가 없다
                if (!load) { Reject("배달 더미에는 되돌릴 수 없다"); return; }
                if (!inRange) { Reject("너무 멀다, 배달 더미에 가까이 갈 것"); return; }
                ActionRunner.TryRun(new TakeFromDeliveryAction(carry, delivery.ProductIndex));
                return;
            }

            StorageRack rack = hit.collider.GetComponentInParent<StorageRack>();
            if (rack != null)
            {
                if (!inRange) { Reject("너무 멀다, 창고 선반에 가까이 갈 것"); return; }

                // 단마다 상품이 다르다 — 조준한 높이의 단
                int tier = rack.TierAt(hit.point);
                int product = rack.ProductOfTier(tier);

                // R 로 빈 단(또는 다 떨어진 단)에 내리면, 상자 속 물건 중 아직 창고 자리가 없는 것에 그 단을 준다
                if (!load && carry != null && carry.IsHolding && StorageManager.Instance != null
                    && (product < 0 || carry.Held.CountOf(product) <= 0))
                {
                    for (int i = 0; i < InventoryManager.Instance.Catalog.Count; i++)
                    {
                        if (carry.Held.CountOf(i) <= 0 || StorageManager.Instance.FindFor(i) != null) continue;
                        if (StorageManager.Instance.ClaimForStore(rack, tier, i)) product = i;
                        break;
                    }
                }

                if (product < 0) { Reject("빈 칸이다"); return; }

                if (load) ActionRunner.TryRun(new TakeOneAction(carry, product));
                else ActionRunner.TryRun(new StoreOneAction(carry, product));
                return;
            }

            ShelfTable shelf = hit.collider.GetComponentInParent<ShelfTable>();
            if (shelf != null)
            {
                if (!inRange) { Reject("너무 멀다, 진열대에 가까이 갈 것"); return; }

                // 어느 칸인지는 <b>조준한 높이</b>가 정한다. 벽 진열대처럼 위아래 두 칸이면
                // 플레이어가 보는 칸에 놓인다 — 칸 고르는 키를 따로 만들 이유가 없다.
                int slot = shelf.SlotAlong(aimRay, hit.point);   // 앞면이 아니라 물건이 놓인 깊이로 칸을 고른다

                if (load) ActionRunner.TryRun(new TakeFromShelfAction(carry, shelf, slot));
                else ActionRunner.TryRun(new PlaceOneAction(carry, shelf, slot));
                return;
            }

            Customer customer = hit.collider.GetComponentInParent<Customer>();
            if (customer != null)
            {
                if (!inRange) { Reject("너무 멀다, 계산대로 갈 것"); return; }
                CustomerManager.Instance.Checkout(customer);
                return;
            }

            // 아래는 전부 상자와 무관한 상호작용이다. R은 상자에서 내리는 키이므로 지나간다
            DirtSpot dirt = hit.collider.GetComponentInParent<DirtSpot>();
            if (dirt != null)
            {
                if (!load) return;
                if (!inRange) { Reject("너무 멀다, 가까이 갈 것"); return; }
                CleanlinessManager.Instance.Clean(dirt);
                return;
            }

            // 온라인 주문 모니터(계산대 옆) — 계산대보다 먼저 본다
            OnlineOrderMenu online = hit.collider.GetComponentInParent<OnlineOrderMenu>();
            if (online != null)
            {
                if (!load) return;
                if (!inRange) { Reject("너무 멀다, 모니터로 갈 것"); return; }
                online.Open();
                return;
            }

            PackingStation packing = hit.collider.GetComponentInParent<PackingStation>();
            if (packing != null)
            {
                if (!load) return;
                if (!inRange) { Reject("너무 멀다, 포장대로 갈 것"); return; }
                string why = packing.Use(hit.collider);
                if (why != null) Reject(why);
                return;
            }

            if (hit.collider.GetComponentInParent<ShopCounter>() != null)
            {
                if (!load) return;
                if (!inRange) { Reject("너무 멀다, 계산대로 갈 것"); return; }
                if (orderMenu != null) orderMenu.Open(ScreenPointOf(hit.point));
                return;
            }

            Dog dog = hit.collider.GetComponentInParent<Dog>();
            if (dog != null)
            {
                if (!load) return;
                if (!inRange) { Reject("너무 멀다, 강아지에게 가까이 갈 것"); return; }
                if (dogMenu != null) dogMenu.Open(dog, ScreenPointOf(hit.point));
                return;
            }

            Bed bed = hit.collider.GetComponentInParent<Bed>();
            if (bed != null)
            {
                if (!load) return;
                if (!inRange) { Reject("너무 멀다, 침대로 갈 것"); return; }
                bed.Open(ScreenPointOf(hit.point));
                return;
            }

            // 문은 자동문이다 — 사람이 다가가면 저절로 열린다. 조준해도 할 일이 없지만,
            // 아래 "바닥에 상자 내려놓기"로 새지 않게 여기서 끝낸다
            if (hit.collider.GetComponentInParent<SwingDoor>() != null) return;

            // 남은 것은 지형이다. 상자는 바닥을 조준했을 때만 내려놓는다 —
            // 벽을 스쳐도 놓아버리면 창고 랙에 담는 도중 상자를 계속 잃는다.
            if (carry == null || !carry.IsHolding) return;

            // 내려놓기는 R 하나로 — E 는 "담기·집기" 키라, 담으려다 조준이 바닥으로 빗나가면 상자를 놓쳐 버렸다
            if (load) return;

            if (!IsGround(hit)) { Reject("바닥에만 내려놓을 수 있다, 발 앞 바닥을 볼 것"); return; }
            if (!inRange) { Reject("너무 멀다, 발 앞 바닥을 볼 것"); return; }

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

        /// <summary>마지막 조준선. 진열대 칸은 맞은 점이 아니라 이 선으로 고른다(<see cref="ShelfTable.SlotAlong"/>).</summary>
        Ray aimRay;

        bool Aim(out RaycastHit hit)
        {
            hit = default;
            if (cam == null) return false;

            Vector2 screenPos = PointerMenus.PickPosition();
            Ray ray = cam.ScreenPointToRay(screenPos);
            aimRay = ray;

            // 3인칭 카메라와 주인공 사이에 있는 것은 주인공 <b>등 뒤</b>다 — 겨눈 대상일 리 없다.
            // 창고 앞벽 선반을 겨누면 조준선이 등 뒤 뒷벽 선반을 먼저 스쳤다. 주인공보다 1m 넘게 카메라 쪽인 것은 건너뛴다(1인칭은 0)
            float behind = Mathf.Max(0f, Vector3.Dot(player.position - ray.origin, ray.direction) - BehindMargin);
            return WallCutaway.Raycast(ray, out hit, RayRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide,
                                       skipPlayer: true, minDistance: behind);   // 숨긴 벽은 통과
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

            if (hit.collider.GetComponentInParent<DeliveryParcel>() != null) hint = "[E] 상자 뜯어 창고에 정리";
            else if (hit.collider.GetComponentInParent<CarryCrate>() != null) hint = "[E] 상자 들기";
            else if (hit.collider.GetComponentInParent<DeliveryStack>() != null) hint = "[E] 상자에 1개 담기";
            // 방향이 키로 갈리므로 둘을 같이 적는다. 무엇이 일어날지 눌러 보고 알게 하면
            // 창고에서 연달아 담다가 한 번 어긋나는 순간 방금 담은 것을 도로 넣게 된다
            else if (hit.collider.GetComponentInParent<StorageRack>() != null)
            {
                int product = hit.collider.GetComponentInParent<StorageRack>().ProductAt(hit.point);
                hint = product < 0 ? (carry != null && carry.IsHolding ? "빈 칸   [R] 상자 물건을 여기 내리기" : "빈 칸")
                     : inv.Catalog.Get(product).nameKo + "   [E] 상자에 " + CarryCrate.PerMove + "개 담기      [R] 창고에 1개 내리기";
            }
            else if (hit.collider.GetComponentInParent<ShelfTable>() != null)
                hint = "[E] 1개 회수      [R] " + CarryCrate.PerMove + "개 진열";
            else if (hit.collider.GetComponentInParent<Customer>() != null) hint = "[E] 계산";
            else if (hit.collider.GetComponentInParent<DirtSpot>() != null) hint = "[E] 청소";
            else if (hit.collider.GetComponentInParent<OnlineOrderMenu>() != null)
            {
                int n = OnlineOrderManager.Instance != null ? OnlineOrderManager.Instance.NewCount : 0;
                hint = n > 0 ? "[E] 온라인 주문 (새 주문 " + n + ")" : "[E] 온라인 주문";
            }
            else if (hit.collider.GetComponentInParent<PackingStation>() != null) hint = hit.collider.GetComponentInParent<PackingStation>().HintFor(hit.collider);
            else if (hit.collider.GetComponentInParent<ShopCounter>() != null) hint = "[E] 발주";
            else if (hit.collider.GetComponentInParent<Dog>() != null) hint = "[E] 강아지 관리";
            else if (hit.collider.GetComponentInParent<Bed>() != null) hint = "[E] 잠자기";
            else if (hit.collider.GetComponentInParent<SwingDoor>() != null)
                hint = "";   // 자동문
            else if (carry != null && carry.IsHolding && IsGround(hit)) hint = "[R] 여기에 상자 내려놓기";
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
                if (carry.IsHolding) { reason = "이미 상자를 들고 있다, 바닥을 보고 R로 내려놓기"; return false; }

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
                    reason = "칸이 부족하다: " + CarryCrate.SlotCostOf(productIndex)
                           + "칸 필요, 남은 " + carry.Held.Room + "칸";
                    return false;
                }
                if (InventoryManager.Instance.StorageOf(productIndex) + InventoryManager.Instance.GradedTotalOf(productIndex) <= 0)
                {
                    reason = "창고 재고 없음, 계산대에서 발주할 것";
                    return false;
                }

                reason = null;
                return true;
            }

            /// <summary>PerMove(1개)만큼 담는다. 칸이 차거나 재고가 떨어지면 거기서 멈춘다.</summary>
            public void Execute()
            {
                for (int n = 0; n < CarryCrate.PerMove; n++)
                {
                    if (n > 0 && !CanExecute(out _)) break;
                    MoveOne();
                }
            }

            void MoveOne()
            {
                InventoryManager inv = InventoryManager.Instance;

                // 등급품이 있으면 <b>좋은 것부터</b> 집는다. 발주품은 언제든 다시 살 수 있지만
                // 상자에서 나온 물건은 그 하나뿐이라, 창고에 묵혀 두면 값을 못 받는다
                for (int g = ItemGrades.Count - 1; g >= 0; g--)
                {
                    ItemGrade grade = (ItemGrade)g;
                    if (inv.GradedOf(productIndex, grade) <= 0) continue;
                    if (!inv.TryConsumeGraded(productIndex, grade)) continue;

                    if (!carry.Held.AddOne(productIndex, grade)) inv.StoreGraded(productIndex, grade);
                    else { GameEvents.RaiseStorageToCrate(); Sound.Play(Sfx.CratePut); }
                    return;
                }

                if (!inv.TryConsumeStorage(productIndex)) return;
                if (!carry.Held.AddOne(productIndex)) inv.Grant(productIndex, 1);
                else { GameEvents.RaiseStorageToCrate(); Sound.Play(Sfx.CratePut); }
            }
        }

        /// <summary>
        /// 가게 앞 배달 더미에서 상자로 1개 옮긴다. 창고 선반에서 꺼내는 것과 같은 동작이고,
        /// 다른 것은 <b>재고가 어디서 줄어드는가</b>뿐이다.
        /// </summary>
        sealed class TakeFromDeliveryAction : IPlayerAction
        {
            readonly PlayerCarry carry;
            readonly int productIndex;

            public TakeFromDeliveryAction(PlayerCarry carry, int productIndex)
            {
                this.carry = carry;
                this.productIndex = productIndex;
            }

            public bool CanExecute(out string reason)
            {
                if (carry == null || !carry.IsHolding) { reason = "상자를 들고 와야 한다 (E)"; return false; }
                if (!carry.Held.Accepts(productIndex))
                {
                    reason = "칸이 부족하다: " + CarryCrate.SlotCostOf(productIndex)
                           + "칸 필요, 남은 " + carry.Held.Room + "칸";
                    return false;
                }
                if (InventoryManager.Instance.DeliveredOf(productIndex) <= 0)
                {
                    reason = "다 날랐다";
                    return false;
                }

                reason = null;
                return true;
            }

            public void Execute()
            {
                if (!InventoryManager.Instance.TryTakeDelivered(productIndex)) return;

                // 상자가 안 받으면 배달 더미에 도로 놓는다 — 물건이 사라지는 경로를 만들지 않는다
                if (!carry.Held.AddOne(productIndex)) InventoryManager.Instance.Store(productIndex);
                else Sound.Play(Sfx.CratePut);
            }
        }

        /// <summary>
        /// 상자에서 창고로 1개 옮긴다. <see cref="TakeOneAction"/> 의 반대 방향이다 —
        /// 배달을 창고에 쌓아 두려면 이 길이 있어야 한다. 강아지 소모품은 창고에서만 나간다.
        /// </summary>
        sealed class StoreOneAction : IPlayerAction
        {
            readonly PlayerCarry carry;
            readonly int productIndex;

            public StoreOneAction(PlayerCarry carry, int productIndex)
            {
                this.carry = carry;
                this.productIndex = productIndex;
            }

            public bool CanExecute(out string reason)
            {
                if (carry == null || !carry.IsHolding) { reason = "상자를 들고 와야 한다 (E)"; return false; }
                if (carry.Held.CountOf(productIndex) <= 0)
                {
                    reason = "상자에 그 물건이 없다";
                    return false;
                }

                reason = null;
                return true;
            }

            public void Execute()
            {
                // 등급 없는 것부터. 진열대에서 되담은 등급품만 남았으면 등급째로 창고에 넣는다 —
                // 예전엔 등급 없는 칸만 빼려다 실패해 R 을 눌러도 아무 일이 없었다
                if (carry.Held.RemoveOne(productIndex))
                {
                    Sound.Play(Sfx.CrateTake);
                    InventoryManager.Instance.Store(productIndex);
                    return;
                }
                ItemGrade grade = carry.Held.BestGradeOf(productIndex);
                if (grade == ItemGrade.None || !carry.Held.RemoveOne(productIndex, grade)) return;
                Sound.Play(Sfx.CrateTake);
                InventoryManager.Instance.StoreGraded(productIndex, grade);
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
                if (crate.IsEmpty) { reason = "상자가 비었다, 창고에서 담아올 것"; return false; }

                int productIndex = Pick();
                if (productIndex < 0)
                {
                    reason = table.AcceptedBulk == 1
                        ? "작은 물건만 올릴 수 있다: " + crate.Describe()
                        : "사료처럼 부피 큰 물건만 올릴 수 있다: " + crate.Describe();
                    return false;
                }

                if (crate.CountOf(productIndex) <= 0)
                {
                    reason = "상자에 " + InventoryManager.Instance.Catalog.Get(productIndex).nameKo + "이(가) 없다";
                    return false;
                }

                return table.CanPlace(slot, productIndex, out reason);
            }

            /// <summary>PerMove(1개)만큼 올린다. 칸이 차거나 상자에서 그 물건이 떨어지면 거기서 멈춘다.</summary>
            public void Execute()
            {
                for (int n = 0; n < CarryCrate.PerMove; n++)
                {
                    if (n > 0 && !CanExecute(out _)) break;
                    PlaceOne();
                }
            }

            void PlaceOne()
            {
                int productIndex = Pick();
                if (productIndex < 0) return;

                // 좋은 등급부터 내놓는다. 손님은 진열대에서 가장 좋은 것을 집어 가므로
                // 창고에 남겨 두면 그만큼 늦게 팔린다
                ItemGrade grade = carry.Held.BestGradeOf(productIndex);
                if (!carry.Held.RemoveOne(productIndex, grade)) return;
                Sound.Play(Sfx.ItemTok);

                table.Place(slot, productIndex, grade);
                GameEvents.RaiseItemDisplayed();
            }
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
                if (!carry.Held.Accepts(productIndex)) { reason = "상자가 가득 찼다: " + carry.Held.Describe(); return false; }

                reason = null;
                return true;
            }

            public void Execute()
            {
                int productIndex = table.ProductAt(slot);
                // 등급품은 등급째로 되담는다 — 예전엔 등급 없이 담겨 상자에서 나온 좋은 물건이 보통 물건이 됐다.
                // 상자가 못 받으면 진열대에 도로 놓는다(물건이 사라지는 길을 만들지 않는다)
                ItemGrade grade = table.GradeAt(slot);
                if (!table.Take(slot, grade)) return;
                if (carry.Held.AddOne(productIndex, grade)) Sound.Play(Sfx.CratePut);
                else table.Place(slot, productIndex, grade);
            }
        }

        // ---- 월드 라벨 ----

        void OnGUI()
        {
            if (cam == null) return;

            EnsureStyles();
            placedLabels.Clear();
            DrawCustomerLabels();
            DrawSlotLabels();
            DrawCrateLabels();
            DrawHint();
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;

            labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 16 };
            labelStyle.normal.textColor = Color.white;
            if (uiFont != null) labelStyle.font = uiFont;

            urgentStyle = new GUIStyle(labelStyle);
            urgentStyle.normal.textColor = new Color(1f, 0.55f, 0.45f);

            crateStyle = new GUIStyle(labelStyle);
            crateStyle.normal.textColor = new Color(0.75f, 1f, 0.8f);

            hintStyle = new GUIStyle(GUI.skin.box) { fontSize = 21 };
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

            // 폭을 글자에 맞춘다 — 260 고정이라 "[E] 상자에 1개 담기   [R] 창고에 1개 내리기" 같은 두 줄 안내가 양옆이 잘렸다
            float hw = Mathf.Min(Screen.width - 40f, Mathf.Max(260f, hintStyle.CalcSize(new GUIContent(hint)).x + 32f));
            GUI.Label(new Rect((Screen.width - hw) * 0.5f, Screen.height - 92f, hw, 28f), hint, hintStyle);
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
                            ? inv.Catalog.Get(index).nameKo + "  " + table.CountAt(slot) + " / " + table.CapacityAt(slot)
                            : (table.AcceptedBulk == 1 ? "빈 칸 (작은 물건)" : table.AcceptedBulk == 2 ? "빈 칸 (큰 물건)" : "빈 칸");

                        DrawLabel(at, text, index >= 0 ? labelStyle : crateStyle, true);
                    }
                }
            }

            StorageManager storage = StorageManager.Instance;
            if (storage != null)
            {
                // 단마다 이름표 — 무엇이 어느 칸에 있는지 한눈에
                for (int i = 0; i < storage.Count; i++)
                {
                    StorageRack rack = storage.Get(i);
                    for (int t = 0; t < StorageRack.Tiers; t++)
                    {
                        int product = rack.ProductOfTier(t);
                        if (product < 0) continue;
                        ProductDef def = inv.Catalog.Get(product);
                        string slots = CarryCrate.SlotCostOf(product) > 1 ? " (2칸)" : "";
                        DrawLabel(rack.LabelPoint(t), def.nameKo + "  " + (inv.StorageOf(product) + inv.GradedTotalOf(product)) + slots, labelStyle, true);
                    }
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
            // 벽 너머 이름표는 그리지 않는다. 매장에 서면 창고 선반 이름표가 뒷벽 위에 떠 보였다(시연 1일차 첫 화면)
            if (rangeLimited && BehindWall(worldPosition)) return;

            Vector3 screen = cam.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) return;

            // 글자 길이만큼만 넓히고, 먼저 놓인 이름표와 겹치면 한 줄씩 아래로 비킨다 —
            // 창고 선반이 나란히 서 있으면 "기본 사료 11 (2칸)" 과 옆 선반 "목줄 10" 이 서로를 가렸다
            float width = Mathf.Clamp(style.CalcSize(new GUIContent(text)).x + 16f, 80f, LabelWidth * 1.4f);
            var rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - 24f, width, 22f);
            for (int tries = 0; tries < 4 && Overlaps(rect); tries++) rect.y += rect.height + 2f;
            placedLabels.Add(rect);
            GUI.Label(rect, text, style);
        }

        /// <summary>
        /// 카메라와 이름표 사이에 벽 같은 고정물이 있는가. 주인·강아지·손님처럼 움직이는 몸은 가림으로 치지 않는다 —
        /// 3인칭 카메라는 늘 주인 등 뒤에 있어서, 몸을 세면 앞에 있는 이름표가 다 사라진다.
        /// </summary>
        bool BehindWall(Vector3 worldPosition)
        {
            Vector3 from = cam.transform.position;
            Vector3 to = worldPosition - from;
            float reach = to.magnitude - LabelOcclusionSlack;
            if (reach <= 0f) return false;

            int count = Physics.RaycastNonAlloc(from, to.normalized, occlusionHits, reach,
                                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = occlusionHits[i].collider;
                if (col.transform.IsChildOf(player)) continue;
                if (col.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null) continue;
                if (col.GetComponentInParent<CarryCrate>() != null) continue;
                // 이름표의 주인(진열대·창고 선반·가구)은 자기 판자에 걸려도 가린 것으로 치지 않는다
                if (col.GetComponentInParent<ShelfTable>() != null || col.GetComponentInParent<StorageRack>() != null
                    || col.GetComponentInParent<PlaceableFurniture>() != null) continue;
                return true;
            }
            return false;
        }

        bool Overlaps(Rect rect)
        {
            for (int i = 0; i < placedLabels.Count; i++)
                if (placedLabels[i].Overlaps(rect)) return true;
            return false;
        }
    }
}
