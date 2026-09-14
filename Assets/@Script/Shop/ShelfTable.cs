using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 진열대 1개. 상품 1종을 담당하고, 입구에서 가까울수록 판매 가중치가 높다.
    /// ShelfManager가 런타임에 생성하고 바인딩한다.
    /// </summary>
    public class ShelfTable : MonoBehaviour
    {
        public int ProductIndex { get; private set; } = -1;
        public float ProximityMultiplier { get; private set; } = 1f;

        /// <summary>손님이 실제로 서는 자리. 진열대 중심은 콜라이더 안이라 도달할 수 없다.</summary>
        public Vector3 ApproachPoint { get; private set; }

        public void Bind(int productIndex, float proximityMultiplier, Vector3 approachPoint)
        {
            ProductIndex = productIndex;
            ProximityMultiplier = proximityMultiplier;
            ApproachPoint = approachPoint;
        }
    }
}
