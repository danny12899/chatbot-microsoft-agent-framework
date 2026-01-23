using Azure.AI.OpenAI;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

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

async Task TestHandoff()
{
    var client = new AzureOpenAIClient(new Uri(""),
        new AzureCliCredential())
            .GetChatClient("gpt-4o")
            .AsIChatClient();


    ChatClientAgent grammarTutor = new(client,
    "You provide assistance with grammar queries. Explain your reasoning at each step and include examples. Only respond about grammar.",
    "grammar_tutor",
    "Specialist agent for grammar questions");

    ChatClientAgent mathTutor = new(client,
        "You provide help with math problems. Explain your reasoning at each step and include examples. Only respond about math.",
        "math_tutor",
        "Specialist agent for math questions");

    ChatClientAgent triageAgent = new(client,
        "You determine which agent to use based on the user's question. ALWAYS handoff to another agent.",
        "triage_agent",
        "Routes messages to the appropriate specialist agent");


    var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
    .WithHandoffs(triageAgent, [mathTutor, grammarTutor]) // Triage can route to either specialist
    .WithHandoff(mathTutor, triageAgent)                  // Math tutor can return to triage
    .WithHandoff(grammarTutor, triageAgent)               // History tutor can return to triage
    .Build();

    List<Microsoft.Extensions.AI.ChatMessage> messages = new();

    while (true)
    {
        Console.Write("Q: ");
        string userInput = Console.ReadLine()!;
        messages.Add(new(ChatRole.User, userInput));


        // Execute workflow and process events
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        List<Microsoft.Extensions.AI.ChatMessage> newMessages = new();
        await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            if (evt is AgentResponseUpdateEvent e)
            {
                Console.WriteLine($"{e.ExecutorId}: {e.Data}");
            }
            else if (evt is WorkflowOutputEvent outputEvt)
            {
                newMessages = (List<Microsoft.Extensions.AI.ChatMessage>)outputEvt.Data!;
                break;
            }
        }

        // Add new messages to conversation history
        messages.AddRange(newMessages.Skip(messages.Count));
    }
}

Task.Run(async () =>
{
    await TestHandoff();

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

            