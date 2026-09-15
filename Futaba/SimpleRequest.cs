namespace Futaba;

internal sealed class SimpleRequest(IExpressionReturn item, int offset, int size, uint time, SourceLine line) : INeedResolution {
	public IExpressionReturn Item => item;

	public int Offset => offset;

	public int Size => size;

	public uint Time => time;

	public int Value => Item.ValueInt32;

	public SourceLine SourceLine => line;

	public bool TryToResolve() {
		return Item.TryToResolve();
	}
}
