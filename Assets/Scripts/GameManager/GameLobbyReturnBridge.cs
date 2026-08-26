using System;
using UnityEngine;

public static class GameLobbyReturnBridge
{
    private static Action<string> returnToLobby;
    private static bool orientationSessionActive;
    private static ScreenOrientation previousOrientation;
    private static bool previousAutorotateToPortrait;
    private static bool previousAutorotateToPortraitUpsideDown;
    private static bool previousAutorotateToLandscapeLeft;
    private static bool previousAutorotateToLandscapeRight;

    public static bool IsConnected => returnToLobby != null;

    public static void Connect(Action<string> handler)
    {
        returnToLobby = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public static void EnterGameSession()
    {
        BeginOrientationSession();
    }

    public static void ExitGameSession()
    {
        EndOrientationSession();
    }

    public static void Disconnect(Action<string> handler)
    {
        if (returnToLobby == handler)
        {
            returnToLobby = null;
            EndOrientationSession();
        }
    }

    public static bool TryReturn(string reason)
    {
        if (returnToLobby == null)
        {
            return false;
        }

        Action<string> handler = returnToLobby;
        EndOrientationSession();
        handler(reason);
        return true;
    }

    private static void BeginOrientationSession()
    {
        if (orientationSessionActive)
        {
            return;
        }

        previousOrientation = Screen.orientation;
        previousAutorotateToPortrait = Screen.autorotateToPortrait;
        previousAutorotateToPortraitUpsideDown = Screen.autorotateToPortraitUpsideDown;
        previousAutorotateToLandscapeLeft = Screen.autorotateToLandscapeLeft;
        previousAutorotateToLandscapeRight = Screen.autorotateToLandscapeRight;
        orientationSessionActive = true;

        Screen.autorotateToPortrait = true;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        Screen.orientation = ScreenOrientation.Portrait;
    }

    private static void EndOrientationSession()
    {
        if (!orientationSessionActive)
        {
            return;
        }

        Screen.autorotateToPortrait = previousAutorotateToPortrait;
        Screen.autorotateToPortraitUpsideDown = previousAutorotateToPortraitUpsideDown;
        Screen.autorotateToLandscapeLeft = previousAutorotateToLandscapeLeft;
        Screen.autorotateToLandscapeRight = previousAutorotateToLandscapeRight;
        Screen.orientation = previousOrientation;
        orientationSessionActive = false;
    }
}
