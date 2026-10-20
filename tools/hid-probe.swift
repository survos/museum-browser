// hid-probe: does the macOS see the controller, and what does its D-pad send?
//
// Reads the pad directly (IOKit HID, and GameController as macOS's own gamepad layer), outside
// Unity, so "Unity ignores my D-pad" can be split into "the pad sends nothing" and "Unity binds
// the wrong control".
//
//   swiftc -swift-version 5 -o /tmp/hid-probe tools/hid-probe.swift && /tmp/hid-probe 20
//
// Press the D-pad (all four directions) and a button or two while it runs. The argument is how
// many seconds to listen (default 20). Needs "Input Monitoring" for the terminal app the first time
// (System Settings > Privacy & Security > Input Monitoring); the script says so if macOS refuses.

import Foundation
import IOKit.hid
import GameController

let seconds = CommandLine.arguments.count > 1 ? (Double(CommandLine.arguments[1]) ?? 20) : 20

struct Seen { var count = 0; var lo = Int.max; var hi = Int.min }
var seen: [String: Seen] = [:]
var lastValue: [String: Int] = [:]
var devices: [String] = []

let hatNames = ["up", "up-right", "right", "down-right", "down", "down-left", "left", "up-left"]

func property(_ device: IOHIDDevice, _ key: String) -> Any? { IOHIDDeviceGetProperty(device, key as CFString) }

func hex(_ n: Int?) -> String { n.map { String(format: "0x%04X", $0) } ?? "?" }

// ---- IOKit HID --------------------------------------------------------------------------------

let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
// Generic Desktop: Joystick (4), Game Pad (5), Keyboard (6), Keypad (7), Multi-axis controller (8).
// Keyboards are included because many button readers / encoders present as one and send keystrokes;
// Apple's own keyboards are skipped (vendor 0x05AC) so typing is never recorded.
let wanted: [[String: Any]] = [4, 5, 6, 7, 8].map {
    [kIOHIDDeviceUsagePageKey as String: 1, kIOHIDDeviceUsageKey as String: $0]
}
IOHIDManagerSetDeviceMatchingMultiple(manager, wanted as CFArray)

IOHIDManagerRegisterDeviceMatchingCallback(manager, { _, _, _, device in
    let name = property(device, kIOHIDProductKey as String) as? String ?? "?"
    let vendor = property(device, kIOHIDVendorIDKey as String) as? Int
    if vendor == 0x05AC { return }
    let product = property(device, kIOHIDProductIDKey as String) as? Int
    let transport = property(device, kIOHIDTransportKey as String) as? String ?? "?"
    let line = "HID device: \"\(name)\"  vendor \(hex(vendor))  product \(hex(product))  via \(transport)"
    devices.append(line)
    print(line)
}, nil)

IOHIDManagerRegisterInputValueCallback(manager, { _, _, _, value in
    let element = IOHIDValueGetElement(value)
    let source = IOHIDElementGetDevice(element)
    if (property(source, kIOHIDVendorIDKey as String) as? Int) == 0x05AC { return }
    let page = Int(IOHIDElementGetUsagePage(element))
    let usage = Int(IOHIDElementGetUsage(element))
    let v = IOHIDValueGetIntegerValue(value)
    let key = "page \(hex(page)) usage \(hex(usage))"

    var s = seen[key] ?? Seen()
    s.count += 1; s.lo = min(s.lo, v); s.hi = max(s.hi, v)
    seen[key] = s

    // Live: the hat switch (page 1, usage 0x39), buttons (page 9) and keys (page 7). Axes only show
    // in the summary.
    let isHat = page == 0x01 && usage == 0x39
    let isButton = page == 0x09
    let isKey = page == 0x07 && usage >= 4
    guard (isHat || isButton || isKey), lastValue[key] != v else { return }
    lastValue[key] = v
    if isKey {
        print("  key (usage \(usage)): \(v == 0 ? "released" : "pressed")  [from \(property(source, kIOHIDProductKey as String) as? String ?? "?")]")
    } else if isHat {
        let name = (0..<hatNames.count).contains(v) ? hatNames[v] : "released"
        print("  D-pad (hat switch, value \(v)): \(name)")
    } else {
        print("  button \(usage): \(v == 0 ? "released" : "pressed")")
    }
}, nil)

IOHIDManagerScheduleWithRunLoop(manager, CFRunLoopGetMain(), CFRunLoopMode.defaultMode.rawValue)
let opened = IOHIDManagerOpen(manager, IOOptionBits(kIOHIDOptionsTypeNone))
if opened != kIOReturnSuccess {
    print(String(format: "IOHIDManagerOpen failed (0x%08X). If this is 'not permitted', allow your terminal under "
        + "System Settings > Privacy & Security > Input Monitoring and run again.", opened))
}

// ---- GameController (macOS's own gamepad layer: what Unity's Gamepad backend sees) --------------

var gcSeen: [String] = []

func attach(_ controller: GCController) {
    let kind = controller.extendedGamepad != nil ? "extended gamepad"
        : controller.microGamepad != nil ? "micro gamepad" : "no gamepad profile"
    let line = "GameController: \"\(controller.vendorName ?? "?")\"  category \(controller.productCategory)  \(kind)"
    gcSeen.append(line)
    print(line)
    controller.extendedGamepad?.dpad.valueChangedHandler = { _, x, y in
        print("  GameController dpad: x \(x) y \(y)")
    }
    controller.microGamepad?.dpad.valueChangedHandler = { _, x, y in
        print("  GameController micro dpad: x \(x) y \(y)")
    }
    controller.extendedGamepad?.buttonA.pressedChangedHandler = { _, _, pressed in
        print("  GameController button A: \(pressed ? "pressed" : "released")")
    }
}

GCController.shouldMonitorBackgroundEvents = true
NotificationCenter.default.addObserver(forName: .GCControllerDidConnect, object: nil, queue: .main) { note in
    if let controller = note.object as? GCController { attach(controller) }
}
GCController.controllers().forEach(attach)

// ---- Listen, then summarise ---------------------------------------------------------------------

print("Listening for \(Int(seconds)) seconds. Press the D-pad in all four directions, then a button.")
RunLoop.main.run(until: Date().addingTimeInterval(seconds))

print("\n== Summary ==")
print(devices.isEmpty ? "No joystick / gamepad HID device found by IOKit." : devices.joined(separator: "\n"))
print(gcSeen.isEmpty ? "macOS GameController does not list it (Unity's Gamepad layout will not match; it appears as a generic HID device)."
                     : gcSeen.joined(separator: "\n"))
if seen.isEmpty {
    print("No input from the device in \(Int(seconds)) s: nothing pressed, wrong device, or Input Monitoring is not allowed.")
} else {
    for (key, s) in seen.sorted(by: { $0.key < $1.key }) {
        print("  \(key): \(s.count) events, values \(s.lo)…\(s.hi)")
    }
    let hat = seen["page 0x0001 usage 0x0039"]
    print(hat != nil ? "D-pad arrives as a hat switch (page 1, usage 0x39)."
                     : "No hat switch seen: the D-pad is either buttons (page 9) or vendor-specific; compare the lines above to what you pressed.")
}
