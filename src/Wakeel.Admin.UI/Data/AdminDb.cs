using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Wakeel.Core.Data;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Data;

/// <summary>
/// The open <c>admin.db</c>: one SQLCipher connection, held for as long as the administrator is
/// signed in, plus the sealing of the few secrets that are stored inside it.
/// </summary>
/// <remarks>
/// <para>
/// Plain SQL rather than an entity model. The admin schema is small, entirely the tool's own, and
/// never synchronised, so the stamping, soft deletion and change-log machinery of
/// <c>WakeelDb</c> would be weight with nothing to carry. The connection is opened through
/// <see cref="DbConnectionFactory"/>, so the key handling and the pragmas are the same ones the
/// الوكيل installation uses.
/// </para>
/// <para>
/// The database key stays in memory while the tool is unlocked, because the organisation private
/// seeds, an office key and a device key seed are sealed with it every time one is written. Signing
/// out wipes it and closes the connection, and nothing writes it anywhere but the wraps in
/// <c>admin.key</c>.
/// </para>
/// </remarks>
public sealed class AdminDb : IDisposable
{
    /// <summary>Associated-data label of anything sealed with the database key before being stored.</summary>
    private const string SealContext = "wakeel.admin.sealed";

    private readonly AdminPaths _paths;
    private SqliteConnection? _connection;
    private byte[]? _key;

    public AdminDb(AdminPaths paths)
    {
        _paths = paths;
    }

    /// <summary>Whether the database is open right now.</summary>
    public bool IsOpen => _connection is not null;

    /// <summary>Whether a database file exists on this machine at all.</summary>
    public bool FileExists => File.Exists(_paths.DatabaseFile);

    /// <summary>The open connection.</summary>
    /// <exception cref="InvalidOperationException">The tool is locked or signed out.</exception>
    public SqliteConnection Connection =>
        _connection ?? throw new InvalidOperationException("The administration database is not open.");

    /// <summary>
    /// Opens the database with <paramref name="key"/> (creating the file on first run) and brings
    /// its schema up to date. The caller keeps ownership of its own copy of the key and should wipe
    /// it; this holds a private copy for the sealing helpers.
    /// </summary>
    public void Open(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("The administration database key must be exactly 32 bytes.", nameof(key));
        }

        Close();

        var copy = key.ToArray();
        var connection = DbConnectionFactory.Open(_paths.DatabaseFile, copy);
        try
        {
            AdminSchemaMigrator.Migrate(connection);
        }
        catch
        {
            connection.Dispose();
            CryptographicOperations.ZeroMemory(copy);
            throw;
        }

        _connection = connection;
        _key = copy;
    }

    /// <summary>Closes the database and wipes the key this object was holding.</summary>
    public void Close()
    {
        _connection?.Dispose();
        _connection = null;

        if (_key is not null)
        {
            CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }
    }

    /// <summary>Seals a secret with the database key, for storing in a column.</summary>
    public byte[] Seal(ReadOnlySpan<byte> plaintext)
    {
        var key = _key ?? throw new InvalidOperationException("The administration database is not open.");
        return Aead.Encrypt(key, plaintext, SealContext);
    }

    /// <summary>Opens a secret that was stored with <see cref="Seal"/>.</summary>
    public byte[] Unseal(ReadOnlySpan<byte> sealedBytes)
    {
        var key = _key ?? throw new InvalidOperationException("The administration database is not open.");
        return Aead.Decrypt(key, sealedBytes, SealContext);
    }

    /// <summary>A command on the open connection, with the given text already set.</summary>
    public SqliteCommand Command(string sql)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    /// <summary>Runs a statement that returns nothing.</summary>
    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql);
        Bind(command, parameters);
        return command.ExecuteNonQuery();
    }

    /// <summary>Runs a statement that returns one number, treating no row and null alike as zero.</summary>
    public long Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql);
        Bind(command, parameters);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a statement that returns one piece of text, or null when there is no row.</summary>
    public string? ScalarText(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql);
        Bind(command, parameters);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    private static void Bind(SqliteCommand command, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
