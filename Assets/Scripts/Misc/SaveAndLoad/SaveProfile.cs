using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class SaveProfile<T> where T : SaveProfileData
{
    public string profileName;
    public T saveData;

    public SaveProfile() { }

    public SaveProfile(string name, T saveData)
    {
        this.profileName = name;
        this.saveData = saveData;
    }
}

public abstract record SaveProfileData {}

public record NodeState : SaveProfileData
{
    public Vector2 localPosition;
    public List<string> childNodeID;
    public string parentNodeID;
    public bool hasPopUp;
    public bool isActive;
}

public record GameProgressState : SaveProfileData
{
    public int version;
    public string saveGeneration;
    public int levelIndex;
    public int graphIndex;
    public List<int> enterNodeGraphTimes;
    public List<List<string>> nodeIdsInGraph;
    public float currentAnxiety;
    public float maxAnxiety;
    public float rate;
    public int previousChapterBot;
    public int level1GetResultTimes;
}

public record TextStoryProgressState : SaveProfileData
{
    public int version;
    public string gameId;
    public string pageId;
}
