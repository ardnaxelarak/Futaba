namespace Futaba;

/// <summary>
/// Base class that mapped assemblers inherit from.
/// </summary>
public unsafe abstract partial class Assembler : IDisposable {

	/// <inheritdoc cref="FutabaApp.Version"/>
	public static Version Version => FutabaApp.Version;


	// ROM writer constants
	internal const int MaxSize = 0x80_0000;

	// private fields
	private SnesArchitecture arch = SnesArchitecture.WDC65816;

	private SnesEncoder CurrentEncoder = SnesEncoder.ASCII;

	private SpannedLookup<SnesEncoder> Encoders = new(32);

	private readonly DirectoryInfo directory;

	private Dictionary<string, SourceFile> LoadedFiles = new(32);
	private Dictionary<string, SourceBinary> LoadedBinaries = new(64);
	private List<SourceObject> LoadedSources = new(128);

	private int NextAllocBlockStart = -1;
	private bool disposed;

	private SpannedLookup<MacroCall> MacrosTable = new(128);
	private SpannedLookup<UserFunction> UserFunctionsTable = new(128);


	// need to use private fields for these
	// so they can be removed during disposal without throwing an exception
	private TextWriter _messageOut = Console.Out;
	private TextWriter _errorOut = Console.Error;
	private byte[]? _baseRom = null;





	/// <summary>
	/// Gets the file that acts as the entry point of assembly.
	/// </summary>
	public FileInfo EntryPoint { get; }

	/// <summary>
	/// Gets or sets the base binary to patch over.
	/// If <see langword="null"/>, assembly begins with a zero-filled buffer.
	/// </summary>
	/// <inheritdoc cref="OverflowAction" path="//remarks|//exception"/>
	public byte[]? BaseRom {
		get => _baseRom;
		set {
			ThrowIfAssembling();
			_baseRom = value;
		}
	}

	private UInt128 RNGSeed = new((ulong) DateTime.Now.Ticks, (ulong) DateTime.UtcNow.Ticks);

	internal readonly XoRandom RNG = new(0);

	/// <summary>
	/// Seeds the internal RNG to allow deterministic output.
	/// </summary>
	/// <inheritdoc cref="ThrowIfAssembling" path="//remarks|//exception"/>
	public void SeedRNG(long seed1, long seed2) {
		ThrowIfAssembling();
		RNGSeed = new((ulong) seed1, (ulong) seed2);
	}

	/// <summary>
	/// Gets or sets what action to take when assembly occurs beyond the ROM's current size.
	/// </summary>
	/// <remarks>
	/// This property cannot be modified while assembly is occurring.
	/// Attempting to do so will throw an <see cref="InvalidOperationException"/>.
	/// </remarks>
	/// <inheritdoc cref="ThrowIfAssembling" path="/exception"/>
	public RomOverflowAction OverflowAction {
		get;
		set {
			ThrowIfAssembling();
			field = value;
		}
	} = RomOverflowAction.Error;


	/// <summary>
	/// Get or set the warning level for missing tokens.
	/// </summary>
	/// <inheritdoc cref="OverflowAction" path="//remarks|//exception"/>
	public MissingTokenSeverity MissingTokenSeverity {
		get;
		set {
			ThrowIfAssembling();
			field = value;
		}
	} = MissingTokenSeverity.Ambiguous;


	/// <summary>
	/// Returns whether or not any breaking errors occured during assembly.
	/// </summary>
	/// <remarks>
	/// If this returns <see langword="true"/>,
	/// then assembly is considered to have failed,
	/// and whatever output was produced is not reliable.
	/// </remarks>
	public bool HasErrors => ErrorCount > 0;

	/// <summary>
	/// Returns the number of errors encountered during assembly.
	/// </summary>
	public int ErrorCount { get; private set; } = 0;

	/// <summary>
	/// Gets or sets the maximum number of errors that can occur during assembly.
	/// If the number of errors exceeds this value, an <see cref="InvalidOperationException"/> will be thrown.
	/// </summary>
	/// <inheritdoc cref="OverflowAction" path="//remarks|//exception"/>
	/// <exception cref="ArgumentOutOfRangeException">If the passed value is less than 0.</exception>
	public int MaximumErrors {
		get;
		set {
			ThrowIfAssembling();
			ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
			field = value;
		}
	} = int.MaxValue;

	/// <summary>
	/// <para>
	/// If an exception is thrown during assembly,
	/// this will return information about the source line
	/// that was being handled at the time the exception was thrown.
	/// </para>
	/// <para>
	/// If no exception has occurred, this will return <see langword="null"/>.
	/// </para>
	/// </summary>
	/// <remarks>
	/// Note that the source line returned is not necessarily the cause of the exception.
	/// </remarks>
	public string? ErrorLine { get; private set; } = null;

	/// <summary>
	/// Gets or sets where print messages are printed.
	/// Defaults to <see cref="Console.Out"/>.
	/// </summary>
	/// <remarks>
	/// The assembler instance does not handle the disposal of this field.
	/// <para>
	/// <inheritdoc cref="OverflowAction" path="//remarks"/>
	/// </para>
	/// </remarks>
	/// <inheritdoc cref="OverflowAction" path="//exception"/>
	public TextWriter MessageOut {
		get => _messageOut;
		set {
			ThrowIfAssembling();
			_messageOut = value;
		}
	}


