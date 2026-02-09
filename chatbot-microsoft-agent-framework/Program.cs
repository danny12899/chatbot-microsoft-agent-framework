using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.KnowledgeBases;
using Azure.Search.Documents.KnowledgeBases.Models;
using Azure.Search.Documents.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Assistants;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAI.VectorStores;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;





string searchEndpoint = "";
string aoaiEndpoint = "";
var apiKey = "";
var indexName = "";
var credential = new AzureKeyCredential(apiKey);
var searchClient = new SearchClient(new Uri(searchEndpoint), indexName, credential);
var indexClient = new SearchIndexClient(new Uri(searchEndpoint), credential);

//await UploadFileContent("3", "test3.txt", "My name is Bob. My favourite color is Green.");
//await AskQuestion("What is Bob's favourite color?");
//await TestKnowledgeSource();
await Handoff();

async Task UploadFileContent(string id, string fileName, string extractedText)
{
    var doc = new MyFileDocument
    {
        Id = id,
        FileName = fileName,
        Content = extractedText
    };

    await searchClient.IndexDocumentsAsync(IndexDocumentsBatch.Upload(new[] { doc }));
    Console.WriteLine("File uploaded to index.");
}

async Task AskQuestion(string query)
{
    var options = new SearchOptions
    {
        Select = { "FileName", "Content" },
        HighlightFields = { "Content" },
        Size = 3
    };

    SearchResults<MyFileDocument> results = await searchClient.SearchAsync<MyFileDocument>(query, options);

    await foreach(var result in results.GetResultsAsync())
    {
        Console.WriteLine($"Found in: {result.Document.FileName}");
        //Console.WriteLine($"Snippet: {result.Document.Content}");

        if (result.Highlights != null && result.Highlights.ContainsKey("Content"))
        {
            foreach(var snippet in result.Highlights["Content"])
            {
                Console.WriteLine($"Match Found: ...{snippet}...");
            }
        }
    }

}

