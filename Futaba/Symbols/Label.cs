namespace Futaba.Symbols;

/// <summary>
/// Represents a standard label declared inline.
/// </summary>
public sealed class Label : Symbol {
	/// <inheritdoc/>
	public override int DefaultValue => Address;

	/// <inheritdoc/>
	public override int Address { get; }

	/// <inheritdoc/>
	public override int RomAddress { get; }

	/// <inheritdoc/>
	public override int BinaryOffset { get; }

	internal Label(string name, int address, int romaddress, int offset) : base(name) {
		Address = address;
		RomAddress = romaddress;
		BinaryOffset = offset;
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
		}

		val = 0;
		return false;
	}
}