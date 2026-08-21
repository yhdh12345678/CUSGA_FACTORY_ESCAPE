using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;

public class GameMenu : SingletonMonobehaviour<GameMenu>
{
    public List<Node> startNodes = new List<Node>();
    public GameObject LinePrefab;
    public Dictionary<Node,Line> nodeLineBinding = new Dictionary<Node,Line>();
    public bool AwaitingNewGameConfirmation { get; private set; }

    public void ClearAllSelectedNode(Node node)
    {
        foreach (Node currentNode in startNodes)
        {
            if (currentNode != node && currentNode.isSelected)
            {
                currentNode.isSelected = false;
                currentNode.GetUnSelectedAnimate();
            }
        }
    }

    public void CreateLine(Node node)
    {
        GameObject line = Instantiate(LinePrefab, transform.position, Quaternion.identity,transform);

        Line lineComponent = line.GetComponent<Line>();

        Node parentNode = null;

        foreach (Node currentNode in startNodes)
        {
            if (currentNode.id == node.parentID)
            {
                parentNode = currentNode;
            }
        }

        if (parentNode != null)
        {
            lineComponent.InitializeLine(node.transform,parentNode.transform);
            Debug.Log($"{node.name} has create Line");
        }

        nodeLineBinding.Add(node, lineComponent);
    }

    public void DeleteLine(Node node)
    {
        Line line = nodeLineBinding[node];
        if (line != null)
        {
            line.gameObject.SetActive(false);
        }
    }

    public void StartGame()
    {
        if (GameManager.Instance.IsSceneTransitionInProgress)
        {
            return;
        }

        if (GameManager.Instance.HasSavedGame)
        {
            if (!AwaitingNewGameConfirmation)
            {
                AwaitingNewGameConfirmation = true;
                AssistiveSupport.notificationDispatcher.SendAnnouncement(
                    "已有存档。再次选择开始游戏将删除原存档");
                return;
            }
        }

        ConfirmStartGame();
    }

    public bool RequestStartGameForAccessibility()
    {
        if (GameManager.Instance.IsSceneTransitionInProgress)
        {
            return false;
        }

        if (GameManager.Instance.HasSavedGame)
        {
            if (!AwaitingNewGameConfirmation)
            {
                AwaitingNewGameConfirmation = true;
                AssistiveSupport.notificationDispatcher.SendAnnouncement(
                    "已有存档。请选择确认重新开始或保留存档");
            }

            return true;
        }

        return ConfirmStartGame();
    }

    public bool ConfirmStartGame()
    {
        if (GameManager.Instance.IsSceneTransitionInProgress)
        {
            return false;
        }

        AwaitingNewGameConfirmation = false;
        GameManager.Instance.StartNewGame();
        if (GameManager.Instance.IsDarkRoomMode)
        {
            return GameManager.Instance.LaunchDarkRoom();
        }
        GameManager.Instance.StartChangeSceneCoroutine("MainMenu","GameScene",GameState.Generating);
        return true;
    }

    public bool CancelStartGame()
    {
        AwaitingNewGameConfirmation = false;
        return true;
    }

    public void ReturnToLobby()
    {
        TryReturnToLobby();
    }

    public bool TryReturnToLobby()
    {
        return GameLobbyReturnBridge.TryReturn("factory_escape_main_menu");
    }

    public void ContinueFromMain(Node targetNode)
    {
        TryContinueFromMain(targetNode);
    }

    public bool TryContinueFromMain(Node targetNode)
    {
        AwaitingNewGameConfirmation = false;
        if (GameManager.Instance.IsSceneTransitionInProgress)
        {
            return false;
        }

        if (!GameManager.Instance.TryLoadSavedGame(out string errorMessage))
        {
            Debug.Log(errorMessage);
            AssistiveSupport.notificationDispatcher.SendAnnouncement(errorMessage);
            if (targetNode != null)
            {
                NoGameArchive(targetNode);
            }
            return false;
        }

        if (GameManager.Instance.IsDarkRoomMode)
        {
            return GameManager.Instance.LaunchDarkRoom();
        }

        GameManager.Instance.ChangeAndLoadGameScene("MainMenu");
        return true;
    }

    public void ContinueByLoad()
    {
        GameManager.Instance.ChangeAndLoadGameScene("PauseMenu");
    }

    public void PauseBackMain()
    {
        GameManager.Instance.StartChangeSceneCoroutine("PauseMenu","MainMenu",GameState.Start);
    }

    public void ReStart()
    {
        GameManager.Instance.StartChangeSceneCoroutine("FailMenu","GameScene",GameState.Generating);
    }

    public void FailBackMain()
    {
        GameManager.Instance.StartChangeSceneCoroutine("FailMenu","MainMenu",GameState.Start);
    }

    public void ChangeMusicVolume(float volume)
    {
        soundManager.Instance.setMusicVolume(volume);
    }

    public void ChangeSFXVolume(float volume)
    {
        soundManager.Instance.setSfxVolume(volume);
    }

    public void ChangeMusicEnabled(bool enabled)
    {
        soundManager.Instance.setMusicEnabled(enabled);
    }

    public void ChangeSFXEnabled(bool enabled)
    {
        soundManager.Instance.setSfxEnabled(enabled);
    }

    private void NoGameArchive(Node currentNode)
    {   
        Node parentNode = startNodes.Find(x => x.id == currentNode.parentID);

        currentNode.transform.position = FactoryEscapeAccessibility.ReduceMotion
            ? parentNode.transform.position + (Vector3)(new Vector2(-0.4f,0.4f) * GameManager.Instance.popUpForce)
            : parentNode.transform.position;
        currentNode.transform.localScale = FactoryEscapeAccessibility.ReduceMotion
            ? Vector3.one
            : Vector3.one * 0.3f;
        currentNode.gameObject.SetActive(true);

        Instance.CreateLine(currentNode);
        soundManager.Instance.PlaySFX("NodeBorn");

        if (FactoryEscapeAccessibility.ReduceMotion)
        {
            currentNode.isPopping = false;
            return;
        }

        Sequence sequence = DOTween.Sequence();
        sequence.Append(currentNode.transform.DOMove(
            new Vector2(-0.4f,0.4f) * GameManager.Instance.popUpForce,GameManager.Instance.tweenDuring
            ).SetRelative().OnStart(() => 
            {
                currentNode.isPopping = true;
            }).OnComplete(() => 
            {
                currentNode.isPopping = false;
            }));
            
        sequence.Append(currentNode.transform.DOScale(new Vector3(1f,0.3f,1),0.1f));
        sequence.Append(currentNode.transform.DOScale(new Vector3(1f,1f,1),0.1f));
    }
}
