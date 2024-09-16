using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Send
{
    using RabbitMQ.Client;
    using System;
    using System.Text;

    public class RabbitConnection
    {
        public static void sendMessageToRabbitMQ(string message)
        {
            //Vi sætter rabbitMQ op
            var factory = new ConnectionFactory { HostName = "localhost" };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            // Vi opretter en queue, som vi kalder "send-card-uid"
            channel.QueueDeclare(queue: "send-card-uid",
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            // Vi sender beskeden til "send-card-uid"
            var body = Encoding.UTF8.GetBytes(message);
            channel.BasicPublish(exchange: string.Empty,
                                 routingKey: "send-card-uid",
                                 basicProperties: null,
                                 body: body);

            // Vi viser den sendte besked i konsollen, så vi kan se at det virker
            Console.WriteLine($" [x] Sent message: {message}");
        }
    }
}
