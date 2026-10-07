using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseumBrowser.App
{
    /// Steps the camera from work to work. Input is event-driven: InputAction
    /// callbacks set the target; Update only eases the camera toward it.
    public sealed class GalleryWalk : MonoBehaviour
    {
        [SerializeField] ExhibitionBuilder exhibition;
        [SerializeField] float viewingDistance = 2.4f;
        [SerializeField] float smoothTime = 0.45f;

        InputAction next, previous;
        int index;
        Vector3 target, velocity, facing = Vector3.forward;

        void Awake()
        {
            next = new InputAction("Next", InputActionType.Button);
            next.AddBinding("<Keyboard>/rightArrow");
            next.AddBinding("<Keyboard>/d");
            next.AddBinding("<Mouse>/leftButton");
            previous = new InputAction("Previous", InputActionType.Button);
            previous.AddBinding("<Keyboard>/leftArrow");
            previous.AddBinding("<Keyboard>/a");
            previous.AddBinding("<Mouse>/rightButton");

            next.performed += _ => Go(index + 1);
            previous.performed += _ => Go(index - 1);
            exhibition.Built += _ => Go(0, snap: true);
        }

        void OnEnable() { next.Enable(); previous.Enable(); }
        void OnDisable() { next.Disable(); previous.Disable(); }
        void OnDestroy() { next.Dispose(); previous.Dispose(); }

        void Go(int i, bool snap = false)
        {
            var works = exhibition.Works;
            if (works.Count == 0) return;
            index = Mathf.Clamp(i, 0, works.Count - 1);
            var work = works[index];
            // Stand in front of the work (its forward points into the wall), a little toward its label.
            target = work.position - work.forward * viewingDistance + work.right * 0.2f;
            target.y = 1.6f;
            facing = work.forward;
            if (snap) transform.position = target;
        }

        void Update()
        {
            transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, smoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing, Vector3.up), Time.deltaTime * 6f);
        }
    }
}
