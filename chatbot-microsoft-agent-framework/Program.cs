using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
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
using System.Threading.Tasks;






var searchServiceEndpoint = "";
var apiKey = "";
var indexName = "";
var credential = new AzureKeyCredential(apiKey);
var searchClient = new SearchClient(new Uri(searchServiceEndpoint), indexName, credential);
var indexClient = new SearchIndexClient(new Uri(searchServiceEndpoint), credential);

//await UploadFileContent("2", "test2.txt", "\r\nSkip to main content\r\nSkip to Ask Learn chat experience\r\nLearn\r\n\r\nSign in\r\nAzure\r\n\r\nVersion\r\nSearch\r\n\r\n    What is Microsoft Foundry (new)?\r\n        Code interpreter\r\n        Custom code interpreter (preview)\r\n        Browser automation (preview)\r\n        Computer Use (preview)\r\n        Image generation (preview)\r\n            Retrieval Augmented Generation (RAG) overview\r\n            Azure AI Search\r\n            File search\r\n            Vector stores for file search\r\n            Foundry IQ (preview)\r\n            SharePoint (preview)\r\n            Fabric data agent (preview)\r\n\r\n    Learn Azure Microsoft Foundry \r\n\r\nFile search tool for agents\r\nWhat would you like to see?\r\n\r\nThe file search tool augments Microsoft Foundry agents with knowledge from outside their model, such as proprietary product information or documents provided by your users. This article shows you how to upload files, create a vector store, and enable file search for an agent to answer queries from your documents.\r\n\r\nNote\r\n\r\nBy using the standard agent setup, the improved file search tool ensures your files remain in your own storage. Your Azure AI Search resource ingests the files, so you maintain complete control over your data.\r\n\r\nImportant\r\n\r\nFile search has additional charges beyond the token-based fees for model usage.\r\nUsage support\r\nMicrosoft Foundry support \tPython SDK \tC# SDK \tJavaScript SDK \tJava SDK \tREST API \tBasic agent setup \tStandard agent setup\r\n✔️ \t✔️ \t✔️ \t✔️ \t- \t✔️ \t✔️ \t✔️\r\nPrerequisites\r\n\r\n    A basic or standard agent environment\r\n    The latest prerelease package. See the quickstart for details\r\n    Storage Blob Data Contributor role on your project's storage account (required for uploading files to your project's storage)\r\n    Azure AI Owner role on your Foundry resource (required for creating agent resources)\r\n    Environment variables configured: FOUNDRY_PROJECT_ENDPOINT, MODEL_DEPLOYMENT_NAME\r\n\r\nCode example\r\n\r\nNote\r\n\r\nYou need the latest prerelease package. See the quickstart for details.\r\nFile search sample with agent\r\n\r\nIn this example, you create a local file, upload it to Azure, and use it in the newly created VectorStore for file search. The code in this example is synchronous and streaming. For asynchronous usage, see the sample code in the Azure SDK for .NET repository on GitHub.\r\nC#\r\n\r\n// Create project client and read the environment variables, which is used in the next steps.\r\nvar projectEndpoint = System.Environment.GetEnvironmentVariable(\"FOUNDRY_PROJECT_ENDPOINT\");\r\nvar modelDeploymentName = System.Environment.GetEnvironmentVariable(\"MODEL_DEPLOYMENT_NAME\");\r\nAIProjectClient projectClient = new(endpoint: new Uri(projectEndpoint), tokenProvider: new DefaultAzureCredential());\r\n\r\n// Create a toy example file and upload it using OpenAI mechanism.\r\nstring filePath = \"sample_file_for_upload.txt\";\r\nFile.WriteAllText(\r\n    path: filePath,\r\n    contents: \"The word 'apple' uses the code 442345, while the word 'banana' uses the code 673457.\");\r\nOpenAIFileClient fileClient = projectClient.OpenAI.GetOpenAIFileClient();\r\nOpenAIFile uploadedFile = fileClient.UploadFile(filePath: filePath, purpose: FileUploadPurpose.Assistants);\r\nFile.Delete(filePath);\r\n\r\n// Create the VectorStore and provide it with uploaded file ID.\r\nVectorStoreClient vctStoreClient = projectClient.OpenAI.GetVectorStoreClient();\r\nVectorStoreCreationOptions options = new()\r\n{\r\n    Name = \"MySampleStore\",\r\n    FileIds = { uploadedFile.Id }\r\n};\r\nVectorStore vectorStore = vctStoreClient.CreateVectorStore(options: options);\r\n\r\n// Create an Agent capable of using File search.\r\nPromptAgentDefinition agentDefinition = new(model: modelDeploymentName)\r\n{\r\n    Instructions = \"You are a helpful agent that can help fetch data from files you know about.\",\r\n    Tools = { ResponseTool.CreateFileSearchTool(vectorStoreIds: new[] { vectorStore.Id }), }\r\n};\r\nAgentVersion agentVersion = projectClient.Agents.CreateAgentVersion(\r\n    agentName: \"myAgent\",\r\n    options: new(agentDefinition));\r\n\r\n// Ask a question about the file's contents.\r\nProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentVersion.Name);\r\n\r\nResponseResult response = responseClient.CreateResponse(\"Can you give me the documented codes for 'banana' and 'orange'?\");\r\n\r\n// Create the response and throw an exception if the response contains the error.\r\nAssert.That(response.Status, Is.EqualTo(ResponseStatus.Completed));\r\nConsole.WriteLine(response.GetOutputText());\r\n\r\n// Remove all the resources created in this sample.\r\nprojectClient.Agents.DeleteAgentVersion(agentName: agentVersion.Name, agentVersion: agentVersion.Version);\r\nvctStoreClient.DeleteVectorStore(vectorStoreId: vectorStore.Id);\r\nfileClient.DeleteFile(uploadedFile.Id);\r\n\r\nExpected output\r\n\r\nThe following output comes from the preceding code sample:\r\nConsole\r\n\r\nThe code for 'banana' is 673457. I couldn't find any documented code for 'orange' in the files I have access to.\r\n\r\nFile search sample with agent in streaming scenarios\r\n\r\nIn this example, you create a local file, upload it to Azure, and use it in the newly created VectorStore for file search. The code in this example is synchronous and streaming. For asynchronous usage, see the sample code in the Azure SDK for .NET repository on GitHub.\r\nC#\r\n\r\n// Create project client and read the environment variables, which will be used in the next steps.\r\nvar projectEndpoint = System.Environment.GetEnvironmentVariable(\"FOUNDRY_PROJECT_ENDPOINT\");\r\nvar modelDeploymentName = System.Environment.GetEnvironmentVariable(\"MODEL_DEPLOYMENT_NAME\");\r\nAIProjectClient projectClient = new(endpoint: new Uri(projectEndpoint), tokenProvider: new DefaultAzureCredential());\r\n\r\n// Create a toy example file and upload it using OpenAI mechanism.\r\nstring filePath = \"sample_file_for_upload.txt\";\r\nFile.WriteAllText(\r\n    path: filePath,\r\n    contents: \"The word 'apple' uses the code 442345, while the word 'banana' uses the code 673457.\");\r\nOpenAIFile uploadedFile = projectClient.OpenAI.Files.UploadFile(filePath: filePath, purpose: FileUploadPurpose.Assistants);\r\nFile.Delete(filePath);\r\n\r\n// Create the `VectorStore` and provide it with uploaded file ID.\r\nVectorStoreCreationOptions options = new()\r\n{\r\n    Name = \"MySampleStore\",\r\n    FileIds = { uploadedFile.Id }\r\n};\r\nVectorStore vectorStore = projectClient.OpenAI.VectorStores.CreateVectorStore(options);\r\n\r\n// Create an agent capable of using File search.\r\nPromptAgentDefinition agentDefinition = new(model: modelDeploymentName)\r\n{\r\n    Instructions = \"You are a helpful agent that can help fetch data from files you know about.\",\r\n    Tools = { ResponseTool.CreateFileSearchTool(vectorStoreIds: new[] { vectorStore.Id }), }\r\n};\r\nAgentVersion agentVersion = projectClient.Agents.CreateAgentVersion(\r\n    agentName: \"myAgent\",\r\n    options: new(agentDefinition)\r\n);\r\n\r\n// Create the conversation to store responses.\r\nProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();\r\nCreateResponseOptions responseOptions = new()\r\n{\r\n    Agent = agentVersion,\r\n    AgentConversationId = conversation.Id,\r\n    StreamingEnabled = true,\r\n};\r\n\r\n// Create a helper method ParseResponse to format streaming response output.\r\n// If the stream ends up in error state, it will throw an error. \r\nprivate static void ParseResponse(StreamingResponseUpdate streamResponse)\r\n{\r\n    if (streamResponse is StreamingResponseCreatedUpdate createUpdate)\r\n    {\r\n        Console.WriteLine($\"Stream response created with ID: {createUpdate.Response.Id}\");\r\n    }\r\n    else if (streamResponse is StreamingResponseOutputTextDeltaUpdate textDelta)\r\n    {\r\n        Console.WriteLine($\"Delta: {textDelta.Delta}\");\r\n    }\r\n    else if (streamResponse is StreamingResponseOutputTextDoneUpdate textDoneUpdate)\r\n    {\r\n        Console.WriteLine($\"Response done with full message: {textDoneUpdate.Text}\");\r\n    }\r\n    else if (streamResponse is StreamingResponseOutputItemDoneUpdate itemDoneUpdate)\r\n    {\r\n        if (itemDoneUpdate.Item is MessageResponseItem messageItem)\r\n        {\r\n            foreach (ResponseContentPart part in messageItem.Content)\r\n            {\r\n                foreach (ResponseMessageAnnotation annotation in part.OutputTextAnnotations)\r\n                {\r\n                    if (annotation is FileCitationMessageAnnotation fileAnnotation)\r\n                    {\r\n                        // Note fileAnnotation.Filename will be available in OpenAI package versions\r\n                        // greater then 2.6.0.\r\n                        Console.WriteLine($\"File Citation - File ID: {fileAnnotation.FileId}\");\r\n                    }\r\n                }\r\n            }\r\n        }\r\n    }\r\n    else if (streamResponse is StreamingResponseErrorUpdate errorUpdate)\r\n    {\r\n        throw new InvalidOperationException($\"The stream has failed with the error: {errorUpdate.Message}\");\r\n    }\r\n}\r\n\r\n// Wait for the stream to complete.\r\nresponseOptions.InputItems.Clear();\r\nresponseOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(\"Can you give me the documented codes for 'banana' and 'orange'?\"));\r\nforeach (StreamingResponseUpdate streamResponse in projectClient.OpenAI.Responses.CreateResponseStreaming(responseOptions))\r\n{\r\n    ParseResponse(streamResponse);\r\n}\r\n\r\n// Ask follow up question and start a new stream.\r\nConsole.WriteLine(\"Demonstrating follow-up query with streaming...\");\r\nresponseOptions.InputItems.Clear();\r\nresponseOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(\"What was my previous question about?\"));\r\nforeach (StreamingResponseUpdate streamResponse in projectClient.OpenAI.Responses.CreateResponseStreaming(responseOptions))\r\n{\r\n    ParseResponse(streamResponse);\r\n}\r\n\r\n// Remove all the resources created in this sample.\r\nprojectClient.Agents.DeleteAgentVersion(agentName: agentVersion.Name, agentVersion: agentVersion.Version);\r\nprojectClient.OpenAI.VectorStores.DeleteVectorStore(vectorStoreId: vectorStore.Id);\r\nprojectClient.OpenAI.Files.DeleteFile(uploadedFile.Id);\r\n\r\nExpected output\r\n\r\nThe following output comes from the preceding code sample:\r\nConsole\r\n\r\nStream response created with ID: <response-id>\r\nDelta: The code for 'banana' is 673457. I couldn't find any documented code for 'orange' in the files I have access to.\r\nResponse done with full message: The code for 'banana' is 673457. I couldn't find any documented code for 'orange' in the files I have access to.\r\nFile Citation - File ID: <file-id>\r\nDemonstrating follow-up query with streaming...\r\nStream response created with ID: <response-id>\r\nDelta: Your previous question was about the documented codes for 'banana' and 'orange'.\r\nResponse done with full message: Your previous question was about the documented codes for 'banana' and\r\n'orange'.\r\n\r\nVerify results\r\n\r\nAfter running a code sample in this article, verify that file search is working:\r\n\r\n    Confirm that the vector store and file are created.\r\n        In the Python and TypeScript samples, the upload-and-poll helpers complete only after ingestion finishes.\r\n    Ask a question that you can answer only from your uploaded content.\r\n    Confirm that the response is grounded in your documents.\r\n\r\nFile sources\r\n\r\n    Upload local files (Basic and Standard agent setup)\r\n    Azure Blob Storage (Standard setup only)\r\n\r\nDependency on agent setup\r\nBasic agent setup\r\n\r\nThe file search tool has the same functionality as Azure OpenAI Responses API. The tool uses Microsoft managed search and storage resources.\r\n\r\n    You store uploaded files in Microsoft managed storage.\r\n    You create a vector store by using a Microsoft managed search resource.\r\n\r\nStandard agent setup\r\n\r\nThe file search tool uses the Azure AI Search and Azure Blob Storage resources you connect to during agent setup.\r\n\r\n    You store uploaded files in your connected Azure Blob Storage account.\r\n    You create vector stores by using your connected Azure AI Search resource.\r\n\r\nFor both agent setups, the service handles the entire ingestion process, which includes:\r\n\r\n    Automatically parsing and chunking documents.\r\n    Generating and storing embeddings.\r\n    Utilizing both vector and keyword searches to retrieve relevant content for user queries.\r\n\r\nThere's no difference in the code between the two setups. The only variation is in where you store your files and created vector stores.\r\nHow it works\r\n\r\nThe file search tool uses several retrieval best practices to help you extract the right data from your files and improve the model’s responses. The file search tool:\r\n\r\n    Rewrites user queries to make them better for search.\r\n    Breaks down complex user queries into multiple searches that it can run at the same time.\r\n    Runs both keyword and semantic searches across both agent and conversation vector stores.\r\n    Reranks search results to pick the most relevant ones before generating the final response.\r\n    Uses the following settings by default:\r\n        Chunk size: 800 tokens\r\n        Chunk overlap: 400 tokens\r\n        Embedding model: text-embedding-3-large at 256 dimensions\r\n        Maximum number of chunks added to context: 20\r\n\r\nVector stores\r\n\r\nVector store objects give the file search tool the ability to search your files. When you add a file to a vector store, the process automatically parses, chunks, embeds, and stores the file in a vector database that supports both keyword and semantic search. Each vector store can hold up to 10,000 files. You can attach vector stores to both agents and conversations. Currently, you can attach at most one vector store to an agent and at most one vector store to a conversation.\r\n\r\nFor background concepts and lifecycle guidance (readiness, deletion behavior, and expiration policies), see Vector stores for file search.\r\n\r\nYou can remove files from a vector store by either:\r\n\r\n    Delete the vector store file object.\r\n    Delete the underlying file object. This action removes the file from all vector_store and code_interpreter configurations across all agents and conversations in your organization.\r\n\r\nThe maximum file size is 512 MB. Each file should contain no more than 5,000,000 tokens (computed automatically when you attach a file).\r\nEnsuring vector store readiness before creating runs\r\n\r\nEnsure the system fully processes all files in a vector store before you create a run. This step ensures that all the data in your vector store is searchable. You can check for vector store readiness by using the polling helpers in the SDKs, or by manually polling the vector store object to ensure the status is completed.\r\n\r\nAs a fallback, the run object includes a 60-second maximum wait when the conversation's vector store contains files that are still processing. This wait ensures that any files your users upload in a conversation are fully searchable before the run proceeds. This fallback wait doesn't apply to the agent's vector store.\r\nConversation vector stores have default expiration policies\r\n\r\nVector stores that you create by using conversation helpers (like tool_resources.file_search.vector_stores in conversations or message.attachments in Messages) have a default expiration policy of seven days after they were last active (defined as the last time the vector store was part of a run).\r\n\r\nWhen a vector store expires, the runs on that conversation fail. To fix this problem, recreate a new vector store with the same files and reattach it to the conversation.\r\nSupported file types\r\n\r\nNote\r\n\r\nFor text MIME types, the encoding must be UTF-8, UTF-16, or ASCII.\r\nFile format \tMIME Type\r\n.c \ttext/x-c\r\n.cs \ttext/x-csharp\r\n.cpp \ttext/x-c++\r\n.doc \tapplication/msword\r\n.docx \tapplication/vnd.openxmlformats-officedocument.wordprocessingml.document\r\n.html \ttext/html\r\n.java \ttext/x-java\r\n.json \tapplication/json\r\n.md \ttext/markdown\r\n.pdf \tapplication/pdf\r\n.php \ttext/x-php\r\n.pptx \tapplication/vnd.openxmlformats-officedocument.presentationml.presentation\r\n.py \ttext/x-python\r\n.py \ttext/x-script.python\r\n.rb \ttext/x-ruby\r\n.tex \ttext/x-tex\r\n.txt \ttext/plain\r\n.css \ttext/css\r\n.js \ttext/javascript\r\n.sh \tapplication/x-sh\r\n.ts \tapplication/typescript\r\nLimitations\r\n\r\nKeep these limits in mind when you plan your file search integration:\r\n\r\n    File search supports specific file formats and encodings. See Supported file types.\r\n    Each vector store can hold up to 10,000 files.\r\n    You can attach at most one vector store to an agent and at most one vector store to a conversation.\r\n    Features and availability vary by region. See Azure AI Foundry region support.\r\n\r\nTroubleshooting\r\nIssue \tCause \tResolution\r\n401 Unauthorized \tThe access token is missing, expired, or scoped incorrectly. \tGet a fresh token and retry the request. For REST calls, confirm you set AGENT_TOKEN correctly.\r\n403 Forbidden \tThe signed-in identity doesn't have the required roles. \tConfirm the roles in Prerequisites and retry after role assignment finishes propagating.\r\n404 Not Found \tThe project endpoint or resource identifiers are incorrect. \tConfirm FOUNDRY_PROJECT_ENDPOINT and IDs such as agent name, version, vector store ID, and file ID.\r\nResponses ignore your files \tThe agent isn't configured with file_search, or the vector store isn't attached. \tConfirm the agent definition includes file_search and the vector_store_ids list contains your vector store ID.\r\nRelated content\r\n\r\nUse the Azure AI Search tool\r\n\r\nUse the web search tool\r\nNote: The author created this article with assistance from AI. Learn more\r\nAdditional resources\r\n\r\nDocumentation\r\n\r\n    How to upload files using the file search tool - Microsoft Foundry\r\n\r\n    Find code samples and instructions for uploading files to Foundry Agent Service.\r\n\r\n    What are tools in Foundry Agent Service - Microsoft Foundry\r\n\r\n    Learn how to use the various tools available in the Foundry Agent Service.\r\n\r\n    How to use Azure AI Search in Foundry Agent Service - Microsoft Foundry\r\n\r\n    Learn how to ground Azure AI Agents with content indexed in Azure AI Search.\r\n\r\nTraining\r\n\r\nLearning path\r\n\r\nImplement knowledge mining with Azure AI Search - Training\r\n\r\nImplement knowledge mining with Azure AI Search\r\n\r\nCertification\r\n\r\nMicrosoft Certified: Azure AI Engineer Associate - Certifications\r\n\r\nDesign and implement an Azure AI solution using Azure AI services, Azure AI Search, and Azure Open AI.\r\n\r\nEvents\r\n\r\nMicrosoft AI Tour\r\n\r\nDec 16, 11 AM - May 26, 12 PM\r\n\r\nTake your business to the AI frontier with the Microsoft AI Tour\r\nFree to join. Request to attend\r\n\r\n    Last updated on 01/23/2026\r\n\r\nIn this article\r\n\r\n    Prerequisites\r\n    Code example\r\n    File search sample with agent\r\n    File search sample with agent in streaming scenarios\r\n    Verify results\r\n    Dependency on agent setup\r\n    How it works\r\n    Vector stores\r\n    Ensuring vector store readiness before creating runs\r\n\r\nWas this page helpful?\r\n\r\n\t\t\t\r\n\t\t\t\r\n\t\t\t\r\n\t\t\t\r\n\t\t\r\n\r\n    AI Disclaimer\r\n    Previous Versions\r\n    Blog\r\n    Contribute\r\n    Privacy\r\n    Terms of Use\r\n    Trademarks\r\n    © Microsoft 2026\r\n\r\n");
await AskQuestion("how do i do a standard agent setup?");

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









