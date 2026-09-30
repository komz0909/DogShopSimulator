using DogShop.Core;
using DogShop.Data;
using DogShop.UI;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 랜덤 상자. <b>돈 구멍</b>이다 — 가게를 다 키우고 나면 돈이 갈 데가 없어 쌓이는데,
    /// 그 돈을 걸 자리를 만든다.
    ///
    /// 나오는 것은 <b>지금 팔고 있는 상품</b>이고, 다른 점은 등급뿐이다.
    /// 등급은 값에 배수로 붙는다(F 0.6배 ~ S 5배). 새 상품을 만들지 않으므로
    /// 수요가중치·진열 칸·발주 계산을 하나도 건드리지 않는다.
    ///
    /// 값은 <b>고정</b>이다(Plan.md 7번). 레벨에 따라 오르게 하면 후반에 상자가
    /// 저축의 대안이 아니라 또 하나의 고정비가 된다.
    /// </summary>
    public class RandomBox : MonoBehaviour
    {
        /// <summary>Lv 1 기준 상자 값.</summary>
        [SerializeField] int basePrice = 100;

        /// <summary>레벨이 하나 오를 때마다 붙는 값.</summary>
        [SerializeField] int pricePerLevel = 20;

        /// <summary>해금 레벨. 초반에는 재고 살 돈도 빠듯해 도박이 성립하지 않는다.</summary>
        [SerializeField] int unlockLevel = 3;

        public static RandomBox Instance { get; private set; }

        /// <summary>
        /// 지금 레벨의 상자 값. 레벨이 오르면 <b>나오는 물건의 정가도 같이 오르므로</b>
        /// 값만 고정해 두면 후반에 상자가 확정 수입이 된다.
        /// </summary>
        public int Price
        {
            get
            {
                int level = ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Level : 1;
                return basePrice + Mathf.Max(0, level - 1) * pricePerLevel;
            }
        }

        public int UnlockLevel => unlockLevel;

        /// <summary>방금 깐 결과. 확인 버튼을 누를 때까지 창이 떠 있다.</summary>
        /// <summary>지금까지 깐 상자 수와 거기 쓴 돈. 측정에서 "돈이 어디로 갔나"를 보는 데 쓴다.</summary>
        public int OpenedTotal { get; private set; }

        /// <summary>값이 레벨마다 다르므로 <b>낸 돈을 그때그때 더한다</b>. 곱셈으로는 못 낸다.</summary>
        public int SpentTotal { get; private set; }

        public bool HasResult { get; private set; }
        public int ResultProduct { get; private set; } = -1;
        public ItemGrade ResultGrade { get; private set; } = ItemGrade.None;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool IsUnlocked =>
            ShopLevelManager.Instance != null && ShopLevelManager.Instance.Level >= unlockLevel;

        public bool CanOpen(out string reason)
        {
            if (!IsUnlocked) { reason = "Lv " + unlockLevel + " 에 열린다"; return false; }
            if (HasResult) { reason = "먼저 확인할 것"; return false; }

            if (GameManager.Instance.Money < Price)
            {
                reason = "돈 부족 — " + GameManager.Instance.Money + " / " + Price;
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 상자를 깐다. 상품은 <b>해금된 것 중에서 고르게</b> 뽑고, 등급은 확률표로 뽑는다.
        /// 수요가중치로 뽑지 않는 이유: 상자는 장사가 아니라 도박이라, 잘 팔리는 물건이
        /// 더 자주 나오면 "뭐가 나올까"가 사라진다.
        /// </summary>
        public bool Open()
        {
            string reason;
            if (!CanOpen(out reason)) return false;

            int paid = Price;
            if (!GameManager.Instance.TrySpend(paid)) return false;

            InventoryManager inv = InventoryManager.Instance;
            int level = ShopLevelManager.Instance.Level;

            int unlocked = 0;
            for (int i = 0; i < inv.Catalog.Count; i++)
                if (inv.Catalog.Get(i).unlockLevel <= level) unlocked++;
            if (unlocked <= 0) return false;

            int pick = Random.Range(0, unlocked);
            int chosen = -1;
            for (int i = 0; i < inv.Catalog.Count; i++)
            {
                if (inv.Catalog.Get(i).unlockLevel > level) continue;
                if (pick-- > 0) continue;
                chosen = i;
                break;
            }
            if (chosen < 0) return false;

            ResultProduct = chosen;
            ResultGrade = ItemGrades.Roll(level);
            HasResult = true;
            OpenedTotal++;
            SpentTotal += paid;

            // 창고로 바로 들어간다. 진열은 평소처럼 상자로 날라서 한다
            inv.StoreGraded(chosen, ResultGrade);
            return true;
        }

        /// <summary>결과 창의 확인 버튼.</summary>
        public void Acknowledge()
        {
            HasResult = false;
            ResultProduct = -1;
            ResultGrade = ItemGrade.None;
        }

        /// <summary>결과 창에 쓸 한 줄.</summary>
        public string ResultLine()
        {
            if (!HasResult || ResultProduct < 0) return "";

            ProductDef p = InventoryManager.Instance.Catalog.Get(ResultProduct);
            return p.nameKo + "  " + ItemGrades.PriceOf(p.retail, ResultGrade) + "원";
        }
    }
}
