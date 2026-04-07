namespace VibeTrade.Backend.Data.Entities;

/// <summary>Archivo binario persistido (imágenes, PDFs, docs) para referenciar desde catálogos.</summary>
public sealed class StoredMediaRow
{
    public string Id { get; set; } = "";

    public string MimeType { get; set; } = "application/octet-stream";

    public string FileName { get; set; } = "file";

    public long SizeBytes { get; set; }

    /// <summary>Bytes del archivo. En Postgres se almacena como bytea.</summary>
    public byte[] Bytes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}

