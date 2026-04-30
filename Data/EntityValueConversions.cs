using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Data;

/// <summary>Serialización a <c>jsonb</c> solo en el límite EF; entidades y servicios usan tipos C#.</summary>
internal static class EntityValueConversions
{
    public static ValueConverter<List<string>, string> StringList() =>
        new(
            to => JsonSerializer.Serialize(to, MarketJsonDefaults.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(from, MarketJsonDefaults.Options) ?? new List<string>());

    public static ValueComparer<List<string>> StringListComparer() =>
        new(
            (a, b) => a != null && b != null && a.Count == b.Count && a.SequenceEqual(b),
            c => c.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
            c => c.ToList());

    public static ValueConverter<List<StoreCustomFieldBody>, string> CustomFields() =>
        new(
            to => JsonSerializer.Serialize(to, MarketJsonDefaults.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new List<StoreCustomFieldBody>()
                : JsonSerializer.Deserialize<List<StoreCustomFieldBody>>(from, MarketJsonDefaults.Options) ?? new List<StoreCustomFieldBody>());

    public static ValueComparer<List<StoreCustomFieldBody>> CustomFieldsComparer() =>
        new(
            (a, b) => SerEq(a, b),
            c => SerHash(c),
            c => JCloneList(c));

    public static ValueConverter<List<ServiceEvidenceAttachmentBody>, string> ServiceEvidenceAttachments() =>
        new(
            to => JsonSerializer.Serialize(to, MarketJsonDefaults.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new List<ServiceEvidenceAttachmentBody>()
                : JsonSerializer.Deserialize<List<ServiceEvidenceAttachmentBody>>(from, MarketJsonDefaults.Options) ?? new List<ServiceEvidenceAttachmentBody>());

    public static ValueComparer<List<ServiceEvidenceAttachmentBody>> ServiceEvidenceAttachmentsComparer() =>
        new(
            (a, b) => SerEq(a, b),
            c => SerHash(c),
            c => JsonSerializer.Deserialize<List<ServiceEvidenceAttachmentBody>>(
                     JsonSerializer.Serialize(c, MarketJsonDefaults.Options), MarketJsonDefaults.Options) ??
                 new List<ServiceEvidenceAttachmentBody>());

    public static ValueConverter<ServiceRiesgosBody, string> ServiceRiesgos() =>
        new(
            to => JsonSerializer.Serialize(to, MarketJsonDefaults.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new ServiceRiesgosBody()
                : JsonSerializer.Deserialize<ServiceRiesgosBody>(from, MarketJsonDefaults.Options) ?? new ServiceRiesgosBody());

    public static ValueComparer<ServiceRiesgosBody> ServiceRiesgosComparer() =>
        new(
            (a, b) => SerEq(a, b), c => SerHash(c), c => JCloneM(c));

    public static ValueConverter<ServiceDependenciasBody, string> ServiceDependencias() =>
        new(
            to => JsonSerializer.Serialize(to, MarketJsonDefaults.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new ServiceDependenciasBody()
                : JsonSerializer.Deserialize<ServiceDependenciasBody>(from, MarketJsonDefaults.Options) ?? new ServiceDependenciasBody());

    public static ValueComparer<ServiceDependenciasBody> ServiceDependenciasComparer() =>
        new(
            (a, b) => SerEq(a, b), c => SerHash(c), c => JCloneM(c));

    public static ValueConverter<ServiceGarantiasBody, string> ServiceGarantias() =>
        new(
            to => JsonSerializer.Serialize(to, MarketJsonDefaults.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new ServiceGarantiasBody()
                : JsonSerializer.Deserialize<ServiceGarantiasBody>(from, MarketJsonDefaults.Options) ?? new ServiceGarantiasBody());

    public static ValueComparer<ServiceGarantiasBody> ServiceGarantiasComparer() =>
        new(
            (a, b) => SerEq(a, b), c => SerHash(c), c => JCloneM(c));

    public static ValueConverter<MarketWorkspaceState, string> MarketWorkspace() =>
        new(
            to => JsonSerializer.Serialize(to, MarketJsonDefaults.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new MarketWorkspaceState()
                : JsonSerializer.Deserialize<MarketWorkspaceState>(from, MarketJsonDefaults.Options) ?? new MarketWorkspaceState());

    public static ValueComparer<MarketWorkspaceState> MarketWorkspaceComparer() =>
        new(
            (a, b) => SerEq(a, b), c => SerHash(c), c => JCloneM(c));

    public static ValueConverter<SessionUser, string> SessionUser() =>
        new(
            to => JsonSerializer.Serialize(to, AuthSessionJson.Options),
            from => string.IsNullOrWhiteSpace(from)
                ? new SessionUser()
                : JsonSerializer.Deserialize<SessionUser>(from, AuthSessionJson.Options) ?? new SessionUser());

    public static ValueComparer<SessionUser> SessionUserComparer() =>
        new(
            (a, b) => SerEqSession(a, b), c => SerHashSession(c), c => JCloneSession(c));

    private static bool SerEq<T>(T? a, T? b) =>
        JsonSerializer.Serialize(a, MarketJsonDefaults.Options) == JsonSerializer.Serialize(b, MarketJsonDefaults.Options);

    private static int SerHash<T>(T? c) =>
        JsonSerializer.Serialize(c, MarketJsonDefaults.Options).GetHashCode(StringComparison.Ordinal);

    private static T JCloneM<T>(T c) where T : class, new() =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(c, MarketJsonDefaults.Options), MarketJsonDefaults.Options) ?? new T();

    private static bool SerEqSession(SessionUser? a, SessionUser? b) =>
        JsonSerializer.Serialize(a, AuthSessionJson.Options) == JsonSerializer.Serialize(b, AuthSessionJson.Options);

    private static int SerHashSession(SessionUser? c) =>
        JsonSerializer.Serialize(c, AuthSessionJson.Options).GetHashCode(StringComparison.Ordinal);

    private static List<StoreCustomFieldBody> JCloneList(List<StoreCustomFieldBody> c) =>
        JsonSerializer.Deserialize<List<StoreCustomFieldBody>>(
            JsonSerializer.Serialize(c, MarketJsonDefaults.Options), MarketJsonDefaults.Options) ?? new List<StoreCustomFieldBody>();

    private static SessionUser JCloneSession(SessionUser c) =>
        JsonSerializer.Deserialize<SessionUser>(JsonSerializer.Serialize(c, AuthSessionJson.Options), AuthSessionJson.Options) ?? new();
}
