using AssetRipper.IO.Files;
using System.Text;

namespace AssetRipper.CLI;

/// <summary>Stops export I/O cooperatively, without deleting files already written.</summary>
internal sealed class CancellableFileSystem : FileSystem
{
	private readonly FileSystem inner;
	private readonly CancellationToken cancellation;
	private int writeDelay;

	public CancellableFileSystem(FileSystem inner, CancellationToken cancellation)
	{
		this.inner = inner;
		this.cancellation = cancellation;
		File = new CancellableFile(this);
		Directory = new CancellableDirectory(this);
		if (!int.TryParse(Environment.GetEnvironmentVariable("ASSETRIPPER_TEST_EXPORT_WRITE_DELAY_MS"), out writeDelay)) writeDelay = 0;
	}

	public override FileImplementation File { get; }
	public override DirectoryImplementation Directory { get; }
	public override PathImplementation Path => inner.Path;
	public override string TemporaryDirectory { get => inner.TemporaryDirectory; set => inner.TemporaryDirectory = value; }

	private void AfterWrite()
	{
		int delay = Interlocked.Exchange(ref writeDelay, 0);
		if (delay > 0) cancellation.WaitHandle.WaitOne(delay);
		cancellation.ThrowIfCancellationRequested();
	}

	private sealed class CancellableFile(CancellableFileSystem owner) : FileImplementation(owner)
	{
		public override Stream Create(string path)
		{
			owner.cancellation.ThrowIfCancellationRequested();
			return new CancellableStream(owner.inner.File.Create(path), owner);
		}
		public override Stream OpenWrite(string path)
		{
			owner.cancellation.ThrowIfCancellationRequested();
			return new CancellableStream(owner.inner.File.OpenWrite(path), owner);
		}
		public override void Delete(string path) { owner.cancellation.ThrowIfCancellationRequested(); owner.inner.File.Delete(path); }
		public override bool Exists(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.File.Exists(path); }
		public override Stream OpenRead(string path) { owner.cancellation.ThrowIfCancellationRequested(); return new CancellableStream(owner.inner.File.OpenRead(path), owner); }
		public override byte[] ReadAllBytes(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.File.ReadAllBytes(path); }
		public override string ReadAllText(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.File.ReadAllText(path); }
		public override string ReadAllText(string path, Encoding encoding) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.File.ReadAllText(path, encoding); }
		public override void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
		{
			using Stream stream = Create(path);
			stream.Write(bytes);
		}
		public override void WriteAllText(string path, ReadOnlySpan<char> contents) => WriteAllText(path, contents, new UTF8Encoding(false));
		public override void WriteAllText(string path, ReadOnlySpan<char> contents, Encoding encoding)
		{
			using Stream stream = Create(path);
			using StreamWriter writer = new(stream, encoding);
			writer.Write(contents);
		}
	}

	private sealed class CancellableStream(Stream inner, CancellableFileSystem owner) : Stream
	{
		public override bool CanRead => inner.CanRead;
		public override bool CanSeek => inner.CanSeek;
		public override bool CanWrite => inner.CanWrite;
		public override long Length => inner.Length;
		public override long Position { get => inner.Position; set => inner.Position = value; }
		public override void Flush() => inner.Flush();
		public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
		public override void SetLength(long value) { owner.cancellation.ThrowIfCancellationRequested(); inner.SetLength(value); }
		public override int Read(byte[] buffer, int offset, int count) { owner.cancellation.ThrowIfCancellationRequested(); return inner.Read(buffer, offset, count); }
		public override int Read(Span<byte> buffer) { owner.cancellation.ThrowIfCancellationRequested(); return inner.Read(buffer); }
		public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
		public override void Write(ReadOnlySpan<byte> buffer)
		{
			owner.cancellation.ThrowIfCancellationRequested();
			inner.Write(buffer);
			owner.AfterWrite();
		}
		protected override void Dispose(bool disposing)
		{
			if (disposing) inner.Dispose();
			base.Dispose(disposing);
		}
	}

	private sealed class CancellableDirectory(CancellableFileSystem owner) : DirectoryImplementation(owner)
	{
		public override void Create(string path) { owner.cancellation.ThrowIfCancellationRequested(); owner.inner.Directory.Create(path); }
		public override void Delete(string path) { owner.cancellation.ThrowIfCancellationRequested(); owner.inner.Directory.Delete(path); }
		public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.EnumerateDirectories(path, searchPattern, searchOption); }
		public override IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.EnumerateFiles(path, searchPattern, searchOption); }
		public override string[] GetDirectories(string path, string searchPattern, SearchOption searchOption) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.GetDirectories(path, searchPattern, searchOption); }
		public override string[] GetFiles(string path, string searchPattern, SearchOption searchOption) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.GetFiles(path, searchPattern, searchOption); }
		public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.EnumerateDirectories(path, searchPattern); }
		public override IEnumerable<string> EnumerateFiles(string path, string searchPattern) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.EnumerateFiles(path, searchPattern); }
		public override string[] GetDirectories(string path, string searchPattern) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.GetDirectories(path, searchPattern); }
		public override string[] GetFiles(string path, string searchPattern) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.GetFiles(path, searchPattern); }
		public override IEnumerable<string> EnumerateDirectories(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.EnumerateDirectories(path); }
		public override IEnumerable<string> EnumerateFiles(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.EnumerateFiles(path); }
		public override string[] GetDirectories(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.GetDirectories(path); }
		public override string[] GetFiles(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.GetFiles(path); }
		public override bool Exists(string path) { owner.cancellation.ThrowIfCancellationRequested(); return owner.inner.Directory.Exists(path); }
	}
}
