#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

public static class Function1
{
    [Function("Function1")]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger("Function1");

        // Parse query parameters
        var queryParams = req.Url.Query;
        Dictionary<string, string?[]> queryParameters = queryParams
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(q => q.Split('=', 2))
            .GroupBy(q => q[0], q => q.Length > 1 ? WebUtility.UrlDecode(q[1]) : null)
            .ToDictionary(g => g.Key, g => g.ToArray());

        // Get external API URL from query parameters
        string? externalApiUrl = queryParameters.TryGetValue("externalApiUrl", out var externalApiUrlValues)
            ? externalApiUrlValues.FirstOrDefault()
            : null;

        // Read the request body
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

        // Validate the external API URL
        if (string.IsNullOrEmpty(externalApiUrl) || !Uri.IsWellFormedUriString(externalApiUrl, UriKind.Absolute))
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            response.Headers.Add("Content-Type", "text/plain");
            response.WriteString("Invalid or missing externalApiUrl");
            return response;
        }

        try
        {
            // Create a new HttpClient instance
            using (var client = new HttpClient())
            {
                HttpRequestMessage relayRequest = new HttpRequestMessage(
                    req.Method == HttpMethod.Post.Method ? HttpMethod.Post : HttpMethod.Get,
                    externalApiUrl);

                // Set request content for POST method
                if (req.Method == HttpMethod.Post.Method)
                {
                    relayRequest.Content = new StringContent(requestBody);
                }

                // Copy headers from the incoming request to the outgoing request
                foreach (var header in req.Headers)
                {
                    relayRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }

                // Send the request to the external API
                using (var relayResponse = await client.SendAsync(relayRequest))
                {
                    var responseBody = await relayResponse.Content.ReadAsStringAsync();

                    // Create the response to return
                    var response = req.CreateResponse((HttpStatusCode)relayResponse.StatusCode);

                    // Copy headers from the external API response
                    foreach (var header in relayResponse.Headers)
                    {
                        response.Headers.Add(header.Key, header.Value.ToArray());
                    }

                    // Copy content headers from the external API response
                    foreach (var header in relayResponse.Content.Headers)
                    {
                        response.Headers.Add(header.Key, header.Value.ToArray());
                    }

                    response.WriteString(responseBody);
                    return response;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Exception occurred: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            response.Headers.Add("Content-Type", "text/plain");
            response.WriteString("An error occurred while processing the request");
            return response;
        }
    }
}
