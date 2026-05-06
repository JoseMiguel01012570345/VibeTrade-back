namespace VibeTrade.Backend.Features.Search.Embeddings;

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

