using UnityEngine;

namespace DogShop.Core
{
    /// <summary>가게 바닥 1m 그리드. 좌표 변환과 범위 판정만 담당한다.</summary>
    public class GridManager : MonoBehaviour
    {
        public const float CellSize = 1f;
        public const int MinAisleTiles = 2;

        public static GridManager Instance { get; private set; }

        [SerializeField] int width = 8;
        [SerializeField] int height = 6;

        public int Width => width;
        public int Height => height;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public Vector2Int WorldToCell(Vector3 world) => new Vector2Int(
            Mathf.FloorToInt(world.x / CellSize),
            Mathf.FloorToInt(world.z / CellSize));

        public Vector3 CellToWorld(Vector2Int cell) => new Vector3(
            (cell.x + 0.5f) * CellSize,
            0f,
            (cell.y + 0.5f) * CellSize);

        public bool IsInside(Vector2Int cell) =>
            cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

        /// <summary>가게 레벨이 올라갈 때 호출한다.</summary>
        public void SetSize(int newWidth, int newHeight)
        {
            width = Mathf.Max(1, newWidth);
            height = Mathf.Max(1, newHeight);
        }

        // ponytail: 배치 점유/통로 검증은 진열대가 생기는 D4-7에 붙인다. 지금은 그리드가 보이는 것까지.
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 0.6f, 0.35f);
            for (int x = 0; x <= width; x++)
                Gizmos.DrawLine(new Vector3(x * CellSize, 0f, 0f), new Vector3(x * CellSize, 0f, height * CellSize));
            for (int z = 0; z <= height; z++)
                Gizmos.DrawLine(new Vector3(0f, 0f, z * CellSize), new Vector3(width * CellSize, 0f, z * CellSize));
        }
#endif
    }
}
