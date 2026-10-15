using DogShop.Core;
using DogShop.Shop;

namespace DogShop.Dogs
{
    /// <summary>
    /// 유지 스탯 액션. 훈련 슬롯을 쓰지 않고 <b>재고</b>를 쓴다 (자원축 직교: 슬롯=성장, 재고=유지).
    /// 재고 하나를 강아지에게 쓰면 그만큼 손님에게 팔 물건이 사라진다 — 이것이 이중용도 긴장이다.
    /// </summary>
    public static class DogCare
    {
        // ProductCatalog의 고정 인덱스. 카탈로그 순서를 바꾸면 여기도 바꿀 것.
        public const int FoodIndex = 0;
        public const int ShampooIndex = 1;
        public const int MedicineIndex = 2;
        public const int PremiumFoodIndex = 6;       // 고급 사료
        public const int PremiumShampooIndex = 20;   // 고급 샴푸

        /// <summary>
        /// 이 날부터 밥·목욕에 <b>고급</b> 사료·샴푸를 쓴다. 10일차까지는 기본 사료·기본 샴푸.
        /// 강아지가 자라면서 돌보는 데 드는 돈도 같이 커지게 — 쌓이기만 하던 돈을 쓸 곳이다.
        /// </summary>
        public const int PremiumFromDay = 11;

        public static bool PremiumCare => GameManager.Instance != null && GameManager.Instance.Day >= PremiumFromDay;

        /// <summary>지금 밥·목욕에 쓰는 상품.</summary>
        public static int FoodNow => PremiumCare ? PremiumFoodIndex : FoodIndex;
        public static int ShampooNow => PremiumCare ? PremiumShampooIndex : ShampooIndex;

        /// <summary>돌보기 때문에 레벨과 상관없이 발주할 수 있어야 하는 상품인가(11일차부터 고급 사료·샴푸).</summary>
        public static bool RequiredForCare(int productIndex) =>
            PremiumCare && (productIndex == PremiumFoodIndex || productIndex == PremiumShampooIndex);

        public const int FeedRecovery = 20;
        public const int BathRecovery = 45;
        public const int MedicineRecovery = 40;

        /// <summary>재고 1개를 소모해 유지 스탯을 회복하는 액션.</summary>
        public sealed class CareAction : IPlayerAction
        {
            readonly Dog dog;
            readonly int productIndex;
            readonly int recovery;
            readonly bool isHealth;
            readonly string label;

            CareAction(Dog dog, int productIndex, int recovery, bool isHealth, string label)
            {
                this.dog = dog;
                this.productIndex = productIndex;
                this.recovery = recovery;
                this.isHealth = isHealth;
                this.label = label;
            }

            public static CareAction Feed(Dog dog) => new CareAction(dog, FoodNow, FeedRecovery, true, "밥 주기");
            public static CareAction Bath(Dog dog) => new CareAction(dog, ShampooNow, BathRecovery, false, "목욕");
            public static CareAction Medicine(Dog dog) => new CareAction(dog, MedicineIndex, MedicineRecovery, true, "약 먹이기");

            public bool CanExecute(out string reason)
            {
                if (dog == null) { reason = "대상 없음"; return false; }

                InventoryManager inv = InventoryManager.Instance;
                if (!inv.IsUnlocked(productIndex)) { reason = label + ": 미해금 상품"; return false; }
                if (inv.StorageOf(productIndex) <= 0)
                {
                    reason = label + ": 창고 재고 없음 (" + inv.Catalog.Get(productIndex).nameKo + ")";
                    return false;
                }

                reason = null;
                return true;
            }

            public void Execute()
            {
                if (!InventoryManager.Instance.TryConsumeStorage(productIndex)) return;

                if (isHealth) dog.Stats.RecoverHealth(recovery);
                else dog.Stats.RecoverCleanliness(recovery);

                dog.Animator.Play(isHealth ? DogAnim.Eat : DogAnim.Sit);
                dog.SyncMood();
            }
        }
    }
}
