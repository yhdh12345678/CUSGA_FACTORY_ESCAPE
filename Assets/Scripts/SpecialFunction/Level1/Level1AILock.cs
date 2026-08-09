using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Level1AILock : MonoBehaviour, IAccessibleNodeAction
{
    private Node myNode;
    [SerializeField] private int submissionTimes;
    [SerializeField] private bool hasResult = false;
    private List<CutSceneCell> firstFailResult;
    private List<CutSceneCell> secondFailResult;
    private bool getCutScene = false;

    public void InitializeLevel1AILock(NodeSO nodeSO)
    {
        Level1AILockSO level1AILockSO= (Level1AILockSO)nodeSO;

        submissionTimes = level1AILockSO.submissionTimes;
        firstFailResult = level1AILockSO.firstFailResult;
        secondFailResult = level1AILockSO.secondFailResult;

        myNode = GetComponent<Node>();
    }

    private void OnEnable() {
        StaticEventHandler.OnCommit += StaticEventHandler_OnCommit;
    }

    private void OnDisable() {
        StaticEventHandler.OnCommit -= StaticEventHandler_OnCommit;
    }

    private void Start() {
        myNode = transform.GetComponent<Node>();
    }

    public void InitializeAILocked(NodeSO nodeSO)
    {
        AILockedNodeSO aiLockedNodeSO = (AILockedNodeSO)nodeSO;

        submissionTimes = aiLockedNodeSO.submissionTimes;
    }

    private void OnMouseUp()
    {
        if (myNode.isPopping || UIManager.Instance.UIShow) return;

        if (!myNode.isDragging)
        {
            if (myNode.isSelected)
            {
                // 节点交互内容
                if(!hasResult)
                {
                    OpenAIDialog();
                }
                
            }
            else
            {
                // 删除其他所有节点的选中状态
                NodeMapBuilder.Instance.ClearAllSelectedNode(myNode);
                myNode.GetSelectedAnimate();
    
                myNode.isSelected = true;
            }
        }
        else
        {
            myNode.isDragging = false;
            GameManager.Instance.haveNodeDrag = false;
        }        

    }

    private void StaticEventHandler_OnCommit(CommitArgs args)
    {
        GameManager.Instance.currentAnxiety += args.anxiety_change_value;
        submissionTimes--;
        tongyi_AI.instance.SubmitTimer--;

        DialogSystem.Instance.anxietyValue.localScale = new Vector3(GameManager.Instance.currentAnxiety/GameManager.Instance.maxAnxiety, 1, 1);
        DialogSystem.Instance.value.text = (GameManager.Instance.currentAnxiety/GameManager.Instance.maxAnxiety * 100).ToString("F0") + "%";

        if (submissionTimes == 0)
        {
            hasResult = true;
            tongyi_AI.instance.send_button.interactable = false;
        }

        FactoryEscapeAccessibility.UpdateAIStatus(submissionTimes == 0);
    }

    private void Update() {
        if (hasResult && DialogSystem.Instance.textFinished && Input.GetMouseButtonDown(0) && DialogSystem.Instance.AIDialogPanel.gameObject.activeSelf)
        {
            CompleteAccessibilityResult();
        }

        if (hasResult && !DialogSystem.Instance.AIDialogPanel.gameObject.activeSelf && !getCutScene)
        {
            CompleteAccessibilityResult();
        }
    }

    /// <summary>
    /// 检查焦虑值并给出相关的结局
    /// </summary>
    private void CheckAnxietyValue()
    {
        if (GameManager.Instance.level1GetResultTimes == 0)
        {
            if (GameManager.Instance.CheckAnxietyValue())
            {
                AdvanceToNextLevel();
            }
            else
            {
                VideoManager.Instance.ShowCutScenes(firstFailResult);

                UIManager.Instance.leftNodeGraphButton.gameObject.SetActive(true);
                UIManager.Instance.rightNodeGraphButton.gameObject.SetActive(true);

                GameManager.Instance.level1GetResultTimes++;
            }
        }
        else
        {
            if (GameManager.Instance.CheckAnxietyValue())
            {
                AdvanceToNextLevel();
            }
            else
            {
                VideoManager.Instance.ShowCutScenes(secondFailResult);
            }
        }
    }

    public bool ActivateAccessibility()
    {
        if (myNode == null || hasResult)
        {
            return AccessibilityResultReady && CompleteAccessibilityResult();
        }

        OpenAIDialog();
        return true;
    }

    public bool AccessibilityResultReady => hasResult && DialogSystem.Instance != null &&
                                            DialogSystem.Instance.textFinished;

    public bool CompleteAccessibilityResult()
    {
        if (!AccessibilityResultReady)
        {
            return false;
        }

        DialogSystem.Instance.AIDialogPanel.gameObject.SetActive(false);
        UIManager.Instance.UIShow = false;
        if (!getCutScene)
        {
            CheckAnxietyValue();
            getCutScene = true;
        }

        return true;
    }

    private void OpenAIDialog()
    {
        tongyi_AI.instance.SubmitTimer = submissionTimes;
        tongyi_AI.instance.send_button.interactable = true;
        tongyi_AI.instance.input_field.SetActive(true);
        UIManager.Instance.UIShow = true;
        DialogSystem.Instance.AIDialogPanel.gameObject.SetActive(true);
        DialogSystem.Instance.anxietyValue.localScale = new Vector3(
            GameManager.Instance.currentAnxiety / GameManager.Instance.maxAnxiety, 1, 1);
        DialogSystem.Instance.value.text =
            (GameManager.Instance.currentAnxiety / GameManager.Instance.maxAnxiety * 100).ToString("F0") + "%";
        FactoryEscapeAccessibility.RefreshScreen();
    }

    private static void AdvanceToNextLevel()
    {
        GameManager.Instance.levelIndex++;
        GameManager.Instance.gameState = GameState.Generating;
    }
}
