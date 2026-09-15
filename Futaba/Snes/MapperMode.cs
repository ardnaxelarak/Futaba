namespace Futaba.Snes;

/// <summary>
/// Constants that represent a specific SNES mapper mode.
/// </summary>
public enum MapperMode {
	/// <summary>
	/// No map mode
	/// </summary>
	None,

	/// <summary>
	/// Lorom mapper mode
	/// </summary>
	Lorom,

	/// <summary>
	/// Hirom mapper mode
	/// </summary>
	Hirom,

	/// <summary>
	/// Ex-lorom mapper mode
	/// </summary>
	ExLorom,

	/// <summary>
	/// Ex-hirom mapper mode
	/// </summary>
	ExHirom,

	/// <summary>
	/// SA-1 mapper mode
	/// </summary>
	Sa1,

	/// <summary>
	/// Represents an invalid mapper type
	/// </summary>
	Invalid = -1,
}