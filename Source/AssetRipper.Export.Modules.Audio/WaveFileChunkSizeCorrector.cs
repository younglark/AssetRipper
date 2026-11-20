namespace AssetRipper.Export.Modules.Audio;

/// <summary>
/// Fills the zero RIFF and data sizes left by Fmod5Sharp 3.1.0's IMA-ADPCM rebuilder.
/// </summary>
/// <remarks>
/// Official Fmod5Sharp 3.1.0 (no later package is published; GitHub issue/PR #21 is open)
/// calls <c>MemoryStream.ToArray()</c> before NAudio's <c>WaveFileWriter</c> flushes those
/// fields. This is not a general WAV repairer. It only accepts the exact layout that
/// <c>FmodImaAdPcmRebuilder.Rebuild</c> produces with NAudio.Core 3.1.0
/// (<c>WaveFormat.CreateCustomFormat</c>, <c>extraSize = 0</c>: canonical 16-byte PCM
/// <c>fmt</c>, then a <c>data</c> chunk, no other chunks) and only when both size fields
/// are 0. PCM bytes are never rewritten. Odd-length payloads are rejected because this
/// path emits 16-bit PCM whose total length must be a multiple of <c>blockAlign</c>.
/// </remarks>
internal static class WaveFileChunkSizeCorrector
{
	private const int CanonicalHeaderLength = 44;
	private const int RiffSizeOffset = 4;
	private const int FmtLengthOffset = 16;
	private const int FormatTagOffset = 20;
	private const int ChannelsOffset = 22;
	private const int SampleRateOffset = 24;
	private const int ByteRateOffset = 28;
	private const int BlockAlignOffset = 32;
	private const int BitsPerSampleOffset = 34;
	private const int DataSizeOffset = 40;
	private const int PcmPayloadOffset = CanonicalHeaderLength;
	private const uint CanonicalPcmFmtLength = 16;
	private const ushort PcmFormatTag = 1;
	private const ushort ImaPcmBitsPerSample = 16;
	private const ushort ImaBytesPerSample = 2;

	/// <summary>
	/// Attempts to fill both zero-valued RIFF and data size fields in place.
	/// </summary>
	/// <returns><see langword="true"/> if the size fields were written.</returns>
	public static bool TryFillZeroChunkSizes(byte[]? file)
	{
		if (file is null || file.Length < CanonicalHeaderLength)
		{
			return false;
		}

		if (!IsFourCc(file, 0, "RIFF"u8)
			|| !IsFourCc(file, 8, "WAVE"u8)
			|| !IsFourCc(file, 12, "fmt "u8)
			|| !IsFourCc(file, 36, "data"u8))
		{
			return false;
		}

		uint declaredRiffSize = ReadUInt32LittleEndian(file, RiffSizeOffset);
		uint declaredFmtLength = ReadUInt32LittleEndian(file, FmtLengthOffset);
		uint declaredDataSize = ReadUInt32LittleEndian(file, DataSizeOffset);
		// The known defect leaves both placeholders at 0. A lone zero, RF64's
		// 0xFFFFFFFF, a valid empty data chunk with a trailing LIST, or an 18-byte fmt
		// is not that defect and is left unchanged.
		if (declaredRiffSize != 0 || declaredDataSize != 0 || declaredFmtLength != CanonicalPcmFmtLength)
		{
			return false;
		}

		ushort formatTag = ReadUInt16LittleEndian(file, FormatTagOffset);
		ushort channels = ReadUInt16LittleEndian(file, ChannelsOffset);
		uint sampleRate = ReadUInt32LittleEndian(file, SampleRateOffset);
		uint declaredByteRate = ReadUInt32LittleEndian(file, ByteRateOffset);
		ushort blockAlign = ReadUInt16LittleEndian(file, BlockAlignOffset);
		ushort bitsPerSample = ReadUInt16LittleEndian(file, BitsPerSampleOffset);

		// FmodImaAdPcmRebuilder writes 16-bit PCM with one or two channels.
		if (formatTag != PcmFormatTag
			|| bitsPerSample != ImaPcmBitsPerSample
			|| channels is not (1 or 2)
			|| sampleRate == 0)
		{
			return false;
		}

		int expectedBlockAlign = channels * ImaBytesPerSample;
		if (blockAlign != expectedBlockAlign)
		{
			return false;
		}

		long expectedByteRate = (long)sampleRate * blockAlign;
		if (expectedByteRate > uint.MaxValue || declaredByteRate != (uint)expectedByteRate)
		{
			return false;
		}

		int pcmLength = file.Length - PcmPayloadOffset;
		if (pcmLength <= 0 || pcmLength % blockAlign != 0)
		{
			return false;
		}

		long riffSize = file.Length - 8;
		if (riffSize > uint.MaxValue)
		{
			return false;
		}

		WriteUInt32LittleEndian(file, RiffSizeOffset, (uint)riffSize);
		WriteUInt32LittleEndian(file, DataSizeOffset, (uint)pcmLength);
		return true;
	}

	private static bool IsFourCc(byte[] file, int offset, ReadOnlySpan<byte> fourCc)
	{
		return fourCc.Length == 4 && file.AsSpan(offset, 4).SequenceEqual(fourCc);
	}

	private static ushort ReadUInt16LittleEndian(byte[] file, int offset)
	{
		return (ushort)(file[offset] | (file[offset + 1] << 8));
	}

	private static uint ReadUInt32LittleEndian(byte[] file, int offset)
	{
		return (uint)(file[offset]
			| (file[offset + 1] << 8)
			| (file[offset + 2] << 16)
			| (file[offset + 3] << 24));
	}

	private static void WriteUInt32LittleEndian(byte[] file, int offset, uint value)
	{
		// The project compiles with CheckForOverflowUnderflow. Truncating a uint into
		// little-endian bytes is intentional and must not throw for values > 255.
		unchecked
		{
			file[offset] = (byte)value;
			file[offset + 1] = (byte)(value >> 8);
			file[offset + 2] = (byte)(value >> 16);
			file[offset + 3] = (byte)(value >> 24);
		}
	}
}
