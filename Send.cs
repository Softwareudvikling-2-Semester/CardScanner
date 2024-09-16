using System;
using PCSC;
using PCSC.Iso7816;
using PCSC.Monitoring;
using Send;

public class Program
{
    static void Main(string[] args)
    {
        ProcessCard();
    }

    private static void ProcessCard() {
        var contextFactory = ContextFactory.Instance;
        using var context = contextFactory.Establish(SCardScope.System);
        var readers = context.GetReaders();
        if (readers.Length == 0)
        {
            Console.WriteLine("No readers found.");
            return;
        }
        var readerName = readers[0];
        Console.WriteLine("Waiting for NFC card scan...");

        using var monitor = new SCardMonitor(contextFactory, SCardScope.System);
        monitor.CardInserted += (sender, args) =>
        {
            Console.WriteLine("NFC Transponder detected.");
            using var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any);

            // Vi kalder GetCardUID metoden, for at udtrække UID fra kortet.
            var uid = GetCardUID(reader);
            if (uid != null)
            {
                //Vi konverterer byte arrayet til en string, så vi kan bruge det, og sende til RabbitMQ.
                RabbitConnection.sendMessageToRabbitMQ(BitConverter.ToString(uid));
            }
            else
            {
                Console.WriteLine("Failed to read Card UID.");
            }
        };

        monitor.CardRemoved += (sender, args) =>
        {
            Console.WriteLine("NFC Transponder removed.");
        };

        monitor.Start(readerName);

        Console.WriteLine("Press enter to exit");
        Console.ReadLine();
        monitor.Cancel();
    }

    //Vi bruger PCSC library til at læse data fra kortet, ud fra dokumentationen.
    private static byte[] GetCardUID(ICardReader reader)
    {
        //Vi opretter en CommandApdu for at kunne læse UID korrekt
        var getUidCommand = new CommandApdu(IsoCase.Case2Short, reader.Protocol)
        {
            CLA = 0xFF,
            INS = 0xCA,
            P1 = 0x00,
            P2 = 0x00,
            Le = 0x00 
        };

        var sendBuffer = getUidCommand.ToArray();
        var receiveBuffer = new byte[258];

        int receivedLength = reader.Transmit(sendBuffer, receiveBuffer);

        if (receivedLength >= 2)
        {
            byte sw1 = receiveBuffer[receivedLength - 2];
            byte sw2 = receiveBuffer[receivedLength - 1];

            if (sw1 == 0x90 && sw2 == 0x00)
            {
                // UID is in the response data before SW1 and SW2
                int uidLength = receivedLength - 2;
                byte[] uid = new byte[uidLength];
                Array.Copy(receiveBuffer, 0, uid, 0, uidLength);
                return uid;
            }
            else
            {
                Console.WriteLine($"Failed to get UID. SW1SW2: {sw1:X2}{sw2:X2}");
                return null;
            }
        }
        else
        {
            Console.WriteLine("Failed to get UID. Invalid response length.");
            return null;
        }
    }
}
