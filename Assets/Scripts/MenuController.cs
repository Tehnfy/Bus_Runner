using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu scene glue. Only exists so the Play button has something to call —
/// Menu is build index 0, so without this there is no way to reach the level.
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] string levelSceneName = "Level_1";

    public void PlayLevel()
    {
        SceneManager.LoadScene(levelSceneName, LoadSceneMode.Single);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
