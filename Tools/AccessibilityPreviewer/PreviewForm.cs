using System.Drawing;

namespace AccessibilityPreviewer;

internal sealed class PreviewForm : Form
{
    private sealed class LiveStatusLabel : Label
    {
        public void NotifyScreenReader()
        {
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }

    private readonly PreviewFileChannel channel;
    private readonly ListBox itemList = new();
    private readonly TextBox detailText = new();
    private readonly TextBox inputText = new();
    private readonly Button activateButton = new();
    private readonly Button backButton = new();
    private readonly Button refreshButton = new();
    private readonly LiveStatusLabel statusLabel = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private long lastRevision = -1;
    private string lastStatus = string.Empty;

    public PreviewForm(string projectRoot)
    {
        channel = new PreviewFileChannel(projectRoot);
        channel.Enable();

        Text = "游戏无障碍流程预览器";
        AccessibleName = "游戏无障碍流程预览器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 480);
        Size = new Size(780, 620);
        KeyPreview = true;

        Controls.Add(BuildLayout());

        itemList.SelectedIndexChanged += (_, _) => UpdateSelectionDetails();
        itemList.DoubleClick += (_, _) => ActivateCurrent();
        activateButton.Click += (_, _) => ActivateCurrent();
        backButton.Click += (_, _) => SendBack();
        refreshButton.Click += (_, _) => SendRefresh();
        FormClosed += (_, _) => channel.Disable();

        refreshTimer.Interval = 200;
        refreshTimer.Tick += (_, _) => RefreshState();
        refreshTimer.Start();

        AnnounceStatus("正在等待 Unity 播放模式");
        Shown += (_, _) => itemList.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        PreviewKeyAction action = PreviewKeyMap.GetAction(
            keyData, inputText.Focused, detailText.Focused, itemList.Focused);
        switch (action)
        {
            case PreviewKeyAction.Previous:
                MovePreviewFocus(-1);
                return true;
            case PreviewKeyAction.Next:
                MovePreviewFocus(1);
                return true;
            case PreviewKeyAction.First:
                MovePreviewFocusTo(0);
                return true;
            case PreviewKeyAction.Last:
                MovePreviewFocusTo(itemList.Items.Count - 1);
                return true;
            case PreviewKeyAction.Activate:
                ActivateCurrent();
                return true;
            case PreviewKeyAction.Back:
                SendBack();
                return true;
            case PreviewKeyAction.Refresh:
                SendRefresh();
                return true;
            case PreviewKeyAction.CommitInput:
                CommitInput();
                return true;
            case PreviewKeyAction.CancelInput:
                inputText.Text = (itemList.SelectedItem as PreviewItem)?.Value ?? string.Empty;
                itemList.Focus();
                AnnounceStatus("已取消输入");
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 3,
            RowCount = 5,
            TabStop = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        itemList.Dock = DockStyle.Fill;
        itemList.AccessibleName = "当前页面";
        itemList.TabIndex = 0;
        itemList.IntegralHeight = false;
        layout.Controls.Add(itemList, 0, 0);
        layout.SetColumnSpan(itemList, 3);

        detailText.Dock = DockStyle.Fill;
        detailText.Multiline = true;
        detailText.ReadOnly = true;
        detailText.ScrollBars = ScrollBars.Vertical;
        detailText.AccessibleName = "当前项目详情";
        detailText.TabIndex = 1;
        layout.Controls.Add(detailText, 0, 1);
        layout.SetColumnSpan(detailText, 3);

        inputText.Dock = DockStyle.Fill;
        inputText.AccessibleName = "输入内容";
        inputText.TabIndex = 2;
        inputText.Enabled = false;
        layout.Controls.Add(inputText, 0, 2);
        layout.SetColumnSpan(inputText, 3);

        ConfigureButton(activateButton, "激活", 3);
        ConfigureButton(backButton, "返回", 4);
        ConfigureButton(refreshButton, "刷新", 5);
        activateButton.Enabled = false;
        layout.Controls.Add(activateButton, 0, 3);
        layout.Controls.Add(backButton, 1, 3);
        layout.Controls.Add(refreshButton, 2, 3);

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.AccessibleRole = AccessibleRole.StaticText;
        statusLabel.TabStop = false;
        layout.Controls.Add(statusLabel, 0, 4);
        layout.SetColumnSpan(statusLabel, 3);
        return layout;
    }

    private static void ConfigureButton(Button button, string text, int tabIndex)
    {
        button.Text = text;
        button.AccessibleName = text;
        button.Dock = DockStyle.Fill;
        button.TabIndex = tabIndex;
        button.UseVisualStyleBackColor = true;
    }

    private void RefreshState()
    {
        try
        {
            PreviewState? state = channel.ReadState();
            if (state == null || state.Revision == lastRevision)
            {
                return;
            }

            lastRevision = state.Revision;
            string previousKey = (itemList.SelectedItem as PreviewItem)?.Key ?? string.Empty;
            bool listHadFocus = itemList.Focused;
            bool inputHadFocus = inputText.Focused;
            itemList.BeginUpdate();
            itemList.Items.Clear();
            itemList.Items.AddRange(state.Items.Cast<object>().ToArray());
            itemList.EndUpdate();

            string targetKey = string.IsNullOrWhiteSpace(state.FocusKey)
                ? previousKey
                : state.FocusKey;
            int targetIndex = state.Items.FindIndex(item => item.Key == targetKey);
            if (targetIndex < 0 && state.Items.Count > 0)
            {
                targetIndex = 0;
            }

            if (targetIndex >= 0)
            {
                itemList.SelectedIndex = targetIndex;
            }

            UpdateSelectionDetails();
            string status = string.IsNullOrWhiteSpace(state.Status) ? "页面已更新" : state.Status;
            if (!string.Equals(status, lastStatus, StringComparison.Ordinal))
            {
                lastStatus = status;
                AnnounceStatus(status);
            }
            if (inputHadFocus && itemList.SelectedItem is PreviewItem { Editable: true })
            {
                inputText.Focus();
            }
            else if (listHadFocus || state.Items.Count > 0)
            {
                itemList.Focus();
            }
        }
        catch (IOException)
        {
            // Unity may be replacing the state file; the next timer tick retries.
        }
        catch (Exception exception)
        {
            AnnounceStatus($"读取失败：{exception.Message}");
        }
    }

    private void MovePreviewFocus(int offset)
    {
        if (itemList.Items.Count == 0)
        {
            AnnounceStatus("当前页面没有项目");
            return;
        }

        int current = itemList.SelectedIndex < 0 ? 0 : itemList.SelectedIndex;
        itemList.SelectedIndex = Math.Clamp(current + offset, 0, itemList.Items.Count - 1);
        itemList.Focus();
    }

    private void MovePreviewFocusTo(int index)
    {
        if (itemList.Items.Count == 0)
        {
            AnnounceStatus("当前页面没有项目");
            return;
        }

        itemList.SelectedIndex = Math.Clamp(index, 0, itemList.Items.Count - 1);
        itemList.Focus();
    }

    private void ActivateCurrent()
    {
        if (itemList.SelectedItem is not PreviewItem item || !item.Actionable ||
            item.State.Contains("Disabled", StringComparison.Ordinal))
        {
            AnnounceStatus("当前项目不可激活");
            itemList.Focus();
            return;
        }

        if (item.Editable)
        {
            channel.Send("activate", item.Key);
            inputText.Enabled = true;
            inputText.Text = item.Value;
            inputText.Focus();
            inputText.SelectAll();
            AnnounceStatus("可以输入内容");
            return;
        }

        channel.Send("activate", item.Key);
        AnnounceStatus($"正在激活：{item.Label}");
        itemList.Focus();
    }

    private void CommitInput()
    {
        if (itemList.SelectedItem is not PreviewItem item || !item.Editable)
        {
            AnnounceStatus("当前项目不可输入");
            itemList.Focus();
            return;
        }

        channel.Send("set-text", item.Key, inputText.Text);
        AnnounceStatus("输入内容已写入");
        itemList.Focus();
    }

    private void SendBack()
    {
        channel.Send("back");
        AnnounceStatus("正在返回");
        itemList.Focus();
    }

    private void SendRefresh()
    {
        channel.Send("refresh");
        AnnounceStatus("正在刷新");
        itemList.Focus();
    }

    private void UpdateSelectionDetails()
    {
        if (itemList.SelectedItem is not PreviewItem item)
        {
            detailText.Clear();
            inputText.Clear();
            inputText.Enabled = false;
            activateButton.Enabled = false;
            return;
        }

        string role = item.Role switch
        {
            "Button" => "可操作项目",
            "StaticText" => "提示文字",
            "Header" => "标题",
            "TextField" => "输入内容",
            _ => "页面项目"
        };
        detailText.Text = string.IsNullOrWhiteSpace(item.Value)
            ? $"{item.Label}\r\n{role}"
            : $"{item.Label}\r\n{item.Value}\r\n{role}";
        activateButton.Enabled = item.Actionable &&
                                 !item.State.Contains("Disabled", StringComparison.Ordinal);
        inputText.Enabled = item.Editable;
        if (!inputText.Focused)
        {
            inputText.Text = item.Editable ? item.Value : string.Empty;
        }
    }

    private void AnnounceStatus(string text)
    {
        statusLabel.Text = text;
        statusLabel.AccessibleName = text;
        statusLabel.NotifyScreenReader();
    }
}
