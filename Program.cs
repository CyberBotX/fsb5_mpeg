using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace fsb5_mpeg
{
	static class Program
	{
		class FSB5Header
		{
			public byte[] id { get; set; }
			public int version { get; set; }
			public int numSamples { get; set; }
			public int shdrSize { get; set; }
			public int nameSize { get; set; }
			public int dataSize { get; set; }
			public uint mode { get; set; }
			public byte[] extra { get; set; }
			public byte[] zero { get; set; }
			public byte[] hash { get; set; }
			public byte[] dummy { get; set; }
		}

		static Func<BinaryReader, uint> fr32;

		static uint fri32(BinaryReader br)
		{
			return br.ReadUInt32();
		}

		static uint frb32(BinaryReader br)
		{
			return BitConverter.ToUInt32(br.ReadBytes(4).Reverse().ToArray(), 0);
		}

		static int CheckSignEndian(BinaryReader br)
		{
			br.BaseStream.Position = 0;
			var sign = br.ReadBytes(4);
			br.BaseStream.Position = 0;

			if (sign[0] == 'F' && sign[1] == 'S' && sign[2] == 'B')
			{
				fr32 = fri32;
				return sign[3];
			}
			else if (sign[1] == 'B' && sign[2] == 'S' && sign[3] == 'F')
			{
				fr32 = frb32;
				return sign[0];
			}
			else
				return -1;
		}

		static int ReadFSB5Header(BinaryReader br, FSB5Header fsb5Header)
		{
			long oldPosition = br.BaseStream.Position;

			fsb5Header.id = br.ReadBytes(4);
			fsb5Header.version = (int)fr32(br);
			fsb5Header.numSamples = (int)fr32(br);
			fsb5Header.shdrSize = (int)fr32(br);
			fsb5Header.nameSize = (int)fr32(br);
			fsb5Header.dataSize = (int)fr32(br);
			fsb5Header.mode = fr32(br);
			if (fsb5Header.version == 0)
				fsb5Header.extra = br.ReadBytes(4);
			else
				fsb5Header.extra = null;
			fsb5Header.zero = br.ReadBytes(8);
			fsb5Header.hash = br.ReadBytes(16);
			fsb5Header.dummy = br.ReadBytes(8);

			return (int)(br.BaseStream.Position - oldPosition);
		}

		static Func<uint, uint> GET_FSB5_OFFSET = X => (X >> 7) * 0x20;

		static byte PeekByte(this BinaryReader br, long skip = 0)
		{
			br.BaseStream.Position += skip;
			byte b = br.ReadByte();
			br.BaseStream.Position -= skip + 1;
			return b;
		}

		enum mpg123_version
		{
			MPG123_1_0 = 0, /**< MPEG Version 1.0 */
			MPG123_2_0, /**< MPEG Version 2.0 */
			MPG123_2_5 /**< MPEG Version 2.5 */
		};

		static readonly int[][] v1_bitrates =
		{
			new[] { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, -1 }, /* Layer I */
			new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, -1 }, /* Layer II */
			new[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, -1 } /* Layer III */
		};
		static readonly int[][] v2_bitrates =
		{
			new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, -1 }, /* Layer I */
			new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, -1 } /* Layer II & Layer III */
		};

		static int getMPEGBitrate(mpg123_version mpegVersion, int layer, int bitrateIndex)
		{
			if (mpegVersion >= mpg123_version.MPG123_2_0 && layer == 3)
				layer = 2;
			--layer;
			if (mpegVersion == mpg123_version.MPG123_1_0)
				return v1_bitrates[layer][bitrateIndex];
			else
				return v2_bitrates[layer][bitrateIndex];
		}

		static readonly int[][] sampleRates =
		{
			new[] { 44100, 48000, 32000, -1 }, /* Version 1 */
			new[] { 22050, 24000, 16000, -1 }, /* Version 2 */
			new[] { 11025, 12000, 8000, -1 } /* Version 2.5 */
		};

		static int getMPEGSampleRate(int mpegVersion, int sampleRateIndex)
		{
			return sampleRates[mpegVersion][sampleRateIndex];
		}

		static int getMPEGFrameRateInBytes(int layer, int bitrate, int sampleRate, int padding)
		{
			if (layer == 1)
				return (12 * bitrate * 1000 / sampleRate + padding) * 4;
			else
				return 144 * bitrate * 1000 / sampleRate + padding;
		}

		static int GetNextMultipleOf4(int origNum)
		{
			int remainder = origNum % 4;
			if (remainder == 0)
				return origNum;
			return origNum + 4 - remainder;
		}

		static void Main(string[] args)
		{
			if (args.Length < 1)
			{
				Console.WriteLine("Syntax: {0} <fsb file> [<output file>]", typeof(Program).Assembly.Location);
				Console.WriteLine("NOTE: If output file is not given, it will overwrite the given FSB file.");
				return;
			}

			string outputFile = args[args.Length == 2 ? 1 : 0];

			var fsb5Header = new FSB5Header();
			var mpegData = new List<byte>();
			byte[] shdrData;
			string name = "";
			using (var br = new BinaryReader(File.OpenRead(args[0])))
			{
				int sign = CheckSignEndian(br);
				if (sign < 0)
				{
					Console.WriteLine("Invalid file detected.");
					return;
				}
				if (sign != '5')
				{
					Console.WriteLine("This tool is designed for FSB5 files only.");
					return;
				}

				int fhSize = ReadFSB5Header(br, fsb5Header);

				if (fsb5Header.numSamples != 1)
				{
					Console.WriteLine("ERROR: Currently this tool only supports FSB5 files that contain a single file within them.");
					return;
				}
				if (fsb5Header.mode != 0x0B)
				{
					Console.WriteLine("ERROR: This tool is meant to process MP3-based FSB5s only.");
					return;
				}

				uint nameOffset = (uint)(fhSize + fsb5Header.shdrSize);
				uint fileOffset = (uint)(nameOffset + fsb5Header.nameSize);
				uint baseOffset = fileOffset;

				uint offset = fr32(br);

				uint type = offset & 0x7F;
				shdrData = BitConverter.GetBytes(type);
				shdrData = shdrData.Concat(br.ReadBytes(4)).ToArray();
				offset = GET_FSB5_OFFSET(offset); // This is the offset into the file section

				long currOffset;
				while ((type & 1) == 1)
				{
					uint t32 = fr32(br);
					shdrData = shdrData.Concat(BitConverter.GetBytes(t32)).ToArray();
					type = t32 & 1;
					int len = (int)((t32 & 0xFFFFFF) >> 1);
					t32 >>= 24;
					currOffset = br.BaseStream.Position;
					shdrData = shdrData.Concat(br.ReadBytes(len)).ToArray();
					currOffset += len;
					br.BaseStream.Position = currOffset;
				}

				currOffset = br.BaseStream.Position;
				uint size;
				if (br.BaseStream.Position < nameOffset)
				{
					size = fr32(br);
					if (size == 0)
						size = (uint)br.BaseStream.Length;
					else
						size = GET_FSB5_OFFSET(size) + baseOffset;
				}
				else
					size = (uint)br.BaseStream.Length;
				br.BaseStream.Position = currOffset;
				fileOffset = baseOffset + offset;
				size -= fileOffset;

				if (fsb5Header.nameSize != 0)
				{
					currOffset = br.BaseStream.Position;
					br.BaseStream.Position = nameOffset/* + i * 4*/;
					br.BaseStream.Position = nameOffset + fr32(br);
					do
					{
						byte c = br.ReadByte();
						if (c == 0)
							break;
						name += (char)c;
					} while (true);
					br.BaseStream.Position = currOffset;
				}

				br.BaseStream.Position = currOffset = fileOffset;

				// Get MPEG data and remove padding
				long endOffset = fileOffset + size;
				while (currOffset < endOffset)
				{
					var header = br.ReadBytes(4);

					var mpegVersion = (mpg123_version)(3 - ((header[1] >> 3) & 0x03));
					int layer = 4 - ((header[1] >> 1) & 0x03);
					int bitrateIndex = (header[2] >> 4) & 0x0F;
					int sampleRateIndex = (header[2] >> 2) & 0x03;
					int padding = (header[2] >> 1) & 0x01;
					int bitrate = getMPEGBitrate(mpegVersion, layer, bitrateIndex);
					int sampleRate = getMPEGSampleRate((int)mpegVersion, sampleRateIndex);
					int frameLengthInBytes = getMPEGFrameRateInBytes(layer, bitrate, sampleRate, padding);

					mpegData.AddRange(header);
					mpegData.AddRange(br.ReadBytes(frameLengthInBytes - 4));

					// Check if the next 2 bytes would be an MPEG header, if not, skip to the next 4-byte offset as well as skip any other nul bytes we encounter after that
					if (br.BaseStream.Position < endOffset && br.PeekByte() != 0xFF && (br.PeekByte(1) & 0xF0) != 0xF0)
					{
						br.BaseStream.Position += GetNextMultipleOf4(frameLengthInBytes) - frameLengthInBytes;
						if (br.BaseStream.Position < endOffset && br.PeekByte() == 0)
						{
							while (br.BaseStream.Position < endOffset && br.ReadByte() == 0)
								;
							if (br.BaseStream.Position != endOffset)
								--br.BaseStream.Position;
						}
					}
					currOffset = br.BaseStream.Position;
				}
			}

			using (var bw = new BinaryWriter(File.Create(outputFile)))
			{
				bw.Write(Encoding.ASCII.GetBytes("FSB5"));
				bw.Write(fsb5Header.version);
				bw.Write(1);
				bw.Write(shdrData.Length);
				int fullNameSize = (int)Math.Ceiling((name.Length + 5) / 16.0) * 16;
				bw.Write(fullNameSize);
				bw.Write(mpegData.Count);
				bw.Write(fsb5Header.mode);
				if (fsb5Header.version == 0)
					bw.Write(fsb5Header.extra);
				bw.Write(fsb5Header.zero);
				bw.Write(fsb5Header.hash);
				bw.Write(fsb5Header.dummy);
				bw.Write(shdrData);
				bw.Write(4);
				bw.Write(Encoding.ASCII.GetBytes(name));
				bw.Write((byte)0);
				for (int j = name.Length + 5; j < fullNameSize; ++j)
					bw.Write((byte)0);
				bw.Write(mpegData.ToArray());
			}

			if (args.Length == 1)
				Console.WriteLine("Overwrote {0} with un-padded MPEG FSB5.", outputFile);
			else
				Console.WriteLine("Wrote un-padded MPEG FSB5 to {0}.", outputFile);
		}
	}
}
