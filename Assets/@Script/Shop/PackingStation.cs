using DogShop.Core;
using DogShop.Data;
using DogShop.UI;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 창고 포장대. 모니터에서 온라인 주문을 수락하면 여기서 포장해 배송한다.
    ///   1) 수락한 주문이 있으면 테이블에 열린(포장 전) 상자가 놓인다
    ///   2) 상자를 E — 창고에서 주문 물건을 하나씩 꺼내 상자에 담는다(누를 때마다 하나, 담은 개수를 화면에 알린다)
    ///   3) 다 담았으면 테이프를 E 로 집는다(손에 든다)
    ///   4) 테이프를 든 채 상자를 E — 포장된 상자로 바뀐다
    ///   5) 화면에 [배송하기] 버튼이 뜨고, 누르면 상자가 나가고 판매가·명성을 받는다
    /// 수락한 주문이 여러 개면 하나씩 차례로.
    /// </summary>
    public class PackingStation : MonoBehaviour, IPointerMenu
    {
        [SerializeField] GameObject openBox;
        [SerializeField] GameObject closedBox;
        [SerializeField] Transform tape;

        enum Step { Idle, Filling, Packed }
        Step step = Step.Idle;
        OnlineOrderManager.Order current;
        int packedCount;      // 지금 상자에 담은 물건 수
        bool holdingTape;

        Transform tapeHome;   // 테이프 원래 자리(같은 부모 아래 빈 표식)

        public bool IsOpen => step == Step.Packed;

        void Awake()
        {
            PlayerHoldingTape = false;   // 정적이라 지난 판(시연 초기화 등)의 값이 남지 않게
            if (tape != null)
            {
                tapeHome = new GameObject("TapeHome").transform;
                tapeHome.SetParent(tape.parent, false);
                tapeHome.localPosition = tape.localPosition;
                tapeHome.localRotation = tape.localRotation;
                tapeScale = tape.localScale;
            }
            Show(false, false);
        }

        void OnDestroy() => PointerMenus.SetOpen(this, false);

        void Show(bool open, bool closed)
        {
            if (openBox != null) openBox.SetActive(open);
            if (closedBox != null) closedBox.SetActive(closed);
        }

        int TotalItems => current == null ? 0 : Sum(current.quantities);
        static int Sum(int[] a) { int s = 0; foreach (int v in a) s += v; return s; }

        void Update()
        {
            OnlineOrderManager m = OnlineOrderManager.Instance;
            if (m == null) return;

            // 수락한 주문이 생기면 상자를 놓는다
            if (step == Step.Idle && current == null && m.NextToPack != null)
            {
                current = m.NextToPack;
                packedCount = 0;
                step = Step.Filling;
                Show(true, false);
            }

            // 하루가 끝나 주문이 취소됐거나 목록에서 사라졌으면 상자를 치운다. 봉한 상자의 물건은 창고로 되돌린다
            if (current != null && (current.state != OnlineOrderManager.State.Accepted || !Contains(m, current)))
            {
                if (step == Step.Packed) ReturnItems();
                Reset();
            }

            // [배송하기] 창은 포장대 곁에 있을 때만 — 늘 열어 두면 메뉴가 열린 셈이라 카메라 회전·ESC 가 묶였다
            PointerMenus.SetOpen(this, PanelShown);
        }

        const float PanelRange = 3.5f;

        bool PanelShown
        {
            get
            {
                if (step != Step.Packed || current == null) return false;
                Transform player = FindPlayer();
                return player != null && Vector3.Distance(player.position, transform.position) <= PanelRange;
            }
        }

        static bool Contains(OnlineOrderManager m, OnlineOrderManager.Order o)
        {
            foreach (OnlineOrderManager.Order x in m.Orders) if (x == o) return true;
            return false;
        }

        void Reset()
        {
            current = null;
            packedCount = 0;
            ReturnTape();
            step = Step.Idle;
            Show(false, false);
        }

        // ---- 조준 힌트 ----

        public string HintFor(Collider hit)
        {
            if (step == Step.Packed) return "포장 완료, 화면의 [배송하기]를 누르자";
            bool onTape = tape != null && hit.transform.IsChildOf(tape);
            bool onBox = openBox != null && openBox.activeSelf && hit.transform.IsChildOf(openBox.transform);

            if (current == null)
            {
                OnlineOrderManager m = OnlineOrderManager.Instance;
                return m != null && m.NewCount > 0 ? "모니터에서 온라인 주문을 수락하자" : "포장대 (포장할 주문 없음)";
            }
            if (onTape && !holdingTape) return packedCount < TotalItems ? "테이프 (물건을 먼저 다 담자)" : "[E] 테이프 들기";
            if (onBox || !onTape)
            {
                if (packedCount < TotalItems) return "[E] 물건 담기 " + packedCount + " / " + TotalItems;
                return holdingTape ? "[E] 테이프로 포장하기" : "다 담았다. 테이프를 들자";
            }
            return "";
        }

        // ---- E ----

        /// <summary>E. 거절 사유가 있으면 돌려준다.</summary>
        public string Use(Collider hit)
        {
            if (step == Step.Packed) return null;
            if (current == null)
            {
                OnlineOrderManager m = OnlineOrderManager.Instance;
                return m != null && m.NewCount > 0 ? "모니터에서 주문을 먼저 수락하자" : "포장할 주문이 없다";
            }

            bool onTape = tape != null && hit.transform.IsChildOf(tape);
            if (onTape)
            {
                if (holdingTape) return null;
                if (packedCount < TotalItems) return "주문 물건을 먼저 상자에 다 담자";
                HoldTape();
                return null;
            }

            // 상자(또는 테이블)
            if (packedCount < TotalItems) return PutNext();
            if (!holdingTape) return "테이프를 먼저 들자";
            return Seal();
        }

        /// <summary>
        /// 다음 물건 하나를 상자에 담는다. 창고에 있는지만 보고 <b>아직 빼지 않는다</b> — 봉할 때(<see cref="Seal"/>) 한꺼번에 뺀다.
        /// 담는 동안 빼 두면 포장대 상태는 저장되지 않아, 그 사이 저장·불러오기를 하면 뺀 물건이 사라졌다.
        /// </summary>
        string PutNext()
        {
            InventoryManager inv = InventoryManager.Instance;
            int n = packedCount;
            for (int i = 0; i < current.products.Length; i++)
            {
                if (n >= current.quantities[i]) { n -= current.quantities[i]; continue; }
                int product = current.products[i];
                if (inv.StorageOf(product) <= n)   // n = 이 상품을 이미 담은 개수
                    return "창고에 " + inv.Catalog.Get(product).nameKo + "이(가) 모자란다. 발주부터";
                packedCount++;
                // 물건 모델은 상자에 넣어 보이지 않는다 — 크기가 제각각이라 상자 밖으로 삐져나왔다. 담은 것만 알린다
                var hud = FindAnyObjectByType<GameHud>();
                if (hud != null)
                    hud.Toast(inv.Catalog.Get(product).nameKo + " 담음  " + packedCount + " / " + TotalItems
                              + (packedCount >= TotalItems ? "  다 담았다, 테이프를 들자" : ""), new Color(1f, 0.9f, 0.55f));
                return null;
            }
            return null;
        }

        /// <summary>봉한 상자(창고에서 이미 뺀 물건)를 창고로 되돌린다.</summary>
        /// <summary>
        /// 저장할 때만 쓴다. 봉한 상자의 물건은 창고에서 이미 빠졌는데 포장대 상태(봉함)는 저장되지 않는다 —
        /// 그대로 두면 불러온 뒤 같은 주문을 처음부터 다시 담아 물건이 두 번 빠졌다. 저장본의 창고 수치에 되돌려 넣는다.
        /// </summary>
        public void AddPackedInto(int[] storage)
        {
            if (step != Step.Packed || current == null || storage == null) return;
            for (int i = 0; i < current.products.Length; i++)
            {
                int p = current.products[i];
                if (p >= 0 && p < storage.Length) storage[p] += current.quantities[i];
            }
        }

        void ReturnItems()
        {
            if (current == null) return;
            InventoryManager inv = InventoryManager.Instance;
            for (int i = 0; i < current.products.Length; i++) inv.Store(current.products[i], current.quantities[i]);
        }

        // ---- 테이프 ----

        /// <summary>
        /// 주인이 테이프를 들고 있나 — 오른팔을 "가슴 앞에 든" 자세(Owner_HoldTape, Tape 층)로 바꾸는 신호.
        /// 포장대는 하나뿐이라 정적으로 둔다.
        /// </summary>
        public static bool PlayerHoldingTape { get; private set; }

        /// <summary>
        /// 테이프를 오른손 뼈에 붙이는 자리(손 뼈 기준). 손바닥 가운데쯤 — 들고 있는 자세에서 실측해 맞췄다.
        /// 예전엔 몸 앞 허공(0.28, 0.95, 0.38)에 띄워 손과 따로 놀았다.
        /// </summary>
        /// 손 뼈의 배율이 173 이라(모델 배율 × 뼈대 cm 단위) 값이 아주 작다. 크기는 손 배율로 나눠 원래 크기를 지킨다.
        static readonly Vector3 TapeInHand = new Vector3(-0.00049f, -0.00029f, 0.00019f);
        static readonly Vector3 TapeInHandEuler = new Vector3(311.0f, 314.7f, 304.9f);

        /// <summary>테이프의 원래 크기(포장대 위). 손에 붙일 때 손 배율로 나누고, 돌려놓을 때 되돌린다.</summary>
        Vector3 tapeScale = Vector3.one;

        void HoldTape()
        {
            Transform player = FindPlayer();
            if (player == null || tape == null) return;
            holdingTape = true;
            PlayerHoldingTape = true;
            foreach (Collider c in tape.GetComponentsInChildren<Collider>()) c.enabled = false;

            Animator animator = player.GetComponentInChildren<Animator>();
            Transform hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand != null)
            {
                Vector3 world = Vector3.Scale(tapeHome.parent.lossyScale, tapeScale);   // 포장대 위에서의 실제 크기
                tape.SetParent(hand, false);
                tape.localPosition = TapeInHand;
                tape.localRotation = Quaternion.Euler(TapeInHandEuler);
                Vector3 h = hand.lossyScale;
                tape.localScale = new Vector3(world.x / h.x, world.y / h.y, world.z / h.z);
            }
            else
            {
                tape.SetParent(player, false);
                tape.localPosition = new Vector3(0.28f, 0.95f, 0.38f);   // 손 뼈를 못 찾으면 예전처럼 오른손 앞쯤
                tape.localRotation = Quaternion.Euler(0f, -90f, 0f);
            }
        }

        void ReturnTape()
        {
            if (tape == null || tapeHome == null) return;
            holdingTape = false;
            PlayerHoldingTape = false;
            tape.SetParent(tapeHome.parent, false);
            tape.localPosition = tapeHome.localPosition;
            tape.localRotation = tapeHome.localRotation;
            tape.localScale = tapeScale;
            foreach (Collider c in tape.GetComponentsInChildren<Collider>()) c.enabled = true;
        }

        static Transform FindPlayer()
        {
            Player.PlayerCarry carry = FindAnyObjectByType<Player.PlayerCarry>();
            return carry != null ? carry.transform : null;
        }

        /// <summary>테이프로 봉한다 — 이때 창고에서 주문 물건을 한꺼번에 뺀다. 그 사이 창고가 줄었으면 봉하지 않는다.</summary>
        string Seal()
        {
            InventoryManager inv = InventoryManager.Instance;
            for (int i = 0; i < current.products.Length; i++)
                if (inv.StorageOf(current.products[i]) < current.quantities[i])
                    return "창고에 " + inv.Catalog.Get(current.products[i]).nameKo + "이(가) 모자란다. 발주부터";
            for (int i = 0; i < current.products.Length; i++)
                for (int q = 0; q < current.quantities[i]; q++) inv.TryConsumeStorage(current.products[i]);

            ReturnTape();
            Show(false, true);
            step = Step.Packed;
            Sound.Play(Sfx.Packing);
            return null;
        }

        // ---- 배송하기 ----

        void Ship()
        {
            OnlineOrderManager.Instance.Ship(current);
            // 그 사이 하루가 끝나 취소됐으면 배송이 안 된다 — 봉한 물건을 창고로 되돌린다
            if (current.state != OnlineOrderManager.State.Shipped) ReturnItems();
            Reset();
        }

        static float UiScale => UiSkin.ScreenScale;   // 작은 판 하나라 화면 밖으로 나갈 일이 없다
        static Rect PanelRect() { float s = UiScale; return new Rect((Screen.width / s - 420f) * 0.5f, Screen.height / s * 0.62f, 420f, 130f); }

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (!PanelShown) return false;
            float s = UiScale;
            return PanelRect().Contains(new Vector2(screenPos.x / s, (Screen.height - screenPos.y) / s));
        }

        GUIStyle label;

        void OnGUI()
        {
            if (!PanelShown) return;
            if (label == null || label.font != UiSkin.Font)
            {
                label = new GUIStyle { font = UiSkin.Font, fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                label.normal.textColor = new Color(0.33f, 0.2f, 0.1f);
            }
            Matrix4x4 keep = GUI.matrix;
            float s = UiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            Rect r = PanelRect();
            GUI.Box(r, GUIContent.none, UiSkin.Tag(new Color(0.98f, 0.94f, 0.85f)));
            int price = current.Price(InventoryManager.Instance.Catalog);
            GUI.Label(new Rect(r.x, r.y + 10f, r.width, 30f), "포장 완료!  " + price + "원 / 명성 +" + current.reputation, label);
            if (UiSkin.Click(new Rect(r.center.x - 110f, r.y + 56f, 220f, 52f), "배송하기", new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 20 }))
                Ship();
            GUI.matrix = keep;
        }
    }
}
