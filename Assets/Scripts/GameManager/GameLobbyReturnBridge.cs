using System;

public static class GameLobbyReturnBridge
{
    private static Action<string> returnToLobby;

    public static bool IsConnected => returnToLobby != null;

    public static void Connect(Action<string> handler)
    {
        returnToLobby = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public static void Disconnect(Action<string> handler)
    {
        if (returnToLobby == handler)
        {
            returnToLobby = null;
        }
    }

    public static bool TryReturn(string reason)
    {
        if (returnToLobby == null)
        {
            return false;
        }

        returnToLobby(reason);
        return true;
    }
}