	/// <summary>
	/// Gets or sets where error and warning messages are printed.
	/// Defaults to <see cref="Console.Error"/>.
	/// </summary>
	/// <inheritdoc cref="MessageOut" path="//remarks|//exception"/>
	public TextWriter ErrorOut {
		get => _errorOut;
		set {
			ThrowIfAssembling();
			_errorOut = value;
		}
	}

	/// <summary>
	/// Creates a new assembler object using the given file as its entry point.
	/// </summary>
	private protected Assembler(FileInfo entry) {
		EntryPoint = entry;

		directory = entry.Directory ?? throw new DirectoryNotFoundException();

		RomBuffer = default;
		TimeBuffer = default;
		PcPointer = default;

		Offset = 0;

		InitialRomSize = MinRomSize;
	}

	/// <summary>
	/// Starts assembly beginning at this instance's designated entry point.
	/// </summary>
	/// <remarks>
	/// Assembly errors do not throw an exception or halt assembly.
	/// Verify assembly succeeded with <see cref="Assembler.HasErrors"/>,
	/// <para>
	/// <inheritdoc cref="ThrowIfAssembling()" path="/remarks"/>
	/// </para>
	/// </remarks>
	/// <returns>A new <see langword="byte"/> array containing the assembled machine code.</returns>
	/// <inheritdoc cref="ThrowIfAssembling()" path="/exception"/>
	public byte[] Assemble() {
		ThrowIfAssembling();

		try {
			TryAssembly();

			return [.. new ReadOnlySpan<byte>(RomBuffer, CurrentRomSize)];
		} catch {
			throw;
		} finally {
			DeallocRomBuffer();
		}
	}

	/// <summary>
	/// Starts assembly beginning at the designated entry point.
	/// Data is written directly to the file specified.
	/// </summary>
	/// <inheritdoc cref="Assemble()" path="//remarks|//exception"/>
	public void AssembleFile(string outputFilePath) {
		ThrowIfAssembling();

		try {
			TryAssembly();

			using var stream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

			stream.Position = 0;
			stream.SetLength(0);

			stream.Write(new ReadOnlySpan<byte>(RomBuffer, CurrentRomSize));
			stream.Flush(true);
		} catch {
			throw;
		} finally {
			DeallocRomBuffer();
		}
	}


	/// <inheritdoc cref="AssembleFile(string)"/>
	public void AssembleFile(FileInfo outputFile) {
		AssembleFile(outputFile.FullName);
	}

	/// <remarks>
	/// This action cannot be performed while assembly is occurring.
	/// Attempting to do so will throw an <see cref="InvalidOperationException"/>.
	/// </remarks>
	/// <exception cref="InvalidOperationException">If the assembler is busy with assembly.</exception>
	private void ThrowIfAssembling() {
		if (AmBusy()) {
			ThrowBusy();

			[DoesNotReturn]
			static void ThrowBusy() {
				throw new InvalidOperationException("This action cannot be performed while assembly is occurring.");
			}
		}
	}

	/// <summary>
	/// Just a wrapper so that it can handle disposal exceptions too.
	/// </summary>
	private bool AmBusy() {
		ObjectDisposedException.ThrowIf(disposed, this);
		return Busy;
	}




	/// <summary>
	/// Finalizer.
	/// </summary>
	~Assembler() {
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: false);
	}

	/// <inheritdoc/>
	public void Dispose() {
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <inheritdoc/>
	protected virtual void Dispose(bool disposing) {
		Busy = true;

		if (disposed) {
			return;
		}
		disposed = true;
		if (disposing) {
			// managed resources
			if (LoadedFiles is not null) {
				foreach (var (_, f) in LoadedFiles) {
					f?.Dispose();
				}
			}

			if (LoadedBinaries is not null) {
				foreach (var (_, f) in LoadedBinaries) {
					f?.Dispose();
				}
			}
		}

		// unmanaged resources
		DeallocRomBuffer();

		CurrentSourceObject = default!;

		LoadedFiles?.Clear();
		LoadedFiles = null!;

		LoadedBinaries?.Clear();
		LoadedBinaries = null!;

		SymbolsTable.Clear();
		SymbolsTable = default;

		InitialVariables.Clear();
		InitialVariables = default!;

		VariablesTable.Clear();
		VariablesTable = default;

		LoadedSources.Clear();
		LoadedSources = default!;

		MacrosTable.Clear();
		MacrosTable = default!;

		UserFunctionsTable.Clear();
		UserFunctionsTable = default;

		Encoders.Clear();
		Encoders = default;
		CurrentEncoder = default!;

		previousState = default;
		ifstack = default;

		SimpleRequests.Clear();
		SimpleRequests = default!;

		FillRequests.Clear();
		FillRequests = default!;

		JumpRequests.Clear();
		JumpRequests = default!;

		BranchRequests.Clear();
		BranchRequests = default!;

		Segments.Clear();
		Segments = default!;

		_messageOut = default!;
		_errorOut = default!;
		_baseRom = null;
	}

}
