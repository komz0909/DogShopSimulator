using System.Collections.Generic;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 겉모습 — <b>동물용품점</b>. 밖에서 보면 하얀 벽 상자(컨테이너)였다 — 벽이 안쪽 벽지 재질 하나뿐이라서.
    /// 벽 바깥에 얇은 외장을 덧대고, 가게 레벨에 따라 3단계로 키운다.
    ///
    ///   작은 동물용품점(L1~3)  파스텔 노랑 판벽, 문 양옆 진열창(사료·장난감이 보이는 쇼윈도) + 발바닥 차양 + 화단
    ///   중간 동물용품점(L4~6)  하늘색 발바닥 무늬 벽, 난간벽, 문 옆 벽등, 입구 강아지 마스코트
    ///   큰 동물용품점(L7~10)   흰·민트 타일 2층 정면, 가운데 "PET SHOP" 큰 간판, 위층 창, 마스코트 한 쌍
    ///
    /// 가게는 <b>문(x 4)을 가운데 두고 양쪽으로</b> 넓어진다(<see cref="ShopSpace"/>: 8 → 9.5 → 12m).
    /// 외장도 문을 축으로 좌우 대칭이다. 에셋으로 굳히지 않고 <b>지금 단계·폭으로 매번 조립</b>한다.
    ///
    /// 쇼윈도·벽·차양·간판 그림과 마스코트는 VARCO 로 뽑았다(<c>Town/T_Pet*</c>, <c>AIProps/Town/P_TownMascot</c>).
    /// 외장은 보이기만 한다. 충돌체가 없고 NavMesh 에도 들어가지 않는다.
    ///
    /// <b>에디터에서도 조립한다</b>(ExecuteAlways) — 실행 전 씬 화면이 예전 갈색 문·발바닥 간판 그대로라
    /// 게임 화면과 달라 보였다. 에디터에서 만든 것은 씬에 저장되지 않게(<see cref="HideFlags.DontSave"/>) 붙이고,
    /// 다시 조립할 때 이름으로 찾아 지운다. 레벨·폭을 모르는 에디터에서는 1단계(작은 가게, 폭 8)로 보인다.
    /// </summary>
    [ExecuteAlways]
    public class ShopExterior : MonoBehaviour
    {
        [Header("벽 재질(단계별)")]
        [SerializeField] Material wallSmall;
        [SerializeField] Material wallMedium;
        [SerializeField] Material wallLarge;

        [Header("장식")]
        [SerializeField] Material trim;       // 몰딩·창틀(크림)
        [SerializeField] Material wood;       // 화단
        [SerializeField] Material displayA;   // 쇼윈도 안쪽 그림
        [SerializeField] Material displayB;
        [SerializeField] Material awning;     // 발바닥 줄무늬
        [SerializeField] Material lamp;       // 벽등(발광)
        [SerializeField] Material plinth;     // 바닥 띠
        [SerializeField] Material billboard;  // 큰 가게 2층 간판
        [SerializeField] GameObject mascot;   // 입구 강아지 조각상

        [Header("건물 통째 모델(단계별) — 있으면 덧댄 외장 대신 이걸 씌운다")]
        [SerializeField] GameObject[] shells = new GameObject[3];
        /// <summary>판매장 맞배지붕. 건물 모델에 제 지붕이 있으니 그때는 숨긴다.</summary>
        [SerializeField] Transform shopRoof;

        /// <summary>
        /// 판매장 앞벽(문 왼쪽·오른쪽·문 위). 건물 모델을 씌우면 <b>바깥면을 안 그리고 안쪽 면만</b> 남긴다 —
        /// 그래야 모델 진열창 너머로 하얀 벽이 아니라 실제 가게 안이 보인다. 충돌체는 그대로다.
        /// </summary>
        [SerializeField] Transform[] frontWalls = new Transform[0];

        /// <summary>문 위 간판(Doorway/Sign·Sign_Back). 건물 모델을 쓸 때는 숨긴다.</summary>
        [SerializeField] Transform[] signParts = new Transform[0];

        /// <summary>
        /// 건물 모델 프리팹 안의 간판 글자 판. 바르코가 3D 로 옮기며 "DogShop" 글자를 뭉개 버려서,
        /// 간판 자리에 또렷한 글자 그림을 한 장 덧대 둔다. 벽 찾기·문 파내기에서는 뺀다.
        /// </summary>
        const string SignDecal = "SignDecal";


        /// <summary>벽 바깥면. 앞벽은 z -0.2. 문은 x 3~5(가게 가운데).</summary>
        const float FrontFace = -0.20f;
        const float Skin = 0.03f;
        const float WallHeight = 3f;
        const float DoorLeft = 3f, DoorRight = 5f, DoorTop = 2.46f;

        /// <summary>문짝 윗변. 그 위 상인방(Lintel)을 숨기므로, 문 둘레 상인방이 여기까지 내려와야 틈이 안 생긴다.</summary>
        const float LeafTop = 2.18f;

        /// <summary>창고·옆방(가게 뒤 별채). 가게 폭과 무관하게 고정이다.</summary>
        const float AnnexLeft = -0.2f, AnnexRight = 12.4f, AnnexFront = 6.0f, AnnexBack = 10.2f;

        readonly List<Mesh> madeMeshes = new List<Mesh>();

        int builtTier = -1;
        float builtWidth = -1f;
        Transform root;
        MaterialPropertyBlock block;

        public static int TierOf(int level) => level >= 7 ? 2 : level >= 4 ? 1 : 0;

        void LateUpdate()
        {
            if (!gameObject.scene.IsValid()) return;   // 프리팹 에셋 자체에서는 조립하지 않는다
            int level = ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Level : 1;
            float width = ShopSpace.Instance != null ? ShopSpace.Instance.Width : 8f;
            int tier = TierOf(level);
            if (tier == builtTier && Mathf.Abs(width - builtWidth) < 0.01f) { SyncShellWithRoof(); return; }
            Build(tier, width);
            if (!Application.isPlaying) MarkPreview();
        }

        public void Build(int tier, float width)
        {
            builtTier = tier;
            builtWidth = width;
            block ??= new MaterialPropertyBlock();

            // 옛 외장은 프레임 끝에야 지워진다 — 그 사이 새 외장과 겹쳐 보이지 않게 먼저 숨긴다
            if (root != null) { root.gameObject.SetActive(false); Kill(root.gameObject); }
            // 에디터 미리보기가 남긴 것(스크립트가 다시 읽히면 root 참조를 잃는다)도 이름으로 찾아 지운다
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform stale = transform.GetChild(i);
                if (stale.name == "Built" && stale != root) { stale.gameObject.SetActive(false); Kill(stale.gameObject); }
            }
            shellRenderers = null;
            // 지난번에 잘라 만든 메시 사본 — 안 지우면 조립할 때마다(에디터에선 스크립트가 다시 읽힐 때마다) 쌓였다
            foreach (Mesh m in madeMeshes) Kill(m);
            madeMeshes.Clear();
            root = new GameObject("Built").transform;
            root.SetParent(transform, false);
            if (!Application.isPlaying) root.gameObject.hideFlags = HideFlags.DontSave;

            float left = ShopSpace.CenterX - width * 0.5f;
            float right = ShopSpace.CenterX + width * 0.5f;

            Material wall = tier == 2 ? wallLarge : tier == 1 ? wallMedium : wallSmall;
            float tile = tier == 2 ? 1.5f : 2.0f;

            // 건물 모델이 있으면 판매장 몸체는 모델이 맡고, 뒤 별채(창고·옆방)만 외장을 덧댄다
            GameObject shell = tier < shells.Length ? shells[tier] : null;
            SetShopRoof(shell == null);
            SetFrontWallsInnerOnly(shell != null);
            PullSign(shell != null);
            if (shell == null) { ClearLeaves(); SetDoorVisuals(true); RestoreDoor(); }
            if (shell != null)
            {
                FitShell(shell, left, right);
                if (mascot != null && tier >= 1)
                {
                    Mascot(DoorLeft - 0.6f);
                    if (tier == 2) Mascot(DoorRight + 0.6f);
                }
                AnnexCladding(wall, tile, left, right);
                return;
            }
            float top = tier == 2 ? 5.9f : tier == 1 ? 3.7f : WallHeight + 0.1f;

            // ---- 앞면 ----
            Panel("Front_L", left - 0.25f, DoorLeft, 0f, top, wall, tile);
            Panel("Front_R", DoorRight, right + 0.25f, 0f, top, wall, tile);
            Panel("Front_Header", DoorLeft, DoorRight, DoorTop, top, wall, tile, 0.02f);

            Box("Plinth_L", new Vector3(left - 0.25f, 0f, FrontFace - 0.06f), new Vector3(DoorLeft, 0.32f, FrontFace), plinth);
            Box("Plinth_R", new Vector3(DoorRight, 0f, FrontFace - 0.06f), new Vector3(right + 0.25f, 0.32f, FrontFace), plinth);

            float corniceDepth = tier == 2 ? 0.3f : 0.18f;
            float corniceH = tier == 2 ? 0.26f : 0.16f;
            Box("Cornice", new Vector3(left - 0.35f, top - corniceH, FrontFace - corniceDepth), new Vector3(right + 0.35f, top, FrontFace), trim);
            if (tier == 2) Box("Cornice_Mid", new Vector3(left - 0.3f, WallHeight - 0.1f, FrontFace - 0.16f), new Vector3(right + 0.3f, WallHeight + 0.06f, FrontFace), trim);

            // ---- 쇼윈도: 문 양쪽 대칭 ----
            int shown = 0;
            foreach (Vector2 side in new[] { new Vector2(left + 0.45f, DoorLeft - 0.45f), new Vector2(DoorRight + 0.45f, right - 0.45f) })
            {
                var spans = new List<Vector2>();
                if (side.y - side.x > 3.4f)
                {
                    float mid = (side.x + side.y) * 0.5f;
                    spans.Add(new Vector2(side.x, mid - 0.2f));
                    spans.Add(new Vector2(mid + 0.2f, side.y));
                }
                else spans.Add(side);

                foreach (Vector2 w in spans)
                {
                    Window(w.x, w.y, 0.62f, 2.2f, shown++ % 2 == 0 ? displayA : displayB);
                    Awning(w.x - 0.16f, w.y + 0.16f, 2.52f, tier == 0 ? 0.75f : 0.95f);
                    FlowerBox(w.x, w.y, 0.62f);
                }
            }

            // ---- 단계별 장식 ----
            if (tier >= 1)
            {
                Lamp(DoorLeft - 0.26f);
                Lamp(DoorRight + 0.26f);
            }

            // 입구 마스코트 — 손님 통로(x 3.5~4.5)와 배송 자리 짐을 비켜 문 바로 옆
            if (mascot != null && tier >= 1)
            {
                Mascot(DoorLeft - 0.42f);
                if (tier == 2) Mascot(DoorRight + 0.42f);
            }

            if (tier == 2)
            {
                // 2층 가운데 큰 간판, 양옆엔 위층 창
                float bw = Mathf.Min(6.2f, width - 4.6f), bh = bw * 9f / 16f;
                bh = Mathf.Min(bh, 2.2f);   // 위 처마(5.64)에 닿지 않게
                bw = bh * 16f / 9f;
                float cx = ShopSpace.CenterX, y0 = 3.32f;
                Picture("Billboard", cx - bw * 0.5f, cx + bw * 0.5f, y0, y0 + bh, billboard);
                Frame(cx - bw * 0.5f, cx + bw * 0.5f, y0, y0 + bh, 0.12f);

                foreach (Vector2 span in new[] { new Vector2(left + 0.4f, cx - bw * 0.5f - 0.35f), new Vector2(cx + bw * 0.5f + 0.35f, right - 0.4f) })
                {
                    if (span.y - span.x < 0.8f) continue;
                    Window(span.x, span.y, 3.55f, 4.85f, null);
                    Box("Sill", new Vector3(span.x - 0.1f, 3.46f, FrontFace - 0.14f), new Vector3(span.y + 0.1f, 3.55f, FrontFace), trim);
                }
            }

            // ---- 옆·뒤 ----
            SidePanelX("Right_Shop", right + 0.2f, -0.2f, AnnexFront + 0.2f, wall, tile, +1);
            if (right + 0.2f < AnnexRight) PanelZ("Right_AnnexFront", right + 0.2f, AnnexRight, AnnexFront, wall, tile, -1);
            SidePanelX("Right_Annex", AnnexRight, AnnexFront, AnnexBack, wall, tile, +1);
            SidePanelX("Left_Shop", left - 0.2f, -0.2f, AnnexFront + 0.2f, wall, tile, -1);
            if (left - 0.2f < AnnexLeft) PanelZ("Left_ShopBack", left - 0.2f, AnnexLeft, AnnexFront + 0.2f, wall, tile, +1);
            SidePanelX("Left_Annex", AnnexLeft, AnnexFront + 0.2f, AnnexBack, wall, tile, -1);
            PanelZ("Back", AnnexLeft, AnnexRight, AnnexBack, wall, tile, +1);
        }

        // ---- 건물 모델 ----

        /// <summary>
        /// 판매장 맞배지붕을 숨기거나 되살린다. enabled 로 끄면 <see cref="RoofVisibility"/>가
        /// 실내 출입 때마다 다시 켜 버린다 — 그쪽이 건드리지 않는 forceRenderingOff 를 쓴다.
        /// </summary>
        void SetShopRoof(bool visible)
        {
            if (shopRoof == null) return;
            foreach (Renderer r in shopRoof.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !visible;
        }

        /// <summary>
        /// 앞벽을 안쪽 면만 그리게 하거나 되돌린다. 벽(정육면체)은 그대로 두고 그리기만 끈 뒤,
        /// 안쪽(+Z)을 보는 쿼드를 자식으로 붙인다 — 벽이 폭에 따라 늘어나면 쿼드도 같이 늘어난다.
        /// </summary>
        void SetFrontWallsInnerOnly(bool innerOnly)
        {
            foreach (Transform w in frontWalls)
            {
                if (w == null) continue;
                Renderer cube = w.GetComponent<Renderer>();
                if (cube == null) continue;
                cube.forceRenderingOff = innerOnly;

                Transform inner = w.Find("InnerFace");
                if (inner == null && innerOnly)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "InnerFace";
                    Strip(q);
                    q.transform.SetParent(w, false);
                    q.transform.localPosition = new Vector3(0f, 0f, 0.5f + 0.002f);   // 정육면체 안쪽(+Z) 면 바로 앞
                    q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);       // 쿼드는 -Z 를 본다 — 돌려서 매장 안쪽을 보게
                    var r = q.GetComponent<MeshRenderer>();
                    r.sharedMaterial = cube.sharedMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    inner = q.transform;
                }
                if (inner != null) inner.gameObject.SetActive(innerOnly);
            }
        }

        /// <summary>
        /// 문 위에 따로 붙여 둔 간판. 건물 모델은 간판에 "DogShop" 이름까지 그려져 있어서 그때는 숨긴다 —
        /// 둘 다 두면 모델의 문 위에 간판이 덧붙어 어색했다.
        /// </summary>
        void PullSign(bool hide)
        {
            if (signParts == null) return;
            foreach (Transform s in signParts)
                if (s != null)
                    foreach (Renderer r in s.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = hide;
        }

        Renderer[] shellRenderers;
        Renderer roofProbe;

        /// <summary>
        /// 플레이어가 가게 안에 들어가면 지붕은 '그림자만' 모드가 된다(<see cref="RoofVisibility"/>) —
        /// 위에서 내려다보는 카메라가 안을 보게. 건물 모델도 같은 때 같이 숨는다.
        /// </summary>
        void SyncShellWithRoof()
        {
            if (shellRenderers == null || shellRenderers.Length == 0) return;
            if (roofProbe == null && shopRoof != null) roofProbe = shopRoof.GetComponentInChildren<Renderer>(true);
            if (roofProbe == null) return;

            var mode = roofProbe.shadowCastingMode;
            if (shellRenderers[0] != null && shellRenderers[0].shadowCastingMode == mode) return;
            foreach (Renderer r in shellRenderers) if (r != null) r.shadowCastingMode = mode;
        }

        /// <summary>뒤 별채(창고·옆방) 바깥면. 건물 모델은 판매장(z 0~6)만 감싼다.</summary>
        void AnnexCladding(Material wall, float tile, float left, float right)
        {
            if (right + 0.2f < AnnexRight) PanelZ("Right_AnnexFront", right + 0.25f, AnnexRight, AnnexFront, wall, tile, -1);
            SidePanelX("Right_Annex", AnnexRight, AnnexFront, AnnexBack, wall, tile, +1);
            if (left - 0.2f < AnnexLeft) PanelZ("Left_ShopBack", left - 0.25f, AnnexLeft, AnnexFront + 0.2f, wall, tile, +1);
            SidePanelX("Left_Annex", AnnexLeft, AnnexFront + 0.2f, AnnexBack, wall, tile, -1);
            PanelZ("Back", AnnexLeft, AnnexRight, AnnexBack, wall, tile, +1);
        }

        /// <summary>건물 모델 벽이 가게 벽 바로 바깥에 오게 하는 여유.</summary>
        const float ShellGap = 0.06f;

        /// <summary>
        /// 건물 모델을 판매장에 맞춰 씌운다.
        ///
        /// 경계 상자로 맞추면 안 된다 — 차양·간판이 앞으로 튀어나와 있어서, 상자 앞면을 벽에 맞추면
        /// 모델의 진짜 벽은 가게 벽 안쪽으로 들어가 하얀 원래 벽이 드러난다. 그래서 모델에 광선을 쏘아
        /// <b>앞·뒤·좌·우 벽면</b>을 찾고, 그 벽면이 가게 벽 바로 바깥에 오도록 늘이고 옮긴다.
        ///
        /// 안에서는 안 보인다 — 모델 면은 바깥을 향해서 안쪽에서는 그려지지 않는다(뒷면 컬링).
        /// 문 자리는 삼각형을 지워 구멍을 낸다. 그 뒤에 진짜 문틀과 문이 있다.
        /// </summary>
        void FitShell(GameObject prefab, float left, float right)
        {
            GameObject shell = Instantiate(prefab, root);
            shell.name = "Shell";
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;   // VARCO 모델은 앞이 -Z
            shell.transform.localScale = Vector3.one;

            var colliders = new List<MeshCollider>();
            foreach (MeshFilter mf in shell.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name == SignDecal) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                colliders.Add(mc);
            }
            Physics.SyncTransforms();

            Bounds b = new Bounds(shell.transform.position, Vector3.zero);
            foreach (Renderer r in shell.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);

            // 벽면 찾기 — 가운데(문)를 피해 양옆, 진열창 아래 벽 높이에서 쏜다.
            // 허리 높이에서 쏘면 진열창 구멍으로 들어가 안쪽 진열장 뒤를 맞혔다(앞면이 4m 안쪽으로 잡혔다)
            float y = b.min.y + b.size.y * 0.08f;
            float front = WallPlane(colliders, b, y, Vector3.forward);
            float back = WallPlane(colliders, b, y, Vector3.back);
            float wallL = WallPlane(colliders, b, y, Vector3.right);
            float wallR = WallPlane(colliders, b, y, Vector3.left);

            float sx = (right - left + ShellGap * 2f + 0.4f) / Mathf.Max(0.1f, wallR - wallL);
            float sz = (AnnexFront + 0.15f - (FrontFace - ShellGap)) / Mathf.Max(0.1f, back - front);
            float sy = (sx + sz) * 0.5f;
            shell.transform.localScale = new Vector3(sx, sy, sz);
            shell.transform.localPosition = new Vector3(
                left - 0.2f - ShellGap - wallL * sx,
                -b.min.y * sy,
                FrontFace - ShellGap - front * sz);
            Physics.SyncTransforms();

            Rect opening = ModelDoorOpening(colliders);
            foreach (MeshCollider mc in colliders) Kill(mc);

            CutShell(shell, left, right, opening);
            FitDoor(opening);
            DoorSurround(opening);
            HangDoorLeaves(builtTier);

            shellRenderers = shell.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in shellRenderers)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        [Header("정문 둘레 — 단계별 문틀 색(건물 모델의 문 구멍이 진짜 문보다 커서 생기는 틈을 메운다)")]
        [SerializeField] Material[] doorFrames = new Material[3];

        /// <summary>
        /// 건물 모델에 뚫린 문 구멍의 크기(월드 x·y). 모델 문은 진짜 문(x 3~5, 높이 2.46)보다 넓고 높아서,
        /// 그 틈으로 가게 안 천장과 잘린 모서리가 보였다. 문 양옆·위를 앞에서 쏘아 벽(또는 문틀)에 처음 닿는 곳까지를 구멍으로 본다.
        /// 문 가운데 기둥(두 짝 사이)은 피해서 쏜다.
        ///
        /// 모델 문은 두 가지다 — 뚫린 구멍(광선이 안쪽 깊이 들어간다)과 막힌 판(벽보다 몇 cm 앞에 선 문짝).
        /// 그래서 "막혔나"가 아니라 <b>문 가운데와 깊이가 다른가</b>로 가장자리를 찾는다. 판 문이면 문틀(더 튀어나옴)이나
        /// 벽(살짝 들어감)에서 깊이가 바뀐다. 막힌 판을 벽으로 봤더니 진짜 문 위로 모델 문 윗도리가 남았다.
        /// </summary>
        Rect ModelDoorOpening(List<MeshCollider> colliders)
        {
            float wall = FrontFace - ShellGap;
            float Depth(float x, float y)
            {
                var ray = new Ray(new Vector3(x, y, wall - 5f), Vector3.forward);
                float best = float.MaxValue;
                foreach (MeshCollider mc in colliders)
                    if (mc.Raycast(ray, out RaycastHit hit, 20f) && hit.distance < best) best = hit.distance;
                return best < float.MaxValue ? wall - 5f + best : float.MaxValue;
            }

            // 문 가운데 깊이(구멍이면 아주 깊거나 안 맞는다)
            var probes = new List<float>();
            foreach (float px in new[] { ShopSpace.CenterX - 0.4f, ShopSpace.CenterX + 0.4f })
                foreach (float py in new[] { 0.9f, 1.4f })
                    probes.Add(Depth(px, py));
            probes.Sort();
            float door = probes[probes.Count / 2];
            bool Edge(float x, float y)
            {
                float d = Depth(x, y);
                if (door > wall + 0.35f) return d < wall + 0.35f;   // 뚫린 구멍: 벽이나 문틀에 닿으면 가장자리
                return Mathf.Abs(d - door) > 0.05f;                  // 막힌 판: 깊이가 바뀌면 가장자리
            }

            float l = DoorLeft, r = DoorRight, top = DoorTop;
            foreach (float y in new[] { 0.6f, 1.2f, 2.0f })
            {
                float x = ShopSpace.CenterX - 0.3f;
                while (x > DoorLeft - 1.2f && !Edge(x, y)) x -= 0.03f;
                l = Mathf.Min(l, x);
                x = ShopSpace.CenterX + 0.3f;
                while (x < DoorRight + 1.2f && !Edge(x, y)) x += 0.03f;
                r = Mathf.Max(r, x);
            }
            foreach (float x in new[] { ShopSpace.CenterX - 0.4f, ShopSpace.CenterX + 0.4f })
            {
                float y = 1.4f;
                while (y < DoorTop + 2f && !Edge(x, y)) y += 0.04f;
                top = Mathf.Max(top, y);
            }
            return Rect.MinMaxRect(l, 0f, r, top);
        }

        /// <summary>
        /// 진짜 문을 모델 문 구멍 크기에 맞춘다 — 경첩을 구멍 양 끝으로 옮기고 문짝(Panel) 폭·높이를 늘린다.
        /// 예전엔 진짜 문(2m×2.2m)을 두고 남는 틈을 채광창으로 메웠는데, 모델 문이 크면 "문 위에 문"처럼 보였다.
        /// 벽 사이 통로(x 3~5)와 NavMesh 는 그대로라 손님·강아지 동선은 바뀌지 않는다. 문짝 충돌체만 넓어진다.
        /// </summary>
        void FitDoor(Rect opening)
        {
            if (frontDoor == null || frontDoor.LeafCount < 2) return;
            RememberDoor();
            float l = Mathf.Min(opening.xMin, DoorLeft), r = Mathf.Max(opening.xMax, DoorRight);
            float top = Mathf.Clamp(opening.yMax, LeafTop, MaxLeafTop);
            float w = (r - l) * 0.5f, h = top - LeafBottom;

            for (int i = 0; i < frontDoor.LeafCount; i++)
            {
                Transform hinge = frontDoor.HingeOf(i);
                bool rightSide = hinge.localPosition.x > ShopSpace.CenterX;
                hinge.localPosition = new Vector3(rightSide ? r : l, LeafBottom + h * 0.5f, hinge.localPosition.z);
                Transform panel = PanelOf(hinge);
                if (panel == null) continue;
                panel.localPosition = new Vector3(w * 0.5f, 0f, panel.localPosition.z);
                panel.localScale = new Vector3(w, h, panel.localScale.z);
            }
            frontDoor.Remeasure();
        }

        /// <summary>문짝이 이보다 높으면 문 위 벽(높이 3m)에 닿는다.</summary>
        const float MaxLeafTop = 2.9f;
        const float LeafBottom = 0.03f;

        Vector3[] doorHingeHome, doorPanelHome, doorPanelScaleHome;

        /// <summary>처음 맞추기 전 문 자리를 적어 둔다 — 건물 모델이 없는 단계로 돌아가면 되돌린다.</summary>
        void RememberDoor()
        {
            if (doorHingeHome != null) return;
            int n = frontDoor.LeafCount;
            doorHingeHome = new Vector3[n]; doorPanelHome = new Vector3[n]; doorPanelScaleHome = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Transform hinge = frontDoor.HingeOf(i), panel = PanelOf(hinge);
                doorHingeHome[i] = hinge.localPosition;
                if (panel != null) { doorPanelHome[i] = panel.localPosition; doorPanelScaleHome[i] = panel.localScale; }
            }
        }

        void RestoreDoor()
        {
            if (doorHingeHome == null || frontDoor == null) return;
            for (int i = 0; i < frontDoor.LeafCount; i++)
            {
                Transform hinge = frontDoor.HingeOf(i), panel = PanelOf(hinge);
                hinge.localPosition = doorHingeHome[i];
                if (panel != null) { panel.localPosition = doorPanelHome[i]; panel.localScale = doorPanelScaleHome[i]; }
            }
            frontDoor.Remeasure();
        }

        Transform PanelOf(Transform hinge)
        {
            foreach (Renderer r in hinge.GetComponentsInChildren<Renderer>(true))
                if (System.Array.IndexOf(doorVisuals, r) >= 0) return r.transform;
            return null;
        }

        /// <summary>
        /// 문 구멍 둘레의 잘린 모서리를 가는 문틀로 덮는다(양옆 기둥 + 위 상인방). 문짝이 구멍을 꽉 채우므로 틀은 10cm 남짓이다.
        /// </summary>
        void DoorSurround(Rect opening)
        {
            Material m = builtTier < doorFrames.Length ? doorFrames[builtTier] : null;
            if (m == null) return;
            // outside 는 잘린 모서리(구멍 둘레 패드 0.04 + 들쭉날쭉한 삼각형)를 넉넉히 덮을 만큼
            const float outside = 0.24f, inside = 0.04f;
            float l = Mathf.Min(opening.xMin, DoorLeft), r = Mathf.Max(opening.xMax, DoorRight);
            float top = Mathf.Clamp(opening.yMax, LeafTop, MaxLeafTop);
            float z0 = FrontFace - ShellGap - 0.1f, z1 = -0.12f;
            Box("DoorJamb_L", new Vector3(l - outside, 0f, z0), new Vector3(l + inside, top + outside, z1), m);
            Box("DoorJamb_R", new Vector3(r - inside, 0f, z0), new Vector3(r + outside, top + outside, z1), m);
            Box("DoorHeader", new Vector3(l + inside, top - inside, z0), new Vector3(r - inside, top + outside, z1), m);
            // 모델 구멍이 문짝 최고 높이보다 높으면 그 위는 벽색으로 막는다
            if (opening.yMax > top + outside)
                Box("DoorFill", new Vector3(l - outside, top + outside, z0 + 0.04f), new Vector3(r + outside, opening.yMax + outside, z1), m);
        }

        /// <summary>
        /// 경계 상자 밖에서 <paramref name="dir"/> 방향으로 쏘아 처음 닿는 벽면 좌표(그 축)의 중앙값.
        /// 앞/뒤는 x 를, 좌/우는 z 를 여러 줄로 바꿔 쏜다 — 창·문에 걸린 한두 줄은 중앙값이 버린다.
        /// </summary>
        static float WallPlane(List<MeshCollider> colliders, Bounds b, float y, Vector3 dir)
        {
            var hits = new List<float>();
            bool alongZ = Mathf.Abs(dir.z) > 0.5f;
            for (int i = 0; i < 9; i++)
            {
                float t = (i + 0.5f) / 9f;
                // 앞/뒤: 가운데 문 둘레(0.4~0.6)는 건너뛴다
                if (alongZ && t > 0.38f && t < 0.62f) continue;
                Vector3 origin = alongZ
                    ? new Vector3(Mathf.Lerp(b.min.x, b.max.x, Mathf.Lerp(0.1f, 0.9f, t)), y, dir.z > 0 ? b.min.z - 1f : b.max.z + 1f)
                    : new Vector3(dir.x > 0 ? b.min.x - 1f : b.max.x + 1f, y, Mathf.Lerp(b.min.z, b.max.z, Mathf.Lerp(0.2f, 0.8f, t)));
                var ray = new Ray(origin, dir);
                float best = float.MaxValue;
                foreach (MeshCollider mc in colliders)
                    if (mc.Raycast(ray, out RaycastHit hit, 100f) && hit.distance < best) best = hit.distance;
                if (best < float.MaxValue)
                {
                    Vector3 p = origin + dir * best;
                    hits.Add(alongZ ? p.z : p.x);
                }
            }
            if (hits.Count == 0) return alongZ ? (dir.z > 0 ? b.min.z : b.max.z) : (dir.x > 0 ? b.min.x : b.max.x);
            hits.Sort();
            return hits[hits.Count / 2];
        }

        /// <summary>
        /// 모델 앞면의 문 구멍(<see cref="ModelDoorOpening"/>)을 지운다. 진짜 문과의 틈은 <see cref="DoorSurround"/>가 메운다. 메시를 복사해서 지우므로 원본 에셋은 그대로다.
        /// 깊이는 모델 앞면에서 1m 까지만 — 그보다 안쪽(뒷벽 등)은 건드리지 않는다.
        /// </summary>
        /// <summary>문 구멍 둘레에서 모델 문틀을 납작하게 누르는 폭. 옆 창문·위 간판에는 닿지 않을 만큼.</summary>
        const float FrameStrip = 0.3f;

        static bool InRoom(Vector3 p, float left, float right) =>
            p.x > left + 0.01f && p.x < right - 0.01f && p.z > 0.01f && p.z < AnnexFront && p.y > 0.01f && p.y < WallHeight + 0.1f;

        void CutShell(GameObject shell, float left, float right, Rect opening)
        {
            const float pad = 0.04f;
            float zMax = FrontFace + 1.0f;
            foreach (MeshFilter mf in shell.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name == SignDecal) continue;   // 간판 글자 판은 자르지 않는다
                Mesh src = mf.sharedMesh;
                Vector3[] v = src.vertices;
                Transform t = mf.transform;
                var world = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) world[i] = t.TransformPoint(v[i]);

                Mesh copy = Instantiate(src);
                copy.hideFlags = HideFlags.DontSave;
                madeMeshes.Add(copy);

                // 문 둘레 모델 문틀은 벽보다 25~30cm 튀어나와 있어서, 문 구멍을 파면 그 잘린 끝이 들쭉날쭉 앞으로 삐져나왔다.
                // 지우면 벽에 구멍이 나므로(문틀이 곧 벽 면이다) 꼭짓점을 벽면까지 눌러 납작하게 만든다 — 색은 그림으로 남는다.
                // 그러면 진짜 문틀(DoorSurround, 벽보다 10cm 앞)이 잘린 끝을 다 덮는다.
                float wallZ = FrontFace - ShellGap;
                bool flattened = false;
                for (int i = 0; i < world.Length; i++)
                {
                    Vector3 p = world[i];
                    if (p.z >= wallZ - 0.03f || p.y > opening.yMax + FrameStrip) continue;
                    if (p.x < opening.xMin - FrameStrip || p.x > opening.xMax + FrameStrip) continue;
                    p.z = wallZ - 0.02f;
                    world[i] = p;
                    v[i] = t.InverseTransformPoint(p);
                    flattened = true;
                }
                if (flattened) { copy.vertices = v; copy.RecalculateBounds(); }

                for (int s = 0; s < src.subMeshCount; s++)
                {
                    int[] tris = src.GetTriangles(s);
                    var kept = new List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        Vector3 c = (world[tris[i]] + world[tris[i + 1]] + world[tris[i + 2]]) / 3f;
                        // 모델 문 구멍 전체(진짜 문보다 넓고 높다)를 판다 — 문 위에 모델 문 윗도리와 손잡이가 남아 "문 위의 문"처럼 보였다
                        bool inDoor = c.x > opening.xMin - pad && c.x < opening.xMax + pad && c.y < opening.yMax + pad && c.z < zMax;
                        // 판매장 안으로 파고든 것(진열창 안쪽 진열 공간 등) — 안에서 보이면 허공에 뜬 잡동사니다
                        // 꼭짓점이 하나라도 들어와 있으면 지운다 — 중심만 보면 벽 밖에 걸친 큰 삼각형이 안쪽으로 삐져나와 조각처럼 보였다
                        bool inRoom = InRoom(world[tris[i]], left, right) || InRoom(world[tris[i + 1]], left, right) || InRoom(world[tris[i + 2]], left, right);
                        if (inRoom) continue;
                        if (inDoor) continue;   // 모델의 문은 파낸다 — 그 자리에 바르코 문짝(doorLeaves)이 열고 닫힌다
                        kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                    }
                    copy.SetTriangles(kept, s);
                }
                mf.sharedMesh = copy;
            }
        }

        [Header("정문 — 단계별 바르코 문짝(한 짝). 두 짝 중 오른쪽은 좌우를 뒤집어 쓴다")]
        [SerializeField] GameObject[] doorLeaves = new GameObject[3];
        [SerializeField] SwingDoor frontDoor;

        /// <summary>원래 문틀·문짝(갈색). 건물 모델 문을 쓸 때는 그리지 않는다 — 충돌체와 여닫힘은 그대로다.</summary>
        [SerializeField] Renderer[] doorVisuals = new Renderer[0];

        readonly List<GameObject> hungLeaves = new List<GameObject>();

        void ClearLeaves()
        {
            foreach (GameObject g in hungLeaves) if (g != null) Kill(g);
            hungLeaves.Clear();
            // 에디터 미리보기가 단 문짝(목록에 없다)도 지운다
            if (frontDoor == null) return;
            for (int i = 0; i < frontDoor.LeafCount; i++)
            {
                Transform hinge = frontDoor.HingeOf(i);
                for (int c = hinge.childCount - 1; c >= 0; c--)
                    if (hinge.GetChild(c).name == "DoorLeaf") Kill(hinge.GetChild(c).gameObject);
            }
        }

        /// <summary>
        /// 에디터(실행 전)에서 조립한 것을 씬에 저장되지 않게 표시하고, 문을 게임 시작 때처럼 닫아 둔다.
        /// 문짝 크기·경첩 자리는 씬 값이 바뀌지만 게임이 시작하면 어차피 같은 값으로 다시 맞춘다.
        /// </summary>
        void MarkPreview()
        {
            if (root != null)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            foreach (GameObject g in hungLeaves)
                if (g != null) foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            foreach (Transform w in frontWalls)
            {
                Transform inner = w != null ? w.Find("InnerFace") : null;
                if (inner != null) inner.gameObject.hideFlags = HideFlags.DontSave;
            }
            if (frontDoor != null) frontDoor.ShowClosed();
        }

        void SetDoorVisuals(bool shown)
        {
            foreach (Renderer r in doorVisuals) if (r != null) r.forceRenderingOff = !shown;
        }

        /// <summary>
        /// 단계에 맞는 바르코 문짝을 두 경첩에 하나씩 단다. 모델 정면(-Z)이 거리 쪽을 보게 돌리고,
        /// x 가 큰 쪽 경첩의 짝은 좌우를 뒤집어 손잡이가 가운데로 오게 한다(모델은 손잡이가 오른쪽).
        /// 크기는 원래 문짝(Panel)에 맞춘다 — 충돌·여닫힘은 그 Panel 이 계속 맡는다.
        /// </summary>
        void HangDoorLeaves(int tier)
        {
            ClearLeaves();
            GameObject prefab = tier < doorLeaves.Length ? doorLeaves[tier] : null;
            SetDoorVisuals(prefab == null || frontDoor == null);
            if (prefab == null || frontDoor == null) return;

            float midX = 0f;
            for (int i = 0; i < frontDoor.LeafCount; i++) midX += frontDoor.HingeOf(i).position.x / frontDoor.LeafCount;

            for (int i = 0; i < frontDoor.LeafCount; i++)
            {
                Transform hinge = frontDoor.HingeOf(i);
                Renderer panel = null;
                foreach (Renderer r in hinge.GetComponentsInChildren<Renderer>(true))
                    if (System.Array.IndexOf(doorVisuals, r) >= 0) { panel = r; break; }
                if (panel == null) continue;
                Bounds target = LocalBounds(hinge, panel.GetComponentsInChildren<MeshFilter>(true));

                bool mirror = hinge.position.x > midX;
                GameObject leaf = Instantiate(prefab, hinge, false);
                leaf.name = "DoorLeaf";
                leaf.transform.localPosition = Vector3.zero;
                leaf.transform.localRotation = Quaternion.Euler(0f, -frontDoor.ClosedYawOf(i), 0f);
                leaf.transform.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
                foreach (Collider c in leaf.GetComponentsInChildren<Collider>(true)) Kill(c);
                Vector3 front = leaf.transform.TransformDirection(Vector3.back);
                foreach (MeshFilter mf in leaf.GetComponentsInChildren<MeshFilter>(true))
                    mf.sharedMesh = TwoFaced(mf.sharedMesh, mf.transform.InverseTransformDirection(front).normalized);

                // 경첩 축과 모델 축은 Y 둘레 0°/180° 만큼만 다르다 — 크기는 축별로 바로 곱하면 된다
                MeshFilter[] mfs = leaf.GetComponentsInChildren<MeshFilter>(true);
                Bounds b = LocalBounds(hinge, mfs);
                float sx = target.size.x / Mathf.Max(b.size.x, 0.001f);
                float sy = target.size.y / Mathf.Max(b.size.y, 0.001f);
                float sz = Mathf.Min((sx + sy) * 0.5f, 0.1f / Mathf.Max(b.size.z, 0.001f));   // 두께는 10cm 를 넘기지 않는다
                Vector3 s = leaf.transform.localScale;
                leaf.transform.localScale = new Vector3(s.x * sx, s.y * sy, s.z * sz);

                b = LocalBounds(hinge, mfs);
                leaf.transform.localPosition += target.center - b.center;
                hungLeaves.Add(leaf);
            }
        }

        static readonly Dictionary<Mesh, Mesh> twoFaced = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// 문짝 메시의 앞쪽 절반만 남기고, 그걸 두께 가운데 면에 비춰 뒤쪽 절반으로 붙인다.
        /// 바르코는 사진 한 장으로 앞면만 보고 뒷면은 지어내는데, 문짝 뒷면은 유리 없는 어두운 판이 되어
        /// 가게 안에서 보면 막힌 벽처럼 보였다. 이렇게 하면 안팎 어디서 봐도 같은 유리문이다.
        /// <paramref name="front"/>는 메시 공간에서 모델 정면 쪽 방향.
        /// </summary>
        static Mesh TwoFaced(Mesh src, Vector3 front)
        {
            if (src == null || !src.isReadable) return src;
            if (twoFaced.TryGetValue(src, out Mesh cached) && cached != null) return cached;

            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 p in v) { float s = Vector3.Dot(p, front); lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); }
            float mid = (lo + hi) * 0.5f;

            int count = v.Length;
            var verts = new Vector3[count * 2];
            var normals = new Vector3[n.Length == count ? count * 2 : 0];
            for (int i = 0; i < count; i++)
            {
                verts[i] = v[i];
                verts[count + i] = v[i] - 2f * (Vector3.Dot(v[i], front) - mid) * front;
                if (normals.Length > 0)
                {
                    normals[i] = n[i];
                    normals[count + i] = n[i] - 2f * Vector3.Dot(n[i], front) * front;
                }
            }
            Vector2[] uv = src.uv;
            var uvs = new Vector2[uv.Length == count ? count * 2 : 0];
            for (int i = 0; i < uvs.Length / 2; i++) { uvs[i] = uv[i]; uvs[count + i] = uv[i]; }

            var tris = new List<int>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] t = src.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    if (Vector3.Dot(c, front) < mid) continue;   // 뒤쪽 절반(지어낸 면)은 버린다
                    tris.Add(t[i]); tris.Add(t[i + 1]); tris.Add(t[i + 2]);
                    // 비춘 쪽은 감김 방향을 뒤집어야 바깥을 본다
                    tris.Add(count + t[i]); tris.Add(count + t[i + 2]); tris.Add(count + t[i + 1]);
                }
            }

            var mesh = new Mesh { name = src.name + "_TwoFaced", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            if (normals.Length > 0) mesh.normals = normals;
            if (uvs.Length > 0) mesh.uv = uvs;
            mesh.SetTriangles(tris, 0);
            if (normals.Length == 0) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            twoFaced[src] = mesh;
            return mesh;
        }

        static Bounds LocalBounds(Transform space, MeshFilter[] filters)
        {
            bool any = false;
            var result = new Bounds();
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }


        // ---- 조각 ----

        /// <summary>앞면 외장 판(x0~x1, y0~y1). 벽 바깥면에 얇게 붙인다.</summary>
        void Panel(string name, float x0, float x1, float y0, float y1, Material m, float tile, float depth = Skin)
        {
            Transform t = Box(name, new Vector3(x0, y0, FrontFace - depth), new Vector3(x1, y1, FrontFace), m);
            Tile(t, x1 - x0, y1 - y0, tile);
        }

        /// <summary>x 가 일정한 옆면(z0~z1). outward 는 바깥쪽(+1 이면 +x).</summary>
        void SidePanelX(string name, float x, float z0, float z1, Material m, float tile, int outward)
        {
            float a = outward > 0 ? x : x - Skin, b = outward > 0 ? x + Skin : x;
            Transform t = Box(name, new Vector3(a, 0f, z0), new Vector3(b, WallHeight, z1), m);
            Tile(t, z1 - z0, WallHeight, tile);
        }

        /// <summary>z 가 일정한 면(x0~x1). outward 는 바깥쪽(+1 이면 +z).</summary>
        void PanelZ(string name, float x0, float x1, float z, Material m, float tile, int outward)
        {
            float a = outward > 0 ? z : z - Skin, b = outward > 0 ? z + Skin : z;
            Transform t = Box(name, new Vector3(x0, 0f, a), new Vector3(x1, WallHeight, b), m);
            Tile(t, x1 - x0, WallHeight, tile);
        }

        /// <summary>창. <paramref name="display"/>가 있으면 쇼윈도(안쪽 진열 그림), 없으면 위층 유리창.</summary>
        void Window(float x0, float x1, float y0, float y1, Material display)
        {
            if (display != null) Picture("Display", x0, x1, y0, y1, display);
            else Box("Glass", new Vector3(x0, y0, FrontFace - Skin - 0.02f), new Vector3(x1, y1, FrontFace - Skin), upperGlass ??= Tint(new Color(0.42f, 0.62f, 0.72f), 0.9f));
            Frame(x0, x1, y0, y1, 0.09f);
        }

        /// <summary>
        /// 그림 한 장(쇼윈도 안쪽·간판). 정육면체 앞면(-Z)은 UV 가 뒤집혀 그림이 거꾸로 나왔다 —
        /// 앞을 보는 쿼드에 그린다(쿼드는 -Z 쪽에서 똑바로 보인다).
        /// </summary>
        void Picture(string name, float x0, float x1, float y0, float y1, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Strip(go);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, FrontFace - Skin - 0.01f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(x1 - x0, y1 - y0, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Material upperGlass;

        void Frame(float x0, float x1, float y0, float y1, float f)
        {
            float z0 = FrontFace - Skin - 0.09f;
            Box("Frame_B", new Vector3(x0 - f, y0 - f, z0), new Vector3(x1 + f, y0, FrontFace), trim);
            Box("Frame_T", new Vector3(x0 - f, y1, z0), new Vector3(x1 + f, y1 + f, FrontFace), trim);
            Box("Frame_L", new Vector3(x0 - f, y0, z0), new Vector3(x0, y1, FrontFace), trim);
            Box("Frame_R", new Vector3(x1, y0, z0), new Vector3(x1 + f, y1, FrontFace), trim);
        }

        /// <summary>창 위 차양. 벽에서 비스듬히 내려오는 천과 앞 술 장식.</summary>
        void Awning(float x0, float x1, float y, float reach)
        {
            float w = x1 - x0;
            const float slope = 24f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Awning";
            Strip(go);
            go.transform.SetParent(root, false);
            float len = reach / Mathf.Cos(slope * Mathf.Deg2Rad);
            go.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
            go.transform.localScale = new Vector3(w, 0.04f, len);
            Vector3 hinge = new Vector3((x0 + x1) * 0.5f, y, FrontFace - Skin);
            go.transform.localPosition = hinge + go.transform.localRotation * new Vector3(0f, 0f, -len * 0.5f);
            Paint(go, awning);
            Tile(go.transform, w, 1f, 0.9f);

            Vector3 lip = hinge + go.transform.localRotation * new Vector3(0f, 0f, -len);
            Transform v = Box("Awning_Valance", new Vector3(x0, lip.y - 0.24f, lip.z - 0.02f), new Vector3(x1, lip.y, lip.z + 0.02f), awning);
            Tile(v, w, 1f, 0.9f);
        }

        void FlowerBox(float x0, float x1, float sillY)
        {
            float z1 = FrontFace - Skin;
            Box("FlowerBox", new Vector3(x0 + 0.05f, sillY - 0.32f, z1 - 0.3f), new Vector3(x1 - 0.05f, sillY - 0.1f, z1), wood);
            Box("Leaves", new Vector3(x0 + 0.1f, sillY - 0.12f, z1 - 0.26f), new Vector3(x1 - 0.1f, sillY + 0.02f, z1 - 0.03f), leaves ??= Tint(new Color(0.42f, 0.68f, 0.38f), 0.2f));
            int n = Mathf.Max(3, Mathf.RoundToInt((x1 - x0) / 0.35f));
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(x0 + 0.2f, x1 - 0.2f, i / (float)(n - 1));
                Material m = i % 2 == 0 ? (petalA ??= Tint(new Color(0.97f, 0.6f, 0.72f), 0.2f)) : (petalB ??= Tint(new Color(1f, 0.88f, 0.4f), 0.2f));
                Box("Flower", new Vector3(x - 0.07f, sillY, z1 - 0.2f), new Vector3(x + 0.07f, sillY + 0.12f, z1 - 0.08f), m);
            }
        }

        Material leaves, petalA, petalB;

        Material Tint(Color c, float smooth)
        {
            var m = new Material(trim) { name = "Ext_Tint" };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        void Lamp(float x)
        {
            Box("LampArm", new Vector3(x - 0.04f, 2.1f, FrontFace - 0.22f), new Vector3(x + 0.04f, 2.16f, FrontFace), wood);
            Box("Lamp", new Vector3(x - 0.1f, 1.9f, FrontFace - 0.32f), new Vector3(x + 0.1f, 2.14f, FrontFace - 0.14f), lamp);
        }

        void Mascot(float x)
        {
            GameObject m = Instantiate(mascot, root);
            m.name = "Mascot";
            m.transform.localPosition = new Vector3(x, 0f, FrontFace - 0.36f);
            m.transform.localScale = Vector3.one * 0.8f;   // 배송 자리 첫 줄 짐(z -0.97~)에 닿지 않게
            m.transform.localRotation = Quaternion.identity;   // VARCO 모델은 앞이 -Z(길 쪽)
            foreach (Collider c in m.GetComponentsInChildren<Collider>()) Kill(c);
        }

        /// <summary>두 모서리(min, max)로 상자를 만든다.</summary>
        Transform Box(string name, Vector3 min, Vector3 max, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Strip(go);
            go.transform.SetParent(root, false);
            go.transform.localPosition = (min + max) * 0.5f;
            go.transform.localScale = new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z));
            Paint(go, m);
            return go.transform;
        }

        /// <summary>에디터(실행 전)에서는 Destroy 가 안 되므로 바로 지운다.</summary>
        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static void Strip(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Kill(c); }
        }

        static void Paint(GameObject go, Material m)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>판 크기에 맞춰 텍스처를 반복한다(정육면체 UV 는 면마다 0~1 이라).</summary>
        void Tile(Transform t, float u, float v, float tile)
        {
            var r = t.GetComponent<MeshRenderer>();
            r.GetPropertyBlock(block);
            block.SetVector("_BaseMap_ST", new Vector4(u / tile, v / tile, 0f, 0f));
            r.SetPropertyBlock(block);
            block.Clear();
        }
    }
}
