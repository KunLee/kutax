using System.Text.Json;

namespace KuTax.Infrastructure.Processing;

/// <summary>One strict-JSON-schema text request to the configured AI provider (Groq chat completions or OpenAI Responses).</summary>
internal static class AiJsonClient
{
    /// <returns>The model's JSON text, or null when AI isn't configured.</returns>
    public static async Task<string?> CompleteAsync(HttpClient client,AiOptions options,string instructions,string input,string schemaName,string schema,int maxTokens,CancellationToken ct)
    {
        if(!AiPrivacy.Configured(options))return null;
        if(!Uri.TryCreate(AiPrivacy.Endpoint(options),UriKind.Absolute,out var uri)||uri.Scheme!="https")return null;
        using var parsed=JsonDocument.Parse(schema);
        if(AiPrivacy.IsGroq(options))
        {
            var payload=new{model=options.TextModel,temperature=0,max_completion_tokens=maxTokens,
                messages=new object[]{new{role="system",content=instructions},new{role="user",content=input}},
                response_format=new{type="json_schema",json_schema=new{name=schemaName,strict=true,schema=parsed.RootElement}}};
            using var result=await ReceiptExtractor.PostAsync(client,uri,options.ApiKey!,payload,ct);
            var choice=result.RootElement.GetProperty("choices")[0];
            if(choice.GetProperty("finish_reason").GetString()!="stop")throw new InvalidDataException("AI response incomplete.");
            return choice.GetProperty("message").GetProperty("content").GetString()??"";
        }
        var request=new{model=options.Model,store=false,max_output_tokens=maxTokens,instructions,input,
            text=new{format=new{type="json_schema",name=schemaName,strict=true,schema=parsed.RootElement}}};
        using var response=await ReceiptExtractor.PostAsync(client,uri,options.ApiKey!,request,ct);
        if(response.RootElement.GetProperty("status").GetString()!="completed")throw new InvalidDataException("AI response incomplete.");
        return string.Concat(response.RootElement.GetProperty("output").EnumerateArray().Where(x=>x.GetProperty("type").GetString()=="message")
            .SelectMany(x=>x.GetProperty("content").EnumerateArray()).Where(x=>x.GetProperty("type").GetString()=="output_text").Select(x=>x.GetProperty("text").GetString()));
    }
}
