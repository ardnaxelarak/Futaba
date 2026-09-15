namespace Futaba;

internal enum ExpressionState : int {
	Resolved = 0,

	// Possibly resolved later
	ResolvedButForceRequest = 1,
	Unresolved,
	DivideByZero,
	MissingSymbol,

	// TODO make these have a negative value because they can never be resolved?
	// we want the ExpressionState.WarrantsRequest extension property to not request these and report errors inline
	// but as it currently stands, a failed request being attempted is how some errors are reported

	// Unresolvable
	SyntaxError, // = int.MinValue,
	InvalidContext,
	InvalidArgument,
	InvalidArgumentToStringFunction,
	VariableNotFound,
	MissingIdentifier,
}
