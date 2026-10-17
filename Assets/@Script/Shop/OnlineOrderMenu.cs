using DogShop.Core;
using DogShop.Data;
using DogShop.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Shop
{
    /// <summary>
    /// 계산대 옆 모니터. E 로 열면 온라인 주문 목록이 뜨고, 새 주문을 [주문 수락] 한다.
    /// 수락한 주문은 창고 포장대(<see cref="PackingStation"/>)에서 포장해야 출고된다.
    /// 주문마다 물건·판매가·명성과, 창고에 물건이 다 있는지를 같이 보여 준다 — 수락하기 전에 포장할 수 있는지 안다.
    /// ESC 나 창 밖 클릭으로 닫는다.
    /// </summary>
    public class OnlineOrderMenu : MonoBehaviour, IPointerMenu
    {
        const float Width = 640f;
        const float RowHeight = 64f;
        const float Pad = 16f;
        const int MaxRows = 6;

        public bool IsOpen { get; private set; }

        public void Open()
        {
            IsOpen = true;
            PointerMenus.SetOpen(this, true);
        }

        public void Close()
        {
            IsOpen = false;
            PointerMenus.SetOpen(this, false);
        }

        void OnDestroy() { if (IsOpen) PointerMenus.SetOpen(this, false); }

        /// <summary>창 배율 — 주문이 가장 많을 때의 크기로 화면 안에 맞춘다(UiSkin.Boost 배까지 키움).</summary>
        static float UiScale => UiSkin.ScaleFor(Width, Pad * 2f + 44f + MaxRows * (RowHeight + 6f) + 40f);

        Rect WindowRect()
        {
            // 보이는 주문 수만큼만 길게(없으면 한 줄짜리 안내 자리)
            int rows = 0;
            if (OnlineOrderManager.Instance != null)
                foreach (OnlineOrderManager.Order o in OnlineOrderManager.Instance.Orders) if (o.state != OnlineOrderManager.State.Waiting) rows++;
            rows = Mathf.Clamp(rows, 1, MaxRows);
            float h = Pad * 2f + 44f + rows * (RowHeight + 6f) + 40f;
            float s = UiScale;
            return new Rect((Screen.width / s - Width) * 0.5f, (Screen.height / s - h) * 0.5f, Width, h);
        }

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (!IsOpen) return false;
            float s = UiScale;
            return WindowRect().Contains(new Vector2(screenPos.x / s, (Screen.height - screenPos.y) / s));
        }

        void Update()
        {
            if (!IsOpen) return;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { PointerMenus.TakeEscape(); Close(); return; }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !ContainsPoint(PointerMenus.PickPosition())) Close();
        }

        GUIStyle title, line, small, warn, okStyle;

        void Styles()
        {
            if (title != null && title.font == UiSkin.Font) return;
            Color ink = new Color(0.33f, 0.2f, 0.1f);
            title = new GUIStyle { font = UiSkin.Font, fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            title.normal.textColor = ink;
            line = new GUIStyle { font = UiSkin.Font, fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            line.normal.textColor = ink;
            small = new GUIStyle(line) { fontSize = 13 };
            small.normal.textColor = new Color(0.47f, 0.36f, 0.26f);
            warn = new GUIStyle(small);
            warn.normal.textColor = new Color(0.8f, 0.3f, 0.25f);
            okStyle = new GUIStyle(small);
            okStyle.normal.textColor = new Color(0.2f, 0.55f, 0.25f);
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            OnlineOrderManager mgr = OnlineOrderManager.Instance;
            if (mgr == null) return;
            Styles();

            Matrix4x4 keep = GUI.matrix;
            float s = UiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.depth = -50;

            Rect win = WindowRect();
            GUI.Box(win, GUIContent.none, UiSkin.Tag(new Color(0.98f, 0.94f, 0.85f)));
            float x = win.x + Pad, w = win.width - Pad * 2f, y = win.y + Pad;

            GUI.Label(new Rect(x, y, w, 36f), "온라인 주문", title);
            if (UiSkin.Click(new Rect(win.xMax - Pad - 36f, y, 36f, 34f), "X", UiSkin.Button(UiSkin.Coral))) { Close(); GUI.matrix = keep; return; }
            y += 44f;

            int shown = 0;
            ProductCatalog catalog = InventoryManager.Instance.Catalog;
            // 처리할 주문(새 주문·수락함)을 위로 — 끝난 주문이 줄을 먹어 새 주문이 창 밖으로 밀리지 않게
            var sorted = new System.Collections.Generic.List<OnlineOrderManager.Order>();
            foreach (OnlineOrderManager.Order o in mgr.Orders)
                if (o.state == OnlineOrderManager.State.New || o.state == OnlineOrderManager.State.Accepted) sorted.Add(o);
            foreach (OnlineOrderManager.Order o in mgr.Orders)
                if (o.state == OnlineOrderManager.State.Shipped || o.state == OnlineOrderManager.State.Cancelled) sorted.Add(o);
            foreach (OnlineOrderManager.Order o in sorted)
            {
                if (shown >= MaxRows) break;
                shown++;

                var row = new Rect(x, y, w, RowHeight);
                GUI.Box(row, GUIContent.none, UiSkin.Tag(new Color(1f, 1f, 1f, 0.7f)));
                GUI.Label(new Rect(row.x + 12f, row.y + 6f, w - 190f, 26f), OnlineOrderManager.Describe(o), line);

                string missing;
                bool stock = mgr.HasStock(o, out missing);
                string info = o.Price(catalog) + "원 / 명성 +" + o.reputation + "   " + o.arriveHour.ToString("00") + ":00 주문";
                // "창고 부족" 경고가 같은 줄 오른쪽에 붙으면 그만큼 좁혀 겹치지 않게
                bool warnShown = !stock && o.state == OnlineOrderManager.State.New;
                GUI.Label(new Rect(row.x + 12f, row.y + 34f, warnShown ? w - 380f : w - 190f, 22f), info, small);

                var btn = new Rect(row.xMax - 170f, row.y + 12f, 158f, 40f);
                switch (o.state)
                {
                    case OnlineOrderManager.State.New:
                        if (UiSkin.Click(btn, "주문 수락", UiSkin.Button(UiSkin.Green))) mgr.Accept(o);
                        if (!stock) GUI.Label(new Rect(btn.x - 190f, row.y + 34f, 185f, 22f), "창고 부족: " + missing, warn);
                        break;
                    case OnlineOrderManager.State.Accepted:
                        GUI.Label(btn, stock ? "수락함 / 포장대로" : "수락함 / 창고 부족", stock ? okStyle : warn);
                        break;
                    case OnlineOrderManager.State.Shipped:
                        GUI.Label(btn, "출고 완료", okStyle);
                        break;
                    default:
                        GUI.Label(btn, "취소됨", warn);
                        break;
                }
                y += RowHeight + 6f;
            }

            if (shown == 0)
                GUI.Label(new Rect(x, y + 20f, w, 30f), "아직 주문이 없다. 영업시간(09:30~19:30)에 들어온다", line);

            GUI.Label(new Rect(x, win.yMax - Pad - 24f, w, 24f),
                "수락하면 창고 포장대에 상자가 놓인다. 오늘 안에 배송 못 하면 취소되고 명성이 깎인다", small);

            GUI.matrix = keep;
        }
    }
}
