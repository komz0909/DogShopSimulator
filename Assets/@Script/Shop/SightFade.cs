using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// <b>카메라와 플레이어 사이를 가리면</b> 반투명해진다. 펜던트 조명·간판·문짝에 붙는다.
    ///
    /// 3인칭 카메라는 3m 높이에서 내려다본다. 램프는 2.7~3.0m 에 매달려 카메라 앞을 덮고,
    /// 게임을 시작하면 플레이어가 문 앞에 서고 카메라가 그 뒤 앞마당에 서서
    /// 간판이 첫 화면 한가운데를 통째로 가렸다.
    ///
    /// 판정은 카메라→플레이어 <b>시야선이 물체의 경계 상자를 지나는가</b>다.
    /// 카메라와의 거리로 재면 옆에 있어 안 가리는 것까지 흐려지고, 중심과 시야선의 거리로 재면
    /// 1.5m 짜리 간판은 가장자리가 걸려도 못 잡는다.
    ///
    /// 완전히 보일 때는 불투명 머티리얼로 되돌린다. 반투명을 계속 쓰면 정렬 순서 때문에
    /// 뒤에 있는 물체가 앞에 그려지는 일이 생긴다.
    /// </summary>
    public class SightFade : MonoBehaviour
    {
        [SerializeField] Material opaque;
        [SerializeField] Material faded;

        [SerializeField, Range(0f, 1f)] float fadedAlpha = 0.22f;

        /// <summary>경계 상자를 이만큼 키워서 본다. 시야선이 아슬아슬하게 비껴가도 가리는 것으로.</summary>
        [SerializeField] float margin = 0.15f;

        /// <summary>초당 알파 변화량. 너무 빠르면 깜빡이고 느리면 늦게 비킨다.</summary>
        [SerializeField] float speed = 5f;

        /// <summary>
        /// 0보다 크면 카메라가 이만큼 안에 들어왔을 때도 비킨다. 들보(1.4m)와 램프(2m)가 쓴다 —
        /// 둘 다 플레이어 위로 지나가 시야선에는 안 걸리지만, 카메라 바로 앞에 오면
        /// 들보는 화면 위쪽을 두꺼운 띠로 가로지르고 램프 갓은 화면 구석을 크게 덮는다.
        ///
        /// 들보는 fadedAlpha 0 으로 아예 숨긴다. 천장을 3m 로 올린 뒤 들보가 카메라 높이와
        /// 같아져, 반투명으로 남기면 비스듬히 길게 늘어진 유령 같은 쐐기가 화면에 걸렸다.
        ///
        /// 램프의 반투명 머티리얼은 Unlit 이다. Lit 으로 비치게 하면 갓 안쪽 면이 보이는데,
        /// 그 면은 10cm 아래 매달린 자기 조명에 HDR 로 밝혀져 22%만 섞여도 하얗게 날아갔다.
        /// </summary>
        [SerializeField] float nearCamera;

        /// <summary>플레이어의 어디를 보는가 — 머리쯤.</summary>
        const float PlayerEye = 1.3f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        static Transform player;

        Renderer view;
        MaterialPropertyBlock block;
        float alpha = 1f;

        void Awake()
        {
            view = GetComponentInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (view == null || cam == null || opaque == null || faded == null) return;

            if (player == null)
            {
                Player.PlayerCarry carry = FindAnyObjectByType<Player.PlayerCarry>();
                if (carry != null) player = carry.transform;
            }
            if (player == null) return;

            Vector3 eye = cam.transform.position;
            bool blocking = Blocks(eye, player.position + Vector3.up * PlayerEye)
                         || (nearCamera > 0f && view.bounds.SqrDistance(eye) < nearCamera * nearCamera);
            alpha = Mathf.MoveTowards(alpha, blocking ? fadedAlpha : 1f, speed * Time.deltaTime);

            if (alpha >= 0.999f)
            {
                if (view.sharedMaterial != opaque) view.sharedMaterial = opaque;
                view.SetPropertyBlock(null);
                return;
            }

            if (view.sharedMaterial != faded) view.sharedMaterial = faded;
            block.SetColor(BaseColor, new Color(1f, 1f, 1f, alpha));
            view.SetPropertyBlock(block);
        }

        /// <summary>카메라에서 눈까지 가는 선이, 눈에 닿기 <b>전에</b> 이 물체에 걸리는가.</summary>
        bool Blocks(Vector3 from, Vector3 to)
        {
            Bounds b = view.bounds;
            b.Expand(margin * 2f);

            Vector3 seg = to - from;
            float length = seg.magnitude;
            if (length < 1e-3f) return false;

            float hit;
            return b.IntersectRay(new Ray(from, seg / length), out hit) && hit < length;
        }
    }
}