async Task TestKnowledgeSource()
{
    string aoaiEmbeddingModel = "text-embedding-3-large";
    string aoaiEmbeddingDeployment = "text-embedding-3-large";
    string aoaiGptModel = "gpt-4o";
    string aoaiGptDeployment = "gpt-4o";

    string indexName = "earth-at-night";
    string knowledgeSourceName = "earth-knowledge-source";
    string knowledgeBaseName = "earth-knowledge-base";


    // Define fields for the index
    var fields = new List<SearchField>
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true, IsFacetable = true },
                new SearchField("page_chunk", SearchFieldDataType.String) { IsFilterable = false, IsSortable = false, IsFacetable = false },
                new SearchField("page_embedding_text_3_large", SearchFieldDataType.Collection(SearchFieldDataType.Single)) { VectorSearchDimensions = 3072, VectorSearchProfileName = "hnsw_text_3_large" },
                new SimpleField("page_number", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true, IsFacetable = true }
            };

    // Define a vectorizer
    var vectorizer = new AzureOpenAIVectorizer(vectorizerName: "azure_openai_text_3_large")
    {
        Parameters = new AzureOpenAIVectorizerParameters
        {
            ResourceUri = new Uri(aoaiEndpoint),
            DeploymentName = aoaiEmbeddingDeployment,
            ModelName = aoaiEmbeddingModel
        }
    };

    // Define a vector search profile and algorithm
    var vectorSearch = new VectorSearch()
    {
        Profiles =
    {
        new VectorSearchProfile(
            name: "hnsw_text_3_large",
            algorithmConfigurationName: "alg"
        )
        {
            VectorizerName = "azure_openai_text_3_large"
        }
    },
        Algorithms =
    {
        new HnswAlgorithmConfiguration(name: "alg")
    },
        Vectorizers =
    {
        vectorizer
    }
    };

    // Define a semantic configuration
    var semanticConfig = new SemanticConfiguration(
        name: "semantic_config",
        prioritizedFields: new SemanticPrioritizedFields
        {
            ContentFields = { new SemanticField("page_chunk") }
        }
    );

    var semanticSearch = new SemanticSearch()
    {
        DefaultConfigurationName = "semantic_config",
        Configurations = { semanticConfig }
    };

    // Create the index
    var index = new SearchIndex(indexName)
    {
        Fields = fields,
        VectorSearch = vectorSearch,
        SemanticSearch = semanticSearch
    };

    // Create the index client, deleting and recreating the index if it exists
    var indexClient = new SearchIndexClient(new Uri(searchEndpoint), credential);
    await indexClient.CreateOrUpdateIndexAsync(index);
    Console.WriteLine($"Index '{indexName}' created or updated successfully.");

    // Upload sample documents from the GitHub URL
    string url = "https://raw.githubusercontent.com/Azure-Samples/azure-search-sample-data/refs/heads/main/nasa-e-book/earth-at-night-json/documents.json";
    var httpClient = new HttpClient();
    var response = await httpClient.GetAsync(url);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync();
    var documents = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
    var searchClient = new SearchClient(new Uri(searchEndpoint), indexName, credential);
    var searchIndexingBufferedSender = new SearchIndexingBufferedSender<Dictionary<string, object>>(
        searchClient,
        new SearchIndexingBufferedSenderOptions<Dictionary<string, object>>
        {
            KeyFieldAccessor = doc => doc["id"].ToString(),
        }
    );

    await searchIndexingBufferedSender.UploadDocumentsAsync(documents);
    await searchIndexingBufferedSender.FlushAsync();
    Console.WriteLine($"Documents uploaded to index '{indexName}' successfully.");

    // Create a knowledge source
    var indexKnowledgeSource = new SearchIndexKnowledgeSource(
        name: knowledgeSourceName,
        searchIndexParameters: new SearchIndexKnowledgeSourceParameters(searchIndexName: indexName)
        {
            SourceDataFields = { new SearchIndexFieldReference(name: "id"), new SearchIndexFieldReference(name: "page_chunk"), new SearchIndexFieldReference(name: "page_number") }
        }
    );

    await indexClient.CreateOrUpdateKnowledgeSourceAsync(indexKnowledgeSource);
    Console.WriteLine($"Knowledge source '{knowledgeSourceName}' created or updated successfully.");

    // Create a knowledge base
    var openAiParameters = new AzureOpenAIVectorizerParameters
    {
        ResourceUri = new Uri(aoaiEndpoint),
        DeploymentName = aoaiGptDeployment,
        ModelName = aoaiGptModel
    };

    var model = new KnowledgeBaseAzureOpenAIModel(azureOpenAIParameters: openAiParameters);

    var knowledgeBase = new KnowledgeBase(
        name: knowledgeBaseName,
        knowledgeSources: new KnowledgeSourceReference[] { new KnowledgeSourceReference(knowledgeSourceName) }
    )
    {
        RetrievalReasoningEffort = new KnowledgeRetrievalLowReasoningEffort(),
        AnswerInstructions = "Provide a two sentence concise and informative answer based on the retrieved documents.",
        Models = { model }
    };

    await indexClient.CreateOrUpdateKnowledgeBaseAsync(knowledgeBase);
    Console.WriteLine($"Knowledge base '{knowledgeBaseName}' created or updated successfully.");


    // Set up messages
    string instructions = @"A Q&A agent that can answer questions about the Earth at night.
            If you don't have the answer, respond with ""I don't know"".";

    var messages = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "role", "system" },
                    { "content", instructions }
                }
            };

    // Run agentic retrieval
    var baseClient = new KnowledgeBaseRetrievalClient(
        endpoint: new Uri(searchEndpoint),
        knowledgeBaseName: knowledgeBaseName,
        //credential : credential
        tokenCredential: new DefaultAzureCredential()
    );

    string query = @"Why do suburban belts display larger December brightening than urban cores even though absolute light levels are higher downtown? Why is the Phoenix nighttime street grid is so sharply visible from space, whereas large stretches of the interstate between midwestern cities remain comparatively dim?";

    messages.Add(new Dictionary<string, string>
            {
                { "role", "user" },
                { "content", query }
            });

    Console.WriteLine($"Running the query...{query}");
    var retrievalRequest = new KnowledgeBaseRetrievalRequest();
    foreach (Dictionary<string, string> message in messages)
    {
        if (message["role"] != "system")
        {
            retrievalRequest.Messages.Add(new KnowledgeBaseMessage(content: new[] { new KnowledgeBaseMessageTextContent(message["content"]) }) { Role = message["role"] });
        }
    }
    retrievalRequest.RetrievalReasoningEffort = new KnowledgeRetrievalLowReasoningEffort();

    // In Azure AI search > Access Cotnrol (IAM) > Add > add the search service managed identity with role "Cognitive Services OpenAI User"

    var retrievalResult = await baseClient.RetrieveAsync(retrievalRequest);

    messages.Add(new Dictionary<string, string>
            {
                { "role", "assistant" },
                { "content", (retrievalResult.Value.Response[0].Content[0] as KnowledgeBaseMessageTextContent)!.Text }
            });

    // Print the response, activity, and references
    Console.WriteLine("Response:");
    Console.WriteLine((retrievalResult.Value.Response[0].Content[0] as KnowledgeBaseMessageTextContent)!.Text);

    Console.WriteLine("Activity:");
    foreach (var activity in retrievalResult.Value.Activity)
    {
        Console.WriteLine($"Activity Type: {activity.GetType().Name}");
        string activityJson = JsonSerializer.Serialize(
            activity,
            activity.GetType(),
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(activityJson);
    }

    Console.WriteLine("References:");
    foreach (var reference in retrievalResult.Value.References)
    {
        Console.WriteLine($"Reference Type: {reference.GetType().Name}");
        string referenceJson = JsonSerializer.Serialize(
            reference,
            reference.GetType(),
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(referenceJson);
    }

}

async Task Handoff()
{

    AzureOpenAIClient azureOpenAIClient = new(
       new Uri(aoaiEndpoint),
       new DefaultAzureCredential());

    Console.WriteLine("1: Chat with Math Agent");
    Console.WriteLine("2: Practice Group Remap");

    var userInput = Console.ReadLine();
    if (userInput == "1")
    {
        var triageAgent = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
            name: "Triage Agent",
            instructions: "You determine which agent to use based on the user's question. ALWAYS handoff to another agent."
        );

        var mathAgent = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
            name: "Math Agent",
            instructions: "You provide help with math problems. Answer the question and Explain your reasoning at each step and include examples. Only respond about math."
        );


        List<Microsoft.Extensions.AI.ChatMessage> messages = new();

        while (true)
        {
            var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
               .WithHandoffs(triageAgent, [mathAgent])
               .WithHandoff(mathAgent, triageAgent)
               .Build();

            Console.Write("Q: ");
            string _userInput = Console.ReadLine()!;
            messages.Add(new(ChatRole.User, _userInput));

            // Execute workflow and process events
            StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            List<Microsoft.Extensions.AI.ChatMessage> newMessages = new();
            await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
            {
                if (evt is AgentResponseUpdateEvent e)
                {
                    //Console.WriteLine($"{e.ExecutorId}: {e.Data}");
                }
                else if (evt is WorkflowOutputEvent outputEvt)
                {
                    newMessages = (List<Microsoft.Extensions.AI.ChatMessage>)outputEvt.Data!;
                    break;
                }
            }

            Console.WriteLine($"A: {newMessages.Skip(messages.Count).FirstOrDefault().Text}");
            messages.AddRange(newMessages.Skip(messages.Count));
        }
    }
    else if (userInput == "2")
    {
        Console.WriteLine("Enter path to CSV file");
        var filePath = Console.ReadLine();
        if (File.Exists(filePath))
        {
            string csvContent = File.ReadAllText(filePath);

            var fileCleaner = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
                name: "File Cleaner Agent",
                instructions: """
                The input string is in a csv file format.
                Go through each value in the csv file and repalce ' with ''
                Reply with only the end result
                """
            );

            var cleanedCsv = await fileCleaner.RunAsync(csvContent);

            var practiceGroupAgent = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
                name: "Practice Group Agent",
                instructions: """
                Using the below list of practice groups with its corresponding ID

                General, 001
                IT, 002
                Mergers and Acquisitions, 003
                Banking, 004
                Real Estate, 005
                I.P. Litigation, 006
                ...

                The input string is in a csv file format.
                Go through each value in the csv file and repalce the NewPracticeGroups column value with the corresponding IDs for example

                General;IT

                repalce with

                001;002

                Reply with only the end result
                """
            );
            var newPracticeGroupCsv = await practiceGroupAgent.RunAsync(cleanedCsv.Text);

            var sqlGenerator = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
              name: "Sql Generator Agent",
              instructions: """
                The input string is in a csv file format.
                Go through each row in the csv file and follow the below steps
                    1. print "DELETE * FROM ActivityPracticeGroup WHERE ID =" then print the ID value of the row
                    2. For each NewPracticeGroup value
                        a. print "INSERT INTO ActivityPracticeGroup VALUES (" then print the ID value of the row then , then the NewPracticeGroup value
                    3. print a new line
                Reply with only the end result
                """
            );

            var sql = await sqlGenerator.RunAsync(newPracticeGroupCsv.Text);
            Console.WriteLine(sql);
        }
        else
        {
            Console.WriteLine("File not found.");
        }
    }
}








