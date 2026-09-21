using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 플레이어가 사서 들여놓은 가구에 붙는 꼬리표. <b>어느 카탈로그 항목인지</b>만 들고 있다.
    ///
    /// 씬에 원래 있던 가구와 구별하려고 둔다 — 세이브를 복원할 때 씬 가구는 자리만 되돌리면
    /// 되지만, 산 가구는 <b>다시 만들어야</b> 하고 그러려면 무엇을 샀는지 알아야 한다.
    /// </summary>
    public class BoughtFurniture : MonoBehaviour
    {
        [SerializeField] int catalogIndex = -1;

        public int CatalogIndex => catalogIndex;

        public void Bind(int index) => catalogIndex = index;
    }
}
