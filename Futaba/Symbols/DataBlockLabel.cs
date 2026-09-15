namespace Futaba.Symbols;

/// <summary>
/// Represents a label declared with data.
/// </summary>
public sealed class DataBlockLabel : Symbol {
	/// <inheritdoc/>
	public override int DefaultValue => Address;

	/// <inheritdoc/>
	public override int Address { get; }

	/// <inheritdoc/>
	public override int RomAddress { get; }

	/// <inheritdoc/>
	public override int BinaryOffset { get; }

	/// <summary>
	/// Returns the size of the data this symbol marks.
	/// </summary>
	public int Size { get; }

	internal DataBlockLabel(string name, int address, int romaddress, int offset, int size) : base(name) {
		Address = address;
		RomAddress = romaddress;
		BinaryOffset = offset;
		Size = size;
	}

	internal override bool TryGetProperty(SymbolContext prop, out int val) {
		switch (prop) {
			case SymbolContext.Default:
			case SymbolContext.Provenance:
				val = Address;
				return true;

			case SymbolContext.RomAddress:
				val = RomAddress;
				return true;

			case SymbolContext.BinaryOffset:
				val = BinaryOffset;
				return true;

			case SymbolContext.Size:
				val = Size;
				return true;
		}

		val = 0;
		return false;
	}

}