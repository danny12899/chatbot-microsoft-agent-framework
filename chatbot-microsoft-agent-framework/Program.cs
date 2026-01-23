using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

//USE [test_vector]
//GO

///****** Object:  StoredProcedure [dbo].[SearchContext]    Script Date: 1/23/2026 9:25:17 AM ******/
//SET ANSI_NULLS ON
//GO

//SET QUOTED_IDENTIFIER ON
//GO

//CREATE TABLE KnowledgeBase (
//    Id INT IDENTITY(1,1) PRIMARY KEY,
//    Content NVARCHAR(MAX),
//    Embedding VARBINARY(6152)
//);

//CREATE PROCEDURE [dbo].[SearchContext] (@QueryEmbedding VECTOR(1536))
//AS
//BEGIN
//    SELECT TOP 3 Content
//    FROM KnowledgeBase
//    ORDER BY VECTOR_DISTANCE('cosine', Embedding, @QueryEmbedding);
//END
//GO

void InsertDocument(string content, string embedding)
{
    using var conn = new SqlConnection("Data Source=localhost;Initial Catalog=test_vector;Integrated Security=True;Pooling=False;Encrypt=False;Trust Server Certificate=False");
    conn.Open();

    var cmd = new SqlCommand(
        "INSERT INTO KnowledgeBase (Content, Embedding) VALUES (@content, @embedding)",
        conn);

    cmd.Parameters.AddWithValue("@content", content);
    cmd.Parameters.AddWithValue("@embedding", embedding);

    cmd.ExecuteNonQuery();
}

Task.Run(async () =>
{
    var client = new AzureOpenAIClient(
      new Uri(""),
      new AzureCliCredential());

    var embeddingClient = client.GetEmbeddingClient("text-embedding-ada-002");

    var embeddingResponse = await embeddingClient.GenerateEmbeddingAsync("work");
    var vectorJson = $"[{string.Join(",", embeddingResponse.Value.ToFloats().ToArray())}]";

    InsertDocument("Sql Server 2025 supports native vector search", vectorJson);

    // Query SQL Server 2025 with VECTOR
    string context = "";
    using var conn = new SqlConnection("Data Source=localhost;Initial Catalog=test_vector;Integrated Security=True;Pooling=False;Encrypt=False;Trust Server Certificate=False");
    await conn.OpenAsync();

    using var cmd = new SqlCommand("SearchContext", conn);
    cmd.CommandType = System.Data.CommandType.StoredProcedure;
    cmd.Parameters.AddWithValue("@QueryEmbedding", vectorJson);

    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        context += reader["Content"].ToString() + "\n---\n";
    }

    Console.WriteLine(string.IsNullOrEmpty(context) ? "No relevant info found." : context);

}).GetAwaiter().GetResult();

            