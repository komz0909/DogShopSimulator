using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 지붕과 천장. <b>플레이어가 건물 안에 있으면 숨는다.</b>
    ///
    /// 3인칭 카메라는 기본 피치 20°·거리 4.5m 에서 높이 3.0m 에 선다 — 벽(2.5m)보다 위다.
    /// 지붕을 그냥 덮으면 플레이 중에는 매장이 통째로 가려져 아무것도 안 보인다.
    /// 그렇다고 지붕을 안 만들면 밖에서 본 가게가 뚜껑 없는 상자가 된다.
    ///
    /// 그래서 <b>렌더러만</b> 껐다 켠다. 콜라이더는 애초에 달지 않는다 —
    /// 달면 카메라의 SphereCast 가 지붕에 걸려 시점이 바닥으로 끌려 내려오고,
    /// NavMesh 도 깎인다.
    /// </summary>
    public class RoofVisibility : MonoBehaviour
    {
        /// <summary>건물 바닥 범위(앞마당 제외). 이 안에 서 있으면 실내로 본다.</summary>
        [SerializeField] Vector2 indoorMin = new Vector2(-0.3f, -0.2f);
        [SerializeField] Vector2 indoorMax = new Vector2(12.4f, 10.2f);

        /// <summary>경계에서 오락가락하지 않게 두는 여유.</summary>
        [SerializeField] float hysteresis = 0.4f;

        Transform player;
        Renderer[] visuals;
        bool hidden;

        void Awake()
        {
            visuals = GetComponentsInChildren<Renderer>(true);

            // 콜라이더가 섞여 들어오면 카메라와 NavMesh 가 같이 망가진다. 있으면 지운다
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) Destroy(c);
        }

        void Start()
        {
            Player.PlayerCarry carry = FindAnyObjectByType<Player.PlayerCarry>();
            player = carry != null ? carry.transform : null;

            if (player == null)
            {
                GameObject found = GameObject.Find("Player");
                if (found != null) player = found.transform;
            }

            Apply(false);
        }

        void LateUpdate()
        {
            if (player == null) return;

            Vector3 p = player.position;
            float slack = hidden ? hysteresis : 0f;

            bool inside = p.x > indoorMin.x - slack && p.x < indoorMax.x + slack
                       && p.z > indoorMin.y - slack && p.z < indoorMax.y + slack;

            if (inside != hidden) Apply(inside);
        }

        void Apply(bool hide)
        {
            hidden = hide;
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null) visuals[i].enabled = !hide;
        }
    }
}
