using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using System;
using System.Text.RegularExpressions;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleNotificationService.Model;
using Amazon.SimpleNotificationService;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace HelloWorld
{
    public class Function
    {
        private static readonly HttpClient client = new HttpClient();

        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest apigProxyEvent, ILambdaContext context)
        {
            var currentExtensionDownloadCount = await GetExtensionDownloadCount();
            var ssmParameterKey = Environment.GetEnvironmentVariable("SSM_PARAMETER_KEY");
            var savedDownloadCount = await GetParameterValue(ssmParameterKey);

            if (currentExtensionDownloadCount != savedDownloadCount)
            {
                await UpdateParameter(currentExtensionDownloadCount, ssmParameterKey);

                string message = $"Extension download count, last download count: {savedDownloadCount}, " +
                           $"new download count: {currentExtensionDownloadCount}, updated at: {DateTime.UtcNow.ToLongDateString()}";

                var request = new PublishRequest
                {
                    Message = message,
                    TopicArn = "arn:aws:sns:ap-southeast-2:923871408420:JiraExtensionDownloadNotification",
                };

                try
                {
                    using (var client = new AmazonSimpleNotificationServiceClient())
                    {
                        var topicResponse = await client.PublishAsync(request);

                        Console.WriteLine($"Message published, id: {topicResponse.MessageId}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Unable to publish message: {ex.Message}");
                }
            }

            var body = new Dictionary<string, string>
            {
                { "Extension saved download count", savedDownloadCount },
                { "Extension new download count", currentExtensionDownloadCount }
            };

            return new APIGatewayProxyResponse
            {
                Body = JsonConvert.SerializeObject(body),
                StatusCode = 200,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        public static async Task<string> GetExtensionDownloadCount()
        {
            var websiteUrl = Environment.GetEnvironmentVariable("WEBSITE_URL");
            var extensionDownloadCount = await client.GetStringAsync(websiteUrl)
                .ConfigureAwait(continueOnCapturedContext: false);

            Regex pattern = new Regex(@"UserDownloads:(?<downloads>\d+)");
            Match match = pattern.Match(extensionDownloadCount);

            return match.Groups["downloads"].Value;
        }

        public static async Task<string> GetParameterValue(string parameterKey)
        {
            var request = new GetParameterRequest()
            {
                Name = parameterKey
            };

            var extensionDownloadCount = String.Empty;

            try
            {
                using (var client = new AmazonSimpleSystemsManagementClient())
                {
                    var response = await client.GetParameterAsync(request);
                    extensionDownloadCount = response.Parameter.Value;

                    Console.WriteLine($"Parameter {request.Name} value is: {extensionDownloadCount}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error occurred: {ex.Message}");
            }

            return extensionDownloadCount;
        }

        public static async Task UpdateParameter(string value, string parameterKey)
        {
            var request = new PutParameterRequest()
            {
                Name = parameterKey,
                Value = value,
                Type = ParameterType.String,
                Overwrite = true,
            };

            try
            {
                using (var client = new AmazonSimpleSystemsManagementClient())
                {
                    await client.PutParameterAsync(request);

                    Console.WriteLine($"Parameter {parameterKey} updated successfully");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to put parameter {parameterKey}, Error message: {ex.Message}");
            }
        }
    }
}