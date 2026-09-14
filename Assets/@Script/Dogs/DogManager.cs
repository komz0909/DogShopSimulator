using System;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 강아지 목록과 슬롯. 강아지는 프리팹에서 런타임 생성되어 이 매니저의 자식으로 붙는다.
    /// 주인공견의 성장 스탯을 ShopLevelManager에 공급해 레벨 게이트를 채운다.
    /// </summary>
    public class DogManager : MonoBehaviour, DogShop.Core.ISaveParticipant
    {
        public const int HeroDecayPerDay = 15;
        public const int SaleDecayPerDay = 8;

        public static DogManager Instance { get; private set; }

        [SerializeField] GameObject[] breedPrefabs = new GameObject[0];
        [SerializeField] string[] breedNames = new string[0];
        [SerializeField] int heroBreedIndex;

        readonly List<Dog> dogs = new List<Dog>();

        public Dog Hero { get; private set; }
        public int Count => dogs.Count;
        public Dog Get(int index) => dogs[index];
        public int SlotLimit => ShopLevelManager.Instance.Current.dogSlots;
        public bool HasFreeSlot => dogs.Count < SlotLimit;

        public event Action OnRosterChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnDayStarted += HandleDayStarted;
            SpawnHero();
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= HandleDayStarted;
            if (Instance == this) Instance = null;
        }

        void SpawnHero()
        {
            if (Hero != null) return;

            Hero = Spawn(heroBreedIndex, true);
            PushHeroStatToLevelGate();
        }

        /// <summary>판매견 매입 또는 유기견 입양. 슬롯이 없으면 null.</summary>
        public Dog SpawnSaleDog(int breedIndex)
        {
            if (!HasFreeSlot) return null;
            return Spawn(breedIndex, false);
        }

        Dog Spawn(int breedIndex, bool isHero)
        {
            if (breedPrefabs.Length == 0) return null;

            breedIndex = Mathf.Clamp(breedIndex, 0, breedPrefabs.Length - 1);

            GameObject instance = Instantiate(breedPrefabs[breedIndex], transform);
            instance.transform.localPosition = SlotPosition(dogs.Count);

            Dog dog = instance.GetComponent<Dog>();
            if (dog == null) { Destroy(instance); return null; }

            string breedKo = breedIndex < breedNames.Length ? breedNames[breedIndex] : "강아지";
            dog.Initialize(breedKo, breedIndex, isHero);
            dog.SyncMood();

            if (isHero) dog.Stats.OnChanged += PushHeroStatToLevelGate;

            dogs.Add(dog);
            OnRosterChanged?.Invoke();
            return dog;
        }

        public bool Sell(Dog dog)
        {
            if (dog == null || !dog.CanSell) return false;

            GameManager.Instance.AddMoney(dog.SalePrice);
            dogs.Remove(dog);
            Destroy(dog.gameObject);
            Relayout();
            OnRosterChanged?.Invoke();
            return true;
        }

        void HandleDayStarted()
        {
            for (int i = 0; i < dogs.Count; i++)
            {
                Dog dog = dogs[i];
                dog.Stats.DecayDaily(dog.IsHero ? HeroDecayPerDay : SaleDecayPerDay);
                dog.AdvanceDay();
                dog.SyncMood();
            }
        }

        void PushHeroStatToLevelGate()
        {
            if (Hero == null || ShopLevelManager.Instance == null) return;
            ShopLevelManager.Instance.HeroGrowthStat = Hero.Stats.GrowthTotal;
        }

        public void CaptureInto(SaveData data)
        {
            DogSave[] saved = new DogSave[dogs.Count];
            for (int i = 0; i < dogs.Count; i++)
            {
                Dog dog = dogs[i];
                DogStats st = dog.Stats;
                saved[i] = new DogSave
                {
                    breedIndex = dog.BreedIndex,
                    isHero = dog.IsHero,
                    daysOwned = dog.DaysOwned,
                    cleanliness = st.Cleanliness,
                    health = st.Health,
                    beauty = st.Beauty,
                    training = st.Training
                };
            }
            data.dogs = saved;
        }

        public void RestoreFrom(SaveData data)
        {
            if (data.dogs == null || data.dogs.Length == 0) return;

            for (int i = 0; i < dogs.Count; i++)
                if (dogs[i] != null) Destroy(dogs[i].gameObject);

            dogs.Clear();
            Hero = null;

            for (int i = 0; i < data.dogs.Length; i++)
            {
                DogSave s = data.dogs[i];
                Dog dog = Spawn(s.breedIndex, s.isHero);
                if (dog == null) continue;

                dog.RestoreState(s.daysOwned);
                dog.Stats.Restore(s.cleanliness, s.health, s.beauty, s.training);
                dog.SyncMood();

                if (s.isHero) Hero = dog;
            }

            PushHeroStatToLevelGate();
            OnRosterChanged?.Invoke();
        }

        void Relayout()
        {
            for (int i = 0; i < dogs.Count; i++)
                dogs[i].transform.localPosition = SlotPosition(i);
        }

        /// <summary>가게 안쪽 벽을 따라 1m 그리드 위에 나란히 세운다.</summary>
        static Vector3 SlotPosition(int slot) =>
            new Vector3(1.5f + slot * GridManager.CellSize, 0f, 4.5f);
    }
}
