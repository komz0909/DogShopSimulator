using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 플레이어가 <b>실제로 해낸</b> 일을 알리는 곳. 튜토리얼 퀘스트(<see cref="Shop.TutorialQuestManager"/>)가 듣는다.
    ///
    /// 메뉴를 연 것, 대상을 조준한 것으로는 울리지 않는다 — 각 시스템이 일을 끝낸 바로 그 자리에서만 부른다.
    /// 그래서 퀘스트가 "창을 열었다"로 깨지는 일이 없고, 퀘스트 쪽은 각 시스템의 속을 몰라도 된다.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>창고 선반에서 운반 상자로 물건 하나를 옮겼다(E).</summary>
        public static event Action StorageToCrate;
        /// <summary>운반 상자에서 진열대로 물건 하나를 올렸다(R).</summary>
        public static event Action ItemDisplayed;
        /// <summary>계산대 발주 창에서 상품 발주가 결제까지 끝났다(가구만 산 것은 빼고).</summary>
        public static event Action StockOrdered;
        /// <summary>온라인 주문 하나를 포장해 배송했다. 인자는 주문 번호.</summary>
        public static event Action<int> OnlineOrderShipped;
        /// <summary>강아지 훈련 한 번을 끝냈다(돈을 내고 스탯이 올랐다).</summary>
        public static event Action DogTrained;
        /// <summary>계산대에서 손님 한 명을 계산했다.</summary>
        public static event Action CustomerCheckedOut;
        /// <summary>강아지에게 밥·목욕·약 중 하나를 해 줬다(창고 물건을 썼다).</summary>
        public static event Action DogCared;
        /// <summary>강아지 운동 한 번을 끝냈다.</summary>
        public static event Action DogExercised;
        /// <summary>플레이어가 비용을 내고 가게 레벨을 올렸다(세이브 복원은 빼고).</summary>
        public static event Action ShopLeveledUp;
        /// <summary>계산대에서 상품 하나가 팔렸다. 인자는 상품 번호(일일 퀘스트 '추천 상품·사료 팔기').</summary>
        public static event Action<int> ProductSold;
        /// <summary>돌보기 종류 — 0 밥, 1 목욕, 2 약(일일 퀘스트). DogCared 와 같은 자리에서 함께 울린다.</summary>
        public static event Action<int> DogCareDone;
        /// <summary>마스코트가 손님을 반겨 명성이 올랐다(일일 퀘스트 '손님 반기기').</summary>
        public static event Action MascotGreeted;
        /// <summary>훈련 하나를 마쳤다. 인자는 그 훈련이 올리는 축(GrowthAxis 를 int 로 — 일일 퀘스트 '미모·훈련도 올리는 훈련').</summary>
        public static event Action<int> DogTrainedAxis;

        // 도메인 리로드를 끈 에디터에서 지난 플레이의 구독자가 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSubscribers()
        {
            StorageToCrate = null;
            ItemDisplayed = null;
            StockOrdered = null;
            OnlineOrderShipped = null;
            DogTrained = null;
            CustomerCheckedOut = null;
            DogCared = null;
            DogExercised = null;
            ShopLeveledUp = null;
            ProductSold = null;
            DogCareDone = null;
            MascotGreeted = null;
            DogTrainedAxis = null;
        }

        public static void RaiseStorageToCrate() => StorageToCrate?.Invoke();
        public static void RaiseItemDisplayed() => ItemDisplayed?.Invoke();
        public static void RaiseStockOrdered() => StockOrdered?.Invoke();
        public static void RaiseOnlineOrderShipped(int orderId) => OnlineOrderShipped?.Invoke(orderId);
        public static void RaiseDogTrained() { Exhibition.RecordTraining(); DogTrained?.Invoke(); }
        public static void RaiseCustomerCheckedOut() => CustomerCheckedOut?.Invoke();
        public static void RaiseDogCared() => DogCared?.Invoke();
        public static void RaiseDogExercised() { Exhibition.RecordExercise(); DogExercised?.Invoke(); }
        public static void RaiseShopLeveledUp() => ShopLeveledUp?.Invoke();
        public static void RaiseProductSold(int product) => ProductSold?.Invoke(product);
        public static void RaiseDogCareDone(int care) => DogCareDone?.Invoke(care);
        public static void RaiseMascotGreeted() => MascotGreeted?.Invoke();
        public static void RaiseDogTrainedAxis(int axis) => DogTrainedAxis?.Invoke(axis);
    }
}