//AzureOpenAIClient azureClient = new(
//    new Uri(aoaiEndpoint),
//    new DefaultAzureCredential());

//OpenAIFileClient fileClient = azureClient.GetOpenAIFileClient();
//using FileStream fs = File.OpenRead("test.txt");
//// Use FileUploadPurpose.Assistants and a string filename
//OpenAIFile uploadedFile = await fileClient.UploadFileAsync(
//    fs,
//    "test.txt",
//    FileUploadPurpose.Assistants); // Correct replacement for OpenAIFilePurpose

//// 3. Attach to a Vector Store (Azure AI Search-backed)
//#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
//VectorStoreClient vectorClient = azureClient.GetVectorStoreClient();

//VectorStore store = await vectorClient.CreateVectorStoreAsync(
//    new VectorStoreCreationOptions { Name = "KnowledgeBase" });

//await vectorClient.AddFileToVectorStoreAsync(store.Id, uploadedFile.Id);

//// 4. Create Agent (Assistant) with File Search
//AssistantClient assistantClient = azureClient.GetAssistantClient();
//Assistant assistant = await assistantClient.CreateAssistantAsync(
//    model: "gpt-4o",
//    new AssistantCreationOptions
//    {
//        Tools = { new FileSearchToolDefinition() },
//        ToolResources = new()
//        {
//            FileSearch = new() { VectorStoreIds = { store.Id } }
//        }
//    });



