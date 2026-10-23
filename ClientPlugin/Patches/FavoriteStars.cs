using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using HarmonyLib;
using Sandbox.Engine.Networking;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Steamworks;
using VRage;
using VRage.GameServices;
using VRage.Input;

namespace ClientPlugin.Patches;

// The star in the first column of the server tables marks the player's favorite servers
// instead of Keen's "recommended" ones. Only the Steam favorites are read; servers
// favorited through EOS (crossplay) get no star.
public static class FavoriteStars
{
    private const string ToolTipText = "Favorite server";
    private const uint FavoriteFlag = 1; // k_unFavoriteFlagFavorite; 2 is the history flag

    private static readonly HashSet<string> ConnectionStrings = new();

    private static readonly AccessTools.FieldRef<MyGuiControlTable.Cell, MyToolTips> CellToolTip =
        AccessTools.FieldRefAccess<MyGuiControlTable.Cell, MyToolTips>("ToolTip");

    public static void Reload()
    {
        ConnectionStrings.Clear();
        try
        {
            var appId = MyGameService.AppId;
            var count = SteamMatchmaking.GetFavoriteGameCount();
            for (var i = 0; i < count; i++)
            {
                if (
                    SteamMatchmaking.GetFavoriteGame(
                        i,
                        out var gameAppId,
                        out var ip,
                        out var port,
                        out _,
                        out var flags,
                        out _
                    )
                    && gameAppId.m_AppId == appId
                    && (flags & FavoriteFlag) != 0
                )
                {
                    var endpoint = new IPEndPoint(
                        IPAddressExtensions.FromIPv4NetworkOrder(ip),
                        port
                    );
                    ConnectionStrings.Add("steam://" + endpoint);
                }
            }
        }
        catch (Exception)
        {
            // Steam is not available (offline client), there are no favorites to show
        }
    }

    public static bool IsFavorite(MyGameServerItem server) =>
        server.ConnectionString != null && ConnectionStrings.Contains(server.ConnectionString);

    public static void Mark(MyGuiControlTable.Row row)
    {
        if (row?.UserData is not MyGameServerItem server || row.CellCount == 0)
            return;

        var favorite = IsFavorite(server);
        var cell = row.GetCell(0);
        cell.Icon = favorite ? MyGuiConstants.TEXTURE_ICON_STAR : null;
        CellToolTip(cell) = favorite ? new MyToolTips(ToolTipText) : null;
    }
}

[HarmonyPatch(typeof(MyGuiControlTable), nameof(MyGuiControlTable.Add))]
// ReSharper disable once UnusedType.Global
public static class TableAddPatch
{
    // ReSharper disable once UnusedMember.Global
    public static void Prefix(MyGuiControlTable.Row row) => FavoriteStars.Mark(row);
}

[HarmonyPatch(typeof(MyGuiControlTable), nameof(MyGuiControlTable.Insert))]
// ReSharper disable once UnusedType.Global
public static class TableInsertPatch
{
    // ReSharper disable once UnusedMember.Global
    public static void Prefix(MyGuiControlTable.Row row) => FavoriteStars.Mark(row);
}

// The right click menu offers "Remove from favorites" on starred servers on every tab, not only
// on the Favorites tab, and the star follows the change at once.
[HarmonyPatch(typeof(MyGuiScreenJoinGame), "OnServerTableItemSelected")]
// ReSharper disable once UnusedType.Global
public static class ServerContextMenuPatch
{
    private sealed class MenuItem
    {
        public MyGameServerItem Server;
        public bool Remove;
    }

    // ReSharper disable once UnusedMember.Global
    // ReSharper disable InconsistentNaming
    public static bool Prefix(
        MyGuiControlTable sender,
        MyGuiControlTable.EventArgs eventArgs,
        MyGuiControlContextMenu ___m_contextMenu,
        MyGuiControlTabPage ___m_favoritesPage,
        MyGuiScreenJoinGame __instance
    )
    // ReSharper restore InconsistentNaming
    {
        // Same checks as vanilla
        if (
            eventArgs.MouseButton != MyMouseButtonsEnum.Right
            || sender.SelectedRow?.UserData is not MyGameServerItem server
            || string.IsNullOrEmpty(server.ConnectionString)
            || sender.SelectedRow.GetCell(5)?.ToolTip == null
        )
            return true;

        var remove =
            __instance.SelectedPage == ___m_favoritesPage || FavoriteStars.IsFavorite(server);
        ___m_contextMenu.CreateNewContextMenu();
        ___m_contextMenu.AddItem(
            MyTexts.Get(
                remove
                    ? MyCommonTexts.JoinGame_Favorites_Remove
                    : MyCommonTexts.JoinGame_Favorites_Add
            ),
            "",
            "",
            new MenuItem { Server = server, Remove = remove }
        );
        ___m_contextMenu.ItemList_UseSimpleItemListMouseOverCheck = true;
        ___m_contextMenu.Activate();
        return false;
    }

    [HarmonyPatch(typeof(MyGuiScreenJoinGame), "OnContextMenu_ItemClicked")]
    // ReSharper disable once UnusedType.Global
    public static class ItemClickedPatch
    {
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable InconsistentNaming
        public static bool Prefix(
            MyGuiControlContextMenu.EventArgs eventArgs,
            MyGuiControlTable ___m_gamesTable,
            MyGuiControlTabPage ___m_favoritesPage,
            MyGuiScreenJoinGame __instance
        )
        // ReSharper restore InconsistentNaming
        {
            if (eventArgs.UserData is not MenuItem item)
                return true;

            if (item.Remove)
                MyGameService.RemoveFavoriteGame(item.Server);
            else
                MyGameService.AddFavoriteGame(item.Server);
            FavoriteStars.Reload();

            if (item.Remove && __instance.SelectedPage == ___m_favoritesPage)
            {
                // As vanilla does on the Favorites tab
                ___m_gamesTable.RemoveSelectedRow();
                ___m_favoritesPage.Text = new StringBuilder()
                    .Append(MyTexts.Get(MyCommonTexts.JoinGame_TabTitle_Favorites))
                    .Append(" (")
                    .Append(___m_gamesTable.RowsCount)
                    .Append(")");
            }
            else
            {
                FavoriteStars.Mark(___m_gamesTable.SelectedRow);
            }

            return false;
        }
    }
}
