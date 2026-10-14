using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using KuTax.Core.Receipts;
using Microsoft.Extensions.Configuration;

namespace KuTax.Infrastructure.Storage;
public sealed class ClamAvScanner(IConfiguration configuration):IDocumentScanner
{
    public async Task<bool> IsCleanAsync(Stream content,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));var token=timeout.Token;
        using var client=new TcpClient();await client.ConnectAsync(configuration["Scanning:Host"]??"localhost",configuration.GetValue("Scanning:Port",3310),token);await using var stream=client.GetStream();
        await stream.WriteAsync("zINSTREAM\0"u8.ToArray(),token);var buffer=new byte[65536];var length=new byte[4];long total=0;int count;
        while((count=await content.ReadAsync(buffer,token))>0){total+=count;if(total>20*1024*1024)throw new InvalidDataException("Scanner input limit exceeded.");BinaryPrimitives.WriteInt32BigEndian(length,count);await stream.WriteAsync(length,token);await stream.WriteAsync(buffer.AsMemory(0,count),token);}
        await stream.WriteAsync(new byte[4],token);var response=new byte[4096];var used=0;
        while(used<response.Length){count=await stream.ReadAsync(response.AsMemory(used),token);if(count==0)break;used+=count;if(response.AsSpan(0,used).Contains((byte)0))break;}
        var reply=Encoding.UTF8.GetString(response,0,used).TrimEnd('\0','\n','\r');
        if(reply=="stream: OK")return true;
        if(reply.StartsWith("stream: ",StringComparison.Ordinal)&&reply.EndsWith(" FOUND",StringComparison.Ordinal))return false;
        throw new IOException("Scanner did not return a clean or infected verdict.");
    }
}
