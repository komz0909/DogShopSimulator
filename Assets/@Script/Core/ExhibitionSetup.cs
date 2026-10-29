using System.Collections.Generic;
using DogShop.Player;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 시연 모드 첫 화면을 차린다 — 진열대를 더 세우고, 수요 큰 상품을 몇 가지 진열해 두고, 카메라를 입구 쪽으로 돌린다.
    /// 값은 전부 <see cref="Exhibition"/> 에 있다. 시연일 때만 <see cref="GameManager"/> 가 붙인다.
    ///
    /// 다른 매니저의 Start(레벨 적용·매장 넓이·창고 재고 깔기)가 끝난 뒤에 해야 하므로 한 프레임 미룬다.
    /// 세이브는 하지 않는 모드라 불러오기와 겹칠 일이 없다.
    /// </summary>
    public class ExhibitionSetup : MonoBehaviour
    {
        System.Collections.IEnumerator Start()
        {
            yield return null;

            PlaceShelves();
            Prestock();
            FaceEntrance();

            if (GameManager.Instance != null) GameManager.Instance.OnDayChanged += HandleDayChanged;
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnDayChanged -= HandleDayChanged;
        }

        /// <summary>레벨업하는 날 아침, 승급비가 모자라면 모자란 만큼 채워 준다(<see cref="Exhibition.SupportReserve"/>).</summary>
        static void HandleDayChanged(int day)
        {
            if (day != Exhibition.LevelUpDay) return;
            ShopLevelManager levels = ShopLevelManager.Instance;
            if (levels == null || levels.IsMaxLevel) return;

            int need = levels.Next.upgradeCost + Exhibition.SupportReserve - GameManager.Instance.Money;
            if (need <= 0) return;

            GameManager.Instance.AddMoney(need);
            var hud = FindAnyObjectByType<UI.GameHud>();
            if (hud != null) hud.Toast("할아버지의 응원금 +" + need + "원! 오늘은 강아지를 키워 가게를 넓히자", new Color(1f, 0.84f, 0.38f));
        }

        static void PlaceShelves()
        {
            FurnitureShop shop = FurnitureShop.Instance;
            if (shop == null) return;

            for (int i = 0; i < Exhibition.StartShelfCatalog.Length; i++)
            {
                GameObject placed = shop.Spawn(Exhibition.StartShelfCatalog[i], Exhibition.StartShelfPosition[i],
                                               Quaternion.Euler(0f, Exhibition.StartShelfYaw[i], 0f));
                if (placed == null) continue;

                // 등록만으로는 접근점이 원점에 남는다 — 손님이 서는 자리를 잡아 준다
                PlaceableFurniture furniture = placed.GetComponent<PlaceableFurniture>();
                if (furniture != null && ShelfManager.Instance != null) ShelfManager.Instance.RefreshApproach(furniture);
            }
        }

        /// <summary>열린 상품을 수요 큰 순서로 <see cref="Exhibition.PrestockKinds"/> 가지, 창고에서 덜어 진열대에 올린다.</summary>
        static void Prestock()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            var products = new List<int>();
            for (int i = 0; i < inv.Catalog.Count; i++)
                if (inv.IsUnlocked(i)) products.Add(i);
            products.Sort((a, b) =>
            {
                int byDemand = inv.Catalog.Get(b).demandWeight.CompareTo(inv.Catalog.Get(a).demandWeight);
                return byDemand != 0 ? byDemand : a.CompareTo(b);
            });

            int kinds = 0;
            for (int k = 0; k < products.Count && kinds < Exhibition.PrestockKinds; k++)
            {
                int product = products[k];
                int want = Mathf.Min(Exhibition.PrestockEach, inv.StorageOf(product));
                if (want <= 0) continue;

                // 올라간 만큼만 창고에서 뺀다. 자리가 없으면(부피가 맞는 칸이 다 찼으면) 다음 상품으로
                // 간식은 간식 매대부터 — 배송 때 저절로 채워지는 그 자리를 처음부터 보여 준다
                int placed = inv.Catalog.Get(product).snack ? inv.FillSnackShelves(product, want) : 0;
                if (placed < want)
                {
                    int before = inv.ShelfOf(product);
                    inv.PlaceOnShelf(product, want - placed);
                    placed += inv.ShelfOf(product) - before;
                }
                for (int n = 0; n < placed; n++) inv.TryConsumeStorage(product);
                if (placed > 0) kinds++;
            }
        }

        static void FaceEntrance()
        {
            ThirdPersonCamera cam = FindAnyObjectByType<ThirdPersonCamera>();
            if (cam == null) return;

            cam.FaceYaw(Exhibition.StartYaw);
            // 몸도 같은 쪽을 보게 한다 — 카메라만 돌리면 첫 화면에서 주인이 관람객을 마주 본다
            Transform body = cam.transform.parent != null ? cam.transform.parent : cam.transform;
            body.rotation = Quaternion.Euler(0f, Exhibition.StartYaw, 0f);
        }
    }
}
