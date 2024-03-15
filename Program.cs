using System.Text;
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
    durable: false,
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


    // Enviar mensagem para o webhook
    string webhookUrl = "http://localhost:5039/payments/pix";
    Console.WriteLine($"Enviando para webhook: {webhookUrl}");
    Console.WriteLine($"Conteúdo da mensagem: {message}");

    HttpClient httpClient = new();

    try
    {
        var content = new StringContent(message, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(webhookUrl, content);

        // Verificando o status code da resposta
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Resposta do webhook: {response.StatusCode}");
        }
        else
        {
            Console.WriteLine($"Erro ao enviar para o webhook. Status code: {response.StatusCode}");
            channel.BasicReject(ea.DeliveryTag, false);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro ao enviar para o webhook: {ex.Message}");
    }
    finally
    {
        // Confirmar a entrega da mensagem apenas se o processamento for bem-sucedido
        channel.BasicAck(ea.DeliveryTag, false);
    }
};

channel.BasicConsume(
    queue: "payments",
    autoAck: false,
    consumer: consumer
);

Console.ReadLine();
