using System;
using System.IO;
using UnityEngine;

namespace DogShop.Core
{
    [Serializable]
    public class DogSave
    {
        public int breedIndex;
        public bool isHero;
        public int daysOwned;
        public int cleanliness;
        public int health;
        public int beauty;
        public int training;
    }

    [Serializable]
    public class SaveData
    {
        /// <summary>
        /// 스키마 버전. <b>0은 버전 필드가 없던 옛 세이브</b>다 — 기본값을 현재 버전으로 두면
        /// 옛 세이브가 자기를 최신이라고 주장해 빠진 필드를 조용히 0으로 복원한다.
        /// </summary>
        public int version;

        public int money;
        public int reputation;
        public int day;
        public int shopLevel = 1;
        public int trainingSlotsUsed;
        public int dirtSpots;

        /// <summary>하루 중 시각과 마감 여부. 없으면 로드가 항상 09:00으로 되돌아간다.</summary>
        public float currentHour = TimeManager.OpenHour;
        public bool dayOver;

        /// <summary>그날 매출. 마감 명성(매출/100) 정산의 근거이므로 유실되면 안 된다.</summary>
        public int dailyRevenue;

        public int[] storage = new int[0];
        public int[] shelf = new int[0];
        public int[] incoming = new int[0];

        /// <summary>
        /// 운반 상자. 상자 안의 물건은 <b>이미 창고에서 빠져나온</b> 상태라
        /// 저장하지 않으면 창고에도 상자에도 없는 채로 영구 유실된다.
        /// </summary>
        public int[] crateContents = new int[0];
        public Vector3 cratePosition;
        public bool crateHeld;

        public DogSave[] dogs = new DogSave[0];
    }

    /// <summary>
    /// JSON 저장/로드. 각 매니저의 상태를 이 한 곳에서 모아 쓰고 되돌린다.
    /// 발표용 데모 세이브 3개가 여기에 의존한다.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        /// <summary>
        /// 스키마 버전. 필드를 늘릴 때마다 올린다.
        /// 2 = 시각·그날매출·운반상자 추가 (D20).
        /// </summary>
        public const int SchemaVersion = 2;

        public static SaveManager Instance { get; private set; }

        static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        public bool HasSave => File.Exists(SavePath);

        /// <summary>저장/로드가 끝난 뒤. UI 갱신용.</summary>
        public event Action OnLoaded;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Save()
        {
            SaveData data = Capture();
            if (data == null) return;

            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            Debug.Log("[Save] Day " + data.day + " / Lv " + data.shopLevel
                      + " / 강아지 " + data.dogs.Length + "마리 / " + SavePath);
        }

        public bool Load()
        {
            if (!HasSave) return false;

            SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return false;

            if (data.version < SchemaVersion)
                Debug.LogWarning("[Load] 옛 세이브(v" + data.version + " < v" + SchemaVersion
                    + ") — 시각·그날매출·상자 내용물이 없어 기본값으로 복원한다. 데모 세이브라면 다시 만들 것.");

            Restore(data);
            OnLoaded?.Invoke();
            Debug.Log("[Load] Day " + data.day + " / Lv " + data.shopLevel
                      + " / 강아지 " + data.dogs.Length + "마리");
            return true;
        }

        SaveData Capture()
        {
            GameManager game = GameManager.Instance;
            if (game == null) return null;

            SaveData data = new SaveData
            {
                version = SchemaVersion,
                money = game.Money,
                reputation = game.Reputation,
                day = game.Day,
                dailyRevenue = game.DailyRevenue
            };

            ISaveParticipant[] participants = FindParticipants();
            for (int i = 0; i < participants.Length; i++) participants[i].CaptureInto(data);

            return data;
        }

        void Restore(SaveData data)
        {
            GameManager.Instance.RestoreFrom(data);

            ISaveParticipant[] participants = FindParticipants();
            for (int i = 0; i < participants.Length; i++) participants[i].RestoreFrom(data);
        }

        /// <summary>
        /// 매니저들은 @Managers 하위에 모여 있으므로 부모에서 한 번에 모은다.
        /// 저장 시점에만 호출되므로 할당이 발생해도 문제없다.
        /// </summary>
        ISaveParticipant[] FindParticipants() =>
            transform.parent != null
                ? transform.parent.GetComponentsInChildren<ISaveParticipant>(true)
                : GetComponents<ISaveParticipant>();
    }
}
