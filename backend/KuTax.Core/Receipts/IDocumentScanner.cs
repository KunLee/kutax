namespace KuTax.Core.Receipts;
public interface IDocumentScanner
{
    // Scanner failure is an exception, never a clean result.
    Task<bool> IsCleanAsync(Stream content,CancellationToken ct);
}