//AssistantThread thread = await assistantClient.CreateThreadAsync();
//await assistantClient.CreateMessageAsync(
//    thread.Id,
//    MessageRole.User,
//    ["What is Bob's favourite color?"]
//);

//ThreadRun run = await assistantClient.CreateRunAsync(thread.Id, assistant.Id);

//// 5. Poll for completion
//while (run.Status == OpenAI.Assistants.RunStatus.Queued || run.Status == OpenAI.Assistants.RunStatus.InProgress)
//{
//    await Task.Delay(1000);
//    run = await assistantClient.GetRunAsync(thread.Id, run.Id);
//}

//// 6. Retrieve the response messages
//var messages = assistantClient.GetMessagesAsync(thread.Id);
//await foreach (ThreadMessage msg in messages)
//{
//    if (msg.Role == MessageRole.Assistant)
//    {
//        Console.WriteLine($"Assistant: {msg.Content[0].Text}");
//    }
//}


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
    var client = new AzureOpenAIClient(new Uri(aoaiEndpoint),
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


    List<Microsoft.Extensions.AI.ChatMessage> messages = new();

    while (true)
    {
        Console.Write("Q: ");
        string userInput = Console.ReadLine()!;
        messages.Add(new(ChatRole.User, userInput));

        var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
           .WithHandoffs(triageAgent, [mathTutor, grammarTutor]) // Triage can route to either specialist
           .WithHandoff(mathTutor, triageAgent)                  // Math tutor can return to triage
           .WithHandoff(grammarTutor, triageAgent)               // History tutor can return to triage
           .Build();

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


//Task.Run(async () =>
//{
//    await TestHandoff();

//    WebApplicationBuilder builder = WebApplication.CreateBuilder();


//    var client = new AzureOpenAIClient(
//      new Uri(aoaiEndpoint),
//      new AzureCliCredential());

//    var embeddingClient = client.GetEmbeddingClient("text-embedding-ada-002");

//    var embeddingResponse = await embeddingClient.GenerateEmbeddingAsync("work");
//    var vectorJson = $"[{string.Join(",", embeddingResponse.Value.ToFloats().ToArray())}]";

//    InsertDocument("Sql Server 2025 supports native vector search", vectorJson);

//    // Query SQL Server 2025 with VECTOR
//    string context = "";
//    using var conn = new SqlConnection("Data Source=localhost;Initial Catalog=test_vector;Integrated Security=True;Pooling=False;Encrypt=False;Trust Server Certificate=False");
//    await conn.OpenAsync();

//    using var cmd = new SqlCommand("SearchContext", conn);
//    cmd.CommandType = System.Data.CommandType.StoredProcedure;
//    cmd.Parameters.AddWithValue("@QueryEmbedding", vectorJson);

//    using var reader = await cmd.ExecuteReaderAsync();
//    while (await reader.ReadAsync())
//    {
//        context += reader["Content"].ToString() + "\n---\n";
//    }

//    Console.WriteLine(string.IsNullOrEmpty(context) ? "No relevant info found." : context);

//}).GetAwaiter().GetResult();



public class MyFileDocument
{
    [SimpleField(IsKey = true)]
    public string Id { get; set; }

    [SearchableField]
    public string FileName { get; set; }

    [SearchableField]
    public string Content { get; set; }
}
