using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 바닥 오염 지점 1개. 클릭하면 사라진다.
    /// 트리거 콜라이더라 플레이어·손님·카메라는 통과하고 클릭 판정만 잡는다.
    /// 그림(진흙 웅덩이, 바르코)은 바닥에 붙은 판(Decal)이고, 생길 때마다 방향을 무작위로 돌려 같은 모양이 반복돼 보이지 않게 한다.
    /// </summary>
    public class DirtSpot : MonoBehaviour
    {
        void Awake()
        {
            Transform decal = transform.Find("Decal");
            if (decal != null) decal.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
        }
    }
}
