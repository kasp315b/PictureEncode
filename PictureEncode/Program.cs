using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace PictureEncode
{
    public class Program
    {
        public const byte WHITESPACE = 0x20;

        public static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);


        [Argument(0, Description = "File to read from", Name = "Input File")]
        [Required]
        public string Input { get; }

        [Argument(1, Description = "File to write to", Name = "Output File")]
        [Required]
        public string Output { get; }

        [Option(Description = "Encode or decode mode", ShortName = "m", LongName = "mode", ValueName = "encode|decode")]
        [Required]
        public (bool HasValue, ActionMode Mode) AMode { get; }

        [Option(Description = "Double or Single Byte color mode", ShortName = "c", LongName = "color", ValueName = "single|double")]
        public (bool HasValue, ColorMode Mode) CMode { get; }

        private void OnExecute()
        {
            if(AMode.Mode != ActionMode.Encode && AMode.Mode != ActionMode.Decode)
            {
                Console.Error.WriteLine("An invalid mode option was specified.");
                return;
            }

            if(CMode.Mode != ColorMode.Single && CMode.Mode != ColorMode.Double)
            {
                Console.Error.WriteLine("An invalid color mode was specified");
                return;
            }

            if (AMode.Mode == ActionMode.Encode)
            {
                try
                {
                    using (FileStream inFile = new FileStream(Input, FileMode.Open, FileAccess.Read))
                    using (FileStream outFile = new FileStream(Output, FileMode.CreateNew, FileAccess.ReadWrite))
                    using (BinaryReader reader = new BinaryReader(inFile))
                    using (BinaryWriter writer = new BinaryWriter(outFile))
                    {
                        ColorModeAttribute colorModeAttribute = ColorModeAttribute.ColorModeAttributeLookup(CMode.Mode);

                        long inputFileSize = inFile.Length;
                        double pixelCount = (double)inputFileSize / colorModeAttribute.Divisor;
                        double imageSideLength = Math.Sqrt(pixelCount);
                        int integerSideLength = (int)Math.Ceiling(imageSideLength);
                        int actualPixelCount = integerSideLength * integerSideLength;
                        byte[] sideLengthAsBytes = Encoding.ASCII.GetBytes(integerSideLength.ToString());

                        List<byte> header = new List<byte>();
                        header.Add(0x50); header.Add(0x36);                  // Magic Image Number
                        header.Add(WHITESPACE);                              // Whitespace Newline
                        foreach (byte b in sideLengthAsBytes) header.Add(b); // Add Side length as decimal ASCII
                        header.Add(WHITESPACE);                              // Whitespace Newline
                        foreach (byte b in sideLengthAsBytes) header.Add(b); // Add Side length as decimal ASCII
                        header.Add(WHITESPACE);                              // Whitespace Newline

                        // Add Color MaxValue
                        foreach (byte b in Encoding.ASCII.GetBytes(colorModeAttribute.MaxValue.ToString())) header.Add(b);
                        
                        header.Add(WHITESPACE); // Whitespace Newline


                        // Write header to file
                        writer.Write(header.ToArray<byte>());

                        int bytesWritten = 0;
                        while (reader.BaseStream.Position != reader.BaseStream.Length)
                        {
                            bytesWritten++;
                            writer.Write(reader.ReadByte());
                        }

                        byte[] selection = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
                        int selector = 0;
                        for(int index = bytesWritten; index < actualPixelCount* colorModeAttribute.Divisor; index++)
                        {
                            writer.Write(selection[selector++ % selection.Length]);
                        }

                        // Add metadata
                        List<byte> meta = new List<byte>();
                        meta.Add(WHITESPACE);

                        string metaString = "#" + inputFileSize;
                        foreach (byte b in Encoding.ASCII.GetBytes(metaString)) meta.Add(b);
                        meta.Add(WHITESPACE);

                        writer.Write(meta.ToArray<byte>());

                        reader.Close();
                        inFile.Close();
                        writer.Close();
                        outFile.Close();
                    }
                }
                catch (IOException e)
                {
                    Console.Error.WriteLine(e.ToString());
                    return;
                }
            } else if(AMode.Mode == ActionMode.Decode)
            {
                try
                {
                    if(File.Exists(Output))
                    {
                        File.Delete(Output);
                    }

                    using (FileStream inFile = new FileStream(Input, FileMode.Open, FileAccess.Read))
                    using (FileStream outFile = new FileStream(Output, FileMode.CreateNew, FileAccess.ReadWrite))
                    using (BinaryReader reader = new BinaryReader(inFile))
                    using (BinaryWriter writer = new BinaryWriter(outFile))
                    {
                        int filesize = -1;
                        {
                            byte commentCharacter = Encoding.ASCII.GetBytes("#")[0];

                            List<byte> trail = new List<byte>();
                            int readPosition = (int)inFile.Length - 1;
                            byte[] readBuffer = new byte[1];

                            do
                            {
                                inFile.Seek(readPosition--, SeekOrigin.Begin);
                                int bytesRead = inFile.Read(readBuffer, 0, 1);

                                if (bytesRead > 0)
                                {
                                    byte byteRead = readBuffer[0];

                                    if (byteRead == WHITESPACE) continue;
                                    if (byteRead == commentCharacter) break;

                                    trail.Add(readBuffer[0]);
                                }
                            } while (readBuffer[0] != commentCharacter);

                            inFile.Seek(0, SeekOrigin.Begin);
                            trail.Reverse();

                            try
                            {
                                filesize = int.Parse(Encoding.ASCII.GetString(trail.ToArray<byte>()));
                            }
                            catch(Exception e)
                            {
                                // Do nothing
                            }
                        }

                        if(filesize == -1)
                        {
                            Console.Error.WriteLine("Could not read original file size from metadata");
                            return;
                        }


                        int whitespaceCount = 0;
                        int bytesWritten = 0;
                        while (reader.BaseStream.Position != reader.BaseStream.Length && bytesWritten != filesize)
                        {
                            int read = reader.ReadByte();
                            if(whitespaceCount == 4)
                            {
                                bytesWritten++;
                                writer.Write((byte)read);
                            }
                            else
                            {
                                if(read == WHITESPACE)
                                {
                                    whitespaceCount++;
                                }
                            }
                        }


                        reader.Close();
                        inFile.Close();
                        writer.Close();
                        outFile.Close();
                    }
                } 
                catch(IOException e)
                {
                    Console.Error.WriteLine(e.ToString());
                    return;
                }
            }
        }
    }

    public enum ActionMode : uint
    {
        Encode = 0,
        Decode = 1
    }

    public enum ColorMode : uint
    {
        Single = 0,
        Double = 1
    }

    public class ColorModeAttribute
    {
        public static ColorModeAttribute ColorModeAttributeLookup(ColorMode mode)
        {
            switch(mode)
            {
                case ColorMode.Single:
                    return new ColorModeAttribute(3, 255);
                case ColorMode.Double:
                    return new ColorModeAttribute(6, 65535);
            }

            return null;
        }

        public int Divisor { get; private set; }
        public int MaxValue { get; private set; }

        public ColorModeAttribute(int divisor, int maxValue)
        {
            Divisor = divisor;
            MaxValue = maxValue;
        }
    }
}
