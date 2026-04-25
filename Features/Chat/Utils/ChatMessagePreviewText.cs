using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat.Utils;

public static class ChatMessagePreviewText
{
    public static string FromPayload(ChatMessagePayload payload) =>
        payload switch
        {
            ChatTextPayload p => PreviewText(p.Text),
            ChatAudioPayload => "Nota de voz",
            ChatImagePayload p => string.IsNullOrWhiteSpace(p.Caption) ? "Foto" : p.Caption!.Trim(),
            ChatDocPayload p => string.IsNullOrWhiteSpace(p.Name) ? "Documento" : p.Name.Trim(),
            ChatDocsBundlePayload p => p.Documents.Count switch
            {
                0 => "Documento",
                1 => string.IsNullOrWhiteSpace(p.Documents[0].Name) ? "Documento" : p.Documents[0].Name.Trim(),
                var n => $"{n} documentos",
            },
            ChatAgreementPayload p => string.IsNullOrWhiteSpace(p.Title)
                ? "Acuerdo"
                : $"Acuerdo: {p.Title.Trim()}",
            ChatSystemTextPayload p => PreviewText(p.Text),
            ChatCertificatePayload p => string.IsNullOrWhiteSpace(p.Title)
                ? "Certificado"
                : p.Title.Trim(),
            _ => "Mensaje",
        };

    private static string PreviewText(string tx)
    {
        tx = tx.Trim();
        return tx.Length == 0 ? "Mensaje" : tx;
    }
}
