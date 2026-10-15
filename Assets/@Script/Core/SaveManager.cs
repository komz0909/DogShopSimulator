using System;
using System.IO;
using UnityEngine;

namespace DogShop.Core
{
    [Serializable]
    public class DogSave
    {
        public int breedIndex;
        public string name;
        public bool isHero;
        public int daysOwned;
        public int cleanliness;
        public int health;
        public int beauty;
        public int training;

        /// <summary>어질리티(운동으로 오른다). 없던 시절 세이브는 0으로 시작한다.</summary>
        public int agility;

        /// <summary>견종 배율을 곱하고 남은 소수 몫. 버리면 저장·로드만으로 견종 차이가 깎인다.</summary>
        public float beautyCarry;
        public float trainingCarry;
        public float agilityCarry;
    }

    /// <summary>플레이어가 사서 놓아 둔 가구 하나. 무엇을 어디에 놓았는지만 있으면 다시 세울 수 있다.</summary>
    [Serializable]
    public class FurnitureSave
    {
        public int catalogIndex;
        public Vector3 position;
        public Vector3 rotation;
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

        /// <summary>오늘 운동을 했는가(하루 한 번). 없으면 저장·로드만으로 운동이 다시 열린다.</summary>
        public bool exercisedToday;

        /// <summary>오늘 이미 쓴 훈련(하루 쿨타임). 없으면 저장·로드만으로 쿨이 풀린다.</summary>
        public bool[] trainingUsedToday = new bool[0];

        /// <summary>훈련마다의 단계(1~5). 없으면(옛 세이브) 전부 1단계로 본다.</summary>
        public int[] trainingStage = new int[0];

        /// <summary>강화가 끝나기까지 남은 날. 없으면 강화 중인 것이 없다.</summary>
        public int[] trainingUpgradeDays = new int[0];

        /// <summary>지금 단계에서 그 훈련을 몇 번 했는가. 단계상승의 선행 조건이다.</summary>
        public int[] trainingReps = new int[0];

        public int dirtSpots;

        /// <summary>하루 중 시각과 마감 여부. 없으면 로드가 항상 09:00으로 되돌아간다.</summary>
        public float currentHour = TimeManager.DayStartHour;
        public bool dayOver;

        /// <summary>그날 매출. 마감 명성(매출/100) 정산의 근거이므로 유실되면 안 된다.</summary>
        public int dailyRevenue;

        /// <summary>
        /// 그날 판매·놓침 건수. 마감 카드가 이 둘을 읽는데, 저장하지 않으면 마감 상태로
        /// 저장한 세이브를 불러왔을 때 "매출 1,240원 판매 0건"처럼 앞뒤가 안 맞는 줄이 뜬다.
        /// 없던 시절 세이브는 0으로 복원되고 그게 맞다.
        /// </summary>
        public int soldToday;
        public int lostToday;

        /// <summary>오늘 들어온 손님 수와 그들이 쌓은 명성. 마감 화면이 "방문 N명 · 명성 +M"을 다시 적을 때 쓴다.</summary>
        public int visitorsToday;
        public int reputationToday;

        /// <summary>오늘 쓴 돈(발주·가구·강아지·기타)과 판매 외 수입, 오늘 아침 명성 — 마감 정산서용. -1 은 없던 시절 세이브.</summary>
        public int[] spentToday = new int[0];
        public int bonusToday;
        public int reputationDayStart = -1;

        public int[] storage = new int[0];

        /// <summary>온라인 주문(OnlineOrderManager). 주문마다 [id, 도착 시각, 명성, 상태, 종류 수, (상품, 개수)…] 를 이어 붙였다.</summary>
        public int[] onlineOrders = new int[0];
        public int onlineOrderNextId = 1;
        /// <summary>튜토리얼이 보장해 넣은 온라인 주문 번호(0 = 없음). 이 주문이 처리 중이면 확률 주문을 굴리지 않는다.</summary>
        public int onlineTutorialOrderId;

