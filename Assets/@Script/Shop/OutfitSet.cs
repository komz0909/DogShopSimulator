using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 손님 모델 하나가 입을 수 있는 옷 색 묶음. 손님이 가게에 들어올 때 하나를 무작위로 입힌다.
    ///
    /// 손님 모델은 7종뿐이라 같은 옷의 손님이 여럿 한꺼번에 들어오면 복제 인간처럼 보였다.
    /// 모델을 더 뽑는 대신 텍스처에서 옷 색 범위만 다른 색으로 바꾼 재질(바디는 같다)을 둔다 —
    /// 얼굴·머리·피부는 그대로라 같은 사람이 옷만 갈아입은 셈이다.
    /// </summary>
    public class OutfitSet : MonoBehaviour
    {
        [Tooltip("이 손님이 입을 수 있는 재질들. 비어 있으면 프리팹 재질 그대로")]
        [SerializeField] Material[] outfits = new Material[0];

        /// <summary>무작위 옷 하나를 입힌다.</summary>
        public void Dress()
        {
            if (outfits.Length == 0) return;
            Material pick = outfits[Random.Range(0, outfits.Length)];
            if (pick == null) return;
            foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                r.sharedMaterial = pick;
        }
    }
}
