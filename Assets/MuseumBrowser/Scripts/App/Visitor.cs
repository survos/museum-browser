using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseumBrowser.App
{
    /// The visitor. Non-gamer conventions first (Street View style): up/down arrows walk,
    /// left/right arrows turn (with Shift they step sideways); W/S walk, A/D step
    /// sideways, Q/E turn; drag to look,
    /// scroll to zoom, click a work to walk to it, Space/Backspace for the
    /// next/previous work in tour order.
    /// Input is event-driven: every action's callbacks record its state (a held direction, a
    /// modifier, a drag) or act at once (a look delta, a scroll, a click). Update never reads
    /// a device; it only integrates the recorded state over the frame's time. Keeps the camera
    /// on a CharacterController so walls are solid.
    [RequireComponent(typeof(CharacterController))]
    public sealed class Visitor : MonoBehaviour
    {
        [SerializeField] ExhibitionBuilder exhibition;
        [SerializeField] Camera eye;
        [SerializeField] float walkSpeed = 2.2f;
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float turnSpeed = 90f; // degrees per second for keys
        [SerializeField] float stickLookSpeed = 60f; // degrees per second at full right-stick deflection
        [SerializeField] float viewingDistance = 2.2f;
        [SerializeField] float minFov = 12f, maxFov = 60f;
        [Tooltip("A work counts as 'being looked at' within this distance.")]
        [SerializeField] float viewingRange = 5f;

        /// Index of the work the visitor has arrived at, or -1 when walking freely.
        public event System.Action<int> Focused;
        /// Index of the work centred in view and close enough to read, or -1.
        public event System.Action<int> Viewing;
        int viewing = -1;
        /// Index of the room the visitor has walked (or jumped) into.
        public event System.Action<int> RoomChanged;
        int lastRoom = -1;

        InputAction move, turn, lookDelta, lookStick, primary, zoom, sidestep, next, previous, nextRoom, previousRoom;
        InputAction[] actions;
        int room;
        CharacterController body;
        float yaw, pitch;
        int index = -1;
        bool autoWalking;
        Vector3 autoTarget;
        float autoYaw;

        // State recorded by the action callbacks.
        Vector2 moveInput;
        float turnInput;
        Vector2 stickInput;
        bool sidestepping;
        bool dragging;
        bool dragged;

        void Awake()
        {
            body = GetComponent<CharacterController>();
            if (!eye) eye = GetComponentInChildren<Camera>();

            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow");
            move.AddBinding("<Gamepad>/leftStick");
            // D-pad, like the arrow keys: up/down walk (left/right turn, below). Game Boy-style pads
            // (8BitDo etc.) have no sticks, only the D-pad.
            move.AddCompositeBinding("2DVector").With("Up", "<Gamepad>/dpad/up").With("Down", "<Gamepad>/dpad/down");
            // Shoulder buttons step sideways, like Shift + left/right.
            move.AddCompositeBinding("2DVector").With("Left", "<Gamepad>/leftShoulder").With("Right", "<Gamepad>/rightShoulder");
            // Game Boy-style USB pads (045E:028E, "Controller"; the Xbox 360 IDs are spoofed) send their D-pad as
            // plain HID buttons 12-15 (measured with tools/hid-probe.swift): 12 up, 13 down, 14 left,
            // 15 right; A and B are buttons 1 and 2. Whether Unity exposes it as <HID> or as a Gamepad is
            // to be confirmed from the "Input device" / "Input press" lines InputDiag logs.
            move.AddCompositeBinding("2DVector").With("Up", "*/button12").With("Down", "*/button13");
            turn = new InputAction("Turn", InputActionType.Value);
            turn.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            turn.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
            turn.AddCompositeBinding("1DAxis").With("Negative", "<Gamepad>/dpad/left").With("Positive", "<Gamepad>/dpad/right");
            turn.AddCompositeBinding("1DAxis").With("Negative", "*/button14").With("Positive", "*/button15");
            sidestep = new InputAction("Sidestep", InputActionType.Button, "<Keyboard>/shift");
            lookDelta = new InputAction("Look", InputActionType.Value, "<Pointer>/delta");
            lookStick = new InputAction("Look (stick)", InputActionType.Value, "<Gamepad>/rightStick");
            zoom = new InputAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            // Press to start a drag; release without having dragged is a click on a work.
            primary = new InputAction("Primary", InputActionType.Button, "<Mouse>/leftButton");
            next = new InputAction("Next", InputActionType.Button, "<Keyboard>/space");
            next.AddBinding("<Gamepad>/buttonSouth");
            next.AddBinding("*/trigger");
            previous = new InputAction("Previous", InputActionType.Button, "<Keyboard>/backspace");
            previous.AddBinding("<Gamepad>/buttonEast");
            previous.AddBinding("*/button2");

            nextRoom = new InputAction("Next room", InputActionType.Button, "<Keyboard>/rightBracket");
            nextRoom.AddBinding("<Keyboard>/pageDown");
            previousRoom = new InputAction("Previous room", InputActionType.Button, "<Keyboard>/leftBracket");
            previousRoom.AddBinding("<Keyboard>/pageUp");

            // Held state: set when the value changes, cleared when the control returns to rest.
            move.performed += c => moveInput = c.ReadValue<Vector2>();
            move.canceled += _ => moveInput = Vector2.zero;
            turn.performed += c => turnInput = c.ReadValue<float>();
            turn.canceled += _ => turnInput = 0f;
            lookStick.performed += c => stickInput = c.ReadValue<Vector2>();
            lookStick.canceled += _ => stickInput = Vector2.zero;
            sidestep.started += _ => sidestepping = true;
            sidestep.canceled += _ => sidestepping = false;

            // Acted on at once.
            lookDelta.performed += c => LookBy(c.ReadValue<Vector2>());
            zoom.performed += c => ZoomBy(c.ReadValue<float>());
            primary.started += _ => { dragging = true; dragged = false; };
            primary.canceled += _ => { dragging = false; if (!dragged) SelectUnderPointer(); };
            next.performed += _ => GoTo(index + 1);
            previous.performed += _ => GoTo(index - 1);
            // Step from wherever the visitor actually is, not the last room jumped to.
            nextRoom.performed += _ => GoToRoom((CurrentRoom >= 0 ? CurrentRoom : room) + 1);
            previousRoom.performed += _ => GoToRoom((CurrentRoom >= 0 ? CurrentRoom : room) - 1);
            exhibition.Built += b => StandAt(b.Entrance, b.EntranceYaw);

            actions = new[] { move, turn, lookDelta, lookStick, primary, zoom, sidestep, next, previous, nextRoom, previousRoom };
            yaw = transform.eulerAngles.y;
        }

        void OnEnable() { foreach (var a in actions) a.Enable(); }

        void OnDisable()
        {
            foreach (var a in actions) a.Disable();
            // Disabling cancels held actions, but be certain nothing keeps walking.
            moveInput = stickInput = Vector2.zero;
            turnInput = 0f;
            sidestepping = dragging = false;
        }

        void OnDestroy() { foreach (var a in actions) a.Dispose(); }

        /// Mouse look: only while the button is held, and only for a real drag (not a click's jitter).
        void LookBy(Vector2 delta)
        {
            if (!dragging || delta.sqrMagnitude <= 0.5f) return;
            dragged = true;
            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -60f, 60f);
            autoWalking = false;
        }

        /// Zoom by narrowing the field of view.
        void ZoomBy(float scroll)
        {
            if (Mathf.Abs(scroll) > 0.01f)
                eye.fieldOfView = Mathf.Clamp(eye.fieldOfView - Mathf.Sign(scroll) * 3f, minFov, maxFov);
        }

        void Update()
        {
            // Right stick looks around; a held deflection is turned into a rate here.
            if (stickInput.sqrMagnitude > 0.0001f)
            {
                yaw += stickInput.x * stickLookSpeed * Time.deltaTime;
                pitch = Mathf.Clamp(pitch - stickInput.y * stickLookSpeed * Time.deltaTime, -60f, 60f);
                autoWalking = false;
            }

            // Shift turns the left/right arrows into a sidestep, for scanning along a wall.
            if (Mathf.Abs(turnInput) > 0.01f && !sidestepping)
            {
                yaw += turnInput * turnSpeed * Time.deltaTime;
                autoWalking = false;
            }

            var input = moveInput;
            if (sidestepping) input.x += turnInput;
            Vector3 velocity;
            if (input.sqrMagnitude > 0.01f)
            {
                if (autoWalking || index >= 0) { autoWalking = false; SetFocus(-1); }
                var forward = Quaternion.Euler(0, yaw, 0);
                velocity = forward * new Vector3(input.x, 0, input.y) * walkSpeed;
            }
            else if (autoWalking)
            {
                var to = autoTarget - transform.position;
                to.y = 0;
                velocity = Vector3.ClampMagnitude(to * 2.5f, walkSpeed * 1.6f);
                yaw = Mathf.LerpAngle(yaw, autoYaw, Time.deltaTime * 4f);
                pitch = Mathf.Lerp(pitch, 0f, Time.deltaTime * 4f);
                if (to.magnitude < 0.05f) { autoWalking = false; }
            }
            else velocity = Vector3.zero;

            body.Move((velocity + Physics.gravity) * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            eye.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            UpdateViewing();
            UpdateRoom();
        }

        void UpdateRoom()
        {
            int current = CurrentRoom;
            if (current < 0 || current == lastRoom) return;
            lastRoom = current;
            RoomChanged?.Invoke(current);
        }

        void UpdateViewing()
        {
            int seen = -1;
            var ray = eye.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            if (Physics.SphereCast(ray, 0.25f, out var hit, viewingRange)
                && hit.collider.GetComponentInParent<HungWork>() is { } work)
                seen = work.Index;
            if (seen == viewing) return;
            viewing = seen;
            Viewing?.Invoke(seen);
        }

        /// Place the visitor without walking (start of the visit, or jumping to a room).
        public void StandAt(Vector3 position, float facingYaw)
        {
            autoWalking = false;
            SetFocus(-1);
            body.enabled = false;
            transform.position = position;
            body.enabled = true;
            yaw = facingYaw;
            pitch = 0f;
        }

        /// Jump to a room's entrance (Rooms menu, or [ and ]).
        /// Index of the room the visitor is in (by position along the suite), or -1.
        public int CurrentRoom
        {
            get
            {
                var stops = exhibition.RoomStops;
                float z = exhibition.transform.InverseTransformPoint(transform.position).z;
                for (int i = 0; i < stops.Count; i++)
                    if (z >= stops[i].Start - 0.3f && z <= stops[i].Start + stops[i].Length + 0.3f) return i;
                return -1;
            }
        }

        public void GoToRoom(int i)
        {
            var stops = exhibition.RoomStops;
            if (stops.Count == 0) return;
            room = Mathf.Clamp(i, 0, stops.Count - 1);
            StandAt(stops[room].Position, stops[room].Yaw);
        }

        void SelectUnderPointer()
        {
            var ray = eye.ScreenPointToRay(Pointer.current.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, 40f) && hit.collider.GetComponentInParent<HungWork>() is { } work)
                GoTo(work.Index);
        }

        void GoTo(int i, bool snap = false)
        {
            var works = exhibition.Works;
            if (works.Count == 0) return;
            i = Mathf.Clamp(i, 0, works.Count - 1);
            var work = works[i];
            autoTarget = work.position - work.forward * viewingDistance + work.right * 0.25f;
            autoTarget.y = transform.position.y;
            autoYaw = Quaternion.LookRotation(work.forward).eulerAngles.y;
            eye.fieldOfView = maxFov;
            if (snap)
            {
                body.enabled = false;
                transform.position = autoTarget;
                body.enabled = true;
                yaw = autoYaw;
            }
            else autoWalking = true;
            SetFocus(i);
        }

        void SetFocus(int i)
        {
            if (i == index) return;
            index = i;
            Focused?.Invoke(i);
        }
    }
}
