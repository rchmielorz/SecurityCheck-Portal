using Npgsql;

namespace securitycheck_portal.Core.Scanning;

/// <summary>
/// Proof that this process is the only scan worker: a PostgreSQL session-level advisory lock held on a dedicated
/// connection that stays open until <see cref="DisposeAsync"/> (or until the process dies, which releases it).
/// Restart recovery and the abandoned-scan sweep fail <c>Running</c> rows and delete checkouts without asking who
/// owns them, so they may run only while this lock is held.
/// </summary>
public sealed class WorkerLock : IAsyncDisposable
{
    /// <summary>Fixed key of the worker lock ("SCW" + 1 in ASCII); every worker must use the same one.</summary>
    public const long Key = 0x5343_5730_0000_0001;

    private readonly NpgsqlConnection _connection;

    private WorkerLock(NpgsqlConnection connection) => _connection = connection;

    /// <summary>The lock, or null when another session already holds it. Throws when the database is unreachable.</summary>
    public static async Task<WorkerLock?> TryAcquireAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Pooling off: the lock belongs to the session, and a pooled connection must never be reset or reused under it.
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", Key);

            var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new WorkerLock(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Closes the connection, which releases the lock.</summary>
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
