using System.Collections;
using UnityEngine;

public class Probe : MonoBehaviour, IAccessibleNodeAction
{
    [Header("观测参数")]
    private string targetNodeID;
    public Node myNode;

    [Space(5)]
    [Header("可调整数据")]
    private SpriteRenderer indicatorLight;
    public float blinkSpeed = 0.5f; // 闪烁速度
    public Color targetColor = Color.green; // 目标颜色
    private Color originalColor; // 初始颜色

    public Transform target; // 目标位置
    public float detectionDistance = 10f; // 检测距离
    public float interactiveDistance = 3f; // 交互距离

    private bool isBlinking = false;
    private Coroutine blinkCoroutine;

    private void Start() {
        myNode = transform.GetComponent<Node>();

        indicatorLight = GetComponentInChildren<SpriteRenderer>();

        originalColor = indicatorLight.color;
    }

    public void InitializeProbe(NodeSO nodeSO)
    {
        ProbeNodeSO probeNode = (ProbeNodeSO)nodeSO;

        targetNodeID = probeNode.targetIDForDetection;
    }
    
    void Update()
    {
        if (NodeMapBuilder.Instance.nodeHasCreated.TryGetValue(targetNodeID, out Node targetNode) && target == null && !myNode.isPopping)
        {
            target = targetNode.transform;
        }

        if (target != null)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            if (distance < detectionDistance && distance > interactiveDistance) 
            {
                StartBlink();
                blinkSpeed = Mathf.Lerp(0.1f, 1f, (distance - interactiveDistance)/(detectionDistance - interactiveDistance));
            }
            else if (distance < interactiveDistance)
            {
                StopBlink(true);
                target.gameObject.SetActive(true);
            }
            else
            {
                StopBlink(false);
            }
        }
    }

    private void OnMouseUp()
    {
        if (myNode.isPopping || UIManager.Instance.UIShow) return;

        if (!myNode.isDragging)
        {
            if (myNode.isSelected)
            {
                // 节点交互内容
            }
            else
            {
                // 删除其他所有节点的选中状态
                NodeMapBuilder.Instance.ClearAllSelectedNode(myNode);
                myNode.GetSelectedAnimate();
    
                myNode.isSelected = true;
            }

            // UIManager.Instance.StartDisplayNodeTextForShowRoutine(myNode.nodeTextForShow);
            UIManager.Instance.DisplayNodeText(myNode.nodeTextForShow);
        }
        else
        {
            myNode.isDragging = false;
            GameManager.Instance.haveNodeDrag = false;
        } 
    }

    // 开始闪烁
    void StartBlink()
    {
        if (!isBlinking)
        {
            blinkCoroutine = StartCoroutine(BlinkRoutine());
        }
    }

    // 停止闪烁
    void StopBlink(bool targetReached)
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }

        isBlinking = false;
        if (indicatorLight != null)
        {
            indicatorLight.color = targetReached ? targetColor : originalColor;
        }
    }

    private void OnDisable()
    {
        StopBlink(false);
    }

    IEnumerator BlinkRoutine()
    {
        isBlinking = true;
        while (true)
        {
            // 切换指示灯的颜色
            indicatorLight.color = targetColor;
            yield return new WaitForSeconds(blinkSpeed);
            indicatorLight.color = originalColor;
            yield return new WaitForSeconds(blinkSpeed);
        }
    }

    public bool ActivateAccessibility()
    {
        Node targetNode = NodeMapBuilder.Instance.GetNode(targetNodeID);
        if (targetNode == null)
        {
            return false;
        }

        targetNode.gameObject.SetActive(true);
        target = targetNode.transform;
        StopBlink(true);
        return true;
    }
}
