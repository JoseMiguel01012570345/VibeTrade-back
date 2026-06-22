namespace VibeTrade.Backend.Features.Media.Dtos;

public sealed record MediaUploadResponse(string Id, string MimeType, string FileName, long SizeBytes);
