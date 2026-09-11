using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NeonVanguard
{
    /// <summary>
    /// 升级三选一。升级时把时间冻结，给玩家一个"思考时刻"——
    /// 这是肉鸽玩法的心跳：每次选择都会改变接下来的打法。
    /// </summary>
    public class UpgradePanel : MonoBehaviour
    {
        GameObject _root;
        List<UpgradeCard> _pool;
        readonly List<UpgradeCard> _offered = new List<UpgradeCard>();
        readonly CardView[] _views = new CardView[3];

        GameObject _rerollText;
        int _rerollsLeft;
        bool _visible;

        class CardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public int Index;
            public UpgradePanel Owner;
            public Image Bg;
            public Image Border;
            public Image Hover;
            public Color Accent;

            public void OnPointerClick(PointerEventData e) { if (Owner != null) Owner.Choose(Index); }

            public void OnPointerEnter(PointerEventData e)
            {
                if (Hover != null) Hover.enabled = true;
                transform.localScale = Vector3.one * 1.035f;
            }

            public void OnPointerExit(PointerEventData e)
            {
                if (Hover != null) Hover.enabled = false;
                transform.localScale = Vector3.one;
            }
        }

        public void Build(Canvas canvas)
        {
            _pool = UpgradeLibrary.CreateAll();
            _rerollsLeft = 1;

            var root = new GameObject("UpgradeRoot");
            root.transform.SetParent(canvas.transform, false);
            var rootRt = root.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            _root = root;

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.03f, 0.06f, 0.80f);
            bg.raycastTarget = true;

            var title = TextRenderer.UiText(root.transform, "Title", "选 择 强 化", 56f, Color.white,
                TextAnchor.MiddleCenter, true);
            TextRenderer.SetupRect(title, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -110f), new Vector2(1200f, 80f));

            var sub = TextRenderer.UiText(root.transform, "Sub", "点击卡片，或按 1 / 2 / 3 选择", 22f,
                Palette.TextDim, TextAnchor.MiddleCenter, false);
            TextRenderer.SetupRect(sub, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -170f), new Vector2(1200f, 34f));

            for (int i = 0; i < _views.Length; i++)
            {
                _views[i] = BuildCard(root.transform, i);
            }

            _rerollText = TextRenderer.UiText(root.transform, "Reroll",
                "还有 1 次重抽机会 · 按 R 重抽", 20f, Palette.Warning, TextAnchor.MiddleCenter, false);
            TextRenderer.SetupRect(_rerollText, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 90f), new Vector2(900f, 30f));

            _root.SetActive(false);
        }

        CardView BuildCard(Transform parent, int index)
        {
            var go = new GameObject("Card" + index);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CardView>();
            view.Index = index;
            view.Owner = this;

            var bgImg = go.AddComponent<Image>();
            bgImg.color = new Color(0.075f, 0.09f, 0.145f, 0.98f);
            bgImg.raycastTarget = true;
            view.Bg = bgImg;

            // 卡片尺寸 400x460，三张横向排开
            TextRenderer.SetupRect(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2((index - 1) * 440f, -20f), new Vector2(400f, 460f));

            // 稀有度色条（顶部）
            var accent = new GameObject("Accent");
            accent.transform.SetParent(go.transform, false);
            var accentImg = accent.AddComponent<Image>();
            accentImg.color = Color.white;
            accentImg.raycastTarget = false;
            TextRenderer.SetupRect(accent, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -6f), new Vector2(0f, 12f));
            view.Border = accentImg;

            // hover 时的高亮描边
            var hover = new GameObject("Hover");
            hover.transform.SetParent(go.transform, false);
            var hoverImg = hover.AddComponent<Image>();
            hoverImg.color = new Color(1f, 1f, 1f, 0.07f);
            hoverImg.raycastTarget = false;
            TextRenderer.SetupRect(hover, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hoverImg.enabled = false;
            view.Hover = hoverImg;

            var rarity = TextRenderer.UiText(go.transform, "Rarity", "", 18f, Color.white,
                TextAnchor.MiddleCenter, false);
            TextRenderer.SetupRect(rarity, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -38f), new Vector2(360f, 26f));

            var name = TextRenderer.UiText(go.transform, "Name", "", 30f, Color.white,
                TextAnchor.MiddleCenter, true);
            TextRenderer.SetupRect(name, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -96f), new Vector2(360f, 46f));

            var icon = new GameObject("Icon");
            icon.transform.SetParent(go.transform, false);
            var iconImg = icon.AddComponent<Image>();
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;
            TextRenderer.SetupRect(icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 30f), new Vector2(120f, 120f));

            var desc = TextRenderer.UiText(go.transform, "Desc", "", 21f,
                new Color(0.78f, 0.83f, 0.94f), TextAnchor.UpperCenter, false);
            TextRenderer.SetupRect(desc, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 116f), new Vector2(344f, 120f));

            var stacks = TextRenderer.UiText(go.transform, "Stacks", "", 18f, Palette.TextDim,
                TextAnchor.MiddleCenter, false);
            TextRenderer.SetupRect(stacks, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 52f), new Vector2(360f, 26f));

            view.Accent = Color.white;
            return view;
        }

        // ------------------------------------------------------------------

        public void Show(int level)
        {
            _visible = true;
            _root.SetActive(true);
            Roll();
            _rerollsLeft = 1;
            UpdateRerollText();
        }

        public void Hide()
        {
            _visible = false;
            if (_root != null) _root.SetActive(false);
        }

        /// <summary>重开一局时清空已选层数，保证 build 不跨局继承。</summary>
        public void ResetForNewRun()
        {
            Hide();
            _pool = UpgradeLibrary.CreateAll();
            _offered.Clear();
            _rerollsLeft = 1;
        }

        void Update()
        {
            if (!_visible) return;
            if (Input.GetKeyDown(KeyCode.R)) Reroll();
        }

        void Roll()
        {
            var drawn = UpgradeLibrary.Draw(_pool, 3);
            _offered.Clear();
            _offered.AddRange(drawn);

            for (int i = 0; i < _views.Length; i++)
            {
                var view = _views[i];
                var card = i < _offered.Count ? _offered[i] : null;
                view.gameObject.SetActive(card != null);
                if (card == null) continue;

                var name = view.transform.Find("Name").gameObject;
                var rarity = view.transform.Find("Rarity").gameObject;
                var desc = view.transform.Find("Desc").gameObject;
                var stacks = view.transform.Find("Stacks").gameObject;
                var icon = view.transform.Find("Icon");

                TextRenderer.SetText(name, (i + 1) + ".  " + card.Name);
                TextRenderer.SetColor(name, Color.white);
                TextRenderer.SetText(rarity, card.RarityName);
                TextRenderer.SetColor(rarity, card.RarityColor);
                TextRenderer.SetText(desc, card.Desc);
                TextRenderer.SetText(stacks,
                    card.MaxStacks < 90 ? string.Format("已选 {0} / {1}", card.Stacks, card.MaxStacks)
                        : (card.Stacks > 0 ? "已选 " + card.Stacks : ""));

                view.Border.color = card.RarityColor;
                var iconImg = icon.GetComponent<Image>();
                if (iconImg != null) iconImg.color = new Color(card.RarityColor.r, card.RarityColor.g, card.RarityColor.b, 0.85f);
                view.Accent = card.RarityColor;
            }
        }

        void UpdateRerollText()
        {
            if (_rerollText == null) return;
            TextRenderer.SetText(_rerollText, _rerollsLeft > 0
                ? string.Format("按 R 重抽（剩余 {0} 次）", _rerollsLeft)
                : "没有重抽机会了");
            TextRenderer.SetColor(_rerollText, _rerollsLeft > 0 ? Palette.Warning : Palette.TextDim);
        }

        void Reroll()
        {
            if (_rerollsLeft <= 0) return;
            _rerollsLeft--;
            AudioKit2.PlayUi(AudioKit.Select(), 0.6f);
            Roll();
            UpdateRerollText();
        }

        public void Choose(int index)
        {
            if (!_visible) return;
            if (index < 0 || index >= _offered.Count) return;

            var card = _offered[index];
            card.Stacks++;

            var gm = GameManager.I;
            if (gm != null && gm.Player != null)
            {
                gm.Player.ApplyUpgrade(card);
                AudioKit2.PlayUi(AudioKit.LevelUp(), 0.85f);
                if (Juice.I != null)
                {
                    Juice.I.PopText(card.Name, gm.Player.transform.position + Vector3.up * 2.6f,
                        card.RarityColor, 1.5f, 1.4f);
                }
            }

            Hide();
            _visible = false;
            if (gm != null) gm.OnUpgradeChosen();
        }
    }
}
