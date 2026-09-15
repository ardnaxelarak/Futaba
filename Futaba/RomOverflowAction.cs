namespace Futaba;

/// <summary>
/// Represents what action to take when assembly occurs beyond the ROM's current size.
/// </summary>
public enum RomOverflowAction {
	/// <summary>
	/// Out-of-bounds writes result in an assembly error.
	/// </summary>
	Error = 0,

	/// <summary>
	/// The binary data is expanded to the next legal size that contains the out-of-bounds offset written to.
	/// </summary>
	Expand,

	/// <summary>
	/// The binary data is resized to exactly fit the out-of-bounds offset written to.
	/// </summary>
	Grow
}
