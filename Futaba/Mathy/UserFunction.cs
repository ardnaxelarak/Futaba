namespace Futaba;

internal class UserFunction(string name, ExpressionItem tree, NamedParameter[] args) {
	public string Name => name;
	public int Arity { get; } = args.Length;

	private readonly ExpressionItem Tree = tree;
	private readonly NamedParameter[] Arguments = args;

	
	public ExpressionItem? GetNewInstance(Assembler assembler, List<ExpressionItem> arguments) {
		if (arguments.Count != Arity) {
			assembler.Error($"Wrong number of arguments to {Name} call");
			return null;
		}

		for (int i = 0; i < Arity; i++) {
			Arguments[i].CurrentValue = arguments[i];
		}

		return Tree.Clone();
	}
}
