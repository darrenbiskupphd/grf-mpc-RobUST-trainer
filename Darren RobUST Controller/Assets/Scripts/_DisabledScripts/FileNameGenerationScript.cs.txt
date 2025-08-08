using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;


public class FileNameGenerationScript : MonoBehaviour
{
    public GameObject subjectInformationGameObject;
    private SubjectInfoStorageScript subjectInformationScript; 
    private string sceneName;
    private string subjectNumberString;
    private string subjectGenderString;
    private string directoryNameString;


    // Start is called before the first frame update
    void Start()
    {
        //get the scene name
        Scene scene = SceneManager.GetActiveScene();
        sceneName = scene.name;
        
        //get the subject-specific data
        subjectInformationScript = subjectInformationGameObject.GetComponent<SubjectInfoStorageScript>();
        subjectNumberString = subjectInformationScript.getSubjectNumberString();
        subjectGenderString = subjectInformationScript.getSubjectGenderString();

        // See if a directory for this subject and this scene exists. If not, create one.
        directoryNameString = createSubjectAndSceneSpecificDirectory(); 
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    private string createSubjectAndSceneSpecificDirectory()
    {
        string desiredDirectoryPath;
        DateTime localDate = DateTime.Now;

        Debug.Log("Trying to set up new directory.");
        #if UNITY_EDITOR
            desiredDirectoryPath =  Application.dataPath + "/CSV/";

        #elif UNITY_STANDALONE
            desiredDirectoryPath = Application.dataPath;
        #else
            desiredDirectoryPath = Application.dataPath;
#endif

        string dateAsMonthDayYear = localDate.Month + "_" + localDate.Day + "_" + localDate.Year;
        desiredDirectoryPath = desiredDirectoryPath + "/Subject" + subjectNumberString + "/" + sceneName + "/" + dateAsMonthDayYear;

        try
        {
            if (!Directory.Exists(desiredDirectoryPath))
            {
                Directory.CreateDirectory(desiredDirectoryPath);
            }

        }
        catch (IOException ex)
        {
            Debug.Log(ex.Message);
        }

        return desiredDirectoryPath; 
    }

    public string getFileSaveNamePrefix()
    {
        string fileSaveName = directoryNameString + "/" + sceneName +  "_Subject" + subjectNumberString + "_" + subjectGenderString + "_";
        Debug.Log("Generated file save name is: " + fileSaveName);

        return fileSaveName;
    }

    public string getFileSaveNamePrefixFromProjectFolder()
    {
        DateTime localDate = DateTime.Now;
        string dateAsMonthDayYear = localDate.Month + "_" + localDate.Day + "_" + localDate.Year;
        string desiredDirectoryPath = "Assets/CSV" + "/Subject" + subjectNumberString + "/" + sceneName +  "/" + dateAsMonthDayYear;
        string fileSaveName = desiredDirectoryPath + "/" + sceneName + "_Subject" + subjectNumberString + "_" + subjectGenderString + "_";
        Debug.Log("Generated file save name is: " + fileSaveName);

        return fileSaveName;
    }
}
