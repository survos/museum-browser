using System.Collections.Generic;
using System.Linq;
using MuseumBrowser.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MuseumBrowser.App
{
    /// Full-screen slideshow of the exhibition's works in time order, with a year
    /// timeline along the bottom (after fortepan.hu's home page). UI Toolkit only:
    /// the strip snaps to a slide after a drag or swipe; arrows, Home/End and the
    /// timeline jump; Esc closes. Images load lazily around the current slide.
    [RequireComponent(typeof(UIDocument))]
    public sealed class Slideshow : MonoBehaviour
    {
        [SerializeField] ExhibitionBuilder exhibition;
        [SerializeField] Visitor visitor;
        [SerializeField] StyleSheet styleSheet;

        public bool IsOpen => root != null && !root.ClassListContains("hidden");

        VisualElement root, viewport, track, timeline, marker;
        Label caption, meta, counter;
        readonly List<VisualElement> slides = new();
        List<WallCard> cards = new();
        int index;
        float dragStartX, dragDelta;
        bool dragging;
        int minYear, maxYear;
        InputAction next, previous, first, last, close;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>().rootVisualElement;
            doc.Clear();
            var sheet = styleSheet ? styleSheet : Resources.Load<StyleSheet>("Slideshow");
            if (sheet) doc.styleSheets.Add(sheet);
            doc.pickingMode = PickingMode.Ignore;

            root = Add(doc, "slideshow");
            root.AddToClassList("hidden");
            viewport = Add(root, "viewport");
            track = Add(viewport, "track");
            var info = Add(root, "info");
            meta = Label(info, "meta");
            caption = Label(info, "caption");
            counter = Label(root, "counter");
            var closeButton = new Button(Close) { text = "✕", tooltip = "Back to the museum (Esc)" };
            closeButton.AddToClassList("close");
            root.Add(closeButton);
            timeline = Add(root, "timeline");
            marker = Add(timeline, "marker");

            viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            viewport.RegisterCallback<WheelEvent>(e => { if (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) && Mathf.Abs(e.delta.x) > 2) Go(index + (int)Mathf.Sign(e.delta.x)); });
            viewport.RegisterCallback<GeometryChangedEvent>(_ => Layout(animate: false));
            timeline.RegisterCallback<PointerDownEvent>(e => { JumpToTimeline(e.localPosition.x); timeline.CapturePointer(e.pointerId); });
            timeline.RegisterCallback<PointerMoveEvent>(e => { if (timeline.HasPointerCapture(e.pointerId)) JumpToTimeline(e.localPosition.x); });
            timeline.RegisterCallback<PointerUpEvent>(e => timeline.ReleasePointer(e.pointerId));

            next = Key("<Keyboard>/rightArrow", () => Go(index + 1));
            previous = Key("<Keyboard>/leftArrow", () => Go(index - 1));
            first = Key("<Keyboard>/home", () => Go(0));
            last = Key("<Keyboard>/end", () => Go(cards.Count - 1));
            close = Key("<Keyboard>/escape", Close);
        }

        void OnDisable()
        {
            foreach (var a in new[] { next, previous, first, last, close }) a?.Dispose();
        }

        InputAction Key(string binding, System.Action action)
        {
            var a = new InputAction(binding, InputActionType.Button, binding);
            a.performed += _ => { if (IsOpen) action(); };
            a.Enable();
            return a;
        }

        public void Open()
        {
            // Time order; works without a year go last.
            cards = exhibition.Cards.OrderBy(c => c.Year ?? int.MaxValue).ToList();
            if (cards.Count == 0) return;
            BuildSlides();
            BuildTimeline();
            visitor.enabled = false;
            root.RemoveFromClassList("hidden");
            Go(0, animate: false);
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.AddToClassList("hidden");
            visitor.enabled = true;
        }

        void BuildSlides()
        {
            track.Clear();
            slides.Clear();
            foreach (var _ in cards)
            {
                var slide = Add(track, "slide");
                Add(slide, "photo");
                slides.Add(slide);
            }
        }

        void BuildTimeline()
        {
            timeline.Clear();
            var years = cards.Where(c => c.Year.HasValue).Select(c => c.Year.Value).ToList();
            minYear = years.Count > 0 ? years.Min() / 10 * 10 : 1900;
            maxYear = years.Count > 0 ? (years.Max() / 10 + 1) * 10 : 2000;
            for (int decade = minYear; decade <= maxYear; decade += 10)
            {
                var tick = Add(timeline, "tick");
                tick.style.left = Length.Percent(Pos(decade) * 100f);
                var label = Label(tick, "tick-label");
                label.text = decade.ToString();
            }
            // One faint dot per work: shows where the collection is dense.
            foreach (var y in years)
            {
                var dot = Add(timeline, "dot");
                dot.style.left = Length.Percent(Pos(y) * 100f);
            }
            marker = Add(timeline, "marker");
        }

        float Pos(int year) => Mathf.InverseLerp(minYear, maxYear, year);

        void JumpToTimeline(float x)
        {
            float t = Mathf.Clamp01(x / Mathf.Max(1f, timeline.resolvedStyle.width));
            // The ends are exact: the far right is the last work, the far left the first.
            if (t >= 0.985f) { Go(cards.Count - 1); return; }
            if (t <= 0.015f) { Go(0); return; }
            int year = Mathf.RoundToInt(Mathf.Lerp(minYear, maxYear, t));
            // Among works tied for nearest, moving forward in time lands on the last of them,
            // moving back on the first.
            bool forward = !cards[index].Year.HasValue || year >= cards[index].Year.Value;
            int best = index, bestGap = int.MaxValue;
            for (int i = 0; i < cards.Count; i++)
            {
                if (!cards[i].Year.HasValue) continue;
                int gap = Mathf.Abs(cards[i].Year.Value - year);
                if (gap < bestGap || (gap == bestGap && forward)) { bestGap = gap; best = i; }
            }
            Go(best);
        }

        void Go(int i, bool animate = true)
        {
            if (cards.Count == 0) return;
            index = Mathf.Clamp(i, 0, cards.Count - 1);
            dragDelta = 0;
            Layout(animate);
            var c = cards[index];
            var l = c.Label ?? new WallLabel();
            meta.text = string.Join(" · ", new[] { c.Year?.ToString(), l.Place, l.Creator ?? l.Credit }.Where(s => !string.IsNullOrEmpty(s)));
            caption.text = l.Title ?? c.Title ?? "";
            counter.text = $"{index + 1} / {cards.Count}";
            if (c.Year.HasValue)
            {
                marker.style.display = DisplayStyle.Flex;
                marker.style.left = Length.Percent(Pos(c.Year.Value) * 100f);
            }
            else marker.style.display = DisplayStyle.None;
            for (int k = index - 2; k <= index + 2; k++) EnsureImage(k);
        }

        void Layout(bool animate)
        {
            float width = viewport.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) return;
            foreach (var s in slides) s.style.width = width;
            track.EnableInClassList("animate", animate && !dragging);
            track.style.translate = new Translate(-index * width + dragDelta, 0);
        }

        void EnsureImage(int i)
        {
            if (i < 0 || i >= cards.Count) return;
            var photo = slides[i].Q(className: "photo");
            if (photo.userData != null) return;
            photo.userData = true;
            var url = cards[i].Image?.Medium ?? cards[i].Image?.Best;
            if (url != null) LoadInto(photo, url);
        }

        static async void LoadInto(VisualElement photo, string url)
        {
            try { photo.style.backgroundImage = new StyleBackground(await ImageLoader.LoadAsync(url)); }
            catch (System.Exception e) { Debug.LogWarning(e.Message); }
        }

        void OnPointerDown(PointerDownEvent e)
        {
            dragging = true;
            dragStartX = e.position.x;
            viewport.CapturePointer(e.pointerId);
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!dragging) return;
            dragDelta = e.position.x - dragStartX;
            Layout(animate: false);
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!dragging) return;
            dragging = false;
            viewport.ReleasePointer(e.pointerId);
            float width = viewport.resolvedStyle.width;
            // Snap: a drag past a fifth of the screen moves one slide.
            int step = Mathf.Abs(dragDelta) > width * 0.2f ? -(int)Mathf.Sign(dragDelta) : 0;
            Go(index + step);
        }

        static VisualElement Add(VisualElement parent, string cls)
        {
            var e = new VisualElement();
            e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        static Label Label(VisualElement parent, string cls)
        {
            var l = new Label();
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }
    }
}
