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

        // 도메인 리로드를 끈 에디터에서 지난 플레이의 구독자가 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSubscribers()
        {
            StorageToCrate = null;
            ItemDisplayed = null;
            StockOrdered = null;
            OnlineOrderShipped = null;
            DogTrained = null;
        }

        public static void RaiseStorageToCrate() => StorageToCrate?.Invoke();
        public static void RaiseItemDisplayed() => ItemDisplayed?.Invoke();
        public static void RaiseStockOrdered() => StockOrdered?.Invoke();
        public static void RaiseOnlineOrderShipped(int orderId) => OnlineOrderShipped?.Invoke(orderId);
        public static void RaiseDogTrained() => DogTrained?.Invoke();
    }
}
