using System.Collections.Generic;
using UnityEngine;

namespace DogShop.Player
{
    /// <summary>
    /// 플레이어가 건물(판매장·창고·방) 안에 있으면 <b>카메라와 플레이어 사이의 벽을 숨긴다</b>(그림자만 남김).
    /// 카메라도 그 벽에 걸려 앞으로 당겨지지 않는다(<see cref="ThirdPersonCamera"/> 가 <see cref="Ignores"/> 로 묻는다).
    ///
    /// 예전엔 계산대 쪽으로 가면 카메라가 벽에 부딪혀 플레이어 코앞까지 당겨졌고, 벽 면이 화면 절반을 덮어
    /// 매번 시점을 돌려야 했다. 이제 안에 있는 동안은 벽 너머에서 내려다보듯 안쪽이 다 보인다.
    /// 밖에 나가면 벽은 그대로 서 있고 카메라도 예전처럼 벽에 걸린다.
    ///
    /// 벽은 이름에 "Wall" 이 든 렌더러(진열 선반 WallShelf 는 빼고)다. 가게가 넓어지면 벽이 늘어나므로 가끔 다시 모은다.
    ///
    /// 천장 들보(Beam_*)와 거기 매달린 펜던트 조명(CeilingLights 아래)도 안에 있는 동안은 통째로 숨긴다 —
    /// 내려다보는 시점에서 가로로 긴 들보가 화면을 가로질러 시야를 잡아먹었다. 조명 빛(Light)은 그대로 켜 둔다.
    /// </summary>
    public class WallCutaway : MonoBehaviour
    {
        /// <summary>건물 안 판정 여유(m). 문턱에 서 있을 때 깜빡이지 않게.</summary>
        const float InsideMargin = 0.1f;
        /// <summary>카메라에서 이만큼 안의 벽도 숨긴다 — 카메라가 벽에 반쯤 박힌 채 면이 화면을 덮지 않게.</summary>
        const float NearCamera = 0.8f;
        const float RefreshEvery = 2f;

        static readonly HashSet<Collider> hiddenColliders = new HashSet<Collider>();

        /// <summary>카메라 충돌에서 빼야 하는 벽인가(지금 숨긴 벽).</summary>
        public static bool Ignores(Collider c) => c != null && hiddenColliders.Contains(c);

        static readonly RaycastHit[] buffer = new RaycastHit[32];

        /// <summary>
        /// 숨긴 벽은 통과하는 Raycast. 마우스 조준(계산대·강아지·상자 고르기)과 가구 배치가 쓴다 —
        /// 벽이 안 보이는데 클릭이 벽에 막히면 아무것도 못 누른다.
        /// </summary>
        public static bool Raycast(Ray ray, out RaycastHit hit, float range, int mask, QueryTriggerInteraction triggers)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(ray, buffer, range, mask, triggers);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (Ignores(buffer[i].collider) || buffer[i].distance >= best) continue;
                best = buffer[i].distance;
                hit = buffer[i];
            }
            return best < float.MaxValue;
        }

        readonly List<Renderer> walls = new List<Renderer>();
        readonly List<Renderer> ceiling = new List<Renderer>();
        readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
        Bounds building;
        float refreshTimer;
        Transform player;
        Camera cam;

        void Collect()
        {
            walls.Clear();
            ceiling.Clear();
            bool any = false;
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                string n = r.gameObject.name;
                if (IsCeilingFixture(r.transform)) { ceiling.Add(r); continue; }
                if (n.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Shelf", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (r.bounds.size.y < 2f) continue;   // 낮은 칸막이·장식은 그대로
                walls.Add(r);
                if (!any) { building = r.bounds; any = true; } else building.Encapsulate(r.bounds);
            }
        }

        /// <summary>천장 들보 또는 펜던트 조명의 일부인가.</summary>
        static bool IsCeilingFixture(Transform t)
        {
            if (t.name.StartsWith("Beam_")) return true;
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == "CeilingLights") return true;
            return false;
        }

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (player == null)
            {
                PlayerCarry carry = FindAnyObjectByType<PlayerCarry>();
                if (carry != null) player = carry.transform;
            }
            if (cam == null || player == null) return;

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f) { refreshTimer = RefreshEvery; Collect(); }

            Vector3 p = player.position;
            bool inside = walls.Count > 0
                && p.x > building.min.x + InsideMargin && p.x < building.max.x - InsideMargin
                && p.z > building.min.z + InsideMargin && p.z < building.max.z - InsideMargin;

            Vector3 eye = cam.transform.position;
            Vector3 target = p + Vector3.up * 1.2f;
            Vector3 seg = target - eye;
            float len = seg.magnitude;

            foreach (Renderer r in walls)
            {
                if (r == null) continue;
                bool hide = false;
                if (inside && len > 1e-3f)
                {
                    Bounds b = r.bounds;
                    b.Expand(0.3f);
                    float hit;
                    hide = (b.IntersectRay(new Ray(eye, seg / len), out hit) && hit < len)
                        || r.bounds.SqrDistance(eye) < NearCamera * NearCamera;
                }
                Set(r, hide);
            }

            // 들보·조명은 원래 그림자를 안 드리운다 — 벽처럼 ShadowsOnly 로 두면 갓이 제 불빛을 막는다. 그냥 끈다
            foreach (Renderer r in ceiling)
                if (r != null && r.enabled == inside) r.enabled = !inside;
        }

        void Set(Renderer r, bool hide)
        {
            if (hide == hidden.Contains(r)) return;
            Collider c = r.GetComponent<Collider>();
            if (hide)
            {
                hidden.Add(r);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;   // 빛은 그대로 막는다
                if (c != null) hiddenColliders.Add(c);
            }
            else
            {
                hidden.Remove(r);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (c != null) hiddenColliders.Remove(c);
            }
        }

        void OnDisable()
        {
            foreach (Renderer r in hidden) if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            hidden.Clear();
            hiddenColliders.Clear();
            foreach (Renderer r in ceiling) if (r != null) r.enabled = true;
        }
    }
}
