using System.Linq;
using MuseumBrowser.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace MuseumBrowser.App
{
    /// On-screen wall label for the work the visitor is standing at: the museum
    /// tombstone at a size you can read, plus credit and source.
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabelPanel : MonoBehaviour
    {
        [SerializeField] ExhibitionBuilder exhibition;
        [SerializeField] Visitor walk;
        [SerializeField] StyleSheet styleSheet;

        Label creator, title, subject, details, credit, counter;
        VisualElement card;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            if (styleSheet) root.styleSheets.Add(styleSheet);
            card = new VisualElement { name = "label-card" };
            card.AddToClassList("label-card");
            card.Add(counter = Text("counter"));
            card.Add(creator = Text("creator"));
            card.Add(title = Text("title"));
            card.Add(subject = Text("subject"));
            card.Add(details = Text("details"));
            card.Add(credit = Text("credit"));
            card.style.display = DisplayStyle.None;
            root.Add(card);
            walk.Focused += Show;
        }

        void OnDisable() => walk.Focused -= Show;

        static Label Text(string cls)
        {
            var l = new Label();
            l.AddToClassList(cls);
            return l;
        }

        void Show(int index)
        {
            if (index < 0) { card.style.display = DisplayStyle.None; return; }
            var c = exhibition.Cards[index];
            var l = c.Label ?? new WallLabel();
            counter.text = $"{index + 1} / {exhibition.Cards.Count}";
            creator.text = l.Creator ?? "";
            title.text = l.Title ?? c.Title ?? "";
            subject.text = l.Subject ?? "";
            details.text = string.Join("\n", new[] { l.Date, l.Medium, l.Dimensions }.Where(s => !string.IsNullOrEmpty(s)));
            credit.text = string.Join("\n", new[] { l.Credit, l.Accession, c.License }.Where(s => !string.IsNullOrEmpty(s)));
            foreach (var e in new VisualElement[] { creator, title, subject, details, credit })
                e.style.display = string.IsNullOrEmpty(((Label)e).text) ? DisplayStyle.None : DisplayStyle.Flex;
            card.style.display = DisplayStyle.Flex;
        }
    }
}
