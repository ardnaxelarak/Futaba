namespace Futaba;

internal unsafe class SourceBinary : IWatchedFile {
	public string FileName { get; }

	public bool WasUsed { get; set; } = false;

	public DateTime LastLoaded { get; private set; } = default;
	public string? Error { get; private set; } = null;

	private byte* AllocBlock;

	public int Length { get; private set; }

	public SourceBinary(string path) {
		FileName = Path.GetFullPath(path);
		Refresh();
	}


	public void Refresh() {
		FileInfo check = new(FileName);

		if (check.Exists) {
			if (LastLoaded != check.LastWriteTimeUtc) {
				LastLoaded = check.LastWriteTimeUtc;

				try {
					//Console.WriteLine($"Loading {FullName}");

					using var ftext = check.OpenRead();

					int length = (int) ftext.Length;

					if (AllocBlock == default) {
						AllocBlock = NativeMemory.AllocNice<byte>(length);
						Length = length;

					} else if (Length != length) {
						AllocBlock = NativeMemory.ReAllocNice(AllocBlock, length);
						Length = length;
					}

					ftext.ReadExactly(new Span<byte>(AllocBlock, length));

					Error = null;
					return;
				} catch (Exception e) {
					Error = e.Message;
				}
			} else {
				Error = null;
				//Console.WriteLine($"{FullName} is unchanged.");
				return;
			}
		} else {
			Error = $"File {FileName} does not exist.";
		}

		NativeMemory.AlignedFree(AllocBlock);
		AllocBlock = null;
		Length = 0;
	}


	public bool TryGetSpan(int start, int length, out Span<byte> span) {
		if (disposed || (uint) length > Length || (uint) start >= Length || (start + length) > Length || AllocBlock == default) {
			span = default;
			return false;
		}

		span = new(AllocBlock + start, length);

		return true;
	}



	protected bool disposed = false;

	/// <inheritdoc cref="IDisposable.Dispose()"/>
	public void Dispose() {
		if (disposed) return;

		disposed = true;

		NativeMemory.AlignedFree(AllocBlock);

		GC.SuppressFinalize(this);
	}
}
