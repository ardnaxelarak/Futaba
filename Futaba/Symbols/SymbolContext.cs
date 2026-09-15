namespace Futaba.Symbols;

internal enum SymbolContext {
	Default = 0,
	Invalid,

	BinaryOffset,
	RomAddress,
	Provenance,

	End,
	Size,
	Capacity,

	Distance,
}

internal static class SymbolContexts {
	private const int MaxPropLength = 10;

	public static SymbolContext GetContext(CharSpan name) {
		if (name.Length < MaxPropLength) {
			Span<char> tspan = stackalloc char[name.Length];
			name.FastLower(tspan);

			return tspan switch {
				"default" => /*              */ SymbolContext.Default,
				"offset" => /*               */ SymbolContext.BinaryOffset,
				"rom" => /*                  */ SymbolContext.RomAddress,
				"site" => /*                 */ SymbolContext.Provenance,
				"address" => /*              */ SymbolContext.Provenance,
				"end" => /*                  */ SymbolContext.End,
				"size" => /*                 */ SymbolContext.Size,
				"max" => /*                  */ SymbolContext.Capacity,
				"dist" => /*                 */ SymbolContext.Distance,
				_ => /*                      */ SymbolContext.Invalid
			};
		}

		return SymbolContext.Invalid;
	}
}