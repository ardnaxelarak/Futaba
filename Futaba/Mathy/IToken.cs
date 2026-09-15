namespace Futaba;

internal interface IToken {
	public TokenType Type { get; }
	public TokenRule Rule { get; }
	public bool IsInfix { get; }
	public bool ForcesUnary { get; }

	public ExpressionItem CreateExpressionItem() {
		return MathHelpers.BadExpressionNode;
	}

	public ExpressionItem CreateExpressionItem(ExpressionItem item) {
		return MathHelpers.BadExpressionNode;
	}

	public ExpressionItem CreateExpressionItem(ExpressionItem left, ExpressionItem right) {
		return MathHelpers.BadExpressionNode;
	}
}

internal interface IReturnableToken : IToken, IExpressionReturn { }

internal interface IStringToken : IToken {
	public string Contents { get; }
}