using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
ConnectionFactory factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
{
    Console.WriteLine("Please provide an id");
    Environment.Exit(1);
}

var id = args[0];

var connection = factory.CreateConnection();
var channel = connection.CreateModel();

channel.QueueDeclare(
    queue: "payments",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

Console.WriteLine($" [*] Waiting for messages on consumer {id}");

EventingBasicConsumer consumer = new(channel);

Console.WriteLine("Waiting for new messages");

consumer.Received += async (model, ea) =>
{
    var body = ea.Body.ToArray();
    var message = Encoding.UTF8.GetString(body);

    var headers = ea.BasicProperties.Headers;
    DateTime creationTime = new DateTime();

    if (headers != null && headers.ContainsKey("creation_time"))
    {
        byte[] headerBytes = headers["creation_time"] as byte[];
        if (headerBytes != null)
        {
            string creationTimeString = Encoding.UTF8.GetString(headerBytes);
            if (DateTime.TryParse(creationTimeString, out creationTime))
            {

            }
            else
            {
                Console.WriteLine($"Failed to parse creation time: {creationTimeString}");
            }
        }
    }

    string webhookUrl = "http://localhost:5039/payments/pix";
    string originUrl = $"http://localhost:5039/payments/pix/";

    Console.WriteLine($"Sending to: {webhookUrl}");
    Console.WriteLine($"{message}");
    DateTime minus120sec = DateTime.UtcNow.AddSeconds(-120);
    HttpClient httpClient = new();

    try
    {
        var payment = JsonSerializer.Deserialize<TransferStatus>(message);
        Console.WriteLine($"Creation time: {creationTime} minus120sec: {minus120sec}");
        Console.WriteLine($"Diff: {creationTime - minus120sec}");

        if (minus120sec >= creationTime)
        {
            Console.WriteLine("120 seconds Processing time expired. Sending Failed notification");

            string apiUpdateUrl = $"http://localhost:5041/payment/failed/{payment.Id}";
            var apiUpdateResponse = await httpClient.PatchAsync(apiUpdateUrl, null);

            TransferStatusDTO status = new() { Id = payment.Id, Status = "Failed" };
            string json = JsonSerializer.Serialize(status);
            var statusContent = new StringContent(json, Encoding.UTF8, "application/json");

            var originResponse = await httpClient.PatchAsync(originUrl, statusContent);

            if (apiUpdateResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Payment updated to Failed on database");
            }

            if (originResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed notification sent with success to OriginPsp");
            }
            else
            {
                Console.WriteLine($"Error sending Failed Notification. Status code: {originResponse.StatusCode}");
            }
            channel.BasicReject(ea.DeliveryTag, false);
        }
        else
        {
            var content = new StringContent(message, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(webhookUrl, content);
            DateTime responseTimeMinus120sec = DateTime.UtcNow.AddSeconds(-120);

            if (response.IsSuccessStatusCode)
            {
                if (responseTimeMinus120sec <= creationTime)
                {

                    Console.WriteLine($"Response from destiny PSP: {response.StatusCode}");

                    string apiUpdateUrl = $"http://localhost:5041/payment/success/{payment.Id}";
                    var apiUpdateResponse = await httpClient.PatchAsync(apiUpdateUrl, null);

                    TransferStatusDTO status = new() { Id = payment.Id, Status = "Success" };
                    string json = JsonSerializer.Serialize(status);
                    var statusContent = new StringContent(json, Encoding.UTF8, "application/json");

                    var originResponse = await httpClient.PatchAsync(originUrl, statusContent);

                    if (apiUpdateResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Payment updated to Sucess on database");
                    }

                    if (originResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Success notification sent with success to origin Psp");
                    }
                    else
                    {
                        Console.WriteLine($"Error sending success Notification. Origin: {originResponse.StatusCode}");
                    }

                    channel.BasicAck(ea.DeliveryTag, false);
                }else{
                    channel.BasicReject(ea.DeliveryTag, true);
                }
            }
            else
            {
                Console.WriteLine($"Error sending request to destiny PSP. Status code: {response.StatusCode}");
                channel.BasicReject(ea.DeliveryTag, true);
            }
        }

    }
    catch (Exception ex)
    {
        var payment = JsonSerializer.Deserialize<TransferStatus>(message);
        if (minus120sec >= creationTime)
        {
            Console.WriteLine("120 seconds Processing time expired. Sending Failed notification");

            string apiUpdateUrl = $"http://localhost:5041/payment/failed/{payment.Id}";
            var apiUpdateResponse = await httpClient.PatchAsync(apiUpdateUrl, null);

            if (apiUpdateResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed update sent with success");
            }
            else
            {
                Console.WriteLine($"Error sending failed update. Status code: {apiUpdateResponse.StatusCode}");
            }
            channel.BasicReject(ea.DeliveryTag, false);
        }
        else
        {
            Console.WriteLine($"Error sending webhook: {ex.Message}");
            channel.BasicReject(ea.DeliveryTag, true);
        }
    }
};

channel.BasicConsume(
    queue: "payments",
    autoAck: false,
    consumer: consumer
);

Console.ReadLine();
