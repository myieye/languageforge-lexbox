using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LcmCrdt.Data;

/// <summary>
/// Turns off durability (synchronous=OFF, journal_mode=MEMORY) for throwaway project copies —
/// a crash can only lose a file that gets deleted anyway. Never register this for a real
/// project database. Pragmas are per-connection, so this runs on every open (connections are
/// pooled per connection string, and a temp copy's path is unique).
/// </summary>
public class TempCopyPragmasInterceptor : DbConnectionInterceptor
{
    public static readonly TempCopyPragmasInterceptor Instance = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous=OFF; PRAGMA journal_mode=MEMORY;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous=OFF; PRAGMA journal_mode=MEMORY;";
        command.ExecuteNonQuery();
    }
}
