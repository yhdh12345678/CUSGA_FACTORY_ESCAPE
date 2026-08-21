using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Accessibility;

public sealed class DarkRoomAccessibility : MonoBehaviour
{
    private readonly AccessibilityHierarchy hierarchy = new AccessibilityHierarchy();
    private readonly Dictionary<string, AccessibilityNode> nodes = new Dictionary<string, AccessibilityNode>();
    private DarkRoomApp app;
    private string focusedKey = string.Empty;

    private void Start()
    {
        StartCoroutine(AttachAfterAppStarts());
    }

    private IEnumerator AttachAfterAppStarts()
    {
        yield return null;
        app = DarkRoomApp.Instance;
        if (app == null)
        {
            yield break;
        }
        app.AccessibilityRefreshRequested += OnRefreshRequested;
        AssistiveSupport.screenReaderStatusChanged += OnScreenReaderStatusChanged;
        if (AssistiveSupport.isScreenReaderEnabled)
        {
            Rebuild(string.Empty, string.Empty, true);
        }
    }

    private void OnDestroy()
    {
        if (app != null)
        {
            app.AccessibilityRefreshRequested -= OnRefreshRequested;
        }
        AssistiveSupport.screenReaderStatusChanged -= OnScreenReaderStatusChanged;
        if (AssistiveSupport.activeHierarchy == hierarchy)
        {
            AssistiveSupport.activeHierarchy = null;
        }
    }

    private void OnScreenReaderStatusChanged(bool enabled)
    {
        if (enabled)
        {
            Rebuild(string.Empty, string.Empty, true);
        }
        else if (AssistiveSupport.activeHierarchy == hierarchy)
        {
            AssistiveSupport.activeHierarchy = null;
        }
    }

    private void OnRefreshRequested(string focusKey, string announcement, bool screenChanged)
    {
        if (!AssistiveSupport.isScreenReaderEnabled)
        {
            return;
        }
        Rebuild(focusKey, announcement, screenChanged);
    }

    private void Rebuild(string requestedFocusKey, string announcement, bool screenChanged)
    {
        if (app == null)
        {
            return;
        }

        string targetKey = string.IsNullOrWhiteSpace(requestedFocusKey) ? focusedKey : requestedFocusKey;
        AssistiveSupport.activeHierarchy = null;
        hierarchy.Clear();
        nodes.Clear();

        IReadOnlyList<DarkRoomAccessibleItem> items = app.GetAccessibleItems();
        float fallbackHeight = Screen.height / Mathf.Max(1f, items.Count);
        for (int index = 0; index < items.Count; index++)
        {
            DarkRoomAccessibleItem item = items[index];
            AccessibilityNode node = hierarchy.AddNode(item.Label);
            node.value = item.Value ?? string.Empty;
            node.role = item.IsHeader
                ? AccessibilityRole.Header
                : item.IsButton ? AccessibilityRole.Button : AccessibilityRole.StaticText;
            node.state = item.IsDisabled ? AccessibilityState.Disabled : AccessibilityState.None;
            string key = item.Key;
            Rect fallback = new Rect(0f, index * fallbackHeight, Screen.width, fallbackHeight);
            node.frameGetter = () => app.TryGetScreenFrame(key, out Rect frame) ? frame : fallback;
            node.focusChanged += (_, focused) =>
            {
                if (focused)
                {
                    focusedKey = key;
                }
            };
            if (item.IsButton && !item.IsDisabled)
            {
                node.invoked += () => app.Activate(key);
            }
            nodes[key] = node;
        }

        AssistiveSupport.activeHierarchy = hierarchy;
        AccessibilityNode target = nodes.TryGetValue(targetKey, out AccessibilityNode requested)
            ? requested
            : hierarchy.rootNodes.FirstOrDefault();
        if (screenChanged)
        {
            AssistiveSupport.notificationDispatcher.SendScreenChanged(target);
        }
        else
        {
            AssistiveSupport.notificationDispatcher.SendLayoutChanged(target);
        }
        if (!string.IsNullOrWhiteSpace(announcement))
        {
            AssistiveSupport.notificationDispatcher.SendAnnouncement(announcement);
        }
    }
}

