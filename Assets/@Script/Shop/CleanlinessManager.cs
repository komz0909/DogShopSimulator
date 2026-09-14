using System;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Dogs;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 청결도. 오염 지점 개수에서 파생되므로 따로 동기화할 수치가 없다.
    /// 강아지가 많을수록 빨리 더러워진다 — 강아지 슬롯을 늘리는 숨은 비용이다.
    /// 청결도는 손님 수를 <b>깎는 방향으로만</b> 작동한다. 매출을 올리는 보정을 넣으면
    /// A_max가 천장을 넘어 훈련 선택이 죽는다(Plan.md 감시 지표).
    /// </summary>
    public class CleanlinessManager : MonoBehaviour, ISaveParticipant
    {
        public const int MaxCleanliness = 100;
        public const int DirtPerSpot = 10;
        public const int MaxSpots = MaxCleanliness / DirtPerSpot;

        public const int BaseDirtPerHour = 2;
        public const int DirtPerDogPerHour = 1;

        /// <summary>청결도 0일 때 남는 손님 비율. 1.0을 넘겨서는 안 된다.</summary>
        public const float MinCustomerFactor = 0.6f;

        public static CleanlinessManager Instance { get; private set; }

        [SerializeField] GameObject dirtPrefab;

        readonly List<DirtSpot> spots = new List<DirtSpot>();

        float accumulated;

        public int SpotCount => spots.Count;

        /// <summary>계측 모드가 자동 청소할 때 쓴다.</summary>
        public DirtSpot SpotAt(int index) => index >= 0 && index < spots.Count ? spots[index] : null;
        public int Cleanliness => Mathf.Max(0, MaxCleanliness - spots.Count * DirtPerSpot);
        public float CustomerFactor => Mathf.Lerp(MinCustomerFactor, 1f, Cleanliness / (float)MaxCleanliness);

        public event Action OnChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnWholeHourChanged += HandleHour;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnWholeHourChanged -= HandleHour;
            if (Instance == this) Instance = null;
        }

        void HandleHour(int hour)
        {
            int dogs = DogManager.Instance != null ? DogManager.Instance.Count : 0;
            accumulated += BaseDirtPerHour + dogs * DirtPerDogPerHour;

            while (accumulated >= DirtPerSpot && spots.Count < MaxSpots)
            {
                accumulated -= DirtPerSpot;
                SpawnDirt();
            }

            // 상한에 닿았으면 더 쌓지 않는다
            if (spots.Count >= MaxSpots) accumulated = 0f;
        }

        void SpawnDirt()
        {
            if (dirtPrefab == null) return;

            Vector3 position;
            if (!TryFindFloorSpot(out position)) return;

            GameObject instance = Instantiate(dirtPrefab, transform);
            instance.transform.position = position;

            DirtSpot spot = instance.GetComponent<DirtSpot>();
            if (spot == null) { Destroy(instance); return; }

            spots.Add(spot);
            OnChanged?.Invoke();
        }

        /// <summary>진열대·계산대·기존 오염과 겹치지 않는 바닥 지점을 찾는다.</summary>
        bool TryFindFloorSpot(out Vector3 position)
        {
            ShelfManager shelves = ShelfManager.Instance;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector3 candidate = new Vector3(
                    UnityEngine.Random.Range(0.6f, 7.4f),
                    0.02f,
                    UnityEngine.Random.Range(0.6f, 5.4f));

                if (Overlaps(candidate, new Vector3(6.5f, 0f, 1.0f), 1.3f)) continue;

                bool blocked = false;

                if (shelves != null)
                {
                    for (int i = 0; i < shelves.Count && !blocked; i++)
                        if (Overlaps(candidate, shelves.Get(i).transform.position, 0.8f)) blocked = true;
                }

                for (int i = 0; i < spots.Count && !blocked; i++)
                    if (Overlaps(candidate, spots[i].transform.position, 0.7f)) blocked = true;

                if (blocked) continue;

                position = candidate;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        static bool Overlaps(Vector3 a, Vector3 b, float radius)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz < radius * radius;
        }

        // ponytail: 디버그 HUD의 확인용. 정식 UI가 붙는 D23-25에 지운다.
        public void ForceSpawn(int count)
        {
            for (int i = 0; i < count && spots.Count < MaxSpots; i++) SpawnDirt();
        }

        public void CaptureInto(SaveData data) => data.dirtSpots = spots.Count;

        public void RestoreFrom(SaveData data)
        {
            for (int i = 0; i < spots.Count; i++)
                if (spots[i] != null) Destroy(spots[i].gameObject);
            spots.Clear();
            accumulated = 0f;

            ForceSpawn(Mathf.Clamp(data.dirtSpots, 0, MaxSpots));
            OnChanged?.Invoke();
        }

        public void Clean(DirtSpot spot)
        {
            if (spot == null || !spots.Remove(spot)) return;

            Destroy(spot.gameObject);
            OnChanged?.Invoke();
        }
    }
}
