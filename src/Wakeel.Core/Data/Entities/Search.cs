namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §12 — search chunks (kept in sync with the search_fts FTS5 virtual table by SQL
// triggers — see Migrations/0001_initial.sql) and the discovered models folder. search_fts itself
// is an FTS5 virtual table with no natural EF shape and is queried with raw SQL, not an entity.

/// <summary>One indexed text chunk (FTS + optional embedding) for hybrid search.</summary>
public sealed class SearchChunk
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public Guid? DocumentId { get; set; }

    public int? PageNo { get; set; }

    /// <summary>Normalized Arabic text (see ArabicText.Normalize) fed to FTS5 and shown to the user.</summary>
    public string TextNorm { get; set; } = string.Empty;

    /// <summary>384-dimension float32 embedding vector, raw bytes.</summary>
    public byte[]? Embedding { get; set; }

    public string? ModelId { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>A model discovered in the models folder (AGREEMENT item 30).</summary>
public sealed class SearchModel
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Path { get; set; } = string.Empty;

    public ModelKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public ModelStatus Status { get; set; }

    public string? ReasonAr { get; set; }

    public int? Dims { get; set; }

    public DateTime CheckedAt { get; set; }
}
