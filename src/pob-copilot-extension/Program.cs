using Microsoft.AspNetCore.Mvc;
using Octokit;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Numerics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello Copilot!");


string appName = "pob-copilot-extension";
string githubCopilotCompletionsUrl = "https://api.githubcopilot.com/chat/completions";

app.MapPost("/resetData", async (
    [FromHeader(Name = "X-GitHub-Token")] string githubToken) =>
{
    // Initialize the embeddings for pob cases
    var indexDataResponse = await CallIndexDataApi(githubToken);
    if (!indexDataResponse.IsSuccessStatusCode)
    {
        return Results.Problem("Failed to reset data using the indexData API.");
    }

    return Results.Ok("Data reset successfully.");
});

app.MapPost("/agent", async (
    [FromHeader(Name = "X-GitHub-Token")] string githubToken, 
    [FromBody] Request userRequest) =>
{
    // Identify the user using the GitHub API token provided in the request headers.
    var octokitClient = new GitHubClient(new Octokit.ProductHeaderValue(appName))
    {
        Credentials = new Credentials(githubToken)
    };
    var user = await octokitClient.User.Current();


    //AI Chat response prompts:
    //The second is to have make the extension ‘talk like Blackbeard the Pirate’. You’ll add them in the message list.
    userRequest.Messages.Insert(0, new Message
    {
        Role = "system",
        Content = BuildAIPrompt()
    });

    // Perform a semantic search using the user's request
    var semanticSearchRequest = new
    {
        Query = userRequest.Messages.LastOrDefault()?.Content
    };

    var searchHttpClient = new HttpClient();
    var semanticSearchResponse = await searchHttpClient.PostAsJsonAsync("http://localhost:5259/api/Search/semanticSearch", semanticSearchRequest);
    if (!semanticSearchResponse.IsSuccessStatusCode)
    {
        return Results.Problem("Failed to perform semantic search using the Search API.");
    }
    var semanticSearchResponseContent = await semanticSearchResponse.Content.ReadAsStringAsync();
  
      // Include the search results in the system request
    userRequest.Messages.Insert(0, new Message
    {
        Role = "system",     
        Content = $"Embedding for the conversation: {string.Join(",", semanticSearchResponseContent)}"
    });

    //Use the HttpClient class to communicate back to Copilot
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", githubToken);
    userRequest.Stream = true;

    //Use Copilot's LLM to generate a response to the user's messages
    var copilotLLMResponse = await httpClient.PostAsJsonAsync(githubCopilotCompletionsUrl, userRequest);

    //Stream the response straight back to the user.
    var responseStream = await copilotLLMResponse.Content.ReadAsStreamAsync();

    return Results.Stream(responseStream, "application/json");
});

// Method to call the indexData API
async Task<HttpResponseMessage> CallIndexDataApi(string apiToken)
{
    var httpClient = new HttpClient();
    var requestBody = new { apiToken }; // Map the apiToken parameter to the githubToken
    var response = await httpClient.PostAsJsonAsync("http://localhost:5259/api/Search/indexData", requestBody);
    return response;
}


string BuildAIPrompt()
{
    var contextBuilder = new StringBuilder();
    contextBuilder.AppendLine("Context: Below is the relevant case history retrieved from the search index.");
    contextBuilder.AppendLine("Use this information to answer the user's query accurately and concisely.");
    contextBuilder.AppendLine();

    return contextBuilder.ToString();
}

app.MapGet("/callback", () => "You may close this tab and " + 
    "return to GitHub.com (where you should refresh the page " +
    "and start a fresh chat). If you're using VS Code or " +
    "Visual Studio, return there.");
    
app.Run();
