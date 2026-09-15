namespace Futaba.Symbols;

[DebuggerDisplay("{ToString()}")]
internal sealed class SymbolPlaceholder : Symbol {
	internal Symbol? Desiree { get; set; } = null;

	public override int DefaultValue => Desiree?.DefaultValue ?? 0;
	public override int Address => Desiree?.Address ?? 0;
	public override int RomAddress => Desiree?.RomAddress ?? 0;
	public override int BinaryOffset => Desiree?.BinaryOffset ?? -1;

	internal override bool Resolved => Desiree?.Resolved ?? false;

	internal SymbolPlaceholder(string name) : base(name) { }

	internal override bool TryGetProperty(SymbolContext prop, out int val) {
		if (Desiree is null) {
			val = 0;
			return false;
		}

		return Desiree.TryGetProperty(prop, out val);
	}

	public override string ToString() {
		if (Desiree is null) {
			return $"\"{Name}\" UNRESOLVED";
		} else {
			return Desiree.ToString();
		}
	}
}