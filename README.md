# Fair Server List

Space Engineers client plugin for [Pulsar](https://github.com/SpaceGT/Pulsar) that makes the
Join Game server list treat every server the same.

## What it changes

- **No pinned "recommended" servers.** The game downloads a list of servers from Keen
  (`rankedServers.xml`), marks them with a star and keeps them on top of the Servers, Favorites,
  History, LAN and Friends tables, whatever column you sort by. With the plugin they get no star
  and sort like any other server.
- **Sorted by player count.** The tables open sorted by player count, busiest first, instead of
  by ping. Click a column header to sort by world, game mode, server name, players, ping or mod
  size, and click it again to reverse the order, as before.

The vanilla filters, the search box and the advanced filter keep working. Experimental servers
are listed and sorted like the others. The game shows them in gray while your own Experimental
mode is off, because it won't let you join them until you turn it on.

## Why not uptime or server performance

The game only gets the player count, player limit, ping, world name, game mode, view distance,
mod count and mod size for each server, plus the world settings. Uptime and simulation speed
aren't in the data the servers publish, so they can't be sorted on.

## Bug reports

Please open an issue on [GitHub](https://github.com/viktor-ferenczi/se-fair-server-list/issues).

## Building

Made from the [client plugin template](https://github.com/CometWorks/client-plugin-template).
`Directory.Build.props` finds the game through Steam. To set the path yourself, put `Bin64` into
a `Directory.Build.props.user` file in the repository root (`setup.py` writes one for you).
For testing, add the working copy to Pulsar as a development folder (start Pulsar with
`-sources`, then use the Sources button).
