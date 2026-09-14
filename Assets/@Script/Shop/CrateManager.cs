using DogShop.Core;
using DogShop.Player;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 운반 상자의 소유자. 상자는 씬에 미리 놓지 않고 여기서 런타임에 스폰한다
    /// (관리 대상은 매니저 밑에 두고, 프리팹 인스턴스를 하이어라키에 남기지 않는다).
    ///
    /// 세이브에 반드시 참여해야 한다 — 상자 안의 물건은 <b>이미 창고 재고에서 빠져나온</b>
    /// 상태이므로, 저장하지 않으면 창고에도 상자에도 없는 채로 영구 유실된다.
    /// </summary>
    public class CrateManager : MonoBehaviour, ISaveParticipant
    {
        [SerializeField] GameObject cratePrefab;

        /// <summary>창고 문 안쪽. 문틈(x 3.2~4.8) 중앙을 막지 않게 살짝 안쪽에 둔다.</summary>
        [SerializeField] Vector3 spawnPosition = new Vector3(4f, 0f, 7.6f);

        public static CrateManager Instance { get; private set; }

        CarryCrate crate;

        public CarryCrate Crate => crate;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            if (crate == null) Spawn(spawnPosition);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        CarryCrate Spawn(Vector3 position)
        {
            if (cratePrefab == null)
            {
                Debug.LogError("[CrateManager] cratePrefab이 배선되지 않았다 — 상자가 생기지 않는다.");
                return null;
            }

            GameObject instance = Instantiate(cratePrefab, transform);
            instance.name = "Crate";
            instance.transform.position = position;

            crate = instance.GetComponent<CarryCrate>();
            return crate;
        }

        static PlayerCarry FindCarry() => FindAnyObjectByType<PlayerCarry>();

        public void CaptureInto(SaveData data)
        {
            int count = InventoryManager.Instance.Catalog.Count;
            data.crateContents = new int[count];

            if (crate == null)
            {
                data.cratePosition = spawnPosition;
                data.crateHeld = false;
                return;
            }

            for (int i = 0; i < count; i++) data.crateContents[i] = crate.CountOf(i);

            PlayerCarry carry = FindCarry();
            data.crateHeld = carry != null && carry.Held == crate;

            // 들고 있으면 손 위치가 잡히므로 저장하지 않는다 — 복원 때 다시 들리기 때문에 의미가 없다
            data.cratePosition = data.crateHeld ? spawnPosition : crate.transform.position;
        }

        public void RestoreFrom(SaveData data)
        {
            PlayerCarry carry = FindCarry();
            if (carry != null) carry.ForceRelease();

            if (crate == null) Spawn(spawnPosition);
            if (crate == null) return;

            // 비우고 저장값으로 다시 채운다. AddOne을 쓰면 칸 수와 내부 비주얼이 함께 맞는다
            crate.Clear();
            if (data.crateContents != null)
            {
                for (int i = 0; i < data.crateContents.Length; i++)
                    for (int n = 0; n < data.crateContents[i]; n++)
                        if (!crate.AddOne(i)) break;
            }

            crate.transform.SetParent(transform, true);

            // v2 이전 세이브에는 상자 위치가 없어 (0,0,0)이 들어온다 — 벽 밖으로 나가지 않게 스폰 위치로
            bool hasPosition = data.version >= 2 && data.cratePosition.sqrMagnitude > 0.0001f;
            crate.transform.position = hasPosition ? data.cratePosition : spawnPosition;

            if (data.crateHeld && carry != null) carry.PickUp(crate);
        }
    }
}
