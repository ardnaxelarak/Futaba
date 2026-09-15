namespace Futaba;

internal class ExpressionTree : IExpressionReturn {
	private readonly ExpressionItem root;

	public decimal Value { get; private set; }

	public ExpressionTree(ExpressionItem tree) {
		root = tree;
		TryToResolve();
	}

	public bool Resolved { get; private set; }

	public bool NeedsRequest { get; private set; }

	public ExpressionState ReturnState => root.State;

	public bool TryToResolve() {
		var (value, state) = root.TryEvaluate();

		Value = value;

		NeedsRequest = state.WarrantsRequest;

		return Resolved = state.IsResolved;
	}
}