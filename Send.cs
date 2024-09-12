using System;
using PCSC;
using PCSC.Iso7816;
using PCSC.Monitoring;

class Program
{
    // MIFARE Classic has 16 sectors (for 1K card)
    private const int TotalSectors = 16;
    private const int BlocksPerSector = 4;
    private const int BlockSize = 16;

    // Default key A for MIFARE Classic (commonly used for new cards)
    private static readonly byte[] defaultKeyA = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

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

        var readerName = readers[0];
        Console.WriteLine("Waiting for NFC card scan...");

        using var monitor = new SCardMonitor(contextFactory, SCardScope.System);
        monitor.CardInserted += (sender, args) =>
        {
            Console.WriteLine("NFC Transponder detected.");
            using var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any);

            var atr = ReadATR(reader);
            DetermineCardType(atr);

            // Now process the card and read all data
            ReadAllData(reader);
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

    private static void ReadAllData(ICardReader reader)
    {
        for (int sector = 0; sector < TotalSectors; sector++)
        {
            // Authenticate each sector
            var byteKey = ConvertNumericKeyToByteArray("2662");

            byte[] defaultKeyA = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

            if (AuthenticateSector(reader, sector, defaultKeyA))
            {
                Console.WriteLine($"Authenticated Sector {sector}");

                // Read only data blocks (Block 0, 1, and 2), skip Block 3 (sector trailer)
                for (int block = 0; block < BlocksPerSector - 1; block++)  // Only read blocks 0, 1, and 2
                {
                    int blockNumber = (sector * BlocksPerSector) + block;
                    byte[] blockData = ReadBlock(reader, blockNumber);
                    if (blockData != null)
                    {
                        Console.WriteLine($"Block {blockNumber} Data: {BitConverter.ToString(blockData)}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Failed to authenticate Sector {sector}");
            }
        }
    }


    private static bool AuthenticateSector(ICardReader reader, int sectorNumber, byte[] keyA)
    {
        int blockNumber = sectorNumber * BlocksPerSector; // First block of the sector

        // Step 1: Load the key into the reader's memory
        var loadKeyApdu = new CommandApdu(IsoCase.Case3Short, reader.Protocol)
        {
            CLA = 0xFF,
            INS = 0x82,  // Load Key command
            P1 = 0x00,   // Key Structure (0x00 for MIFARE)
            P2 = 0x00,   // Key Slot (0x00 if the key is loaded in slot 0)
            Data = keyA  // The actual key (6 bytes)
        };

        var loadKeyBuffer = loadKeyApdu.ToArray();
        var loadKeyResponse = new byte[258];
        int loadKeyReceivedLength = reader.Transmit(loadKeyBuffer, loadKeyResponse);

        // Check the last two bytes for SW1 and SW2 (status words)
        if (loadKeyReceivedLength < 2 || loadKeyResponse[loadKeyReceivedLength - 2] != 0x90 || loadKeyResponse[loadKeyReceivedLength - 1] != 0x00)
        {
            Console.WriteLine($"Failed to load key for Sector {sectorNumber}.");
            return false;
        }

        // Step 2: Authenticate the sector with the loaded key
        var authenticate = new CommandApdu(IsoCase.Case3Short, reader.Protocol)
        {
            CLA = 0xFF,
            INS = 0x86,  // INS for General Authenticate
            P1 = 0x00,   // Version number
            P2 = 0x00,   // Always 0
            Data = new byte[]
            {
            0x01,       // Version number
            0x00,       // MSB of block number (0x00 since it's usually < 256)
            (byte)blockNumber, // LSB of block number
            0x60,       // 0x60 indicates Key A
            0x00        // Key slot (0x00 if the key is loaded in slot 0)
            }
        };

        var sendBuffer = authenticate.ToArray();
        var receiveBuffer = new byte[258];
        int receivedLength = reader.Transmit(sendBuffer, receiveBuffer);

        // Check the last two bytes for SW1 and SW2 (status words)
        if (receivedLength >= 2)
        {
            byte sw1 = receiveBuffer[receivedLength - 2];
            byte sw2 = receiveBuffer[receivedLength - 1];
            return (sw1 == 0x90 && sw2 == 0x00);  // 0x9000 indicates success
        }
        else
        {
            Console.WriteLine("Authentication failed: Invalid response length.");
            return false;
        }
    }


    private static byte[] ReadBlock(ICardReader reader, int blockNumber)
    {
        var readCommand = new CommandApdu(IsoCase.Case2Short, reader.Protocol)
        {
            CLA = 0xFF,
            INS = 0xB0,  // INS for READ BINARY
            P1 = (byte)((blockNumber >> 8) & 0xFF),  // MSB of block number
            P2 = (byte)(blockNumber & 0xFF),  // LSB of block number
            Le = BlockSize  // Number of bytes to read (16 bytes)
        };

        var sendBuffer = readCommand.ToArray();
        var receiveBuffer = new byte[258];  // 16 bytes for the block data + 2 bytes for SW1 and SW2

        // Transmit the APDU command
        int receivedLength = reader.Transmit(sendBuffer, receiveBuffer);

        if (receivedLength >= 18)  // 16 bytes of data + 2 bytes for SW1 and SW2
        {
            byte sw1 = receiveBuffer[receivedLength - 2];  // Second-to-last byte is SW1
            byte sw2 = receiveBuffer[receivedLength - 1];  // Last byte is SW2

            if (sw1 == 0x90 && sw2 == 0x00)  // Check if status is 0x9000 (success)
            {
                // Extract the 16 bytes of block data (excluding SW1 and SW2)
                byte[] blockData = new byte[16];
                Array.Copy(receiveBuffer, blockData, 16);
                return blockData;
            }
            else
            {
                Console.WriteLine($"Failed to read Block {blockNumber}. SW1SW2: {sw1:X2}{sw2:X2}");
                return null;
            }
        }
        else
        {
            Console.WriteLine($"Failed to read Block {blockNumber}. Invalid response length.");
            return null;
        }
    }




    private static void DetermineCardType(byte[] atr)
    {
        string atrString = BitConverter.ToString(atr).Replace("-", "");
        if (atrString.StartsWith("3B8F8001"))
        {
            Console.WriteLine("Possible ISO 14443 Type A card, potentially MIFARE.");
        }
        else
        {
            Console.WriteLine("Unknown card type.");
        }
    }

    private static byte[] ReadATR(ICardReader reader)
    {
        try
        {
            byte[] atr = reader.GetAttrib(SCardAttribute.AtrString);
            Console.WriteLine("ATR: " + BitConverter.ToString(atr));
            return atr;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to read ATR: " + ex.Message);
            return null;
        }
    }

    private static byte[] ConvertNumericKeyToByteArray(string numericKey)
    {
        if (string.IsNullOrEmpty(numericKey))
        {
            throw new ArgumentException("The key cannot be null or empty.");
        }

        // Ensure the key is exactly 6 characters long by padding with zeros if necessary
        if (numericKey.Length < 6)
        {
            numericKey = numericKey.PadLeft(6, '0');
        }
        else if (numericKey.Length > 6)
        {
            numericKey = numericKey.Substring(0, 6);  // Truncate to 6 characters if too long
        }

        byte[] byteArray = new byte[6];  // MIFARE Classic keys are always 6 bytes long
        for (int i = 0; i < 6; i++)
        {
            if (!char.IsDigit(numericKey[i]))
            {
                throw new ArgumentException("The key must only contain numeric characters.");
            }
            byteArray[i] = (byte)(numericKey[i] - '0');  // Convert each char digit to its numeric byte value
        }

        return byteArray;
    }

}
