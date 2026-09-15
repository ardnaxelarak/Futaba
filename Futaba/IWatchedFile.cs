namespace Futaba;

internal interface IWatchedFile : IDisposable {
	/// <summary>
	/// The name of this file.
	/// </summary>
	public string FileName { get; }

	public bool WasUsed { get; set; }

	public DateTime LastLoaded { get; }

	public string? Error { get; }

	public abstract void Refresh();
}
