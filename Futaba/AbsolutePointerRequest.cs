namespace Futaba;

internal class AbsolutePointerRequest(IExpressionReturn item, int offset, int provenance, uint time, SourceLine line) : INeedResolution {
	public IExpressionReturn Item => item;
	public int Offset => offset;
	public int Provenance => provenance;

	public int Size => 2;

	public uint Time => time;

	public virtual int Value => Item.ValueInt32;

	public SourceLine SourceLine => line;

	public bool TryToResolve() {
		return Item.TryToResolve();
	}
}

internal sealed class DirectBankPointerRequest(IExpressionReturn item, int offset, int provenance, uint time, SourceLine line)
	: AbsolutePointerRequest(item, offset, provenance, time, line) {
	public override int Value => Item.ValueInt32 & SnesHelpers.AbsoluteMask;
}
