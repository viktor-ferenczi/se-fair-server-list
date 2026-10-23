using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using HarmonyLib;
using Sandbox.Engine.Networking;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Steamworks;
using VRage;
using VRage.FileSystem;
using VRage.GameServices;
using VRage.Input;
using VRage.Utils;

namespace ClientPlugin.Patches;

// The star in the first column of the server tables marks the player's favorite servers
// instead of Keen's "recommended" ones. Only the Steam favorites are read; servers
// favorited through EOS (crossplay) get no star.
//
// Steam keeps favorites by address only. The server and world name seen at each favorite
// address is remembered, so a favorite server that moves to a new address is still
// recognized. It gets the star, and its new address is added to the Steam favorites, which
// brings it back on the Favorites tab. The server name alone is not enough: many networks
// call their entry server "Lobby".
public static class FavoriteStars
{
    private const string ToolTipText = "Favorite server";
    private const string SteamPrefix = "steam://";
    private const uint FavoriteFlag = 1; // k_unFavoriteFlagFavorite; 2 is the history flag

    // Addresses on the Steam favorites list
    private static readonly HashSet<string> ConnectionStrings = new();

    // Server and world name last seen at each favorite address, kept between sessions
    private static readonly Dictionary<string, string> NameByAddress = new();

    // Names of the servers with at least one address on the Steam favorites list
    private static readonly HashSet<string> Names = new();

    private static string NamesPath =>
        Path.Combine(MyFileSystem.UserDataPath, "Storage", "FairServerList.favorites.txt");

    private static bool namesLoaded;

    private static readonly AccessTools.FieldRef<MyGuiControlTable.Cell, MyToolTips> CellToolTip =
        AccessTools.FieldRefAccess<MyGuiControlTable.Cell, MyToolTips>("ToolTip");

    public static void Reload()
    {
        LoadNames();
        ConnectionStrings.Clear();
        try
        {
            // No app id check: favorites added from Steam's own server browser have app id 0.
            // Addresses of other games' servers never match a row anyway.
            var count = SteamMatchmaking.GetFavoriteGameCount();
            for (var i = 0; i < count; i++)
            {
                if (
                    SteamMatchmaking.GetFavoriteGame(
                        i,
                        out _,
                        out var ip,
                        out var port,
                        out _,
                        out var flags,
                        out _
                    )
                    && (flags & FavoriteFlag) != 0
                )
                {
                    var endpoint = new IPEndPoint(
                        IPAddressExtensions.FromIPv4NetworkOrder(ip),
                        port
                    );
                    ConnectionStrings.Add(SteamPrefix + endpoint);
                }
            }
        }
        catch (Exception)
        {
            // Steam is not available (offline client), there are no favorites to show
        }

        Names.Clear();
        foreach (var address in ConnectionStrings)
        {
            if (NameByAddress.TryGetValue(address, out var name))
                Names.Add(name);
        }
    }

    private static string NameOf(MyGameServerItem server) =>
        string.IsNullOrWhiteSpace(server.Name) || string.IsNullOrWhiteSpace(server.Map)
            ? null
            : Clean(server.Name) + "\t" + Clean(server.Map);

    private static string Clean(string text) =>
        text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    public static bool IsFavorite(MyGameServerItem server)
    {
        if (server.ConnectionString == null)
            return false;

        if (ConnectionStrings.Contains(server.ConnectionString))
            return true;

        // Only Steam favorites are read back, so only Steam servers can follow by name
        if (!server.ConnectionString.StartsWith(SteamPrefix))
            return false;

        var name = NameOf(server);
        return name != null && Names.Contains(name);
    }

    // Remembers the name of a server at a favorite address, or adds the new address of a
    // favorite server that moved to the Steam favorites
    private static void Track(MyGameServerItem server)
    {
        var name = NameOf(server);
        if (name == null)
            return;

        if (!ConnectionStrings.Contains(server.ConnectionString))
        {
            MyGameService.AddFavoriteGame(server);
            ConnectionStrings.Add(server.ConnectionString);
        }

        if (NameByAddress.TryGetValue(server.ConnectionString, out var known) && known == name)
            return;

        NameByAddress[server.ConnectionString] = name;
        Names.Add(name);
        SaveNames();
    }

    public static void Add(MyGameServerItem server)
    {
        MyGameService.AddFavoriteGame(server);
        Reload();
        Track(server);
    }

    // Removes every address the server was seen at from the Steam favorites,
    // otherwise its name would bring it back
    public static void Remove(MyGameServerItem server)
    {
        var name = NameOf(server);
        var addresses = NameByAddress
            .Where(pair => name != null && pair.Value == name)
            .Select(pair => pair.Key)
            .Append(server.ConnectionString)
            .Where(ConnectionStrings.Contains)
            .Distinct()
            .ToList();

        foreach (var address in addresses)
            MyGameService.RemoveFavoriteGame(new MyGameServerItem { ConnectionString = address });

        Reload();
    }

    public static void Mark(MyGuiControlTable.Row row)
    {
        if (row?.UserData is not MyGameServerItem server || row.CellCount == 0)
            return;

        var favorite = IsFavorite(server);
        if (favorite)
            Track(server);

        var cell = row.GetCell(0);
        cell.Icon = favorite ? MyGuiConstants.TEXTURE_ICON_STAR : null;
        CellToolTip(cell) = favorite ? new MyToolTips(ToolTipText) : null;
    }

    // One line per address: connection string, server name, world name, tab separated
    private static void LoadNames()
    {
        if (namesLoaded)
            return;
        namesLoaded = true;

        try
        {
            if (!File.Exists(NamesPath))
                return;

            foreach (var line in File.ReadAllLines(NamesPath))
            {
                var parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length == 2)
                    NameByAddress[parts[0]] = parts[1];
            }
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine(
                $"{Plugin.Name}: Cannot load the names of the favorite servers: {e}"
            );
        }
    }

    private static void SaveNames()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NamesPath)!);
            File.WriteAllLines(
                NamesPath,
                NameByAddress.Select(pair => pair.Key + "\t" + pair.Value)
            );
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine(
                $"{Plugin.Name}: Cannot save the names of the favorite servers: {e}"
            );
        }
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
                FavoriteStars.Remove(item.Server);
            else
                FavoriteStars.Add(item.Server);

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
