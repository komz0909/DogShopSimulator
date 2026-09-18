using System;
using DogShop.Core;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 반려견 한 마리를 들고 있는다. 프리팹에서 런타임 생성되어 이 매니저의 자식으로 붙고,
    /// 성장 스탯을 ShopLevelManager에 공급해 레벨 게이트를 채운다.
    ///
    /// 판매견 슬롯(레벨당 2~6칸)을 걷어냈다. 슬롯이 늘어도 살 수단이 없었고,
    /// 훈련을 나눠 주면 항상 손해라 "누구에게 훈련을 줄까"는 선택이 된 적이 없다.
    /// 이제 훈련 슬롯은 <b>미모와 훈련도 중 어디에 쓸까</b>로만 갈린다 —
    /// 챔피언십 심사 가중치가 시작 시점에 공개되므로 그게 매일의 선택이다.
    /// </summary>
    public class DogManager : MonoBehaviour, DogShop.Core.ISaveParticipant
    {
        public const int DecayPerDay = 15;

        public static DogManager Instance { get; private set; }

        [SerializeField] GameObject[] breedPrefabs = new GameObject[0];
        [SerializeField] string[] breedNames = new string[0];
        [SerializeField] int heroBreedIndex;

        /// <summary>반려견. 게임 시작과 동시에 생기고 죽거나 팔리지 않는다.</summary>
        public Dog Hero { get; private set; }

        public event Action OnRosterChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnDayStarted += HandleDayStarted;
            if (Hero == null) Spawn(heroBreedIndex);
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= HandleDayStarted;
            if (Instance == this) Instance = null;
        }

        /// <summary>견종 교체(시작 시 선택)에도 쓸 수 있게 생성은 한 곳으로 모아 둔다.</summary>
        Dog Spawn(int breedIndex)
        {
            if (breedPrefabs.Length == 0) return null;

            breedIndex = Mathf.Clamp(breedIndex, 0, breedPrefabs.Length - 1);

            GameObject instance = Instantiate(breedPrefabs[breedIndex], transform);
            instance.transform.position = HomePosition;

            Dog dog = instance.GetComponent<Dog>();
            if (dog == null) { Destroy(instance); return null; }

            // NavMeshAgent 는 transform 대입을 무시하므로 Warp 로 다시 앉힌다
            DogRoamer roamer = instance.GetComponent<DogRoamer>();
            if (roamer != null) roamer.Warp(HomePosition);

            dog.Initialize(breedIndex < breedNames.Length ? breedNames[breedIndex] : "강아지", breedIndex);
            dog.SyncMood();
            dog.Stats.OnChanged += PushHeroStatToLevelGate;

            Hero = dog;
            PushHeroStatToLevelGate();
            OnRosterChanged?.Invoke();
            return dog;
        }

        void HandleDayStarted()
        {
            if (Hero == null) return;

            Hero.Stats.DecayDaily(DecayPerDay);
            Hero.AdvanceDay();
            Hero.SyncMood();
        }

        void PushHeroStatToLevelGate()
        {
            if (Hero == null || ShopLevelManager.Instance == null) return;
            ShopLevelManager.Instance.HeroGrowthStat = Hero.Stats.GrowthTotal;
        }

        public void CaptureInto(SaveData data)
        {
            if (Hero == null) { data.dogs = new DogSave[0]; return; }

            DogStats st = Hero.Stats;
            data.dogs = new[]
            {
                new DogSave
                {
                    breedIndex = Hero.BreedIndex,
                    isHero = true,
                    daysOwned = Hero.DaysOwned,
                    cleanliness = st.Cleanliness,
                    health = st.Health,
                    beauty = st.Beauty,
                    training = st.Training
                }
            };
        }

        public void RestoreFrom(SaveData data)
        {
            if (data.dogs == null || data.dogs.Length == 0) return;

            // 판매견까지 담겨 있던 옛 세이브는 주인공견만 살리고 나머지는 버린다
            DogSave s = data.dogs[0];
            for (int i = 0; i < data.dogs.Length; i++)
                if (data.dogs[i].isHero) { s = data.dogs[i]; break; }

            if (Hero != null) Destroy(Hero.gameObject);
            Hero = null;

            Dog dog = Spawn(s.breedIndex);
            if (dog == null) return;

            dog.RestoreState(s.daysOwned);
            dog.Stats.Restore(s.cleanliness, s.health, s.beauty, s.training);
            dog.SyncMood();

            PushHeroStatToLevelGate();
            OnRosterChanged?.Invoke();
        }

        /// <summary>창고 옆방 — 주인과 함께 지내는 곳. 여기서 시작해 가게 전체를 돌아다닌다.</summary>
        static readonly Vector3 HomePosition = new Vector3(10.2f, 0f, 8.1f);
    }
}
