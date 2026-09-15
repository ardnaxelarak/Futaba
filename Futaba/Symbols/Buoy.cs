namespace Futaba.Symbols;

/// <summary>
/// Represents a label whose default value is a relative offset from a label earlier in its hierarchy.
/// </summary>
public sealed class Buoy : Symbol {
	/// <inheritdoc/>
	public override int DefaultValue => Address;

	/// <inheritdoc/>
	public override int Address { get; }

	/// <inheritdoc/>
	public override int RomAddress { get; }

	/// <inheritdoc/>
	public override int BinaryOffset { get; }

	/// <summary>
	/// Returns the distance of this label from its anchored parent.
	/// </summary>
	public int BuoyOffset { get; }

	internal Buoy(string name, int address, int romaddress, int binoffset, int displacement) : base(name) {
		Address = address;
		RomAddress = romaddress;
		BinaryOffset = binoffset;
		BuoyOffset = displacement;
	}

	internal override bool TryGetProperty(SymbolContext prop, out int val) {
		switch (prop) {
			case SymbolContext.Default:
			case SymbolContext.Distance:
				val = BuoyOffset;
				return true;

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