        /// <summary>
        /// 튜토리얼 퀘스트(TutorialQuestManager). 지금 퀘스트 번호(0~4, 5 = 끝), 그 퀘스트 진행도,
        /// 12시 보장 온라인 주문을 이미 넣었는가.
        /// </summary>
        public int tutorialStep;
        public int tutorialProgress;
        public bool tutorialOrderIssued;
        public int[] shelf = new int[0];
        public int[] incoming = new int[0];

        /// <summary>
        /// 가게 앞에 배달되어 아직 안 들인 것. 저장하지 않으면 <b>대금은 냈는데 물건은
        /// 어디에도 없는</b> 상태가 된다 — 운반 상자와 같은 이유로 반드시 남긴다.
        /// </summary>
        public int[] delivered = new int[0];

        /// <summary>등급품 재고. 상품마다 등급 수(7)만큼 칸을 쓴다. 랜덤박스에서만 나온다.</summary>
        public int[] graded = new int[0];

        /// <summary>
        /// 아직 안 온 긴급 발주. <b>대금은 이미 냈다</b> — 안 남기면 돈만 내고 물건은
        /// 어디에도 없는 상태가 된다. 세 배열이 같은 첨자를 쓴다.
        /// </summary>
        public int[] expressProduct = new int[0];
        public int[] expressQuantity = new int[0];
        public float[] expressDueHour = new float[0];

        /// <summary>오늘 긴급 발주를 썼는가. 없으면 저장·로드만으로 하루 한 번 제한이 풀린다.</summary>
        public bool expressUsedToday;

        /// <summary>
        /// 가게 앞에 쌓인 <b>안 뜯은 상자</b>. 대금은 이미 치렀고 트럭도 다녀간 물건이라
        /// 안 남기면 자고 일어났을 때 통째로 사라진다. 두 배열이 같은 첨자를 쓴다.
        /// </summary>
        public int[] parcelProduct = new int[0];
        public int[] parcelQuantity = new int[0];

        /// <summary>진열대 칸별 상품과 개수. 진열대를 이어 붙인 한 줄이다. -1 은 빈 칸.</summary>
        public int[] shelfSlotProduct = new int[0];
        public int[] shelfSlotCount = new int[0];

        /// <summary>칸에 올라간 물건의 등급. <b>옛 세이브 전용</b> — 칸마다 등급이 하나였던 시절.</summary>
        public int[] shelfSlotGrade = new int[0];

        /// <summary>칸마다 등급별 개수. 첨자는 <c>칸 * 8 + 등급레인</c>(0 = 발주품).</summary>
        public int[] shelfSlotStock = new int[0];

        /// <summary>승급 당일 특급 입고가 켜져 있는가. 없던 시절 세이브는 false 로 복원되고 그게 맞다.</summary>
        public bool rushDelivery;

        /// <summary>영업 단계(0 준비중 / 1 영업중 / 2 마감). 없던 시절 세이브는 준비중으로 복원된다.</summary>
        public int shopPhase;

        /// <summary>옛 [가게 열기] 예약. 문이 저절로 열리게 된 뒤로는 쓰지 않는다(옛 세이브 호환용 칸).</summary>
        public bool shopReserved;

        /// <summary>
        /// 운반 상자. 상자 안의 물건은 <b>이미 창고에서 빠져나온</b> 상태라
        /// 저장하지 않으면 창고에도 상자에도 없는 채로 영구 유실된다.
        /// </summary>
        public int[] crateContents = new int[0];
        public Vector3 cratePosition;
        public bool crateHeld;

