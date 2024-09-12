using System;
using PCSC;
using PCSC.Iso7816;
using PCSC.Monitoring;

class Program
{
    static void Main(string[] args)
    {
        var contextFactory = ContextFactory.Instance;
        using var context = contextFactory.Establish(SCardScope.System);
        var readers = context.GetReaders();
        if (readers.Length == 0)
        {
            Console.WriteLine("No readers found.");
            return;
        }

        var readerName = readers[0];  // Assumes the first reader is the one you want
        Console.WriteLine("Waiting for NFC card scan...");

        using var monitor = new SCardMonitor(contextFactory, SCardScope.System);
        monitor.CardInserted += (sender, args) =>
        {
            Console.WriteLine("NFC Transponder detected.");
            using var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any);
            ProcessCard(reader);
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

    private static void ProcessCard(ICardReader reader)
    {
        var apdu = new CommandApdu(IsoCase.Case2Short, reader.Protocol)
        {
            CLA = 0xFF, // Class
            Instruction = InstructionCode.GetData,
            P1 = 0x00,  // Parameter 1
            P2 = 0x00,  // Parameter 2
            Le = 0       // Expected length of the data returned
        };

        var sendBuffer = apdu.ToArray();
        var receiveBuffer = new byte[259]; // Adjust size as needed

        // Using the simplified Transmit call
        var receivedLength = reader.Transmit(sendBuffer, receiveBuffer);
        if (receivedLength < 0) // Assuming negative values indicate an error
        {
            Console.WriteLine($"Failed to transmit APDU. Received length: {receivedLength}");
            return;
        }

        // Parsing the response assuming the actual response data starts at index 0 and receivedLength specifies the data size
        var responseApdu = new ResponseApdu(receiveBuffer, receivedLength, IsoCase.Case2Short, reader.Protocol);
        Console.WriteLine("Card UID: " + BitConverter.ToString(responseApdu.GetData()));
    }
}
