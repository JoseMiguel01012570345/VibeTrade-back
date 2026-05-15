using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;

namespace VibeTrade.Backend.Features.Search;

/// <summary>
/// Embeddings locales con TF‑IDF de palabras (ML.NET <see cref="TextFeaturizingEstimator"/>), sin llamadas HTTP.
/// </summary>
public sealed class StoreSearchMlNetTfIdfEmbeddingService(
    IOptions<ElasticsearchStoreSearchOptions> options,
    ILogger<StoreSearchMlNetTfIdfEmbeddingService> logger) : IStoreSearchTextEmbeddingService
{
    private const string FeaturesColumn = "Features";

    private readonly MLContext _ml = new(seed: 0);
    private readonly object _gate = new();
    private ITransformer? _transformer;
    private int _fittedDimensions;
    private int _loggedVectorLengthMismatch;

    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var opt = options.Value;
        if (opt.SemanticVectorDimensions <= 0)
            return Task.FromResult<float[]?>(null);

        var dims = opt.SemanticVectorDimensions;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return Task.FromResult<float[]?>(null);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureFitted(dims, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var row = new[] { new TextInput { Text = trimmed } };
                var data = _ml.Data.LoadFromEnumerable(row);
                var transformed = _transformer!.Transform(data);
                var column = transformed.Schema[FeaturesColumn];
                using var cursor = transformed.GetRowCursor(new[] { column });
                var getter = cursor.GetGetter<VBuffer<float>>(column);
                if (!cursor.MoveNext())
                    return null;

                VBuffer<float> buffer = default;
                getter(ref buffer);

                if (buffer.Length != dims && Interlocked.Exchange(ref _loggedVectorLengthMismatch, 1) == 0)
                {
                    logger.LogInformation(
                        "ML.NET TF-IDF: el pipeline devuelve {Actual} slots; se normaliza a {Expected} (padding o truncado). El mapping dense_vector debe coincidir con SemanticVectorDimensions.",
                        buffer.Length,
                        dims);
                }

                var v = CopyVectorToFixedLength(buffer, dims); // copy the vector to a fixed length array because the elasticsearch vector is fixed length and the ml.net vector mught be sparced and we need to pad or truncate it to the fixed length
                if (!StoreSearchVectorMath.HasNonTrivialL2Norm(v))
                    return null;
                return v;
            },
            cancellationToken);
    }

    private void EnsureFitted(int dimensions, CancellationToken cancellationToken)
    {
        if (_transformer is not null && _fittedDimensions == dimensions)
            return;

        lock (_gate)
        {
            if (_transformer is not null && _fittedDimensions == dimensions)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            var featurizeOptions = new TextFeaturizingEstimator.Options
            {
                CharFeatureExtractor = null,
                Norm = TextFeaturizingEstimator.NormFunction.L2,
                WordFeatureExtractor = new WordBagEstimator.Options
                {
                    NgramLength = 1,
                    SkipLength = 0,
                    UseAllLengths = false,
                    MaximumNgramsCount = new[] { dimensions },
                    Weighting = NgramExtractingEstimator.WeightingCriteria.TfIdf,
                },
            };

            // configure the pipeline to featurize the text
            var pipeline = _ml.Transforms.Text.FeaturizeText(
                outputColumnName: FeaturesColumn,
                options: featurizeOptions,
                inputColumnNames: nameof(TextInput.Text));

            // get the corpus to train the model, this is a lazy load, so the following code will be executed for each load of a word
            var bootstrap = BuildBootstrapRows(dimensions);
            var trainData = _ml.Data.LoadFromEnumerable(bootstrap);
            _transformer = pipeline.Fit(trainData);
            _fittedDimensions = dimensions;

            var preview = _transformer.Transform(trainData);
            var col = preview.Schema[FeaturesColumn];
            using var cursor = preview.GetRowCursor(new[] { col }); // this cursos gives you a getter to index on the column and create a buffer to store the features and check  if the length is the same as the dimensions
            var getter = cursor.GetGetter<VBuffer<float>>(col);
            if (cursor.MoveNext())
            {
                VBuffer<float> buf = default;
                getter(ref buf);
                if (buf.Length != dimensions)
                    logger.LogInformation(
                        "ML.NET TF-IDF: tras el fit, el vector tiene {Actual} slots (SemanticVectorDimensions {Expected}); se normaliza al indexar y al consultar.",
                        buf.Length,
                        dimensions);
            }
        }
    }

    /// <summary>
    /// ML.NET puede emitir menos (o más) slots que <paramref name="dims"/> según el vocabulario del fit; Elasticsearch exige longitud fija.
    /// </summary>
    private static float[] CopyVectorToFixedLength(VBuffer<float> buffer, int dims)
    {
        var dense = new float[dims];
        var len = buffer.Length;
        if (len == 0)
            return dense;

        var tmp = new float[len];
        buffer.CopyTo(tmp);
        var copyLen = Math.Min(len, dims);
        Array.Copy(tmp, 0, dense, 0, copyLen);
        return dense;
    }

    private static IEnumerable<TextInput> BuildBootstrapRows(int dimensions)
    {
        for (var i = 0; i < dimensions; i++)
        {
            var seed = i < BootstrapSpanishRetailTokens.Length
                ? BootstrapSpanishRetailTokens[i]
                : $"vtaux{i:x8}";
            yield return new TextInput { Text = seed };
        }
    }

    /// <summary>
    /// Un término por fila de entrenamiento para poblar el diccionario con léxico de comercio (español).
    /// El resto de slots hasta <see cref="ElasticsearchStoreSearchOptions.SemanticVectorDimensions"/> se rellenan con tokens únicos sintéticos.
    /// </summary>
    private static readonly string[] BootstrapSpanishRetailTokens =
    [
        "tienda", "local", "comercio", "venta", "productos", "ofertas", "precio", "descuento", "marca", "nuevo",
        "ropa", "calzado", "zapatillas", "zapatos", "remera", "camisa", "pantalon", "jean", "short", "buzo",
        "abrigo", "campera", "gorra", "gorro", "medias", "lenceria", "vestido", "falda", "blusa", "saco",
        "electronica", "celular", "telefono", "notebook", "computadora", "tablet", "auriculares", "cargador", "cable", "memoria",
        "hogar", "muebles", "sillon", "mesa", "silla", "cama", "placard", "estanteria", "cocina", "horno",
        "heladera", "lavarropas", "microondas", "licuadora", "batidora", "olla", "vaso", "plato", "cubiertos", "termo",
        "almacen", "supermercado", "dietetica", "verduleria", "carniceria", "panaderia", "rotiseria", "kiosco", "farmacia", "perfumeria",
        "libreria", "jugueteria", "ferreteria", "bazar", "regaleria", "optica", "joyeria", "relojeria", "zapateria", "boutique",
        "deportes", "fitness", "gym", "bicicleta", "camping", "pesca", "running", "skate", "pelota", "raqueta",
        "mascotas", "alimento", "balanceado", "correa", "collar", "juguete", "arena", "veterinaria", "acuario", "pecera",
        "floreria", "plantas", "macetas", "semillas", "fertilizante", "jardin", "herramientas", "pintura", "brocha", "lampara",
        "textil", "cortinas", "alfombra", "toalla", "sabanas", "almohada", "colchon", "decoracion", "cuadro", "espejo",
        "audio", "parlante", "microfono", "camara", "lente", "tripode", "dron", "consola", "videojuego", "joystick",
        "informatica", "monitor", "teclado", "mouse", "impresora", "toner", "router", "modem", "wifi", "pendrive",
        "belleza", "cosmetica", "shampoo", "acondicionador", "crema", "jabon", "maquillaje", "esmalte", "perfume", "colonias",
        "bebe", "pañales", "chupete", "coche", "cuna", "mamadera", "body", "body bebe", "bodybebe", "body-bebe",
        "automotor", "auto", "neumatico", "aceite", "filtro", "bateria", "repuesto", "accesorio", "limpieza", "encerado",
        "papeleria", "cuaderno", "lapiz", "lapicera", "resma", "carpeta", "archivador", "marcador", "adhesivo", "cinta",
        "regalo", "tarjeta", "globo", "peluche", "caramelos", "chocolate", "alfajor", "galleta", "gaseosa", "agua",
        "cerveza", "vino", "licor", "whisky", "cafeteria", "bistro", "cafe", "te", "mate", "yerba",
        "heladeria", "helado", "crema helada", "pizzeria", "pizza", "empanada", "sandwich", "hamburguesa", "lomito", "milanesa",
        "sushi", "sashimi", "comida", "menu", "delivery", "take away", "catering", "eventos", "cumpleaños", "fiesta",
        "musica", "instrumento", "guitarra", "bateria", "teclado", "bajo", "cuerdas", "partitura", "microfono", "mezcladora",
        "arte", "pinceles", "lienzo", "oleo", "acuarela", "manualidades", "scrapbooking", "bisuteria", "accesorios", "cartera",
        "bolso", "mochila", "valija", "maletin", "billetera", "cinturon", "reloj", "pulsera", "anillo", "cadena",
        "servicio", "reparacion", "garantia", "mantenimiento", "instalacion", "presupuesto", "consulta", "turno", "cita", "whatsapp",
    ];

    private sealed class TextInput
    {
        public string Text { get; set; } = "";
    }
}

internal static class StoreSearchVectorMath
{
    /// <summary>Cosine en Elasticsearch no acepta vectores de magnitud cero.</summary>
    public static bool HasNonTrivialL2Norm(ReadOnlySpan<float> v)
    {
        double sumSq = 0;
        for (var i = 0; i < v.Length; i++)
        {
            var x = v[i];
            sumSq += (double)x * x;
        }

        return sumSq > 1e-24;
    }
}
