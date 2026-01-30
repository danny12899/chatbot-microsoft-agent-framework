using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
var azureOpenAIClient = new AzureOpenAIClient(new Uri(""),
        new AzureCliCredential());

builder.Services.AddChatClient(azureOpenAIClient.GetChatClient("gpt-4o")
            .AsIChatClient());
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

AIAgent triageAgent = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
    name: "Triage Agent",
    instructions: "You determine which agent to use based on the user's question. ALWAYS handoff to another agent."
    );

AIAgent mathAgent = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
    name: "Math Agent",
    instructions: "You provide help with math problems. Answer the question and explain your reasoning at each step and include examples. Only respond about math."
    );

AIAgent grammarAgent = azureOpenAIClient.GetChatClient("gpt-4o").CreateAIAgent(
    name: "Grammar Agent",
    instructions: "You provide assistance with grammar queries. Explain your reasoning at each step and include examples. Only respond about grammar."
    );

builder.AddAIAgent("Math Agent", (serviceProver, key) => mathAgent);

var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
           .WithHandoffs(triageAgent, [mathAgent, grammarAgent])
           .WithHandoff(mathAgent, triageAgent)                  
           .WithHandoff(grammarAgent, triageAgent)               
           .Build();

builder.AddWorkflow("MyHandoffWorkflow", (sp, key) => {
    workflow.SetName("MyHandoffWorkflow");
    return workflow;
    }).AddAsAIAgent();

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.MapOpenAIResponses();
app.MapOpenAIConversations();
app.MapDevUI();

app.Run();



public static class WorkflowExtensions
{
    public static T SetName<T>(this T workflow, string name) where T : class
    {
        var type = workflow.GetType();
        // Access the auto-generated backing field for the read-only 'Name' property
        var backingField = type.GetField("<Name>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        if (backingField != null)
        {
            backingField.SetValue(workflow, name);
        }
        return workflow;
    }
}