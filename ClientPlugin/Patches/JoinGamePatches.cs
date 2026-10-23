using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage.GameServices;

namespace ClientPlugin.Patches;

// Keen marks the servers listed in rankedServers.xml as "recommended": they get a star
// and are pinned to the top of the Servers, Favorites, History, LAN and Friends tables,
// whatever the sort column. Treating every server as unranked removes both.
[HarmonyPatch(typeof(MyGuiScreenJoinGame), "IsRanked", typeof(MyGameServerItem))]
// ReSharper disable once UnusedType.Global
public static class IsRankedPatch
{
    // ReSharper disable once UnusedMember.Global
    public static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

// Vanilla sorts the server tables by ping. Sort by player count, busiest first, instead.
// The column headers still switch the sort column and direction as before.
[HarmonyPatch(typeof(MyGuiScreenJoinGame), "InitServersTable")]
// ReSharper disable once UnusedType.Global
public static class InitServersTablePatch
{
    // Index of the Players column in InitServersTable: rank, password, experimental,
    // world, game mode, networking icon, server name, players.
    private const int PlayersColumn = 7;

    // ReSharper disable once UnusedMember.Global
    // ReSharper disable once InconsistentNaming
    public static void Postfix(MyGuiControlTable ___m_gamesTable)
    {
        // PlayerCountComparison is written in reverse, so the first (ascending)
        // sort of that column puts the most players on top.
        ___m_gamesTable.SortByColumn(PlayersColumn);
    }
}
