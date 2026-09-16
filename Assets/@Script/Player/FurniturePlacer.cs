using DogShop.Shop;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Player
{
    /// <summary>
    /// 가구를 들어 옮기는 <b>배치 모드</b>. F로 켜고 끈다.
    ///
    /// 모드를 따로 둔 이유: E는 이미 진열대에 물건을 놓고 창고 선반에서 담는 데 쓰고 있다.
    /// 같은 키로 가구까지 집게 하면 진열하다가 진열대를 통째로 들어버린다.
    ///
    /// 들고 있는 동안 가구는 손이 아니라 <b>조준한 바닥 위에 떠 있고</b>, 놓을 수 있으면 초록,
    /// 겹치면 빨강으로 물든다. 진열대를 얼굴 앞에 들고 다니는 것보다 어디에 놓이는지가 잘 보인다.
    /// </summary>
    public class FurniturePlacer : MonoBehaviour
    {
        /// <summary>조준한 바닥이 이보다 멀면 놓지 않는다. 상호작용 거리와 같게 맞춘다.</summary>
        const float PlaceRange = 4.5f;

        const float RayRange = 60f;

        /// <summary>바닥 높이 허용 오차. 매장·창고 바닥은 둘 다 y=0 이다.</summary>
        const float GroundLevel = 0.06f;

        static readonly Color OkTint = new Color(0.45f, 1f, 0.55f);
        static readonly Color BadTint = new Color(1f, 0.42f, 0.38f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Camera cam;
        Transform player;
        PlayerCarry carry;

        PlaceableFurniture held;
        Quaternion heldRotation = Quaternion.identity;
        Renderer[] heldRenderers;
        MaterialPropertyBlock block;

        Vector3 restorePosition;
        Quaternion restoreRotation;

        bool valid;
        string reason = "";

        public bool Active { get; private set; }
        public bool Holding => held != null;

        void Awake()
        {
            cam = GetComponentInChildren<Camera>();
            carry = GetComponentInParent<PlayerCarry>();
            player = carry != null ? carry.transform : transform;
            block = new MaterialPropertyBlock();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.fKey.wasPressedThisFrame) Toggle();
            if (!Active) return;

            if (held != null)
            {
                // 벽걸이형은 벽이 방향을 정하므로 회전 키가 의미 없다
                if (keyboard.qKey.wasPressedThisFrame && !held.WallMounted) heldRotation *= Quaternion.Euler(0f, 90f, 0f);
                Follow();
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (held != null) TryPlace();
                else TryPick();
            }
        }

        void Toggle()
        {
            if (Active && held != null) Cancel();

            Active = !Active;
            reason = "";
        }

        // ---- 집기 ----

        void TryPick()
        {
            if (carry != null && carry.IsHolding) { reason = "상자를 먼저 내려놓을 것"; return; }

            RaycastHit hit;
            if (!Aim(out hit)) return;

            PlaceableFurniture target = hit.collider.GetComponentInParent<PlaceableFurniture>();
            if (target == null) { reason = "옮길 수 있는 가구가 아니다"; return; }
            if (Vector3.Distance(player.position, hit.point) > PlaceRange) { reason = "너무 멀다"; return; }

            // 물건이 올려진 진열대는 못 옮긴다. 옮기려면 상자로 다 비워야 한다 —
            // 진열을 지우고 옮기면 재고가 어디로 갔는지 설명할 방법이 없고,
            // 그 수고가 배치를 함부로 바꾸지 않게 만드는 비용이기도 하다.
            ShelfTable shelf = target.GetComponent<ShelfTable>();
            if (shelf != null)
            {
                int stocked = 0;
                for (int i = 0; i < shelf.SlotCount; i++) stocked += shelf.CountAt(i);

                if (stocked > 0)
                {
                    reason = "진열된 물건 " + stocked + "개를 상자로 먼저 빼낼 것";
                    return;
                }
            }

            held = target;
            heldRotation = target.transform.rotation;
            restorePosition = target.transform.position;
            restoreRotation = target.transform.rotation;

            heldRenderers = target.GetComponentsInChildren<Renderer>(true);
            SetColliders(target, false);
            reason = "";
        }

        // ---- 따라다니기 ----

        void Follow()
        {
            RaycastHit hit;
            bool aimed = Aim(out hit);

            if (!aimed || !IsGround(hit))
            {
                valid = false;
                reason = "바닥을 조준할 것";
                Tint(BadTint);
                return;
            }

            if (Vector3.Distance(player.position, hit.point) > PlaceRange)
            {
                valid = false;
                reason = "너무 멀다 — 가까이 갈 것";
                Tint(BadTint);
                return;
            }

            Vector3 target = new Vector3(hit.point.x, hit.point.y + held.BottomOffset, hit.point.z);
            Quaternion rotation = heldRotation;

            // 벽걸이형은 플레이어가 고른 회전을 무시하고 벽이 정한 방향을 따른다.
            // 뒤판이 평면인 가구는 벽에 붙어야만 말이 된다.
            if (held.WallMounted)
            {
                Vector3 snapped;
                Quaternion facing;
                if (held.TrySnapToWall(hit.point, out snapped, out facing))
                {
                    target = snapped;
                    rotation = facing;
                }
                else
                {
                    held.transform.SetPositionAndRotation(target, rotation);
                    valid = false;
                    reason = "벽걸이형이다 — 벽 앞 바닥을 조준할 것";
                    Tint(BadTint);
                    return;
                }
            }

            held.transform.SetPositionAndRotation(target, rotation);

            string why;
            valid = held.Fits(target, rotation, player, out why);
            reason = valid ? "" : why;
            Tint(valid ? OkTint : BadTint);
        }

        // ---- 놓기 ----

        void TryPlace()
        {
            if (!valid) return;

            PlaceableFurniture placed = held;
            Release();

            // 콜라이더를 다시 켠 뒤라야 NavMesh가 깎이고, 그래야 설 자리를 제대로 찾는다.
            // 창고 선반은 손님이 가지 않으므로 접근점이 필요 없다.
            if (ShelfManager.Instance != null) ShelfManager.Instance.RefreshApproach(placed);
        }

        void Cancel()
        {
            if (held == null) return;

            held.transform.SetPositionAndRotation(restorePosition, restoreRotation);
            Release();
        }

        void Release()
        {
            Tint(null);
            SetColliders(held, true);

            held = null;
            heldRenderers = null;
            valid = false;
            reason = "";
        }

        /// <summary>들고 있는 동안 콜라이더를 끈다 — 자기 자신과 겹침 판정을 하지 않게.</summary>
        static void SetColliders(PlaceableFurniture target, bool on)
        {
            if (target == null) return;

            foreach (Collider c in target.GetComponentsInChildren<Collider>(true)) c.enabled = on;
            target.SetCarving(on);   // 들고 다니는 내내 NavMesh를 다시 깎지 않게
        }

        void Tint(Color? color)
        {
            if (heldRenderers == null) return;

            foreach (Renderer r in heldRenderers)
            {
                if (r == null) continue;

                r.GetPropertyBlock(block);
                if (color.HasValue)
                {
                    block.SetColor(BaseColorId, color.Value);
                    block.SetColor(ColorId, color.Value);
                }
                else
                {
                    block.Clear();
                }
                r.SetPropertyBlock(block);
            }
        }

        // ---- 조준 ----

        bool Aim(out RaycastHit hit)
        {
            hit = default(RaycastHit);
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(DogShop.Core.PointerMenus.PickPosition());
            return Physics.Raycast(ray, out hit, RayRange, ~0, QueryTriggerInteraction.Ignore);
        }

        static bool IsGround(RaycastHit hit) => hit.normal.y > 0.7f && hit.point.y <= GroundLevel;

        // ---- 화면 ----

        void OnGUI()
        {
            if (!Active) return;

            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 18 };
            style.normal.textColor = held != null && !valid
                ? new Color(1f, 0.55f, 0.45f)
                : new Color(0.7f, 1f, 0.75f);

            string text;
            if (held == null) text = "배치 모드 — 가구를 조준하고 [E]   ·   [F] 끄기";
            else if (held.WallMounted) text = held.DisplayName + " (벽걸이) — [E] 놓기   [F] 취소";
            else text = held.DisplayName + " — [E] 놓기   [Q] 회전   [F] 취소";

            if (reason.Length > 0) text += "\n" + reason;

            GUI.Label(new Rect((Screen.width - 620f) * 0.5f, 24f, 620f, 56f), text, style);
        }
    }
}
