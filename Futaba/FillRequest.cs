namespace Futaba;

internal sealed class FillRequest(IExpressionReturn item, int offset, int blocksize, int wordsize, uint time, SourceLine sourceLine) : INeedResolution {
	public int Offset => offset;

	public int Size => blocksize;

	public int Value => Item.ValueInt32;

	public int WordSize => wordsize;

	public IExpressionReturn Item => item;

	public uint Time => time;

	public SourceLine SourceLine => sourceLine;

	public bool TryToResolve() {
		return Item.TryToResolve();
	}
}
