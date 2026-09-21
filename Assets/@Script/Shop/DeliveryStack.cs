using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 앞에 놓인 배달 더미 하나. 상품 1종이 여기 쌓여 보인다.
    /// 조준하고 E를 누르면 상자에 담긴다 — <b>창고 선반과 같은 동작</b>이다.
    /// DeliveryManager가 런타임에 생성하고 바인딩한다.
    /// </summary>
    public class DeliveryStack : MonoBehaviour
    {
        public int ProductIndex { get; private set; } = -1;

        public void Bind(int productIndex) => ProductIndex = productIndex;
    }
}
