using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class MenuManager : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
public static string StartMode;
public static string JoinCode;
public static string ErrorMessage;   // set by NetworkCommandLine when joining fails
public TMP_InputField joinCodeInput;

void OnGUI()
{
    if (!string.IsNullOrEmpty(ErrorMessage))
        GUI.Label(new Rect(10, 10, 400, 30), ErrorMessage);
}

public void HostGame()
{
    StartMode = "host";
    SceneManager.LoadScene("minigame1");
}

public void JoinGame()
{
    StartMode = "client";
    JoinCode = joinCodeInput.text;
    ErrorMessage = null;
    SceneManager.LoadScene("minigame1");
}

    // Update is called once per frame
    public void quitGame()
    {
        Application.Quit();
    }
}
