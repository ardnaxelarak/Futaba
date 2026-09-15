namespace Futaba;

internal abstract class ExpressionItem {
	public virtual ExpressionItem Clone() => this;
	public abstract ExpressionState State { get; }
	public abstract (decimal, ExpressionState) TryEvaluate();
}