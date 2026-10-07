using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseumBrowser.App
{
    /// The visitor. Non-gamer conventions first (Street View style): up/down arrows walk,
    /// left/right arrows turn (with Shift they step sideways); W/S walk, A/D step
    /// sideways, Q/E turn; drag to look,
    /// scroll to zoom, click a work to walk to it, Space/Backspace for the
    /// next/previous work in tour order.
    /// Discrete actions are InputAction callbacks; move/look/zoom values are read
    /// each frame. Keeps the camera on a CharacterController so walls are solid.
    [RequireComponent(typeof(CharacterController))]
    public sealed class Visitor : MonoBehaviour
    {
        [SerializeField] ExhibitionBuilder exhibition;
        [SerializeField] Camera eye;
        [SerializeField] float walkSpeed = 2.2f;
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float turnSpeed = 90f; // degrees per second for keys
        [SerializeField] float viewingDistance = 2.2f;
        [SerializeField] float minFov = 12f, maxFov = 60f;
        [Tooltip("A work counts as 'being looked at' within this distance.")]
        [SerializeField] float viewingRange = 5f;

        /// Index of the work the visitor has arrived at, or -1 when walking freely.
        public event System.Action<int> Focused;
        /// Index of the work centred in view and close enough to read, or -1.
        public event System.Action<int> Viewing;
        int viewing = -1;

        InputAction move, turn, look, drag, zoom, click, next, previous, nextRoom, previousRoom;
        int room;
        CharacterController body;
        float yaw, pitch;
        int index = -1;
        bool autoWalking;
        Vector3 autoTarget;
        float autoYaw;
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
            turn = new InputAction("Turn", InputActionType.Value);
            turn.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            turn.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
            look = new InputAction("Look", InputActionType.Value, "<Pointer>/delta");
            look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=8,y=8)");
            drag = new InputAction("Drag", InputActionType.Button, "<Mouse>/leftButton");
            zoom = new InputAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            click = new InputAction("Select", InputActionType.Button, "<Mouse>/leftButton");
            next = new InputAction("Next", InputActionType.Button, "<Keyboard>/space");
            next.AddBinding("<Gamepad>/buttonSouth");
            previous = new InputAction("Previous", InputActionType.Button, "<Keyboard>/backspace");
            previous.AddBinding("<Gamepad>/buttonEast");

            nextRoom = new InputAction("Next room", InputActionType.Button, "<Keyboard>/rightBracket");
            nextRoom.AddBinding("<Keyboard>/pageDown");
            previousRoom = new InputAction("Previous room", InputActionType.Button, "<Keyboard>/leftBracket");
            previousRoom.AddBinding("<Keyboard>/pageUp");
            // Step from wherever the visitor actually is, not the last room jumped to.
            nextRoom.performed += _ => GoToRoom((CurrentRoom >= 0 ? CurrentRoom : room) + 1);
            previousRoom.performed += _ => GoToRoom((CurrentRoom >= 0 ? CurrentRoom : room) - 1);

            drag.started += _ => dragged = false;
            click.canceled += _ => { if (!dragged) SelectUnderPointer(); };
            next.performed += _ => GoTo(index + 1);
            previous.performed += _ => GoTo(index - 1);
            exhibition.Built += b => StandAt(b.Entrance, b.EntranceYaw);

            yaw = transform.eulerAngles.y;
        }

        void OnEnable() { foreach (var a in Actions) a.Enable(); }
        void OnDisable() { foreach (var a in Actions) a.Disable(); }
        void OnDestroy() { foreach (var a in Actions) a.Dispose(); }
        InputAction[] Actions => new[] { move, turn, look, drag, zoom, click, next, previous, nextRoom, previousRoom };

        void Update()
        {
            // Look while dragging (mouse) or always (gamepad stick).
            var delta = look.ReadValue<Vector2>();
            bool mouseLook = drag.IsPressed() && delta.sqrMagnitude > 0.5f;
            if (mouseLook) dragged = true;
            if (mouseLook || look.activeControl?.device is Gamepad)
            {
                yaw += delta.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -60f, 60f);
                autoWalking = false;
            }

            // Shift turns the left/right arrows into a sidestep, for scanning along a wall.
            float turning = turn.ReadValue<float>();
            bool sidestep = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (Mathf.Abs(turning) > 0.01f && !sidestep)
            {
                yaw += turning * turnSpeed * Time.deltaTime;
                autoWalking = false;
            }

            // Zoom by narrowing the field of view.
            float scroll = zoom.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f)
                eye.fieldOfView = Mathf.Clamp(eye.fieldOfView - Mathf.Sign(scroll) * 3f, minFov, maxFov);

            var input = move.ReadValue<Vector2>();
            if (sidestep) input.x += turning;
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
