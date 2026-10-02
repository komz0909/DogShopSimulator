using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 앞에 쌓인 <b>안 뜯은 배달 상자</b> 하나. 상품 한 종이 통째로 들어 있다.
    /// 조준하고 E를 누르면 뜯어진다 — 상자는 사라지고 내용물이 그 자리에 쏟아진다.
    ///
    /// 첨자가 아니라 <see cref="Id"/>로 가리킨다. 앞쪽 상자를 뜯으면 목록이 당겨져
    /// 첨자가 밀리는데, 그 사이에 조준하던 상자가 다른 상자로 바뀌어 버린다.
    /// </summary>
    public class DeliveryParcel : MonoBehaviour
    {
        public int Id { get; private set; }
        public int ProductIndex { get; private set; } = -1;

        public void Bind(int id, int productIndex)
        {
            Id = id;
            ProductIndex = productIndex;
        }
    }
}
