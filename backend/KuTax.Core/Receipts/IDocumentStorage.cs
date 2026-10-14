namespace KuTax.Core.Receipts;

public interface IDocumentStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<StoredObject> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

public sealed class StoredObject(Stream content, long length, IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public long Length { get; } = length;
    public async ValueTask DisposeAsync()
    {
        if (owner is not null) owner.Dispose();
        else await Content.DisposeAsync();
    }
}
