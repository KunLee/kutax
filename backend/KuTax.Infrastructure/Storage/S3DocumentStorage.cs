using Amazon.S3;
using Amazon.S3.Model;
using KuTax.Core.Receipts;
using Microsoft.Extensions.Options;

namespace KuTax.Infrastructure.Storage;

public sealed class S3DocumentStorage(IAmazonS3 client, IOptions<StorageOptions> options) : IDocumentStorage
{
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Value.Bucket, Key = key, InputStream = content,
            ContentType = contentType, AutoCloseStream = false
        }, ct);

    public async Task<StoredObject> OpenReadAsync(string key, CancellationToken ct)
    {
        var response = await client.GetObjectAsync(options.Value.Bucket, key, ct);
        return new StoredObject(response.ResponseStream, response.ContentLength, response);
    }

    public async Task DeleteAsync(string key, CancellationToken ct) =>
        await client.DeleteObjectAsync(options.Value.Bucket, key, ct);
}
