using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 창고 선반 1개. 상품 1종의 창고 재고가 여기에 쌓여 보인다.
    /// 클릭하면 그 상품을 상자에 담는다(플레이어가 들고 진열대로 나른다).
    /// StorageManager가 런타임에 생성하고 바인딩한다.
    /// </summary>
    public class StorageRack : MonoBehaviour
    {
        public int ProductIndex { get; private set; } = -1;

        public void Bind(int productIndex) => ProductIndex = productIndex;
    }
}
