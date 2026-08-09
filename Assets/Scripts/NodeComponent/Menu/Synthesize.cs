using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class Synthesize : MonoBehaviour, IAccessibleNodeAction
{
    public Node targetNode;
    Node myNode;

    private void Start() {
        myNode = transform.GetComponent<Node>();
    }

    private void OnMouseUp()
    {
        if (myNode.isPopping || UIManager.Instance.UIShow) return;

        if (!myNode.isDragging)
        {
            if (myNode.isSelected)
            {
            }
            else
            {
                // 删除其他所有节点的选中状态
                GameMenu.Instance.ClearAllSelectedNode(myNode);
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


    private IEnumerator PopUpChildNodes(List<NodeInfo> nodeInfos)
    {
        foreach (NodeInfo childNode in nodeInfos)
        {
            Node currentNode = childNode.node; // Instantiate(childNode.node,transform.position,Quaternion.identity);

            currentNode.transform.position = FactoryEscapeAccessibility.ReduceMotion
                ? transform.position + (Vector3)(childNode.direction * GameManager.Instance.popUpForce)
                : transform.position;
            currentNode.transform.localScale = FactoryEscapeAccessibility.ReduceMotion
                ? Vector3.one
                : Vector3.one * 0.3f;
            currentNode.gameObject.SetActive(true);

            GameMenu.Instance.CreateLine(currentNode);
            soundManager.Instance.PlaySFX("NodeBorn");

            if (FactoryEscapeAccessibility.ReduceMotion)
            {
                currentNode.isPopping = false;
                continue;
            }

            Sequence sequence = DOTween.Sequence();
            sequence.Append(currentNode.transform.DOMove(
                childNode.direction * GameManager.Instance.popUpForce,GameManager.Instance.tweenDuring
                ).SetRelative().OnStart(() => 
                {
                    currentNode.isPopping = true;
                }).OnComplete(() => 
                {
                    currentNode.isPopping = false;
                }));
                
            sequence.Append(currentNode.transform.DOScale(new Vector3(1f,0.3f,1),0.1f));
            sequence.Append(currentNode.transform.DOScale(new Vector3(1f,1f,1),0.1f));

            yield return new WaitForSeconds(0.1f);
        }
    }

    private void OnTriggerStay2D(Collider2D other) {
        if (other.GetComponent<Node>() == targetNode && !myNode.isPopping && !myNode.isDragging && !targetNode.isPopping && !targetNode.isDragging && !myNode.hasPopUp)
        {
            targetNode.gameObject.SetActive(false);
            GameMenu.Instance.DeleteLine(targetNode);
            StartCoroutine(PopUpChildNodes(myNode.nodeInfos));
            myNode.hasPopUp = true;
        }
    }

    public bool ActivateAccessibility()
    {
        if (myNode == null || myNode.hasPopUp)
        {
            return false;
        }

        if (targetNode != null)
        {
            targetNode.gameObject.SetActive(false);
            if (GameMenu.Instance.nodeLineBinding.ContainsKey(targetNode))
            {
                GameMenu.Instance.DeleteLine(targetNode);
            }
        }

        StartCoroutine(PopUpChildNodes(myNode.nodeInfos));
        myNode.hasPopUp = true;
        return true;
    }
}
