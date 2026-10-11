using System.Collections.Generic;
using System.Linq;
using MuseumBrowser.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace MuseumBrowser.App
{
    /// The audio guide for a tour hung as rooms: the stop's story in a side panel, and on
    /// entering a room its recordings play -- audio first, then the films on its screen.
    /// P plays/pauses (gamepad Y), T shows/hides the story; [ and ] walk to the next stop.
    public sealed class TourGuide : MonoBehaviour
    {
        ExhibitionBuilder exhibition;
        Visitor visitor;
        AudioSource audioSource;
        InputAction playAction, panelAction;

        VisualElement panel, recordings;
        Label counter, title, subtitle, address, lede, story, credit;
        Button play;
        ScrollView scroll;

        int room = -1;
        int audioIndex = -1;
        bool audioStarted, audioPaused;
        int loading;   // bumps on every room change, so a slow download cannot start in the wrong room

        Tour Tour => exhibition.Tour;
        TourStop Stop => room >= 0 && room < Tour.Stops.Count ? Tour.Stops[room] : null;
        List<Recording> Audio => Stop?.Recordings.Where(r => !r.IsVideo).ToList() ?? new List<Recording>();
        ScreenPlayer RoomScreen => room >= 0 && room < exhibition.RoomScreens.Count ? exhibition.RoomScreens[room].FirstOrDefault() : null;

        public static TourGuide Attach(ExhibitionBuilder exhibition, Visitor visitor)
        {
            var go = new GameObject("Tour guide");
            go.SetActive(false);
            go.transform.SetParent(exhibition.transform, false);
            var doc = go.AddComponent<UIDocument>();
            // Share the app's panel settings (theme, scaling).
            doc.panelSettings = FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
                .Select(d => d.panelSettings).FirstOrDefault(p => p);
            doc.sortingOrder = 5;
            var guide = go.AddComponent<TourGuide>();
            guide.exhibition = exhibition;
            guide.visitor = visitor;
            guide.audioSource = go.AddComponent<AudioSource>();
            guide.audioSource.playOnAwake = false;
            go.SetActive(true);
            return guide;
        }

        void OnEnable()
        {
            BuildPanel(GetComponent<UIDocument>().rootVisualElement);
            playAction = new InputAction("Play", InputActionType.Button, "<Keyboard>/p");
            playAction.AddBinding("<Gamepad>/buttonNorth");
            playAction.performed += _ => TogglePlay();
            panelAction = new InputAction("Story", InputActionType.Button, "<Keyboard>/t");
            panelAction.performed += _ => panel.ToggleInClassList("hidden");
            playAction.Enable();
            panelAction.Enable();
            if (visitor) visitor.RoomChanged += EnterRoom;
            foreach (var screens in exhibition.RoomScreens)
                foreach (var s in screens) s.Changed += _ => Refresh();
        }

        void OnDisable()
        {
            if (visitor) visitor.RoomChanged -= EnterRoom;
            playAction?.Dispose();
            panelAction?.Dispose();
        }

        void Update()
        {
            // An audio recording finished: the next one, then the room's films.
            if (audioStarted && !audioPaused && audioSource.clip && !audioSource.isPlaying)
            {
                audioStarted = false;
                if (audioIndex + 1 < Audio.Count) PlayAudio(audioIndex + 1);
                else { audioIndex = -1; RoomScreen?.Play(0); }
                Refresh();
            }
            if (Mouse.current is { } mouse)
            {
                if (mouse.leftButton.wasPressedThisFrame) pressedAt = mouse.position.ReadValue();
                // A click, not the end of a drag to look around.
                if (mouse.leftButton.wasReleasedThisFrame && (mouse.position.ReadValue() - pressedAt).sqrMagnitude < 36f)
                    ClickScreen(mouse.position.ReadValue());
            }
        }

        Vector2 pressedAt;

        /// A click on a screen plays or pauses its films (clicks on the panel are the panel's).
        void ClickScreen(Vector2 pointer)
        {
            if (panel.panel != null && !panel.ClassListContains("hidden")
                && panel.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(panel.panel, new Vector2(pointer.x, Screen.height - pointer.y))))
                return;
            var cam = Camera.main;
            if (!cam) return;
            if (Physics.Raycast(cam.ScreenPointToRay(pointer), out var hit, 40f) && hit.collider.GetComponentInParent<ScreenPlayer>() is { } screen)
            {
                StopAudio();
                screen.Toggle();
            }
        }

        /// Show or hide the story panel (T).
        public bool PanelVisible
        {
            get => panel != null && !panel.ClassListContains("hidden");
            set => panel?.EnableInClassList("hidden", !value);
        }

        void EnterRoom(int index)
        {
            if (index == room) return;
            StopAll();
            room = index;
            loading++;
            scroll.scrollOffset = Vector2.zero;
            Refresh();
            // Sound first: the stop's audio, else its films.
            if (Audio.Count > 0) PlayAudio(0);
            else RoomScreen?.Play(0);
        }

        void StopAll()
        {
            StopAudio();
            foreach (var screens in exhibition.RoomScreens)
                foreach (var s in screens) s.Stop();
        }

        void StopAudio()
        {
            audioStarted = audioPaused = false;
            audioIndex = -1;
            if (audioSource) audioSource.Stop();
        }

        async void PlayAudio(int i)
        {
            var list = Audio;
            if (i < 0 || i >= list.Count) return;
            RoomScreen?.Stop();
            StopAudio();
            audioIndex = i;
            int ticket = ++loading;
            Refresh();
            var url = list[i].Url;
            using var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioTypeFor(url));
            await request.SendWebRequest();
            if (ticket != loading || !this) return;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Audio {request.error}: {url}");
                audioIndex = -1;
                Refresh();
                return;
            }
            audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
            audioSource.Play();
            audioStarted = true;
            Refresh();
        }

        static AudioType AudioTypeFor(string url)
        {
            var path = url.Split('?')[0].ToLowerInvariant();
            if (path.EndsWith(".wav")) return AudioType.WAV;
            if (path.EndsWith(".ogg") || path.EndsWith(".oga")) return AudioType.OGGVORBIS;
            if (path.EndsWith(".m4a") || path.EndsWith(".aac")) return AudioType.ACC;
            return AudioType.MPEG;
        }

        void TogglePlay()
        {
            if (audioIndex >= 0)
            {
                if (audioSource.isPlaying) { audioSource.Pause(); audioPaused = true; }
                else { audioSource.UnPause(); audioPaused = false; }
            }
            else if (RoomScreen is { } screen) screen.Toggle();
            else if (Audio.Count > 0) PlayAudio(0);
            Refresh();
        }

        bool Playing => (audioIndex >= 0 && !audioPaused) || (RoomScreen?.IsPlaying ?? false);

        // ---- Panel ----------------------------------------------------------------------------

        void BuildPanel(VisualElement root)
        {
            root.Clear();
            if (Resources.Load<StyleSheet>("TourGuide") is { } sheet) root.styleSheets.Add(sheet);
            panel = new VisualElement();
            panel.AddToClassList("tour-panel");
            panel.pickingMode = PickingMode.Position;
            panel.Add(counter = Text("tour-counter"));
            panel.Add(title = Text("tour-title"));
            panel.Add(subtitle = Text("tour-subtitle"));
            panel.Add(address = Text("tour-address"));

            var controls = new VisualElement();
            controls.AddToClassList("tour-controls");
            controls.Add(Button("‹ Previous", () => visitor.GoToRoom(room - 1)));
            controls.Add(play = Button("Play", TogglePlay));
            controls.Add(Button("Next ›", () => visitor.GoToRoom(room + 1)));
            panel.Add(controls);
            panel.Add(recordings = new VisualElement());
            recordings.AddToClassList("tour-recordings");

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("tour-scroll");
            scroll.Add(lede = Text("tour-lede"));
            scroll.Add(story = Text("tour-story"));
            scroll.Add(credit = Text("tour-credit"));
            panel.Add(scroll);
            root.Add(panel);
            Refresh();
        }

        static Label Text(string cls)
        {
            var l = new Label();
            l.AddToClassList(cls);
            return l;
        }

        static Button Button(string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("tour-button");
            return b;
        }

        void Refresh()
        {
            if (panel == null || exhibition == null || Tour == null) return;
            var stop = Stop;
            counter.text = stop == null ? Tour.Title : $"Stop {room + 1} of {Tour.Stops.Count} · {Tour.Title}";
            title.text = stop?.Title ?? "";
            subtitle.text = stop?.Subtitle ?? "";
            address.text = stop?.Address ?? "";
            lede.text = stop?.Lede ?? "";
            story.text = stop?.Text ?? "";
            credit.text = stop == null ? "" : string.Join("\n", new[] { stop.Sponsor, stop.License, stop.SourceUrl }.Where(s => !string.IsNullOrEmpty(s)));
            foreach (var l in new[] { subtitle, address, lede, credit })
                l.style.display = string.IsNullOrEmpty(l.text) ? DisplayStyle.None : DisplayStyle.Flex;

            play.text = Playing ? "Pause" : "Play";
            play.SetEnabled(stop != null && stop.Recordings.Count > 0);

            recordings.Clear();
            if (stop == null) return;
            var audio = Audio;
            for (int i = 0; i < audio.Count; i++)
            {
                int k = i;
                var row = Button((k == audioIndex ? "♪ " : "") + (audio[k].Caption ?? $"Audio {k + 1}"), () => PlayAudio(k));
                row.AddToClassList("tour-recording");
                row.EnableInClassList("current", k == audioIndex);
                recordings.Add(row);
            }
            if (RoomScreen is { } screen)
                for (int i = 0; i < screen.Videos.Count; i++)
                {
                    int k = i;
                    bool current = audioIndex < 0 && screen.IsPlaying && screen.Current == k;
                    var row = Button((current ? "▶ " : "") + (screen.Videos[k].Caption ?? $"Film {k + 1}") + " (film)", () => { StopAudio(); screen.Play(k); });
                    row.AddToClassList("tour-recording");
                    row.EnableInClassList("current", current);
                    recordings.Add(row);
                }
        }
    }
}
