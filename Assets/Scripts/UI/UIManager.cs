using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
public class UIManager : SingletonMonobehaviour<UIManager>
{
    [Space(10)]
    [Header("节点功能UI对象")]
    [Tooltip("图节点展示图的UI对象")]
    public Transform graphNodeUI;
    [Tooltip("将节点文本显示的UI对象")]
    public Transform nodeTextForShow;
    [Tooltip("滑动文本框内的文本")]
    public Transform scrollViewContent;
    [Tooltip("节点文本显示UI对象")]
    public Transform textNodeUI;
    [Tooltip("AI对话日志")]
    public Transform AIDialogLog;
    [Tooltip("AI对话界面")]
    public Transform AIDialogPanel;

    [Space(5)]
    [Header("场景UI对象")]
    [Tooltip("背景UI对象")]
    public Transform backGround;
    [Tooltip("前景UI对象")]
    public Transform frontGround;
    [Tooltip("向右侧切换节点图按钮")]
    public Transform rightNodeGraphButton;
    [Tooltip("向左侧切换节点图按钮")]
    public Transform leftNodeGraphButton;
    [Tooltip("动画UI对象")]
    public Transform AnimatorUI;
    [Tooltip("第七关的天空UI")]
    public Transform SkyUI;
    [Tooltip("存档并退出按钮UI")]
    public Transform pauseButton;

    public bool UIShow = false;

    private void Start()
    {
        ConfigureSaveAndQuitButton();
    }

    private void Update() {
        if (nodeTextForShow != null)
        {
            nodeTextForShow.gameObject.SetActive(!UIShow);
        }

        if (pauseButton != null)
        {
            pauseButton.gameObject.SetActive(!UIShow);
        }
    }

    /// <summary>
    /// 关闭图片节点UI
    /// </summary>
    public void CloseGraph()
    {
        graphNodeUI.gameObject.SetActive(false);
        UIShow = false;
    }

    /// <summary>
    /// 展示该节点的文本内容
    /// </summary>
    public void DisplayNodeText(string nodeTextForShow)
    {
        if (!this.nodeTextForShow.gameObject.activeSelf)
        {
            this.nodeTextForShow.gameObject.SetActive(true);
        }

        this.nodeTextForShow.GetComponent<TMP_Text>().text = nodeTextForShow;
    }

    /// <summary>
    /// 关闭文本节点UI
    /// </summary>
    public void CloseTextNodeUI()
    {
        textNodeUI.gameObject.SetActive(false);
        UIShow = false;
    } 

    /// <summary>
    /// 打开图片节点UI
    /// </summary>
    public void PopUpGraph(Sprite sprite)
    {
        Image image = graphNodeUI.Find("BackGround/Graph").GetComponent<Image>();

        image.sprite = sprite;
        image.SetNativeSize();

        graphNodeUI.gameObject.SetActive(true);
        
        if (FactoryEscapeAccessibility.ReduceMotion)
        {
            graphNodeUI.transform.localScale = Vector3.one;
            UIShow = true;
            return;
        }

        graphNodeUI.transform.localScale = Vector3.one * 0.3f;
        Sequence sequence = DOTween.Sequence();
        sequence.Append(graphNodeUI.transform.DOScale(new Vector3(1f,0.3f,1),0.1f));
        sequence.Append(graphNodeUI.transform.DOScale(new Vector3(1f,1f,1),0.1f));
        UIShow = true;
    }

    /// <summary>
    /// 展示文本节点UI
    /// </summary>
    public void DisplayTextNodeContent(TextAsset text) 
    {
        if (text == null) return;

        scrollViewContent.GetComponent<TMP_Text>().text = text.text;

        textNodeUI.gameObject.SetActive(true);
        if (FactoryEscapeAccessibility.ReduceMotion)
        {
            textNodeUI.transform.localScale = Vector3.one;
            UIShow = true;
            return;
        }

        textNodeUI.transform.localScale = Vector3.one * 0.3f;
        Sequence sequence = DOTween.Sequence();
        sequence.Append(textNodeUI.transform.DOScale(new Vector3(1f,0.3f,1),0.1f));
        sequence.Append(textNodeUI.transform.DOScale(new Vector3(1f,1f,1),0.1f));
        UIShow = true;
    }

    /// <summary>
    /// 打开与关闭AI日志
    /// </summary>
    public void DisplayAndCloseAILog()
    {
        AIDialogLog.gameObject.SetActive(!AIDialogLog.gameObject.activeSelf);
        if (FactoryEscapeAccessibility.ReduceMotion)
        {
            AIDialogLog.transform.localScale = Vector3.one;
            return;
        }

        AIDialogLog.transform.localScale = Vector3.one * 0.3f;
        Sequence sequence = DOTween.Sequence();
        sequence.Append(AIDialogLog.transform.DOScale(new Vector3(1f,0.3f,1),0.1f));
        sequence.Append(AIDialogLog.transform.DOScale(new Vector3(1f,1f,1),0.1f));
    }

    public IEnumerator Fade(float startFadeAlpha, float targetFadeAlpha, float fadeSecounds, Color color)
    {
        Image image = backGround.GetComponent<Image>();
        image.color = color;

        if (FactoryEscapeAccessibility.ReduceMotion)
        {
            image.color = new Color(image.color.r, image.color.g, image.color.b, targetFadeAlpha);
            yield break;
        }

        float time = 0;

        while (time <= fadeSecounds)
        {
            time += Time.deltaTime;
            image.color = new Color(image.color.r, image.color.g, image.color.b, Mathf.Lerp(startFadeAlpha, targetFadeAlpha, time/fadeSecounds));
            yield return null;
        }

    }

    /// <summary>
    /// 保存当前进度并退出游戏
    /// </summary>
    public void SaveAndQuit()
    {
        GameManager.Instance.StartSaveAndQuit();
    }

    private void ConfigureSaveAndQuitButton()
    {
        if (pauseButton == null)
        {
            return;
        }

        pauseButton.name = "SaveAndQuit";
        if (pauseButton is RectTransform buttonRect)
        {
            buttonRect.sizeDelta = new Vector2(220f, 56f);
        }

        Image buttonImage = pauseButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.sprite = null;
            buttonImage.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        }

        var labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(pauseButton, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = "存档并退出";
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 20f;
        label.fontSizeMax = 32f;
        label.raycastTarget = false;

        TMP_Text visibleText = nodeTextForShow == null
            ? null
            : nodeTextForShow.GetComponent<TMP_Text>();
        if (visibleText != null)
        {
            label.font = visibleText.font;
        }
    }
    
    /// <summary>
    /// 关闭AI对话框
    /// </summary>
    public void CloseAIDialogPanel()
    {
        AIDialogPanel.gameObject.SetActive(false);
        UIShow = false;
    }
}
