using UnityEngine;

namespace MuseumBrowser.App
{
    public sealed class Bootstrap : MonoBehaviour
    {
        void Start()
        {
            Debug.Log($"{Application.productName} {Application.version} started on {Application.platform}");
        }
    }
}
