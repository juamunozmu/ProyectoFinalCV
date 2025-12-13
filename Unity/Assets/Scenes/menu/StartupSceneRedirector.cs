using UnityEngine;
using UnityEngine.SceneManagement;

public static class StartupSceneRedirector
{
    // In the Unity Editor, pressing Play starts from the currently open scene.
    // This redirect ensures the app always starts on the main menu (Home) scene.
    private const string MenuSceneName = "MenuScene";
    private static bool _redirected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RedirectToMenuIfNeeded()
    {
        if (_redirected) return;

        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid()) return;

        if (!string.Equals(active.name, MenuSceneName, System.StringComparison.Ordinal))
        {
            _redirected = true;
            SceneManager.LoadScene(MenuSceneName);
        }
    }
}
