namespace CodeKb.Embedding;

public static class Batcher
{
    public static IEnumerable<IReadOnlyList<T>> Batch<T>(IEnumerable<T> source, int batchSize)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var buf = new List<T>(batchSize);
        foreach (var item in source)
        {
            buf.Add(item);
            if (buf.Count >= batchSize)
            {
                yield return buf.ToArray();
                buf.Clear();
            }
        }
        if (buf.Count > 0) yield return buf.ToArray();
    }
}