AzureOpenAIClient azureClient = new(
    new Uri(""),
    new DefaultAzureCredential());

OpenAIFileClient fileClient = azureClient.GetOpenAIFileClient();
using FileStream fs = File.OpenRead("test.txt");
// Use FileUploadPurpose.Assistants and a string filename
OpenAIFile uploadedFile = await fileClient.UploadFileAsync(
    fs,
    "test.txt",
    FileUploadPurpose.Assistants); // Correct replacement for OpenAIFilePurpose

// 3. Attach to a Vector Store (Azure AI Search-backed)
#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
VectorStoreClient vectorClient = azureClient.GetVectorStoreClient();

VectorStore store = await vectorClient.CreateVectorStoreAsync(
    new VectorStoreCreationOptions { Name = "KnowledgeBase" });

await vectorClient.AddFileToVectorStoreAsync(store.Id, uploadedFile.Id);

// 4. Create Agent (Assistant) with File Search
AssistantClient assistantClient = azureClient.GetAssistantClient();
Assistant assistant = await assistantClient.CreateAssistantAsync(
    model: "gpt-4o",
    new AssistantCreationOptions
    {
        Tools = { new FileSearchToolDefinition() },
        ToolResources = new()
        {
            FileSearch = new() { VectorStoreIds = { store.Id } }
        }
    });



AssistantThread thread = await assistantClient.CreateThreadAsync();
await assistantClient.CreateMessageAsync(
    thread.Id,
    MessageRole.User,
    ["What is Bob's favourite color?"]
);

ThreadRun run = await assistantClient.CreateRunAsync(thread.Id, assistant.Id);

// 5. Poll for completion
while (run.Status == OpenAI.Assistants.RunStatus.Queued || run.Status == OpenAI.Assistants.RunStatus.InProgress)
{
    await Task.Delay(1000);
    run = await assistantClient.GetRunAsync(thread.Id, run.Id);
}

// 6. Retrieve the response messages
var messages = assistantClient.GetMessagesAsync(thread.Id);
await foreach (ThreadMessage msg in messages)
{
    if (msg.Role == MessageRole.Assistant)
    {
        Console.WriteLine($"Assistant: {msg.Content[0].Text}");
    }
}


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


Task.Run(async () =>
{
    await TestHandoff();

    WebApplicationBuilder builder = WebApplication.CreateBuilder();


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



public class MyFileDocument
{
    [SimpleField(IsKey = true)]
    public string Id { get; set; }

    [SearchableField]
    public string FileName { get; set; }

    [SearchableField]
    public string Content { get; set; }
}
