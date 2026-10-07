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
        [SerializeField] StyleSheet styleSheet;
        [SerializeField] float hintSeconds = 10f;

        const string Keys =
            "↑ ↓  walk\n← →  turn\nShift + ← →  step sideways\nW A S D  walk and step\nQ E  turn\n" +
            "Drag  look around\nScroll  zoom\nClick a work  walk to it\nSpace / Backspace  next / previous work\n" +
            "H or ?  this help\nEsc  close";

        VisualElement start, help, about, hints;
        Label folioTitle, folioMeta, status;
        VisualElement fill;
        Button enter;
        InputAction escape, toggleHelp;
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
            folioTitle = Text(start, "folio-title", "");
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
            tools.Add(IconButton("?", "Keyboard help", () => Toggle(help)));
            tools.Add(IconButton("i", "About", () => Toggle(about)));

            help = Panel(root, "help", "How to walk around", Keys);
            about = Panel(root, "about", "About", "");
            hints = Text(root, "hints",
                "↑↓ walk · ←→ turn · Shift+←→ step sideways · drag to look · scroll to zoom · click a work · H for help");
            hints.AddToClassList("hidden");

            escape = new InputAction("Close", InputActionType.Button, "<Keyboard>/escape");
            toggleHelp = new InputAction("Help", InputActionType.Button, "<Keyboard>/h");
            toggleHelp.AddBinding("<Keyboard>/slash");
            escape.performed += _ => CloseAll();
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
            toggleHelp?.Dispose();
        }

        void Update()
        {
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
            var about = this.about.Q<Label>(className: "panel-body");
            about.text =
                $"{b.Title}\n\nA walk-through generated from the collection's records: each room is a selection of works, " +
                "hung by their recorded size (photographs as prints).\n\nMuseado — Historic Technology\nrecordia.org";
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

        void Toggle(VisualElement panel)
        {
            bool open = !IsOpen(panel);
            help.AddToClassList("hidden");
            about.AddToClassList("hidden");
            if (open) panel.RemoveFromClassList("hidden");
            if (!IsOpen(start)) visitor.enabled = !open;
        }

        void CloseAll()
        {
            if (IsOpen(start)) return;
            help.AddToClassList("hidden");
            about.AddToClassList("hidden");
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
