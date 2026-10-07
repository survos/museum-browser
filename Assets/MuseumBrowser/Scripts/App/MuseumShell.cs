using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MuseumBrowser.App
{
    /// The app's chrome, in UI Toolkit: an opening screen (title, loading status or error,
    /// Enter), a toolbar with Help and About, and key hints along the bottom. Walking is
    /// paused while the opening screen or a panel is open. Esc closes panels; H or ?
    /// toggles help.
    [RequireComponent(typeof(UIDocument))]
    public sealed class MuseumShell : MonoBehaviour
    {
        [SerializeField] ExhibitionBuilder exhibition;
        [SerializeField] Visitor visitor;
        [SerializeField] Slideshow slideshow;
        [SerializeField] StyleSheet styleSheet;
        [SerializeField] float hintSeconds = 10f;

        const string Keys =
            "↑ ↓  walk\n← →  turn\nShift + ← →  step sideways\nW A S D  walk and step\nQ E  turn\n" +
            "Drag  look around\nScroll  zoom\nClick a work  walk to it\nSpace / Backspace  next / previous work\n" +
            "[ ]  previous / next gallery\nF  full screen\nH or ?  this help\nEsc  close\n\n" +
            "Controller: D-pad walk / turn · shoulders step sideways · A / B next / previous work · " +
            "Start floor plan (or enter) · Select help";

        VisualElement start, help, about, hints, debug, rooms, plan, here;
        readonly System.Collections.Generic.List<VisualElement> planBoxes = new();
        float planTotal = 1, planStart;
        Label debugText;
        InputAction toggleDebug;
        float debugRefresh;
        Label folioTitle, folioMeta, status;
        VisualElement fill;
        Button enter;
        InputAction escape, toggleHelp, planButton, fullScreenKey;
        float hintsUntil;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            var sheet = styleSheet ? styleSheet : Resources.Load<StyleSheet>("MuseumShell");
            if (sheet) root.styleSheets.Add(sheet);
            root.AddToClassList("shell");

            // Opening screen.
            start = Box(root, "start");
            Text(start, "brand", "Museado");
            Text(start, "tagline", "Historic Technology");
            // The embedding page passes the folio's title, so it shows before anything loads.
            folioTitle = Text(start, "folio-title", ExhibitionBuilder.QueryParam("title") ?? "");
            folioMeta = Text(start, "folio-meta", "");
            var bar = Box(start, "progress");
            fill = Box(bar, "progress-fill");
            status = Text(start, "status", "Opening the galleries…");
            enter = new Button(Enter) { text = "Enter the museum" };
            enter.AddToClassList("enter");
            enter.SetEnabled(false);
            start.Add(enter);

            // Toolbar.
            var tools = Box(root, "toolbar");
            var roomsButton = IconButton("Floor plan", "Floor plan: click a gallery to go there ( [ and ] step )", () => Toggle(rooms));
            roomsButton.AddToClassList("text-button");
            tools.Add(roomsButton);
            var slides = IconButton("Slides", "Slideshow, in time order", OpenSlideshow);
            slides.AddToClassList("text-button");
            tools.Add(slides);
            var full = IconButton("Full screen", "Full screen (F)", ToggleFullScreen);
            full.AddToClassList("text-button");
            tools.Add(full);
            tools.Add(IconButton("?", "Keyboard help", () => Toggle(help)));
            tools.Add(IconButton("i", "About", () => Toggle(about)));

            help = Panel(root, "help", "How to walk around", Keys);
            rooms = Panel(root, "rooms", "Floor plan", "");
            about = Panel(root, "about", "About", "");
            hints = Text(root, "hints",
                "↑↓ walk · ←→ turn · Shift+←→ step sideways · drag to look · scroll to zoom · click a work · H for help");
            hints.AddToClassList("hidden");

            debug = Box(root, "debug");
            debugText = Text(debug, "debug-text", "");
            if (!WantsDebug()) debug.AddToClassList("hidden");
            toggleDebug = new InputAction("Debug", InputActionType.Button, "<Keyboard>/backquote");
            toggleDebug.performed += _ => debug.EnableInClassList("hidden", !debug.ClassListContains("hidden"));
            toggleDebug.Enable();

            // Controller: Start opens the floor plan, Select toggles help, B closes.
            planButton = new InputAction("Floor plan", InputActionType.Button, "<Gamepad>/start");
            planButton.performed += _ => { if (IsOpen(start)) Enter(); else Toggle(rooms); };
            planButton.Enable();

            fullScreenKey = new InputAction("Full screen", InputActionType.Button, "<Keyboard>/f");
            fullScreenKey.performed += _ => ToggleFullScreen();
            fullScreenKey.Enable();

            escape = new InputAction("Close", InputActionType.Button, "<Keyboard>/escape");
            toggleHelp = new InputAction("Help", InputActionType.Button, "<Keyboard>/h");
            toggleHelp.AddBinding("<Keyboard>/slash");
            toggleHelp.AddBinding("<Gamepad>/select");
            escape.performed += _ => { if (slideshow == null || !slideshow.IsOpen) CloseAll(); };
            toggleHelp.performed += _ => { if (!IsOpen(start)) Toggle(help); };
            escape.Enable();
            toggleHelp.Enable();

            exhibition.Built += OnBuilt;
            exhibition.Failed += OnFailed;
            visitor.enabled = false;
        }

        void OnDisable()
        {
            exhibition.Built -= OnBuilt;
            exhibition.Failed -= OnFailed;
            escape?.Dispose();
            planButton?.Dispose();
            fullScreenKey?.Dispose();
            toggleDebug?.Dispose();
            toggleHelp?.Dispose();
        }

        static bool WantsDebug() => Application.absoluteURL?.Contains("debug=1") == true;

        void UpdateDebug()
        {
            if (debug.ClassListContains("hidden") || Time.unscaledTime < debugRefresh) return;
            debugRefresh = Time.unscaledTime + 0.5f;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{Application.platform} · api {Application.absoluteURL} · pending {MuseumBrowser.Core.Diag.PendingCount}");
            foreach (var p in MuseumBrowser.Core.Diag.Pending)
                sb.AppendLine($"  … {p.Value.ElapsedMilliseconds / 1000f:0.0}s {MuseumBrowser.Core.Diag.Short(p.Key)}");
            foreach (var line in System.Linq.Enumerable.Reverse(MuseumBrowser.Core.Diag.Lines).Take(24))
                sb.AppendLine(line);
            debugText.text = sb.ToString();
        }

        void UpdatePlan()
        {
            if (plan == null || !IsOpen(rooms)) return;
            int current = visitor.CurrentRoom;
            for (int i = 0; i < planBoxes.Count; i++) planBoxes[i].EnableInClassList("current", i == current);
            float z = exhibition.transform.InverseTransformPoint(visitor.transform.position).z;
            here.style.left = Length.Percent(Mathf.Clamp01((z - planStart) / planTotal) * 100f);
        }

        void Update()
        {
            UpdateDebug();
            UpdatePlan();
            if (IsOpen(start) && !status.ClassListContains("error"))
            {
                fill.style.width = Length.Percent(100f * exhibition.Progress);
                if (!string.IsNullOrEmpty(exhibition.Stage)) status.text = exhibition.Stage;
            }
            if (hintsUntil > 0 && Time.time > hintsUntil)
            {
                hints.AddToClassList("hidden");
                hintsUntil = 0;
            }
        }

        void OnBuilt(ExhibitionBuilder b)
        {
            folioTitle.text = b.Title;
            folioMeta.text = $"{b.Works.Count} works · {b.RoomCount} room{(b.RoomCount == 1 ? "" : "s")}";
            enter.SetEnabled(true);
            enter.Focus();
            // Floor plan: the suite drawn to scale, one box per gallery; click to go there.
            var list = rooms.Q<Label>(className: "panel-body");
            list.text = $"{b.RoomCount} galleries · click one to go there · [ ] step";
            plan = new VisualElement();
            plan.AddToClassList("plan");
            rooms.Insert(rooms.IndexOf(list) + 1, plan);
            planBoxes.Clear();
            var stops = b.RoomStops;
            float total = stops.Count == 0 ? 1 : stops[^1].Start + stops[^1].Length - stops[0].Start;
            for (int i = 0; i < stops.Count; i++)
            {
                int index = i;
                var stop = stops[i];
                var box = new Button(() => { visitor.GoToRoom(index); CloseAll(); }) { tooltip = $"{stop.Title} · {stop.Works} works" };
                box.AddToClassList("plan-room");
                box.style.left = Length.Percent(100f * (stop.Start - stops[0].Start) / total);
                box.style.width = Length.Percent(100f * stop.Length / total);
                var name = new Label(stop.Title);
                name.AddToClassList("plan-title");
                box.Add(name);
                var count = new Label($"{stop.Works}");
                count.AddToClassList("plan-count");
                box.Add(count);
                plan.Add(box);
                planBoxes.Add(box);
            }
            here = new VisualElement();
            here.AddToClassList("plan-here");
            plan.Add(here);
            planTotal = total;
            planStart = stops.Count > 0 ? stops[0].Start : 0;

            var about = this.about.Q<Label>(className: "panel-body");
            var f = b.Folio;
            var lines = new System.Collections.Generic.List<string> { $"<b>{b.Title}</b>" };
            if (!string.IsNullOrEmpty(b.FolioCode)) lines.Add($"Folio  {b.FolioCode}");
            if (b.TotalRecords > 0) lines.Add($"Records  {b.TotalRecords:N0} (showing {b.Works.Count} in {b.RoomCount} rooms)");
            if (!string.IsNullOrEmpty(f?.License)) lines.Add($"License  {f.License}");
            if (!string.IsNullOrEmpty(f?.Credit)) lines.Add($"Credit  {f.Credit}");
            if (!string.IsNullOrEmpty(b.FolioCode)) lines.Add($"Source  {b.SiteUrl}/en/f/{b.FolioCode}");
            lines.Add("");
            lines.Add("A walk-through generated from the collection's records: each room is a selection of works, " +
                      "hung by their recorded size (photographs as prints).");
            lines.Add("");
            lines.Add("Museado — Historic Technology");
            about.text = string.Join("\n", lines);
        }

        void OnFailed(string message)
        {
            status.text = "The galleries could not be opened.\n" + message;
            status.AddToClassList("error");
        }

        void Enter()
        {
            start.AddToClassList("hidden");
            visitor.enabled = true;
            hints.RemoveFromClassList("hidden");
            hintsUntil = Time.time + hintSeconds;
        }

        /// Browsers only allow full screen from a user gesture; a click or key press is one, and
        /// Unity's Web player applies the change on that same input.
        static void ToggleFullScreen() => Screen.fullScreen = !Screen.fullScreen;

        void OpenSlideshow()
        {
            if (IsOpen(start) || slideshow == null) return;
            help.AddToClassList("hidden");
            about.AddToClassList("hidden");
            slideshow.Open();
        }

        void Toggle(VisualElement panel)
        {
            bool open = !IsOpen(panel);
            help.AddToClassList("hidden");
            about.AddToClassList("hidden");
            rooms.AddToClassList("hidden");
            if (open) panel.RemoveFromClassList("hidden");
            if (!IsOpen(start)) visitor.enabled = !open;
        }

        void CloseAll()
        {
            if (IsOpen(start)) return;
            help.AddToClassList("hidden");
            about.AddToClassList("hidden");
            rooms.AddToClassList("hidden");
            visitor.enabled = true;
        }

        static bool IsOpen(VisualElement e) => !e.ClassListContains("hidden");

        static VisualElement Box(VisualElement parent, string cls)
        {
            var e = new VisualElement();
            e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        static Label Text(VisualElement parent, string cls, string text)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        static Button IconButton(string glyph, string tooltip, System.Action click)
        {
            var b = new Button(click) { text = glyph, tooltip = tooltip };
            b.AddToClassList("icon-button");
            return b;
        }

        VisualElement Panel(VisualElement root, string cls, string title, string body)
        {
            var p = Box(root, "panel");
            p.AddToClassList(cls);
            p.AddToClassList("hidden");
            Text(p, "panel-title", title);
            Text(p, "panel-body", body);
            var close = new Button(CloseAll) { text = "Close" };
            close.AddToClassList("panel-close");
            p.Add(close);
            return p;
        }
    }
}
