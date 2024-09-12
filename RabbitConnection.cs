using RabbitMQ.Client;
using System;
using System.Text;

public class RabbitConnection
{
    public static void sendMessageToRabbitMQ(string message)
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

        // Send the message to RabbitMQ
        var body = Encoding.UTF8.GetBytes(message);
        channel.BasicPublish(exchange: string.Empty,
                             routingKey: "hello",
                             basicProperties: null,
                             body: body);

        Console.WriteLine($" [x] Sent message: {message}");
    }
}
