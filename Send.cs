using System;
using System.Text;
using RabbitMQ.Client;
using Sydesoft.NfcDevice;

class Send
{
    private static ACR122U acr122u = new ACR122U();

    static void Main(string[] args)
    {
        // Initialize the NFC reader
        acr122u.Init(false, 50, 4, 4, 200);  // NTAG213 initialization
        acr122u.CardInserted += Acr122u_CardInserted;
        acr122u.CardRemoved += Acr122u_CardRemoved;
        string message = "Hello";
        sendMessageToRabbitMQ(message);

        Console.WriteLine("Waiting for NFC card scan...");
        Console.ReadLine();
    }

    // Event handler for when a card is inserted
    private static void Acr122u_CardInserted(PCSC.ICardReader reader)
    {
        Console.WriteLine("NFC Transponder detected.");

        // Get the unique ID of the card
        var uid = BitConverter.ToString(acr122u.GetUID(reader)).Replace("-", "");
        Console.WriteLine("Unique ID: " + uid);

        // Read data from the NFC card
        var nfcData = Encoding.UTF8.GetString(acr122u.ReadData(reader));
        Console.WriteLine("Read NFC data: " + nfcData);

        // Set up RabbitMQ connection
        var factory = new ConnectionFactory { HostName = "localhost" };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        // Declare a queue
        channel.QueueDeclare(queue: "hello",
                             durable: false,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);

        // Send NFC data to RabbitMQ
        var body = Encoding.UTF8.GetBytes(nfcData);
        channel.BasicPublish(exchange: string.Empty,
                             routingKey: "hello",
                             basicProperties: null,
                             body: body);

        Console.WriteLine($" [x] Sent NFC data: {nfcData}");
    }

    // Event handler for when a card is removed
    private static void Acr122u_CardRemoved()
    {
        Console.WriteLine("NFC Transponder removed.");
    }

    private static void sendMessageToRabbitMQ(string message)
    {
        // Set up RabbitMQ connection
        var factory = new ConnectionFactory { HostName = "localhost" };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        // Declare a queue
        channel.QueueDeclare(queue: "hello",
                             durable: false,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);

        // Send message to RabbitMQ
        var body = Encoding.UTF8.GetBytes(message);
        channel.BasicPublish(exchange: string.Empty,
                             routingKey: "hello",
                             basicProperties: null,
                             body: body);

        Console.WriteLine($" [x] Sent {message}");
    }
}
