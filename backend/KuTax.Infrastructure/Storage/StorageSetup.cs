using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace KuTax.Infrastructure.Storage;

public sealed class StorageSetup(IAmazonS3 client, IOptions<StorageOptions> options)
{
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        try { await client.GetBucketLocationAsync(options.Value.Bucket, ct); }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // New S3 buckets are private by default. Never install a public policy.
            await client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = options.Value.Bucket, UseClientRegion = true
            }, ct);
        }
    }

    public Task CheckAsync(CancellationToken ct) => client.ListObjectsV2Async(
        new ListObjectsV2Request { BucketName = options.Value.Bucket, MaxKeys = 1 }, ct);
}