        /// <summary>
        /// 산 가구. 대금을 냈지만 아직 안 온 것(<see cref="furnitureOrdered"/>)과
        /// 이미 놓아 둔 것(<see cref="furniturePlaced"/>)을 따로 남긴다.
        /// 안 남기면 진열대를 사서 늘려 둔 가게가 불러올 때마다 처음 둘로 돌아간다.
        /// </summary>
        public int[] furnitureOrdered = new int[0];
        public FurnitureSave[] furniturePlaced = new FurnitureSave[0];

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
        /// 3 = 영업 단계·그날 판매/놓침 건수 추가 (2026-09-18).
        /// 4 = 가게가 오른쪽으로만 넓어지던 것을 문(x 4)을 가운데 두고 양쪽으로 넓어지게 바꿈 (2026-10-07).
        ///     v3 이하 세이브의 판매장 가구는 불러올 때 왼쪽으로 옮긴다(<see cref="Shop.FurnitureShop.RestoreFrom"/>).
        /// 5 = 어질리티·견종 배율의 소수 몫·오늘 운동 여부 추가 (2026-10-08). v4 이하는 셋 다 0/false 로 시작하면 된다.
        /// 6 = 영업시간 09~21시(10시 오픈 예약) — 오픈 예약 여부 추가 (2026-10-12). 옛 세이브의 08시대 시각은 09시로 당겨진다.
        /// 7 = 온라인 주문(수락·포장·출고) 추가 (2026-10-14).
        /// 8 = 튜토리얼 퀘스트 진행·튜토리얼 온라인 주문 추가 (2026-10-14). v7 이하이면서 2일차 이상인 세이브는
        ///     이미 가게를 돌려 본 것으로 보고 튜토리얼을 끝난 상태로 불러온다.
        /// </summary>
        public const int SchemaVersion = 8;

        public static SaveManager Instance { get; private set; }

        static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        /// <summary>
        /// 세이브 파일이 있는가. <b>정적이다</b> — 메인 화면에는 SaveManager 가 없는데
        /// "이어하기"를 켤지 꺼둘지는 거기서 정해야 한다.
        /// </summary>
        public static bool HasSaveFile => File.Exists(SavePath);

        public bool HasSave => HasSaveFile;

        /// <summary>저장/로드가 끝난 뒤. UI 갱신용.</summary>
        public event Action OnLoaded;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>
        /// 메인 화면에서 "이어하기"로 들어왔으면 세이브를 불러온다.
        ///
        /// Start 여야 한다 — 다른 매니저들이 Awake 에서 자기 인스턴스를 세우고
        /// Start 에서 강아지를 생성하므로, 그보다 먼저 복원하면 빈 가게에 값을 덮어쓴다.
        /// 스크립트 실행 순서에 기대지 않으려고 한 프레임 미룬다.
        /// </summary>
        System.Collections.IEnumerator Start()
        {
            if (!GameStart.FromMenu || !GameStart.Continue) yield break;

            yield return null;   // 모든 Start 가 끝난 뒤
            Load();
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
                    + ") — 빠진 필드는 기본값으로 복원한다. 데모 세이브라면 다시 만들 것.");

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
                dailyRevenue = game.DailyRevenue,
                bonusToday = game.BonusToday,
                reputationDayStart = game.ReputationAtDayStart,
                spentToday = new[]
                {
                    game.SpentToday(GameManager.SpendKind.Stock), game.SpentToday(GameManager.SpendKind.Furniture),
                    game.SpentToday(GameManager.SpendKind.Dog), game.SpentToday(GameManager.SpendKind.Other)
                }
            };

            ISaveParticipant[] participants = FindParticipants();
            for (int i = 0; i < participants.Length; i++) participants[i].CaptureInto(data);

            return data;
        }

        /// <summary>세이브를 불러오는 중인가. 불러온 값은 이미 제자리이므로 "따라 옮기는" 처리(벽 확장 등)는 건너뛴다.</summary>
        public static bool IsRestoring { get; private set; }

        void Restore(SaveData data)
        {
            IsRestoring = true;
            try
            {
                GameManager.Instance.RestoreFrom(data);

                ISaveParticipant[] participants = FindParticipants();
                for (int i = 0; i < participants.Length; i++) participants[i].RestoreFrom(data);
            }
            finally { IsRestoring = false; }
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
