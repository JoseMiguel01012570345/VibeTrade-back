namespace VibeTrade.Backend.Features.Inventory.Dtos;

public sealed record StoreCategoryDto(string Id, string Name, string? ParentCategoryId);

public sealed record StoreCategoryCreateBody(string Name, string? ParentCategoryId);

public sealed record StoreCategoryPatchBody(string? Name, bool? Active, int? SortOrder);

public sealed record StoreSupplierDto(
    string Id,
    string StoreId,
    string BusinessName,
    string PortalUsername,
    bool Active,
    decimal PlatformDebtAmount,
    string PlatformDebtCurrencyCode);

public sealed record StoreSupplierCreateBody(
    string BusinessName,
    string PortalUsername,
    string Password);

public sealed record StoreBannerDto(
    string Id,
    string StoreId,
    string Kind,
    int SortOrder,
    string MediaUrl,
    bool Active);

public sealed record StoreBannerCreateBody(string Kind, string MediaUrl, int? SortOrder);

public sealed record StoreBannerPatchBody(bool? Active, int? SortOrder, string? MediaUrl);

public sealed record SupplierPortalMeDto(string BusinessName, string PortalUsername);

public sealed record SupplierPortalDashboardKpisDto(
    decimal FondosBanderaExpress,
    decimal ListoParaRetiro,
    decimal VolumenVenta,
    string CurrencyCode);

public sealed record SupplierPortalInventoryRowDto(
    string Id,
    string Name,
    string? Description,
    string SkuLabel,
    string CategoryName,
    string CategoryId,
    IReadOnlyList<string> CategoryIds,
    decimal Price,
    string CurrencyCode,
    int Stock,
    bool PendingApproval,
    bool IsAvailable,
    string? PhotoUrl);

public sealed record SupplierPortalCategoryOptionDto(string Id, string Name);

public sealed record SupplierPortalPagedTransactionsDto(
    IReadOnlyList<SupplierPortalTransactionRowDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record SupplierPortalTransactionRowDto(
    string Kind,
    string PublicNumber,
    DateTimeOffset CreatedAt,
    decimal Amount,
    string CurrencyCode,
    string? CustomerName);

public sealed record SupplierPortalDashboardDto(
    SupplierPortalDashboardKpisDto Kpis,
    SupplierPortalPagedTransactionsDto Transactions,
    SupplierPortalInventoryDto Inventory);

public sealed record SupplierPortalInventoryDto(
    IReadOnlyList<SupplierPortalInventoryRowDto> Items,
    IReadOnlyList<SupplierPortalCategoryOptionDto> Categories);

public sealed record SupplierPortalInventoryBulkUpdateRequest(
    IReadOnlyList<SupplierPortalInventoryBulkUpdateItem> Items);

public sealed record SupplierPortalInventoryBulkUpdateItem(string Id, int Stock);

public sealed record SupplierPortalInventoryBulkUpdateResult(
    int UpdatedCount,
    IReadOnlyList<SupplierPortalInventoryBulkUpdateError> Errors);

public sealed record SupplierPortalInventoryBulkUpdateError(string Id, string Message);

public sealed record SupplierLoginBody(string? Username, string? Password);
