using System.IO;
using UnityEngine;

namespace MuseumBrowser.App
{
    /// -capture <dir>: stand at each room's entrance in turn, screenshot it once its images
    /// have had time to load, then quit. For checking layouts from the command line.
    public static class RoomCapture
    {
        public static async void Run(ExhibitionBuilder exhibition, Visitor visitor, string dir)
        {
            Directory.CreateDirectory(dir);
            if (Object.FindAnyObjectByType<MuseumShell>() is { } shell) shell.Enter();
            for (int i = 0; i < exhibition.RoomStops.Count; i++)
            {
                visitor.GoToRoom(i);
                await Awaitable.WaitForSecondsAsync(i == 0 ? 10f : 7f);
                await Shot(Path.Combine(dir, $"room-{i + 1:00}.png"));

                // The far wall head-on (title, screen or hero print), without the story panel.
                var guide = Object.FindAnyObjectByType<TourGuide>();
                if (guide) guide.PanelVisible = false;
                var stop = exhibition.RoomStops[i];
                var t = exhibition.transform;
                var position = stop.Position + t.right * (2.6f - (stop.Width - 1.7f)) + t.forward * (stop.Length - 1.2f - 5f);
                visitor.StandAt(position, stop.Yaw);
                await Awaitable.WaitForSecondsAsync(4f);
                await Shot(Path.Combine(dir, $"room-{i + 1:00}-far.png"));
                if (guide) guide.PanelVisible = true;
            }
            Application.Quit();
        }

        static async Awaitable Shot(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            await Awaitable.WaitForSecondsAsync(0.5f);
            Debug.Log($"Captured {path}");
        }
    }
}
