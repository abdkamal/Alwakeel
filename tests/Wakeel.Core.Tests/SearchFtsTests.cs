using Wakeel.Core.Data;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// Exercises the FTS5 <c>search_fts</c> virtual table (unicode61, remove_diacritics 2) and the
/// three SQL triggers that keep it in step with <c>search_chunks</c> (Migrations/0001_initial.sql).
/// The unicode61 tokenizer's own <c>remove_diacritics 2</c> option was empirically found (in this
/// SQLitePCLRaw build) to fold neither tashkeel nor hamza-on-alef forms — which is exactly why
/// <see cref="ArabicText.Normalize"/> must run in application code before text reaches
/// <c>search_chunks.text_norm</c> (see the column's own XML doc); these tests insert
/// already-normalized text, as production does, and check FTS5's per-word MATCH behavior and the
/// sync triggers on top of that.
/// </summary>
public sealed class SearchFtsTests : IDisposable
{
    private readonly string _root;
    private readonly byte[] _key;
    private readonly DbSession _session;

    public SearchFtsTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _key);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public void Insert_IntoSearchChunks_IsFindableViaFtsMatch_WhenTextIsPreNormalized()
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);

        // Written with tashkeel (fatha/damma) and a hamza-on-alef (أ), exactly as a caller might
        // receive raw input — then normalized through ArabicText.Normalize before it is written to
        // text_norm, since indexing is always expected to run on already-normalized text.
        var normalized = ArabicText.Normalize("الأَمَلُ الكَبِيرُ");
        InsertChunk(connection, Guid.CreateVersion7(), normalized);

        Assert.Equal(1, MatchCount(connection, ArabicText.Normalize("الأمل")));
        Assert.Equal(1, MatchCount(connection, ArabicText.Normalize("الكبير")));
    }

    [Fact]
    public void Update_OnSearchChunks_KeepsFtsInStep()
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        var id = Guid.CreateVersion7();
        InsertChunk(connection, id, "نص قديم");

        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE search_chunks SET text_norm = 'نص جديد' WHERE id = $id;";
            update.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
            update.ExecuteNonQuery();
        }

        Assert.Equal(0, MatchCount(connection, "قديم"));
        Assert.Equal(1, MatchCount(connection, "جديد"));
    }

    [Fact]
    public void Delete_FromSearchChunks_RemovesFromFts()
    {
        var paths = WakeelPaths.ForRoot(_root);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        var id = Guid.CreateVersion7();
        InsertChunk(connection, id, "محذوف");

        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM search_chunks WHERE id = $id;";
            delete.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
            delete.ExecuteNonQuery();
        }

        Assert.Equal(0, MatchCount(connection, "محذوف"));
    }

    private static void InsertChunk(Microsoft.Data.Sqlite.SqliteConnection connection, Guid id, string textNorm)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO search_chunks(id, entity_type, entity_id, text_norm, updated_at) VALUES ($id, 'party', $id, $text, $now);";
        insert.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        insert.Parameters.AddWithValue("$text", textNorm);
        insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        insert.ExecuteNonQuery();
    }

    private static int MatchCount(Microsoft.Data.Sqlite.SqliteConnection connection, string query)
    {
        using var match = connection.CreateCommand();
        match.CommandText = "SELECT count(*) FROM search_fts WHERE search_fts MATCH $query;";
        match.Parameters.AddWithValue("$query", query);
        return Convert.ToInt32(match.ExecuteScalar());
    }
}
