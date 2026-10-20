using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace MuseumBrowser.App
{
    // Editor and development builds only: lists the input devices the Input System sees and logs
    // the control path of every button press, so a controller whose D-pad is not recognised can be
    // bound by what it actually reports ("<HID::Controller>/hat/up", "<Gamepad>/dpad/up", ...).
    static class InputDiag
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static IDisposable presses;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            foreach (var device in InputSystem.devices)
                Debug.Log($"Input device: {device.displayName} · layout {device.layout} · {device.path}"
                          + (device.description.interfaceName != null ? $" · {device.description.interfaceName} {device.description.manufacturer} {device.description.product}" : ""));
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onAfterUpdate -= LogAxes;
            InputSystem.onAfterUpdate += LogAxes;
            presses?.Dispose();
            // Keyboards and mice are known; a controller is the interesting case.
            presses = InputSystem.onAnyButtonPress.Call(control =>
            {
                if (control.device is Keyboard or Mouse) return;
                Debug.Log($"Input press: {control.path} (layout {control.device.layout})");
            });
        }

        // Axes (sticks) held past this are logged once per push, by control path.
        static readonly System.Collections.Generic.HashSet<string> pushed = new();

        static void LogAxes()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is Keyboard or Mouse or Pen or Touchscreen) continue;
                foreach (var control in device.allControls)
                {
                    if (control is not UnityEngine.InputSystem.Controls.AxisControl axis
                        || control is UnityEngine.InputSystem.Controls.ButtonControl || axis.synthetic) continue;
                    float value = axis.ReadValue();
                    if (Mathf.Abs(value) > 0.6f) { if (pushed.Add(control.path)) Debug.Log($"Input axis: {control.path} = {value:0.00}"); }
                    else if (Mathf.Abs(value) < 0.3f) pushed.Remove(control.path);
                }
            }
        }

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change is InputDeviceChange.Added or InputDeviceChange.Removed or InputDeviceChange.Reconnected or InputDeviceChange.Disconnected)
                Debug.Log($"Input device {change}: {device.displayName} · layout {device.layout} · {device.path}");
        }
#endif
    }
}
