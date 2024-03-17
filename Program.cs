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
    Console.WriteLine($"Recebi {message}");

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
    Console.WriteLine($"Sending to webhook: {webhookUrl}");
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

            string notificationUrl = $"http://localhost:5041/payment/failed/{payment.Id}";
            var notificationResponse = await httpClient.PatchAsync(notificationUrl, null);

            if (notificationResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed notification sent with success");
            }
            else
            {
                Console.WriteLine($"Error sending failed Notification. Status code: {notificationResponse.StatusCode}");
            }
            channel.BasicReject(ea.DeliveryTag, false);
        }
        else
        {
            var content = new StringContent(message, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(webhookUrl, content);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Response from destiny PSP: {response.StatusCode}");

                string notificationUrl = $"http://localhost:5041/payment/success/{payment.Id}";
                var notificationResponse = await httpClient.PatchAsync(notificationUrl, null);
                channel.BasicAck(ea.DeliveryTag, false);
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

            string notificationUrl = $"http://localhost:5041/payment/failed/{payment.Id}";
            var notificationResponse = await httpClient.PatchAsync(notificationUrl, null);

            if (notificationResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed notification sent with success");
            }
            else
            {
                Console.WriteLine($"Error sending failed Notification. Status code: {notificationResponse.StatusCode}");
            }
            channel.BasicReject(ea.DeliveryTag, false);
        }
        else
        {
            Console.WriteLine($"Error sending webhook: {ex.Message}");
            channel.BasicReject(ea.DeliveryTag, true);
        }
    }
    Thread.Sleep(1000);
};

channel.BasicConsume(
    queue: "payments",
    autoAck: false,
    consumer: consumer
);

Console.ReadLine();
