namespace AccessibilityPreviewer;

internal enum PreviewKeyAction
{
    None,
    Previous,
    Next,
    First,
    Last,
    Activate,
    Back,
    Refresh,
    CommitInput,
    CancelInput
}

internal static class PreviewKeyMap
{
    public static PreviewKeyAction GetAction(
        Keys keyData,
        bool inputFocused,
        bool detailFocused,
        bool listFocused)
    {
        if ((keyData & Keys.Modifiers) != Keys.None)
        {
            return PreviewKeyAction.None;
        }

        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.F5)
        {
            return PreviewKeyAction.Refresh;
        }

        if (key == Keys.Escape)
        {
            return inputFocused ? PreviewKeyAction.CancelInput : PreviewKeyAction.Back;
        }

        if (inputFocused)
        {
            return key == Keys.Enter ? PreviewKeyAction.CommitInput : PreviewKeyAction.None;
        }

        if (detailFocused)
        {
            return PreviewKeyAction.None;
        }

        return key switch
        {
            Keys.Left or Keys.Up => PreviewKeyAction.Previous,
            Keys.Right or Keys.Down => PreviewKeyAction.Next,
            Keys.Home => PreviewKeyAction.First,
            Keys.End => PreviewKeyAction.Last,
            Keys.Enter or Keys.Space when listFocused => PreviewKeyAction.Activate,
            _ => PreviewKeyAction.None
        };
    }

    public static bool RunSelfTest()
    {
        return GetAction(Keys.Left, false, false, true) == PreviewKeyAction.Previous &&
               GetAction(Keys.Up, false, false, true) == PreviewKeyAction.Previous &&
               GetAction(Keys.Right, false, false, true) == PreviewKeyAction.Next &&
               GetAction(Keys.Down, false, false, true) == PreviewKeyAction.Next &&
               GetAction(Keys.Enter, false, false, true) == PreviewKeyAction.Activate &&
               GetAction(Keys.Space, false, false, true) == PreviewKeyAction.Activate &&
               GetAction(Keys.Escape, false, false, true) == PreviewKeyAction.Back &&
               GetAction(Keys.F5, false, false, true) == PreviewKeyAction.Refresh &&
               GetAction(Keys.Home, false, false, true) == PreviewKeyAction.First &&
               GetAction(Keys.End, false, false, true) == PreviewKeyAction.Last &&
               GetAction(Keys.Enter, true, false, false) == PreviewKeyAction.CommitInput &&
               GetAction(Keys.Escape, true, false, false) == PreviewKeyAction.CancelInput &&
               GetAction(Keys.Left, true, false, false) == PreviewKeyAction.None &&
               GetAction(Keys.Left, false, true, false) == PreviewKeyAction.None;
    }
}
