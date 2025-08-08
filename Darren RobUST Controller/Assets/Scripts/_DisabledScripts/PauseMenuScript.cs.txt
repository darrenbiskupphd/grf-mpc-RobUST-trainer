using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PauseMenuScript : MonoBehaviour
{
    public static bool gameIsPaused = false; //whether or not game is paused
    public GameObject pauseMenuUi; //a reference to the pause/instructions menu panel UI

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void PauseGame()
    {
        pauseMenuUi.SetActive(true);  // make the pause/instructions menu visible
        Time.timeScale = 0f;  // stop time from passing in the game
        gameIsPaused = true;  // set the flag indicating that the game is paused
    }

    public void ResumeGame()
    {
        pauseMenuUi.SetActive(false);  // make the pause/instructions menu visible
        Time.timeScale = 1f;  // stop time from passing in the game
        gameIsPaused = false;  // set the flag indicating that the game is paused
    }
}
