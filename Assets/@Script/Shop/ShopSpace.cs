using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    /// <summary>
    /// 매장 공간을 다시 굽는 곳. <b>NavMesh를 건드리는 유일한 자리다.</b>
    ///
    /// 가구는 <see cref="NavMeshObstacle"/> 카빙으로 실시간에 길을 막으므로 다시 구울 필요가 없다.
    /// 다시 구워야 하는 것은 <b>구울 때만 반영되는 것</b> 둘뿐이다 —
    /// 바닥·벽을 옮기거나 키웠을 때, 그리고 <see cref="NavMeshModifierVolume"/>(직원 구역)이
    /// 움직였을 때. 계산대를 옮기면 그 뒤 직원 주머니가 따라 움직이므로 후자에 해당한다.
    ///
    /// 굽는 동안 손님·반려견이 타고 있던 길은 끊긴다. 그래서 굽기 전에 목적지를 적어 두고,
    /// 굽고 나서 가장 가까운 면으로 옮긴 뒤 같은 목적지를 다시 준다 —
    /// 안 그러면 "NavMesh 위에 있지 않다" 경고가 쏟아지고 손님이 그 자리에 굳는다.
    /// </summary>
    public class ShopSpace : MonoBehaviour
    {
        public static ShopSpace Instance { get; private set; }

        [SerializeField] NavMeshSurface surface;

        [Header("판매장 — 오른쪽으로 늘어나는 부분")]
        [SerializeField] Transform floor;        // Floor
        [SerializeField] Transform rightWall;    // Wall_Right
        [SerializeField] Transform frontRight;   // Wall_FrontB — 출입문 오른쪽 앞벽

        /// <summary>
        /// 매장 천장과 지붕. 바닥만 늘이면 넓어진 쪽이 뚜껑 없이 뚫린다.
        /// 비워 두어도 동작한다 — 지붕이 없는 씬에서도 확장은 그대로 된다.
        /// </summary>
        [SerializeField] Transform ceiling;      // Ceiling_Shop
        [SerializeField] Transform roof;         // Gable_Shop

        /// <summary>
        /// 매장 천장 조명. 최대 폭(12m)까지 미리 깔아 두고, <b>지금 폭 바깥의 것은 끈다</b> —
        /// 안 끄면 아직 없는 확장 구역 자리, 즉 가게 밖 허공에서 불이 켜진다.
        /// </summary>
        [SerializeField] Transform shopLights;

        /// <summary>판매장 세로(z). 늘리지 않는다 — 앞은 앞마당, 뒤는 창고라 막혀 있다.</summary>
        const float Depth = 6f;

        /// <summary>벽 두께. 씬의 모든 벽이 같다.</summary>
        const float WallThickness = 0.2f;

        /// <summary>출입문 오른쪽 기둥이 시작하는 x. 문틈(x 3~5)은 넓혀도 그대로 둔다.</summary>
        const float DoorRightEdge = 5f;

        /// <summary>지붕이 벽 밖으로 나오는 처마 길이. 지붕을 세울 때 쓴 값과 같아야 한다.</summary>
        const float RoofOverhang = 0.35f;

        /// <summary>지금 판매장 가로 길이. 바닥에서 직접 읽는다 — 저장할 필요가 없다.</summary>
        public float Width => floor != null ? floor.localScale.x : 8f;

        /// <summary>판매장 바닥 범위(x 0~Width, z 0~6). 가구를 놓을 자리를 찾을 때 쓴다.</summary>
        public Rect FloorRect => new Rect(0f, 0f, Width, Depth);

        /// <summary>다시 굽는 동안 잃어버리지 않으려고 적어 두는 것.</summary>
        struct Traveler
        {
            public NavMeshAgent agent;
            public Vector3 destination;
            public bool hadPath;
        }

        readonly List<Traveler> travelers = new List<Traveler>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (surface == null) surface = FindFirstObjectByType<NavMeshSurface>();
        }

        void Start() => ToggleLights(Width);

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>매장 안에 드는 조명만 켠다. 조명 반경이 벽을 넘어도 되지만, 조명 자체가 벽 밖이면 끈다.</summary>
        void ToggleLights(float width)
        {
            if (shopLights == null) return;
            foreach (Transform lamp in shopLights)
                lamp.gameObject.SetActive(lamp.position.x < width);
        }

        /// <summary>
        /// 그 레벨의 넓이로 판매장을 맞춘다. 늘어난 만큼 바닥이 커지고
        /// 오른쪽 벽과 앞벽 오른쪽 기둥이 따라 밀려난다.
        ///
        /// 뒤쪽(z=6.1)은 손댈 필요가 없다 — 옆방 앞벽(Room_Wall_Front, x 8~12.4)이
        /// 이미 그 자리를 막고 있어서 매장 뒷벽 노릇을 그대로 해 준다.
        ///
        /// 줄이지는 않는다. 가구를 이미 놓아 둔 자리가 벽 바깥으로 밀려나면
        /// 되돌릴 방법이 없다 — 가게는 커지기만 한다.
        /// </summary>
        public bool ApplyWidth(float width)
        {
            if (floor == null) return false;

            width = Mathf.Clamp(width, 8f, 12f);
            if (width <= Width + 0.01f) return false;   // 이미 그만큼 넓다

            floor.localScale = new Vector3(width, floor.localScale.y, Depth);
            floor.position = new Vector3(width * 0.5f, floor.position.y, Depth * 0.5f);

            if (rightWall != null)
                rightWall.position = new Vector3(width + WallThickness * 0.5f, rightWall.position.y, rightWall.position.z);

            if (frontRight != null)
            {
                float span = width - DoorRightEdge;
                frontRight.localScale = new Vector3(span, frontRight.localScale.y, WallThickness);
                frontRight.position = new Vector3(DoorRightEdge + span * 0.5f, frontRight.position.y, frontRight.position.z);
            }

            ToggleLights(width);

            // 뚜껑도 같이 늘인다. 바닥만 넓히면 늘어난 쪽이 하늘로 뚫려 보인다
            if (ceiling != null)
            {
                ceiling.localScale = new Vector3(width, ceiling.localScale.y, ceiling.localScale.z);
                ceiling.position = new Vector3(width * 0.5f, ceiling.position.y, ceiling.position.z);
            }

            // 맞배지붕은 처마만큼 더 길다. 비율을 그대로 유지한 채 폭만 따라간다
            if (roof != null)
            {
                foreach (Transform slope in roof)
                {
                    slope.localScale = new Vector3(width + RoofOverhang * 2f, slope.localScale.y, slope.localScale.z);
                    slope.localPosition = new Vector3(width * 0.5f, slope.localPosition.y, slope.localPosition.z);
                }
            }

            Rebake();
            return true;
        }

        /// <summary>
        /// 바닥·벽·직원 구역이 바뀐 뒤 부른다. 동기로 굽는다 —
        /// 이 게임의 매장은 8×6에서 12×6 남짓이라 한 프레임 안에 끝나고,
        /// 비동기로 굽는 동안 손님이 낡은 면 위를 걷는 편이 더 나쁘다.
        /// </summary>
        public void Rebake()
        {
            if (surface == null) return;

            Remember();
            surface.BuildNavMesh();
            Restore();
        }

        void Remember()
        {
            travelers.Clear();

            foreach (NavMeshAgent agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                if (agent == null || !agent.isActiveAndEnabled) continue;

                travelers.Add(new Traveler
                {
                    agent = agent,
                    // hasPath 가 아니라 목적지를 적는다. 굽고 나면 경로는 어차피 버려진다
                    destination = agent.hasPath || agent.pathPending ? agent.destination : agent.transform.position,
                    hadPath = agent.hasPath || agent.pathPending,
                });
            }
        }

        void Restore()
        {
            for (int i = 0; i < travelers.Count; i++)
            {
                NavMeshAgent agent = travelers[i].agent;
                if (agent == null || !agent.isActiveAndEnabled) continue;

                // 서 있던 자리가 새 면 밖일 수 있다(벽이 밀려나며 바닥이 바뀐 자리).
                // 자기 영역 안에서 가장 가까운 면으로 끌어다 놓는다
                NavMeshHit hit;
                if (NavMesh.SamplePosition(agent.transform.position, out hit, 4f, agent.areaMask))
                    agent.Warp(hit.position);

                if (!travelers[i].hadPath || !agent.isOnNavMesh) continue;

                if (NavMesh.SamplePosition(travelers[i].destination, out hit, 4f, agent.areaMask))
                    agent.SetDestination(hit.position);
            }

            travelers.Clear();
        }
    }
}
