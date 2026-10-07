using MuseumBrowser.Core;
using UnityEngine;

namespace MuseumBrowser.App
{
    public sealed class Bootstrap : MonoBehaviour
    {
        void Start()
        {
            Debug.Log($"{BuildInfo.Name} {Application.version} started on {Application.platform}");
        }
    }
}
